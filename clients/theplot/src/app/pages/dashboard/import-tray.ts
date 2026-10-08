import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { DocumentSummary, ImportProgress } from '../../documents/document-api';
import { ImportJob, jobStatus } from '../../documents/document-import';
import { extensionLabel } from '../../documents/format';
import { describeCost, describeImport, IMPORT_STEPS } from '../../documents/import-progress';

/** One row of the expanded tray, reduced to what it shows. */
interface TrayRow {
  key: string;
  /** Null for an import this session did not upload. */
  job: ImportJob | null;
  fileName: string;
  extension: string;
  kind: 'active' | 'done' | 'failed';
  status: string;
  /** Width of the thin bar, or null for none. */
  percent: number | null;
  note: string | null;
  /** What the server's import has spent so far. */
  cost: string | null;
  action: 'open' | 'retry' | null;
  /** Only an upload can be stopped; once handed off, the server's import runs on. */
  cancellable: boolean;
}

/**
 * The imports tray: a pill pinned bottom-right that sums up every import this session and any
 * the server is still running from an earlier one, and opens into a list with one row each. It
 * floats over every mode, which is what lets a writer keep reading while a file goes up and is
 * read into a story. A row follows the upload,
 * then the server's import of it, and offers Open only once the story exists.
 */
@Component({
  selector: 'app-import-tray',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="tray">
      @if (expanded()) {
        <div class="tray-panel rise" role="region" aria-label="Imports">
          <div class="flex items-center gap-2.5 px-4 pt-3.5 pb-3">
            <h3 class="m-0 flex-1 text-[22px]">Imports</h3>
            <span class="meta">{{ countText() }}</span>
            <button type="button" class="icon-btn" aria-label="Minimise imports" (click)="toggle.emit()">▾</button>
          </div>
          @for (row of rows(); track row.key) {
            <div class="flex items-start gap-3 border-t border-[var(--color-divider)] px-4 py-3">
              <div class="file-ext file-ext-sm">{{ row.extension }}</div>
              <div class="flex min-w-0 flex-1 flex-col gap-[5px]">
                <div class="truncate text-[13px] font-medium">{{ row.fileName }}</div>
                <div
                  class="meta flex items-center gap-1.5 text-[11px]"
                  [class.text-[var(--color-accent-bright)]]="row.kind === 'active'"
                  [class.text-[var(--color-success)]]="row.kind === 'done'"
                  [class.text-[var(--color-danger-300)]]="row.kind === 'failed'"
                >
                  @if (row.kind === 'active') {
                    <span class="spinner h-2.5 w-2.5" aria-hidden="true"></span>
                  } @else {
                    <span class="dot" [style.background]="row.kind === 'done' ? 'var(--color-success)' : 'var(--color-danger-300)'"></span>
                  }
                  <span>{{ row.status }}</span>
                </div>
                @if (row.cost) {
                  <div class="meta text-[11px] text-[var(--color-neutral-600)]">{{ row.cost }}</div>
                }
                @if (row.percent !== null) {
                  <div class="progress progress-thin">
                    <div class="progress-bar" [style.width.%]="row.percent"></div>
                  </div>
                }
                @if (row.note) {
                  <div class="text-[12px] leading-[1.4] text-muted [text-wrap:pretty]">{{ row.note }}</div>
                }
                @if (row.job; as job) {
                  @if (row.action === 'open') {
                    <div class="mt-0.5">
                      <button type="button" class="btn btn-secondary px-2.5 py-[5px] text-[12px]" (click)="open.emit(job)">Open</button>
                    </div>
                  } @else if (row.action === 'retry') {
                    <div class="mt-0.5 flex gap-1.5">
                      <button type="button" class="btn btn-secondary px-2.5 py-[5px] text-[12px]" (click)="retry.emit(job)">Retry</button>
                    </div>
                  }
                }
              </div>
              @if (row.job; as job) {
                @if (row.cancellable) {
                  <button type="button" class="icon-btn h-6 w-6 text-[13px]" aria-label="Cancel upload" (click)="cancel.emit(job)">×</button>
                } @else if (row.kind !== 'active') {
                  <button type="button" class="icon-btn h-6 w-6 text-[15px]" aria-label="Dismiss" (click)="dismiss.emit(job)">×</button>
                }
              }
            </div>
          }
          @if (finished() > 0) {
            <div class="flex justify-end border-t border-[var(--color-divider)] bg-[var(--color-bg)] px-4 py-2">
              <button type="button" class="btn btn-ghost text-[12px]" (click)="clearAll.emit()">
                Clear all
              </button>
            </div>
          }
        </div>
      }
      <button
        type="button"
        class="tray-pill"
        [attr.aria-expanded]="expanded()"
        (click)="toggle.emit()"
      >
        @if (active() > 0) {
          <span class="spinner text-[var(--color-accent-bright)]" aria-hidden="true"></span>
        } @else {
          <span class="dot h-2 w-2" [style.background]="failed() > 0 ? 'var(--color-danger-300)' : 'var(--color-success)'"></span>
        }
        <span>{{ summary() }}</span>
        <span class="text-[11px] text-[var(--color-neutral-600)]">{{ expanded() ? '▾' : '▴' }}</span>
      </button>
    </div>
  `,
})
export class ImportTray {
  readonly jobs = input.required<ImportJob[]>();
  /** Server-side import progress by document id. */
  readonly progress = input<ReadonlyMap<string, ImportProgress>>(new Map());
  /** Documents still importing that no job covers, so a reload still shows them. */
  readonly serverImports = input<DocumentSummary[]>([]);
  readonly expanded = input(false);

  readonly toggle = output<void>();
  readonly open = output<ImportJob>();
  readonly retry = output<ImportJob>();
  readonly cancel = output<ImportJob>();
  readonly dismiss = output<ImportJob>();
  /** Drop every finished or failed import at once; live ones stay. */
  readonly clearAll = output<void>();

  private readonly statuses = computed(() => [
    ...this.jobs().map((job) => jobStatus(job, this.progressOf(job))),
    ...this.serverImports().map(() => 'importing' as const),
  ]);
  protected readonly active = computed(
    () => this.statuses().filter((s) => s === 'uploading' || s === 'importing').length,
  );
  protected readonly done = computed(() => this.statuses().filter((s) => s === 'done').length);
  protected readonly failed = computed(() => this.statuses().filter((s) => s === 'failed').length);
  protected readonly finished = computed(() => this.done() + this.failed());

  protected readonly summary = computed(() => {
    const active = this.active();
    const failed = this.failed();
    const done = this.done();
    if (active > 0) {
      return `Importing ${active} file${active > 1 ? 's' : ''}`;
    }
    if (failed > 0) {
      return `${failed} import${failed > 1 ? 's need' : ' needs'} attention`;
    }
    return `${done} import${done > 1 ? 's' : ''} ready`;
  });

  protected readonly countText = computed(() =>
    [
      this.active() && `${this.active()} running`,
      this.done() && `${this.done()} ready`,
      this.failed() && `${this.failed()} failed`,
    ]
      .filter(Boolean)
      .join(' · '),
  );

  protected readonly rows = computed<TrayRow[]>(() => [
    ...this.jobs().map((job): TrayRow => {
      const row = {
        key: `job-${job.id}`,
        job,
        fileName: job.fileName,
        extension: job.extension,
        percent: null,
        note: null,
        cost: null,
        action: null,
        cancellable: false,
      } as const;
      switch (job.phase) {
        case 'presign':
          return { ...row, kind: 'active', status: 'Preparing upload…', cancellable: true };
        case 'uploading':
          return {
            ...row,
            kind: 'active',
            status: `Uploading · ${job.progress}%`,
            percent: job.progress,
            note: 'Keep this tab open until the upload finishes.',
            cancellable: true,
          };
        case 'saving':
          return { ...row, kind: 'active', status: 'Handing it on to be read…', cancellable: true };
        case 'failed':
          return {
            ...row,
            kind: 'failed',
            status: 'Upload didn’t finish',
            note: job.error,
            action: 'retry',
          };
        case 'uploaded':
          return this.importRow(row, this.progressOf(job));
      }
    }),
    ...this.serverImports().map((document) =>
      this.importRow(
        {
          key: `document-${document.id}`,
          job: null,
          fileName: document.fileName,
          extension: extensionLabel(document.fileName),
        },
        this.progress().get(document.id) ?? document.import,
      ),
    ),
  ]);

  private importRow(
    { key, job, fileName, extension }: Pick<TrayRow, 'key' | 'job' | 'fileName' | 'extension'>,
    progress: ImportProgress | undefined,
  ): TrayRow {
    const row = {
      key,
      job,
      fileName,
      extension,
      percent: null,
      note: null,
      cost: progress ? describeCost(progress) : null,
      action: null,
      cancellable: false,
    } as const;
    if (!progress) {
      return { ...row, kind: 'active', status: 'Waiting to be read' };
    }
    const { label, step } = describeImport(progress);
    switch (progress.state) {
      case 'done':
        return { ...row, kind: 'done', status: 'Ready · in Write', action: 'open' };
      case 'failed':
        return { ...row, kind: 'failed', status: label, note: progress.message || null };
      case 'retrying':
        return { ...row, kind: 'active', status: label, note: progress.message || null };
      default:
        return {
          ...row,
          kind: 'active',
          status: `${label} · ${step} of ${IMPORT_STEPS}`,
          percent: step === null ? null : (step / IMPORT_STEPS) * 100,
        };
    }
  }

  private progressOf(job: ImportJob): ImportProgress | undefined {
    return job.document ? (this.progress().get(job.document.id) ?? job.document.import) : undefined;
  }
}
