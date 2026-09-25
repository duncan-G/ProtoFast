import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { AcceptedFormats } from '../../documents/document-api';
import { ImportJob, ImportRejection } from '../../documents/document-import';
import { extensionLabel, formatBytes, formatLimit } from '../../documents/format';

/** A file that has been picked and checked locally, but not yet sent anywhere. */
export interface ImportDraft {
  file: File;
  rejection: ImportRejection | null;
}

/**
 * What the dialog is showing. Before a job exists it reflects the draft (nothing, a file that
 * passed, a file that was turned down); once one does, it follows the job's phase.
 */
type DialogState =
  | 'empty'
  | 'ready'
  | 'rejected'
  | 'presign'
  | 'uploading'
  | 'saving'
  | 'done'
  | 'failed';

const STEPS = [
  ['Prepare', 'Secure upload link'],
  ['Upload', 'Straight to storage'],
  ['Save', 'Onto your desk'],
] as const;

const STEP_INDEX: Record<DialogState, number> = {
  empty: 0,
  ready: 0,
  rejected: 0,
  presign: 1,
  uploading: 2,
  saving: 3,
  done: 4,
  failed: 0,
};

/**
 * The import dialog: pick a file, see it checked locally, start the import and watch the three
 * steps go by. It is a view over state the page owns — the draft and the job — so closing it
 * mid-upload loses nothing: the job carries on in the tray.
 *
 * The hidden file input lives here because the drop zone and the "choose a file" link both
 * open it; drag-and-drop lands on the same handler.
 */
@Component({
  selector: 'app-import-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'close.emit()' },
  template: `
    <div class="dialog-backdrop" (click)="close.emit()">
      <div
        class="dialog rise"
        role="dialog"
        aria-modal="true"
        aria-labelledby="import-title"
        (click)="$event.stopPropagation()"
      >
        <div class="flex items-start gap-4 px-6 pt-6">
          <div class="min-w-0 flex-1">
            <p class="section-label mb-0">Write · Import</p>
            <h2 id="import-title" class="mt-2.5 mb-1.5 text-[32px] tracking-[-0.01em]">
              Import a file
            </h2>
            <p class="m-0 text-muted text-[14px] leading-[1.5] [text-wrap:pretty]">
              It's added to your desk as soon as the upload finishes.
            </p>
          </div>
          <button type="button" class="icon-btn h-8 w-8 flex-none border border-[var(--color-divider)]" aria-label="Close" (click)="close.emit()">
            ×
          </button>
        </div>

        <ol class="mx-6 mt-[22px] mb-0 grid list-none grid-cols-3 gap-2.5 p-0">
          @for (step of steps(); track step.label) {
            <li class="step" [class.is-active]="step.active" [class.is-done]="step.done">
              <div class="step-bar"></div>
              <div class="flex items-center gap-1.5 font-[family-name:var(--font-mono)] text-[12px] font-medium">
                <span>{{ step.done ? '✓' : step.number }}</span><span>{{ step.label }}</span>
              </div>
              <div class="text-[12px] text-[var(--color-neutral-600)]">{{ step.sub }}</div>
            </li>
          }
        </ol>

        <div class="flex flex-col gap-3.5 px-6 pt-5 pb-6">
          <input
            #picker
            type="file"
            class="hidden"
            [accept]="formats()?.accept ?? ''"
            (change)="onPicked(picker)"
          />

          @if (state() === 'empty') {
            <div
              class="dropzone"
              [class.is-over]="dragging()"
              tabindex="0"
              role="button"
              aria-label="Choose a file to import"
              (click)="browse(picker)"
              (keydown.enter)="browse(picker)"
              (keydown.space)="browse(picker); $event.preventDefault()"
              (dragenter)="onDrag($event, true)"
              (dragover)="onDrag($event, true)"
              (dragleave)="onDrag($event, false)"
              (drop)="onDrop($event)"
            >
              <div class="flex h-11 w-11 items-center justify-center rounded-full border border-[color-mix(in_srgb,var(--color-text)_20%,transparent)] text-[18px] text-[var(--color-accent-bright)]">
                ↑
              </div>
              <div class="font-[family-name:var(--font-heading)] text-[22px] leading-[1.2]">Drop a file here</div>
              <div class="text-[13px] text-muted">
                or
                <span class="text-[var(--color-accent-bright)] underline underline-offset-[3px]">choose a file</span>
              </div>
              @if (formats(); as f) {
                <div class="mt-1 flex flex-wrap justify-center gap-1.5">
                  @for (label of f.labels; track label) {
                    <span class="chip">{{ label }}</span>
                  }
                </div>
                <div class="meta text-[var(--color-neutral-600)]">Up to {{ limit() }} · one file at a time</div>
              } @else if (formatsError()) {
                <div class="meta text-[var(--color-danger-300)]">{{ formatsError() }}</div>
              } @else {
                <div class="meta flex items-center gap-2 text-[var(--color-neutral-600)]">
                  <span class="spinner" aria-hidden="true"></span>Loading accepted types…
                </div>
              }
            </div>
          }

          @if (state() !== 'empty') {
            <div class="file-card" [class.is-error]="state() === 'rejected' || state() === 'failed'">
              <div class="file-ext">{{ extension() }}</div>
              <div class="min-w-0 flex-1">
                <div class="truncate text-[14px] font-medium">{{ fileName() }}</div>
                <div
                  class="meta mt-1"
                  [class.text-[var(--color-danger-300)]]="state() === 'rejected'"
                >
                  {{ fileMeta() }}
                </div>
              </div>
              @if (state() === 'ready' || state() === 'rejected') {
                <button type="button" class="btn btn-ghost text-[13px] text-[var(--color-neutral-400)]" (click)="remove.emit()">
                  Remove
                </button>
              }
            </div>
          }

          @if (state() === 'ready') {
            @if (formats()) {
              <div class="flex flex-wrap gap-[18px] text-[13px] text-[var(--color-success)]">
                <span>✓ Supported format</span><span>✓ Under {{ limit() }}</span>
              </div>
            } @else {
              <!-- The accepted-format table never arrived, so nothing was checked here; the API
                   checks type and size before it signs anything. -->
              <div class="text-[13px] text-muted">Type and size are checked when you start.</div>
            }
          }

          @if (state() === 'rejected') {
            <div class="panel-danger" role="alert">
              <div class="text-[14px] font-medium text-[var(--color-danger-200)]">{{ rejectionTitle() }}</div>
              <div class="mt-1 text-[13px] leading-[1.5] text-[var(--color-neutral-300)] [text-wrap:pretty]">
                {{ rejectionBody() }}
              </div>
            </div>
          }

          @if (state() === 'presign' || state() === 'saving') {
            <div class="flex flex-col gap-2" role="status">
              <div class="progress"><div class="progress-indeterminate"></div></div>
              <div class="meta flex justify-between">
                <span>{{ state() === 'presign' ? 'Requesting a secure upload link…' : 'Adding it to your desk…' }}</span>
                <span>Step {{ state() === 'presign' ? 1 : 3 }} of 3</span>
              </div>
            </div>
          }

          @if (state() === 'uploading') {
            <div class="flex flex-col gap-2" role="status">
              <div class="progress">
                <div class="progress-bar" [style.width.%]="job()?.progress ?? 0"></div>
              </div>
              <div class="meta flex justify-between">
                <span>{{ uploadText() }}</span><span>{{ job()?.progress ?? 0 }}%</span>
              </div>
              <div class="text-[12px] text-[var(--color-neutral-600)]">
                Keep this tab open until the upload finishes. You can close this window.
              </div>
            </div>
          }

          @if (state() === 'done') {
            <div class="panel-success rise flex items-start gap-3.5" role="status">
              <div class="check check-lg">✓</div>
              <div class="flex flex-col gap-1.5">
                <div class="font-[family-name:var(--font-heading)] text-[22px] leading-[1.15]">On your desk</div>
                <div class="text-[13px] leading-[1.55] text-[var(--color-neutral-300)] [text-wrap:pretty]">
                  “{{ job()?.document?.name }}” is uploaded and listed in
                  <b class="font-medium text-[var(--color-text)]">Write</b>.
                </div>
              </div>
            </div>
          }

          @if (state() === 'failed') {
            <div class="panel-danger" role="alert">
              <div class="text-[14px] font-medium text-[var(--color-danger-200)]">The import didn’t finish</div>
              <div class="mt-1 text-[13px] leading-[1.5] text-[var(--color-neutral-300)] [text-wrap:pretty]">
                {{ job()?.error }}
              </div>
            </div>
          }
        </div>

        <div class="dialog-foot">
          <div class="meta min-w-0 flex-1 text-[var(--color-neutral-600)]">{{ footNote() }}</div>
          @switch (state()) {
            @case ('empty') {
              <button type="button" class="btn btn-secondary" (click)="close.emit()">Cancel</button>
            }
            @case ('ready') {
              <button type="button" class="btn btn-secondary" (click)="close.emit()">Cancel</button>
              <button type="button" class="btn btn-primary" (click)="start.emit()">Start import</button>
            }
            @case ('rejected') {
              <button type="button" class="btn btn-secondary" (click)="close.emit()">Cancel</button>
              <button type="button" class="btn btn-primary" (click)="browse(picker)">Choose another file</button>
            }
            @case ('done') {
              <button type="button" class="btn btn-secondary" (click)="importAnother(picker)">Import another</button>
              <button type="button" class="btn btn-primary" (click)="close.emit()">Continue working</button>
            }
            @case ('failed') {
              <button type="button" class="btn btn-secondary" (click)="cancel.emit()">Discard</button>
              <button type="button" class="btn btn-primary" (click)="retry.emit()">Try again</button>
            }
            @default {
              <button type="button" class="btn btn-secondary" (click)="cancel.emit()">Cancel import</button>
              <button type="button" class="btn btn-primary" (click)="close.emit()">Continue in background</button>
            }
          }
        </div>
      </div>
    </div>
  `,
})
export class ImportDialog {
  readonly draft = input<ImportDraft | null>(null);
  readonly job = input<ImportJob | null>(null);
  readonly formats = input<AcceptedFormats | null>(null);
  readonly formatsError = input<string | null>(null);

  /** A file was chosen or dropped; the page checks it and sets the draft. */
  readonly pick = output<File>();
  /** The draft passed and the user asked for the import to begin. */
  readonly start = output<void>();
  /** Drop the draft and go back to the empty state. */
  readonly remove = output<void>();
  /** Close the dialog; a live import keeps going in the tray. */
  readonly close = output<void>();
  /** Stop the live import (or discard a failed one) and close. */
  readonly cancel = output<void>();
  /** Run a failed import again. */
  readonly retry = output<void>();

  private readonly picker = viewChild.required<ElementRef<HTMLInputElement>>('picker');

  protected readonly dragging = signal(false);

  protected readonly state = computed<DialogState>(() => {
    const job = this.job();
    if (job) {
      return job.phase;
    }
    const draft = this.draft();
    if (!draft) {
      return 'empty';
    }
    return draft.rejection ? 'rejected' : 'ready';
  });

  protected readonly fileName = computed(() => this.job()?.fileName ?? this.draft()?.file.name ?? '');
  protected readonly fileSize = computed(() => this.job()?.sizeBytes ?? this.draft()?.file.size ?? 0);
  protected readonly extension = computed(() => extensionLabel(this.fileName()));
  protected readonly limit = computed(() => formatLimit(this.formats()?.maxBytes ?? 10 * 1024 * 1024));

  protected readonly steps = computed(() => {
    const current = STEP_INDEX[this.state()];
    return STEPS.map(([label, sub], i) => ({
      label,
      sub,
      number: i + 1,
      done: current > i + 1,
      active: current === i + 1,
    }));
  });

  protected readonly fileMeta = computed(() => {
    const size = formatBytes(this.fileSize());
    switch (this.state()) {
      case 'rejected':
        return this.draft()?.rejection === 'size'
          ? `${size} · over the ${this.limit()} limit`
          : `${size} · unsupported type`;
      case 'saving':
      case 'done':
        return `${size} · uploaded`;
      default:
        return size;
    }
  });

  protected readonly uploadText = computed(() => {
    const job = this.job();
    if (!job) {
      return 'Uploading…';
    }
    return `${formatBytes(job.loadedBytes)} of ${formatBytes(job.sizeBytes)}`;
  });

  protected readonly rejectionTitle = computed(() =>
    this.draft()?.rejection === 'size'
      ? `This file is over ${this.limit()}`
      : 'This file type isn’t supported',
  );

  protected readonly rejectionBody = computed(() => {
    const draft = this.draft();
    if (!draft) {
      return '';
    }
    if (draft.rejection === 'size') {
      return `“${draft.file.name}” is ${formatBytes(draft.file.size)}. Reduce the file size or split it into smaller files, then import each.`;
    }
    const ext = this.extension().toLowerCase();
    const accepted = this.formats()?.labels.join(', ') ?? 'the listed types';
    return `.${ext} files can’t be imported. Export as one of the accepted types and try again — accepted: ${accepted}.`;
  });

  protected readonly footNote = computed(() => {
    switch (this.state()) {
      case 'empty':
        return '';
      case 'ready':
        return '';
      case 'presign':
      case 'uploading':
      case 'saving':
        return 'Closing won’t stop the import';
      case 'done':
        return '';
      default:
        return '';
    }
  });

  protected browse(picker: HTMLInputElement): void {
    picker.click();
  }

  protected importAnother(picker: HTMLInputElement): void {
    this.remove.emit();
    picker.click();
  }

  protected onPicked(picker: HTMLInputElement): void {
    const file = picker.files?.[0];
    // Reset so picking the same file again still fires `change`.
    picker.value = '';
    if (file) {
      this.pick.emit(file);
    }
  }

  protected onDrag(event: DragEvent, over: boolean): void {
    event.preventDefault();
    this.dragging.set(over);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    const file = event.dataTransfer?.files?.[0];
    if (file) {
      this.pick.emit(file);
    }
  }
}
