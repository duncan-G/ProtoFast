import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { shortDate } from '../../loop/days';
import { messageOf } from '../../loop/errors';
import { LoopRuns } from '../../loop/loop-runs';
import { LoopStore } from '../../loop/loop-store';
import { LoopTray } from '../../loop/loop-tray';
import { SentFile } from '../../loop/model/sent-file';
import { Icon } from '../../shared/icon';

/** What the server can read, told by extension; it checks again. */
const ACCEPT = ['.pdf', '.docx', '.odt', '.rtf', '.txt', '.md'];
const MAX_BYTES = 10 * 1024 * 1024;

/** A resume file for the AI to read into the general resume: a new one, or one sent before. */
@Component({
  selector: 'app-resume-read',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <input
      #chooser
      type="file"
      class="sr-only"
      tabindex="-1"
      aria-hidden="true"
      [accept]="accept"
      (change)="chosen($event)"
    />
    <dialog
      #sheet
      class="sheet"
      aria-labelledby="resume-read-title"
      (close)="closed.emit()"
      (click)="closeOnBackdrop($event)"
    >
      <div class="sheet-body">
        <div class="flex items-start justify-between gap-4">
          <div class="flex flex-col gap-2">
            <h2 id="resume-read-title" class="m-0 text-[24px] font-semibold tracking-[-0.04em]">
              Read a resume
            </h2>
            <p class="m-0 text-[14.5px] text-[var(--color-muted)]">
              The AI reads a resume you have and lists what it says. You tick what goes into your
              resume here; nothing is added before.
            </p>
          </div>
          <button
            type="button"
            class="btn btn-icon -mt-1 -mr-3 border-0"
            aria-label="Close"
            (click)="close()"
          >
            <app-icon name="close" />
          </button>
        </div>

        <div
          class="sheet-step"
          (dragover)="dragOver($event)"
          (dragleave)="over.set(false)"
          (drop)="dropped($event)"
        >
          @if (upload(); as up) {
            <div class="flex flex-col gap-1" role="status">
              <span class="truncate text-[14px] text-[var(--color-text-2)]"
                >Uploading {{ up.fileName }}</span
              >
              <div class="sent-progress"><span [style.width.%]="up.fraction * 100"></span></div>
            </div>
          } @else {
            <button
              type="button"
              class="sent-drop"
              [class.is-over]="over()"
              [disabled]="busy()"
              (click)="choose()"
            >
              <app-icon name="upload" [size]="18" />
              <span class="font-medium">Upload a resume</span>
              <span class="meta"
                >or drop it here · PDF, Word (.docx), ODT, RTF, text · up to 10 MB</span
              >
            </button>
          }
        </div>

        @if (files().length) {
          <section class="sheet-step" aria-labelledby="resume-read-kept">
            <h3 id="resume-read-kept" class="caption">or one you’ve sent before</h3>
            <div class="flex flex-col gap-1">
              @for (file of files(); track file.id) {
                <button type="button" class="row-pick" [disabled]="busy()" (click)="start(file)">
                  <app-icon name="file" [size]="16" />
                  <span class="truncate font-medium">{{ file.name }}</span>
                  <span class="meta truncate"
                    >{{ file.fileName }} · {{ shortDate(file.uploadedOn) }}</span
                  >
                </button>
              }
            </div>
          </section>
        }

        @if (error()) {
          <p class="alert" role="alert">{{ error() }}</p>
        }
      </div>

      <div class="sheet-foot">
        <button type="button" class="btn" (click)="close()">Cancel</button>
        <span class="edit-note">It reads in the background; the AI tray shows how it’s going.</span>
      </div>
    </dialog>
  `,
})
export class ResumeReadSheet {
  private readonly store = inject(LoopStore);
  private readonly runs = inject(LoopRuns);
  private readonly tray = inject(LoopTray);
  private readonly sheet = viewChild.required<ElementRef<HTMLDialogElement>>('sheet');
  private readonly chooser = viewChild.required<ElementRef<HTMLInputElement>>('chooser');

  readonly closed = output<void>();

  protected readonly accept = ACCEPT.join(',');
  protected readonly shortDate = shortDate;
  protected readonly busy = signal(false);
  protected readonly over = signal(false);
  protected readonly error = signal('');
  protected readonly upload = signal<{ fileName: string; fraction: number } | null>(null);

  /** Resumes the server can read; an old .doc it can't. */
  protected readonly files = computed(() =>
    this.store
      .files()
      .filter(
        (f) => f.kind === 'resume' && ACCEPT.some((ext) => f.fileName.toLowerCase().endsWith(ext)),
      ),
  );

  constructor() {
    afterNextRender(() => this.sheet().nativeElement.showModal());
  }

  protected choose(): void {
    this.chooser().nativeElement.click();
  }

  protected chosen(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) {
      void this.send(file);
    }
  }

  protected dragOver(event: DragEvent): void {
    if (event.dataTransfer?.types.includes('Files')) {
      event.preventDefault();
      event.dataTransfer.dropEffect = 'copy';
      this.over.set(true);
    }
  }

  protected dropped(event: DragEvent): void {
    event.preventDefault();
    this.over.set(false);
    const file = event.dataTransfer?.files[0];
    if (file) {
      void this.send(file);
    }
  }

  /** Kept with the resumes sent, so it can go with an application too. */
  private async send(file: File): Promise<void> {
    if (!ACCEPT.some((ext) => file.name.toLowerCase().endsWith(ext))) {
      this.error.set('Send a PDF, Word (.docx), ODT, RTF, text or Markdown file.');
      return;
    }
    if (file.size > MAX_BYTES) {
      this.error.set('The file must be under 10 MB.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.upload.set({ fileName: file.name, fraction: 0 });
    try {
      const saved = await this.store.uploadFile('resume', file, '', (fraction) =>
        this.upload.set({ fileName: file.name, fraction }),
      );
      await this.start(saved);
    } catch (err) {
      this.error.set(messageOf(err, 'The file couldn’t be uploaded.'));
      this.busy.set(false);
    } finally {
      this.upload.set(null);
    }
  }

  protected async start(file: SentFile): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.runs.start(`Read ${file.name}`, { kind: 'readResume', documentId: file.id });
      this.close();
      this.tray.open();
    } catch (err) {
      this.error.set(messageOf(err, 'The AI couldn’t start reading it.'));
      this.busy.set(false);
    }
  }

  protected close(): void {
    this.sheet().nativeElement.close();
  }

  protected closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.sheet().nativeElement) {
      this.close();
    }
  }
}
