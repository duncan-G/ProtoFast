import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  inject,
  linkedSignal,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { AuthIdentityService } from '../../auth/auth-identity';
import {
  describeError,
  DocumentApi,
  DocumentSummary,
  ImportProgress,
} from '../../documents/document-api';
import { DocumentImportService, ImportJob, LIVE_PHASES } from '../../documents/document-import';
import { coverStripe, formatBytes, relativeTime, titleFromFileName } from '../../documents/format';
import {
  describeCost,
  describeImport,
  IMPORT_STEPS,
  isImportActive,
} from '../../documents/import-progress';
import { AccountMenu } from '../../shared/account-menu';
import { StorySummary } from '../../stories/model/story-summary';
import { StoryApi } from '../../stories/story-api';
import { TheplotLogo } from '../../shared/theplot-logo';
import { DESK_MODES, DeskMode, MODES, parseMode } from './dashboard.data';
import { ImportDialog, ImportDraft } from './import-dialog';
import { ImportTray } from './import-tray';

/** A row on the desk that is still uploading. */
interface PendingRow {
  job: ImportJob;
  title: string;
  file: string;
  status: string;
}

/** How a document's import reads on its row. */
interface DeskImport {
  label: string;
  /** What went wrong, while retrying or once failed. */
  note: string | null;
  failed: boolean;
  active: boolean;
  /** What the import has spent on model calls, once it has spent anything. */
  cost: string | null;
  /** Width of the thin bar under the label, or null for none. */
  percent: number | null;
}

/**
 * A row on the desk: a story, or a document that is being read into one, with what the columns
 * show for it.
 */
interface DeskRow {
  kind: 'story' | 'document';
  id: string;
  title: string;
  cover: string;
  state: string;
  updated: string;
  lastModifiedAt: Date;
  isNew: boolean;
  /** Set for a document, whose row is its import. */
  import: DeskImport | null;
}

interface Toast {
  title: string;
  sub: string;
  storyId: string;
}

const TOAST_MS = 7000;

/**
 * The signed-in home: one app, three modes in the top bar — Read, Write, Watch — each
 * listing stories. Write is the writer's own desk and holds New story and Import; Read and Watch
 * are laid out but empty until publishing and adaptation exist.
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
  private readonly storyApi = inject(StoryApi);
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
  protected readonly stories = signal<StorySummary[]>([]);
  protected readonly deskLoading = signal(true);
  protected readonly deskError = signal('');

  protected readonly newStoryOpen = signal(false);
  protected readonly newStoryTitle = signal('');
  protected readonly newStoryError = signal('');
  protected readonly creatingStory = signal(false);
  private readonly newStoryField = viewChild<ElementRef<HTMLInputElement>>('newStoryField');
  /** The story whose delete confirmation is open under its row. */
  protected readonly confirmingDeleteId = signal<string | null>(null);
  /** Stories imports produced this session, so their rows carry the Imported tag. */
  private readonly newStoryIds = signal<ReadonlySet<string>>(new Set());

  protected readonly jobs = this.imports.jobs;
  protected readonly importProgress = this.imports.progress;
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
    write: this.documents().length + this.stories().length,
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
              : 'Handing it on to be read…',
      })),
  );

  protected readonly deskRows = computed<DeskRow[]>(() => {
    // Only "All" has anything in it: publishing and adaptation are not built yet.
    if (this.tab() !== 0) {
      return [];
    }
    const query = this.search().trim().toLowerCase();
    const matches = (...texts: string[]) =>
      query.length === 0 || texts.some((t) => t.toLowerCase().includes(query));
    const isNew = this.newStoryIds();
    const progress = this.importProgress();
    const stories = this.stories()
      .filter((story) => matches(story.title))
      .map<DeskRow>((story) => ({
        kind: 'story',
        id: story.id,
        title: story.title,
        cover: coverStripe(story.id),
        state: 'Screenplay',
        updated: relativeTime(story.lastModifiedAt),
        lastModifiedAt: story.lastModifiedAt,
        isNew: isNew.has(story.id),
        import: null,
      }));
    const documents = this.documents()
      .filter((d) => matches(d.name, d.fileName))
      .filter((d) => (progress.get(d.id) ?? d.import).state !== 'cancelled')
      .map<DeskRow>((document) => ({
        kind: 'document',
        id: document.id,
        title: document.name,
        cover: coverStripe(document.id),
        state: `${document.fileName} · ${formatBytes(document.sizeBytes)}`,
        updated: relativeTime(document.lastModifiedAt),
        lastModifiedAt: document.lastModifiedAt,
        isNew: false,
        import: deskImport(progress.get(document.id) ?? document.import),
      }));
    const importing = (row: DeskRow) => (row.import?.active ? 0 : 1);
    return [...stories, ...documents].sort(
      (a, b) =>
        importing(a) - importing(b) || b.lastModifiedAt.getTime() - a.lastModifiedAt.getTime(),
    );
  });

  /** Imports still running on the server that no upload this session covers, e.g. after a reload. */
  protected readonly serverImports = computed<DocumentSummary[]>(() => {
    const progress = this.importProgress();
    const tracked = new Set(this.jobs().map((job) => job.document?.id));
    return this.documents().filter(
      (d) => !tracked.has(d.id) && isImportActive(progress.get(d.id) ?? d.import),
    );
  });

  protected readonly emptyState = computed(
    () => this.info().empty[this.tab()] ?? this.info().empty[0],
  );

  /** True when the desk has entries but the search matched none of them. */
  protected readonly searchMissed = computed(
    () =>
      this.mode() === 'write' &&
      this.tab() === 0 &&
      this.search().trim().length > 0 &&
      this.documents().length + this.stories().length > 0 &&
      this.deskRows().length === 0,
  );

  constructor() {
    const destroyRef = inject(DestroyRef);
    destroyRef.onDestroy(() => this.clearToast());

    effect(() => this.newStoryField()?.nativeElement.focus());

    this.imports.uploaded$
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe((job) => this.onUploaded(job));
    this.imports.finished$
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe((progress) => this.onImported(progress));

    // Browser-only: the session cookie never reaches the SSR render, so asking there would
    // paint an empty desk and then correct itself on hydration.
    afterNextRender(() => {
      void this.loadDesk();
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

  /** Either list can fail alone; the other still shows. */
  protected async loadDesk(): Promise<void> {
    this.deskLoading.set(true);
    this.deskError.set('');
    const [documents, stories] = await Promise.allSettled([
      this.api.listDocuments(),
      this.storyApi.listStories(),
    ]);
    if (documents.status === 'fulfilled') {
      this.documents.set(documents.value);
      this.imports.track(documents.value.map((d) => d.import));
    }
    if (stories.status === 'fulfilled') {
      this.stories.set(stories.value);
    }
    const failed = [documents, stories].find((r) => r.status === 'rejected');
    if (failed) {
      this.deskError.set(describeError(failed.reason, 'Your desk could not be loaded.'));
    }
    this.deskLoading.set(false);
  }

  // ─── stories ─────────────────────────────────────────────────────────────

  protected openNewStory(): void {
    this.setMode('write');
    this.tab.set(0);
    this.newStoryTitle.set('');
    this.newStoryError.set('');
    this.newStoryOpen.set(true);
  }

  protected closeNewStory(): void {
    if (!this.creatingStory()) {
      this.newStoryOpen.set(false);
    }
  }

  /** Opens the new story's first scene in the editor. */
  protected async createStory(): Promise<void> {
    const title = this.newStoryTitle().trim();
    if (!title) {
      this.newStoryError.set('A story needs a title.');
      return;
    }
    this.creatingStory.set(true);
    this.newStoryError.set('');
    try {
      const story = await this.storyApi.createStory(title);
      const sceneId = story.containers[0]?.scenes[0]?.id;
      await this.router.navigate(
        sceneId ? ['/app/stories', story.id, 'scenes', sceneId] : ['/app/stories', story.id],
      );
    } catch (err) {
      this.newStoryError.set(describeError(err, 'The story could not be created.'));
    } finally {
      this.creatingStory.set(false);
    }
  }

  protected async deleteStory(storyId: string): Promise<void> {
    this.confirmingDeleteId.set(null);
    try {
      await this.storyApi.deleteStory(storyId);
      this.stories.update((stories) => stories.filter((s) => s.id !== storyId));
    } catch (err) {
      this.deskError.set(describeError(err, 'The story could not be deleted.'));
    }
  }

  /** A running import is stopped, a failed one cleared; either way the document leaves the desk. */
  protected async cancelDocumentImport(uploadId: string): Promise<void> {
    this.confirmingDeleteId.set(null);
    try {
      await this.imports.cancelImport(uploadId);
      this.documents.update((documents) => documents.filter((d) => d.id !== uploadId));
    } catch (err) {
      // Refused most likely because it just became a story, which the reload shows.
      await this.loadDesk();
      this.deskError.set(describeError(err, 'The import could not be cancelled.'));
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

  /** Closes the dialog. A live upload moves to the tray, which opens so it is seen to continue. */
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

  /** Opens the story an import produced. */
  protected openStory(storyId: string): void {
    this.trayOpen.set(false);
    this.clearToast();
    void this.router.navigate(['/app/stories', storyId]);
  }

  protected openJob(job: ImportJob): void {
    const storyId = this.imports.progressOf(job)?.storyId;
    if (storyId) {
      this.openStory(storyId);
    }
  }

  /**
   * The upload is handed off, so the dialog has nothing left to show: it closes, and the tray
   * opens to follow the import. The document joins the desk as an importing row.
   */
  private onUploaded(job: ImportJob): void {
    const document = job.document;
    if (!document) {
      return;
    }
    this.documents.update((docs) =>
      docs.some((d) => d.id === document.id) ? docs : [document, ...docs],
    );
    if (this.dialogJobId() === job.id) {
      this.dialogOpen.set(false);
      this.draft.set(null);
      this.dialogJobId.set(null);
    }
    this.trayOpen.set(true);
  }

  /** The document became a story: the desk reloads to swap one for the other. */
  private onImported(progress: ImportProgress): void {
    const storyId = progress.storyId;
    if (!storyId) {
      return;
    }
    const document = this.documents().find((d) => d.id === progress.uploadId);
    this.newStoryIds.update((ids) => new Set([...ids, storyId]));
    void this.loadDesk();

    this.clearToast();
    this.toast.set({
      title: document ? `“${document.name}” is ready` : 'Your import is ready',
      sub: document ? `Imported into Write · ${document.fileName}` : 'Imported into Write',
      storyId,
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

function deskImport(progress: ImportProgress): DeskImport {
  const { label, step } = describeImport(progress);
  const noted = progress.state === 'failed' || progress.state === 'retrying';
  return {
    label: step === null ? label : `${label} · ${step} of ${IMPORT_STEPS}`,
    note: noted ? progress.message || null : null,
    failed: progress.state === 'failed',
    active: isImportActive(progress),
    cost: describeCost(progress),
    percent: step === null ? null : (step / IMPORT_STEPS) * 100,
  };
}
