import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { ImportJob, LIVE_PHASES } from '../../documents/document-import';

/** One row of the expanded tray, reduced to what it shows. */
interface TrayRow {
  job: ImportJob;
  kind: 'active' | 'done' | 'failed';
  status: string;
  showProgress: boolean;
  note: string | null;
  action: 'open' | 'retry' | null;
}

/**
 * The imports tray: a pill pinned bottom-right that sums up every import this session, and
 * opens into a list with one row per job. It floats over every mode, which is what lets a
 * writer keep reading while a file goes up.
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
          @for (row of rows(); track row.job.id) {
            <div class="flex items-start gap-3 border-t border-[var(--color-divider)] px-4 py-3">
              <div class="file-ext file-ext-sm">{{ row.job.extension }}</div>
              <div class="flex min-w-0 flex-1 flex-col gap-[5px]">
                <div class="truncate text-[13px] font-medium">{{ row.job.fileName }}</div>
                <div
                  class="meta flex items-center gap-1.5"
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
                @if (row.showProgress) {
                  <div class="progress progress-thin">
                    <div class="progress-bar" [style.width.%]="row.job.progress"></div>
                  </div>
                }
                @if (row.note) {
                  <div class="text-[12px] leading-[1.4] text-muted [text-wrap:pretty]">{{ row.note }}</div>
                }
                @if (row.action === 'open') {
                  <div class="mt-0.5">
                    <button type="button" class="btn btn-secondary px-2.5 py-[5px] text-[12px]" (click)="open.emit(row.job)">Open</button>
                  </div>
                } @else if (row.action === 'retry') {
                  <div class="mt-0.5 flex gap-1.5">
                    <button type="button" class="btn btn-secondary px-2.5 py-[5px] text-[12px]" (click)="retry.emit(row.job)">Retry</button>
                  </div>
                }
              </div>
              @if (row.kind === 'active') {
                <button type="button" class="icon-btn h-6 w-6 text-[13px]" aria-label="Cancel import" (click)="cancel.emit(row.job)">×</button>
              } @else {
                <button type="button" class="icon-btn h-6 w-6 text-[15px]" aria-label="Dismiss" (click)="dismiss.emit(row.job)">×</button>
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
  readonly expanded = input(false);

  readonly toggle = output<void>();
  readonly open = output<ImportJob>();
  readonly retry = output<ImportJob>();
  readonly cancel = output<ImportJob>();
  readonly dismiss = output<ImportJob>();
  /** Drop every finished or failed import at once; live ones stay. */
  readonly clearAll = output<void>();

  protected readonly active = computed(() => this.jobs().filter((j) => LIVE_PHASES.has(j.phase)).length);
  protected readonly done = computed(() => this.jobs().filter((j) => j.phase === 'done').length);
  protected readonly failed = computed(() => this.jobs().filter((j) => j.phase === 'failed').length);
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

  protected readonly rows = computed<TrayRow[]>(() =>
    this.jobs().map((job) => {
      switch (job.phase) {
        case 'presign':
          return { job, kind: 'active', status: 'Preparing upload…', showProgress: false, note: null, action: null };
        case 'uploading':
          return {
            job,
            kind: 'active',
            status: `Uploading · ${job.progress}%`,
            showProgress: true,
            note: 'Keep this tab open until the upload finishes.',
            action: null,
          };
        case 'saving':
          return { job, kind: 'active', status: 'Adding to your desk…', showProgress: false, note: null, action: null };
        case 'done':
          return { job, kind: 'done', status: 'Ready · on your desk in Write', showProgress: false, note: null, action: 'open' };
        case 'failed':
          return {
            job,
            kind: 'failed',
            status: 'Import didn’t finish',
            showProgress: false,
            note: job.error,
            action: 'retry',
          };
      }
    }),
  );
}
