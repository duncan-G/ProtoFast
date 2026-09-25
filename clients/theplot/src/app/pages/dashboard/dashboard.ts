import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  afterNextRender,
  computed,
  inject,
  linkedSignal,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { AuthIdentityService } from '../../auth/auth-identity';
import { describeError, DocumentApi, DocumentSummary } from '../../documents/document-api';
import { DocumentImportService, ImportJob, LIVE_PHASES } from '../../documents/document-import';
import { coverStripe, formatBytes, relativeTime, titleFromFileName } from '../../documents/format';
import { AccountMenu } from '../../shared/account-menu';
import { TheplotLogo } from '../../shared/theplot-logo';
import { DESK_MODES, DeskMode, MODES, parseMode } from './dashboard.data';
import { ImportDialog, ImportDraft } from './import-dialog';
import { ImportTray } from './import-tray';

/** A row on the desk that is still on its way in. */
interface PendingRow {
  job: ImportJob;
  title: string;
  file: string;
  status: string;
}

/** A row on the desk: a document, with what the columns show for it. */
interface DeskRow {
  document: DocumentSummary;
  cover: string;
  state: string;
  updated: string;
  isNew: boolean;
}

interface Toast {
  title: string;
  sub: string;
  documentId: string;
}

const TOAST_MS = 7000;

/**
 * The signed-in home: one app, three modes in the top bar — Read, Write, Watch — each
 * listing stories. Write is the writer's own desk and holds Import; Read and Watch are laid out
 * but empty until publishing and adaptation exist.
 *
 * Import is asynchronous and never blocks the app: the dialog starts a job in
 * `DocumentImportService`, the tray bottom-right follows every job across all three modes, and a
 * toast announces each one that lands. The mode is a query parameter (`?mode=read`) so a link
 * can open a mode directly and a reload comes back to it.
 */
@Component({
  selector: 'app-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AccountMenu, ImportDialog, ImportTray, RouterLink, TheplotLogo],
  templateUrl: './dashboard.html',
})
export class Dashboard {
  protected readonly auth = inject(AuthIdentityService);
  private readonly api = inject(DocumentApi);
  private readonly imports = inject(DocumentImportService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly modes = DESK_MODES;
  protected readonly modeInfo = MODES;

  protected readonly mode = toSignal(
    this.route.queryParamMap.pipe(map((params) => parseMode(params.get('mode')))),
    { initialValue: 'write' as DeskMode },
  );
  protected readonly info = computed(() => MODES[this.mode()]);

  /** The selected filter tab, back to "All" whenever the mode changes. */
  protected readonly tab = linkedSignal<DeskMode, number>({
    source: this.mode,
    computation: () => 0,
  });

  protected readonly search = signal('');

  protected readonly documents = signal<DocumentSummary[]>([]);
  protected readonly documentsLoading = signal(true);
  protected readonly documentsError = signal('');
  /** Documents that arrived this session, so their rows carry the Imported tag. */
  private readonly newDocumentIds = signal<ReadonlySet<string>>(new Set());

  protected readonly jobs = this.imports.jobs;
  protected readonly formats = this.imports.formats;
  protected readonly formatsError = this.imports.formatsError;

  protected readonly dialogOpen = signal(false);
  protected readonly draft = signal<ImportDraft | null>(null);
  /** The job the open dialog is following, if it started one. */
  private readonly dialogJobId = signal<number | null>(null);
  protected readonly dialogJob = computed(() => {
    const id = this.dialogJobId();
    return id === null ? null : (this.jobs().find((job) => job.id === id) ?? null);
  });

  protected readonly trayOpen = signal(false);
  protected readonly toast = signal<Toast | null>(null);
  private toastTimer: ReturnType<typeof setTimeout> | null = null;

  /** The count next to Write in the top bar. Read and Watch have nothing to count yet. */
  protected readonly counts = computed<Record<DeskMode, number | null>>(() => ({
    read: null,
    write: this.documents().length,
    watch: null,
  }));

  protected readonly pendingRows = computed<PendingRow[]>(() =>
    this.jobs()
      .filter((job) => LIVE_PHASES.has(job.phase))
      .map((job) => ({
        job,
        title: titleFromFileName(job.fileName),
        file: `${job.fileName} · ${formatBytes(job.sizeBytes)}`,
        status:
          job.phase === 'presign'
            ? 'Preparing upload…'
            : job.phase === 'uploading'
              ? `Uploading · ${job.progress}%`
              : 'Adding to your desk…',
      })),
  );

  protected readonly deskRows = computed<DeskRow[]>(() => {
    // Only "All" has anything in it: every document here is an import, and drafts, publishing
    // and adaptation are not built yet.
    if (this.tab() !== 0) {
      return [];
    }
    const query = this.search().trim().toLowerCase();
    const isNew = this.newDocumentIds();
    return this.documents()
      .filter(
        (d) =>
          query.length === 0 ||
          d.name.toLowerCase().includes(query) ||
          d.fileName.toLowerCase().includes(query),
      )
      .map((document) => ({
        document,
        cover: coverStripe(document.id),
        state: `Imported from ${document.fileName} · ${formatBytes(document.sizeBytes)}`,
        updated: relativeTime(document.lastModifiedAt),
        isNew: isNew.has(document.id),
      }));
  });

  protected readonly emptyState = computed(() => this.info().empty[this.tab()] ?? this.info().empty[0]);

  /** True when the desk has documents but the search matched none of them. */
  protected readonly searchMissed = computed(
    () =>
      this.mode() === 'write' &&
      this.tab() === 0 &&
      this.search().trim().length > 0 &&
      this.documents().length > 0 &&
      this.deskRows().length === 0,
  );

  constructor() {
    const destroyRef = inject(DestroyRef);
    destroyRef.onDestroy(() => this.clearToast());

    this.imports.completed$
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe((job) => this.onImported(job));

    // Browser-only: the session cookie never reaches the SSR render, so asking there would
    // paint an empty desk and then correct itself on hydration.
    afterNextRender(() => {
      void this.loadDocuments();
      void this.imports.ensureFormats().catch(() => undefined);
    });
  }

  protected setMode(mode: DeskMode): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { mode: mode === 'write' ? null : mode },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  protected async loadDocuments(): Promise<void> {
    this.documentsLoading.set(true);
    this.documentsError.set('');
    try {
      this.documents.set(await this.api.listDocuments());
    } catch (err) {
      this.documentsError.set(describeError(err, 'Your desk could not be loaded.'));
    } finally {
      this.documentsLoading.set(false);
    }
  }

  // ─── import dialog ───────────────────────────────────────────────────────

  protected openImport(): void {
    this.draft.set(null);
    this.dialogJobId.set(null);
    this.dialogOpen.set(true);
    void this.imports.ensureFormats().catch(() => undefined);
  }

  /** Checks the file locally; nothing is sent until Start. */
  protected pickFile(file: File): void {
    this.dialogJobId.set(null);
    this.draft.set({ file, rejection: this.imports.validate(file) });
  }

  protected removeDraft(): void {
    this.draft.set(null);
    this.dialogJobId.set(null);
  }

  protected startImport(): void {
    const draft = this.draft();
    if (!draft || draft.rejection) {
      return;
    }
    const job = this.imports.start(draft.file);
    this.draft.set(null);
    this.dialogJobId.set(job.id);
  }

  /** Closes the dialog. A live import moves to the tray, which opens so it is seen to continue. */
  protected closeImport(): void {
    const job = this.dialogJob();
    this.dialogOpen.set(false);
    this.draft.set(null);
    this.dialogJobId.set(null);
    if (job && LIVE_PHASES.has(job.phase)) {
      this.trayOpen.set(true);
    }
  }

  /** Stops the dialog's import and closes. */
  protected cancelImport(): void {
    const job = this.dialogJob();
    if (job) {
      this.imports.cancel(job.id);
    }
    this.dialogOpen.set(false);
    this.draft.set(null);
    this.dialogJobId.set(null);
  }

  protected retryImport(): void {
    const job = this.dialogJob();
    if (job) {
      this.imports.retry(job.id);
    }
  }

  // ─── tray ────────────────────────────────────────────────────────────────

  protected toggleTray(): void {
    this.trayOpen.update((open) => !open);
  }

  protected retryJob(job: ImportJob): void {
    this.imports.retry(job.id);
  }

  protected cancelJob(job: ImportJob): void {
    this.imports.cancel(job.id);
    if (this.dialogJobId() === job.id) {
      this.dialogJobId.set(null);
    }
  }

  protected dismissJob(job: ImportJob): void {
    this.imports.dismiss(job.id);
    if (this.dialogJobId() === job.id) {
      this.dialogJobId.set(null);
    }
  }

  protected clearFinishedJobs(): void {
    this.imports.dismissFinished();
    const id = this.dialogJobId();
    if (id !== null && !this.imports.find(id)) {
      this.dialogJobId.set(null);
    }
  }

  /** Shows the imported document on the desk. */
  protected openDocument(documentId: string): void {
    this.setMode('write');
    this.tab.set(0);
    this.search.set('');
    this.trayOpen.set(false);
    this.clearToast();
    this.newDocumentIds.update((ids) => new Set([...ids, documentId]));
  }

  protected openJob(job: ImportJob): void {
    if (job.document) {
      this.openDocument(job.document.id);
    }
  }

  private onImported(job: ImportJob): void {
    const document = job.document;
    if (!document) {
      return;
    }
    this.documents.update((docs) =>
      docs.some((d) => d.id === document.id) ? docs : [document, ...docs],
    );
    this.newDocumentIds.update((ids) => new Set([...ids, document.id]));

    this.clearToast();
    this.toast.set({
      title: `“${document.name}” is on your desk`,
      sub: `Added to Write · ${document.fileName}`,
      documentId: document.id,
    });
    this.toastTimer = setTimeout(() => this.toast.set(null), TOAST_MS);
  }

  protected clearToast(): void {
    if (this.toastTimer !== null) {
      clearTimeout(this.toastTimer);
      this.toastTimer = null;
    }
    this.toast.set(null);
  }
}
