import { DestroyRef, inject, Injectable, PLATFORM_ID, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { Subject } from 'rxjs';
import {
  AcceptedFormats,
  describeError,
  DocumentApi,
  DocumentSummary,
  ImportProgress,
  UploadTicket,
} from './document-api';
import { extensionLabel, extensionOf } from './format';
import { isImportActive } from './import-progress';

/**
 * Where an upload is. The three live phases mirror the three steps the dialog draws:
 * `presign` asks the API for a signed POST, `uploading` posts the file straight to storage, and
 * `saving` tells the API the object landed so it records the document and queues its import.
 * `uploaded` is only the hand-off: the server then reads the file into a story, which `progress`
 * follows.
 */
export type ImportPhase = 'presign' | 'uploading' | 'saving' | 'uploaded' | 'failed';

export const LIVE_PHASES: ReadonlySet<ImportPhase> = new Set(['presign', 'uploading', 'saving']);

/** Why a file was turned down before anything was sent. */
export type ImportRejection = 'type' | 'size';

export interface ImportJob {
  readonly id: number;
  readonly file: File;
  readonly fileName: string;
  readonly sizeBytes: number;
  /** "DOCX", for the little file glyph. */
  readonly extension: string;
  readonly phase: ImportPhase;
  /** 0–100 while uploading; 100 once the bytes are in storage. */
  readonly progress: number;
  readonly loadedBytes: number;
  /** The phase that failed, so a retry can resume rather than start over. */
  readonly failedAt: ImportPhase | null;
  readonly error: string | null;
  /** The document the API recorded, once `phase` is `uploaded`. */
  readonly document: DocumentSummary | null;
  readonly startedAt: Date;
}

/** Where a job stands end to end: its upload, then the import the server runs on it. */
export type JobStatus = 'uploading' | 'importing' | 'done' | 'failed';

export function jobStatus(job: ImportJob, progress: ImportProgress | undefined): JobStatus {
  switch (job.phase) {
    case 'failed':
      return 'failed';
    case 'uploaded':
      if (!progress || isImportActive(progress)) {
        return 'importing';
      }
      return progress.state === 'done' ? 'done' : 'failed';
    default:
      return 'uploading';
  }
}

const POLL_MS = 3000;
// The API answers for at most this many uploads per call.
const POLL_BATCH = 50;

/**
 * Runs document imports and keeps them alive across the app: the tray, the dialog and the desk
 * all read the one `jobs` list, so closing the dialog or switching modes never loses an upload.
 * Once uploaded, each document's import is polled until it is done or has failed; `progress`
 * holds the latest word on every import it has been told about. Root-provided for that reason;
 * browser-only in effect, since nothing here is called during SSR.
 */
@Injectable({ providedIn: 'root' })
export class DocumentImportService {
  private readonly api = inject(DocumentApi);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  private readonly jobsState = signal<ImportJob[]>([]);
  private readonly progressState = signal<ReadonlyMap<string, ImportProgress>>(new Map());
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly formatsState = signal<AcceptedFormats | null>(null);
  private readonly formatsErrorState = signal<string | null>(null);
  private formatsRequest: Promise<AcceptedFormats> | null = null;

  /** Live uploads by job id, so a cancel can abort the request mid-flight. */
  private readonly requests = new Map<number, XMLHttpRequest>();
  /** Signed POSTs by job id, kept so a failure while saving can be retried without re-uploading. */
  private readonly tickets = new Map<number, UploadTicket>();
  private nextId = 1;

  /** Every import this session, newest first. */
  readonly jobs = this.jobsState.asReadonly();
  /** The API's accepted-format table, once loaded; null until then. */
  readonly formats = this.formatsState.asReadonly();
  readonly formatsError = this.formatsErrorState.asReadonly();

  /** Server-side import progress by upload (document) id. */
  readonly progress = this.progressState.asReadonly();

  /** Fires once per upload that reaches `uploaded`. */
  readonly uploaded$ = new Subject<ImportJob>();
  /** Fires once per import seen to turn into a story. */
  readonly finished$ = new Subject<ImportProgress>();

  constructor() {
    if (this.isBrowser) {
      // A closed tab mid-upload loses the file; the prompt is the one thing that can stop it.
      const warn = (event: BeforeUnloadEvent) => {
        if (this.jobs().some((job) => LIVE_PHASES.has(job.phase))) {
          event.preventDefault();
        }
      };
      window.addEventListener('beforeunload', warn);
      inject(DestroyRef).onDestroy(() => {
        window.removeEventListener('beforeunload', warn);
        this.stopPolling();
      });
    }
  }

  /** Takes the latest word on these imports and polls the ones still running. */
  track(progress: ImportProgress[]): void {
    if (progress.length === 0) {
      return;
    }
    this.progressState.update((known) => {
      const next = new Map(known);
      for (const p of progress) {
        next.set(p.uploadId, p);
      }
      return next;
    });
    this.schedulePoll();
  }

  /**
   * Loads the accepted formats once and shares the result; a failure is remembered as an error
   * and asked again on the next call rather than cached.
   */
  ensureFormats(): Promise<AcceptedFormats> {
    if (this.formatsRequest) {
      return this.formatsRequest;
    }
    this.formatsErrorState.set(null);
    this.formatsRequest = this.api
      .listSourceFormats()
      .then((formats) => {
        this.formatsState.set(formats);
        return formats;
      })
      .catch((err: unknown) => {
        this.formatsRequest = null;
        this.formatsErrorState.set(
          describeError(err, 'The accepted file types could not be loaded.'),
        );
        throw err;
      });
    return this.formatsRequest;
  }

  /**
   * The local check the dialog runs before anything is sent: extension in the API's table and
   * size under its limit. Null when the file passes — or when the table has not loaded, in which
   * case the API is the one that says no.
   */
  validate(file: File): ImportRejection | null {
    const formats = this.formatsState();
    if (!formats) {
      return null;
    }
    const extension = extensionOf(file.name);
    if (!formats.formats.some((f) => f.extension === extension)) {
      return 'type';
    }
    if (file.size > formats.maxBytes) {
      return 'size';
    }
    return null;
  }

  /** Starts an import and returns its job. The work runs on; watch `jobs` for its progress. */
  start(file: File): ImportJob {
    const job: ImportJob = {
      id: this.nextId++,
      file,
      fileName: file.name,
      sizeBytes: file.size,
      extension: extensionLabel(file.name),
      phase: 'presign',
      progress: 0,
      loadedBytes: 0,
      failedAt: null,
      error: null,
      document: null,
      startedAt: new Date(),
    };
    this.jobsState.update((jobs) => [job, ...jobs]);
    void this.run(job.id);
    return job;
  }

  /**
   * Runs a failed import again from the step that failed. An upload that never reached storage
   * starts over with a fresh signed POST; one that landed but could not be recorded only asks
   * the API to record it again.
   */
  retry(id: number): void {
    const job = this.find(id);
    if (!job || job.phase !== 'failed') {
      return;
    }
    const resumeSave = job.failedAt === 'saving' && this.tickets.has(id);
    this.patch(id, {
      phase: resumeSave ? 'saving' : 'presign',
      progress: resumeSave ? 100 : 0,
      loadedBytes: resumeSave ? job.sizeBytes : 0,
      failedAt: null,
      error: null,
    });
    void this.run(id, resumeSave);
  }

  /** Stops a live import and drops it. A finished or failed one is simply removed. */
  cancel(id: number): void {
    const request = this.requests.get(id);
    this.requests.delete(id);
    this.tickets.delete(id);
    request?.abort();
    this.jobsState.update((jobs) => jobs.filter((job) => job.id !== id));
  }

  /** Removes a finished or failed import from the tray. */
  dismiss(id: number): void {
    const job = this.find(id);
    if (job && this.isFinished(job)) {
      this.cancel(id);
    }
  }

  /** Removes every finished or failed import; live ones keep going. */
  dismissFinished(): void {
    for (const job of this.jobsState()) {
      if (this.isFinished(job)) {
        this.cancel(job.id);
      }
    }
  }

  /** The job's server-side import, once it has one. */
  progressOf(job: ImportJob): ImportProgress | undefined {
    return job.document ? this.progressState().get(job.document.id) : undefined;
  }

  private isFinished(job: ImportJob): boolean {
    const status = jobStatus(job, this.progressOf(job));
    return status === 'done' || status === 'failed';
  }

  find(id: number): ImportJob | undefined {
    return this.jobsState().find((job) => job.id === id);
  }

  private async run(id: number, resumeSave = false): Promise<void> {
    const job = this.find(id);
    if (!job) {
      return;
    }
    let phase: ImportPhase = job.phase;
    try {
      let ticket = this.tickets.get(id);
      if (!resumeSave || !ticket) {
        phase = 'presign';
        ticket = await this.api.createUploadUrl(job.file);
        if (!this.find(id)) {
          return; // cancelled while the API was signing
        }
        this.tickets.set(id, ticket);

        phase = 'uploading';
        this.patch(id, { phase, progress: 0, loadedBytes: 0 });
        await this.postToStorage(id, ticket, job.file);
        if (!this.find(id)) {
          return; // cancelled mid-upload; the abort rejected above and was swallowed
        }
      }

      phase = 'saving';
      this.patch(id, { phase, progress: 100, loadedBytes: job.sizeBytes });
      const document = await this.api.completeUpload(ticket.uploadId);
      if (!this.find(id)) {
        return;
      }
      this.tickets.delete(id);
      this.patch(id, { phase: 'uploaded', document });
      this.track([document.import]);
      const uploaded = this.find(id);
      if (uploaded) {
        this.uploaded$.next(uploaded);
      }
    } catch (err) {
      if (!this.find(id)) {
        return;
      }
      this.patch(id, {
        phase: 'failed',
        failedAt: phase,
        error: describeError(err, FAILURE_MESSAGES[phase]),
      });
    }
  }

  /**
   * Replays the signed POST as multipart/form-data, fields in signing order and the file part
   * last — S3 stops reading at the file, so a field after it is never seen and the upload fails
   * the policy it was signed against. XMLHttpRequest rather than fetch for the upload progress.
   */
  private postToStorage(id: number, ticket: UploadTicket, file: File): Promise<void> {
    return new Promise<void>((resolve, reject) => {
      const form = new FormData();
      for (const [name, value] of ticket.fields) {
        form.append(name, value);
      }
      form.append('file', file, file.name);

      const xhr = new XMLHttpRequest();
      this.requests.set(id, xhr);

      xhr.upload.addEventListener('progress', (event) => {
        if (event.lengthComputable) {
          const loadedBytes = Math.min(file.size, event.loaded);
          this.patch(id, {
            loadedBytes,
            progress: file.size > 0 ? Math.round((loadedBytes / file.size) * 100) : 100,
          });
        }
      });
      xhr.addEventListener('load', () => {
        this.requests.delete(id);
        if (xhr.status >= 200 && xhr.status < 300) {
          resolve();
        } else {
          reject(new Error(describeStorageFailure(xhr)));
        }
      });
      xhr.addEventListener('error', () => {
        this.requests.delete(id);
        reject(new Error('The upload to storage failed. Check your connection and try again.'));
      });
      xhr.addEventListener('abort', () => {
        this.requests.delete(id);
        reject(new Error('The upload was cancelled.'));
      });

      xhr.open('POST', ticket.postUrl);
      xhr.send(form);
    });
  }

  private schedulePoll(): void {
    if (!this.isBrowser || this.pollTimer !== null) {
      return;
    }
    if (![...this.progressState().values()].some(isImportActive)) {
      return;
    }
    this.pollTimer = setTimeout(() => void this.poll(), POLL_MS);
  }

  private stopPolling(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }

  /** A failed poll is simply tried again on the next tick. */
  private async poll(): Promise<void> {
    const active = [...this.progressState().values()]
      .filter(isImportActive)
      .map((p) => p.uploadId)
      .slice(0, POLL_BATCH);
    if (active.length === 0) {
      this.pollTimer = null;
      return;
    }
    try {
      const latest = await this.api.getImportProgress(active);
      const answered = new Set(latest.map((p) => p.uploadId));
      const finished = latest.filter(
        (p) => p.state === 'done' && this.progressState().get(p.uploadId)?.state !== 'done',
      );
      this.progressState.update((known) => {
        const next = new Map(known);
        for (const id of active) {
          // Left out means it is no longer the caller's or never became a document.
          if (!answered.has(id)) {
            next.delete(id);
          }
        }
        for (const p of latest) {
          next.set(p.uploadId, p);
        }
        return next;
      });
      for (const p of finished) {
        this.finished$.next(p);
      }
    } catch {
      // Kept as it was; the next tick asks again.
    } finally {
      this.pollTimer = null;
      this.schedulePoll();
    }
  }

  private patch(id: number, changes: Partial<ImportJob>): void {
    this.jobsState.update((jobs) =>
      jobs.map((job) => (job.id === id ? { ...job, ...changes } : job)),
    );
  }
}

const FAILURE_MESSAGES: Record<ImportPhase, string> = {
  presign: 'A secure upload link could not be created.',
  uploading: 'The upload to storage failed.',
  saving: 'The file was uploaded but could not be handed on to be read.',
  uploaded: 'The import failed.',
  failed: 'The import failed.',
};

/**
 * S3 answers a refused POST with a small XML document whose `<Code>` names the reason. The two
 * a person can act on get their own sentence; anything else is reported by status.
 */
function describeStorageFailure(xhr: XMLHttpRequest): string {
  const code = /<Code>([^<]+)<\/Code>/.exec(xhr.responseText ?? '')?.[1];
  switch (code) {
    case 'EntityTooLarge':
      return 'Storage refused the file as too large.';
    case 'AccessDenied':
    case 'ExpiredToken':
      return 'The upload link expired before the file finished. Try again.';
    default:
      return `Storage refused the upload (HTTP ${xhr.status}).`;
  }
}
