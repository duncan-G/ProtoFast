import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { Icon } from './icon';

const MAX_FILE_BYTES = 2 * 1024 * 1024;

/** What came in: the file's name or "pasted", and its text, or why it couldn't be taken. */
export type AgentFile = { source: string; text: string } | { source: string; error: string } | null;

/** The file the user's agent wrote: chosen, dropped, or its text pasted. */
@Component({
  selector: 'app-agent-upload',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'flex flex-col gap-2.5',
    '(dragover)': 'dragOver($event)',
    '(dragleave)': 'over.set(false)',
    '(drop)': 'dropped($event)',
  },
  template: `
    <input
      #chooser
      type="file"
      class="sr-only"
      tabindex="-1"
      aria-hidden="true"
      accept=".json,application/json,text/plain"
      (change)="chosen($event)"
    />
    @if (summary()) {
      <p class="m-0 flex flex-wrap items-baseline gap-x-3 text-[14.5px]">
        <span class="mono text-[13px] text-[var(--color-text-2)]">{{ source() }}</span>
        <span class="meta">{{ summary() }}</span>
        <button type="button" class="btn-link" (click)="choose()">choose another</button>
      </p>
    } @else {
      <button type="button" class="sent-drop" [class.is-over]="over()" (click)="choose()">
        <app-icon name="upload" [size]="18" />
        <span class="font-medium">Choose {{ fileName() }}</span>
        <span class="meta">or drop it here</span>
      </button>
      @if (pasting()) {
        <label class="field">
          <span>or paste what it wrote</span>
          <textarea
            #pasteBox
            class="input mono"
            rows="6"
            spellcheck="false"
            [placeholder]="example()"
            (input)="pasted($any($event.target).value)"
          ></textarea>
        </label>
      } @else {
        <button type="button" class="btn-link self-start" (click)="paste()">
          or paste the JSON
        </button>
      }
    }
  `,
})
export class AgentUpload {
  private readonly chooser = viewChild.required<ElementRef<HTMLInputElement>>('chooser');
  private readonly pasteBox = viewChild<ElementRef<HTMLTextAreaElement>>('pasteBox');

  readonly fileName = input.required<string>();
  readonly example = input('');
  /** What was read, once something was; the drop zone gives way to it. */
  readonly summary = input('');
  readonly read = output<AgentFile>();

  protected readonly source = signal('');
  protected readonly pasting = signal(false);
  protected readonly over = signal(false);

  choose(): void {
    this.chooser().nativeElement.click();
  }

  protected chosen(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) {
      void this.load(file);
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
      void this.load(file);
    }
  }

  protected paste(): void {
    this.pasting.set(true);
    setTimeout(() => this.pasteBox()?.nativeElement.focus());
  }

  protected pasted(text: string): void {
    this.source.set('pasted');
    this.read.emit(text.trim() ? { source: 'pasted', text } : null);
  }

  private async load(file: File): Promise<void> {
    this.source.set(file.name);
    this.read.emit(
      file.size > MAX_FILE_BYTES
        ? { source: file.name, error: 'That file is too big to be one your agent wrote.' }
        : { source: file.name, text: await file.text() },
    );
  }
}
