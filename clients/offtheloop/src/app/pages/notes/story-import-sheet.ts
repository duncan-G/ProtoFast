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
import { Router } from '@angular/router';
import { messageOf } from '../../loop/errors';
import { LoopStore } from '../../loop/loop-store';
import { readStories, STORY_FILE, storyPrompt, StoriesRead } from '../../loop/story-import';
import { Icon } from '../../shared/icon';

const MAX_FILE_BYTES = 2 * 1024 * 1024;

const titleKey = (title: string) => title.trim().toLowerCase();

/** Stories the user's own agent drew from their git history, ticked to keep; nothing saves before. */
@Component({
  selector: 'app-story-import',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .step {
      display: flex;
      flex-direction: column;
      gap: 10px;
    }

    .step > p {
      margin: 0;
      font-size: 14.5px;
      color: var(--color-muted);
    }

    .story-prompt {
      max-height: 240px;
      margin: 0;
      padding: 14px 16px;
      overflow: auto;
      border: 1px solid var(--color-line-2);
      border-radius: 12px;
      background: var(--color-bg);
      color: var(--color-text-2);
      font-family: var(--font-mono);
      font-size: 12px;
      line-height: 1.55;
      white-space: pre-wrap;
    }

    .story-pick {
      grid-template-columns: 20px minmax(0, 1fr);
      align-items: start;
      padding: 9px 0;
    }

    .story-pick .finding-check {
      margin-top: 3px;
      align-self: start;
    }

    .story-pick:has(.finding-check:disabled) {
      cursor: default;
    }

    .story-flag {
      font-size: 13px;
      color: var(--color-amber);
    }

    .story-problem {
      font-size: 13px;
      color: var(--color-danger);
    }
  `,
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
    <dialog
      #sheet
      class="sheet"
      aria-labelledby="story-import-title"
      (close)="closed.emit()"
      (click)="closeOnBackdrop($event)"
      (dragover)="dragOver($event)"
      (dragleave)="over.set(false)"
      (drop)="dropped($event)"
    >
      <div class="sheet-body">
        <div class="flex items-start justify-between gap-4">
          <div class="flex flex-col gap-2">
            <h2 id="story-import-title" class="m-0 text-[24px] font-semibold tracking-[-0.04em]">
              Import stories
            </h2>
            <p class="m-0 text-[14.5px] text-[var(--color-muted)]">
              Your own AI agent reads your git history on your computer and writes interview
              stories, each backed by your commits. Only the file it writes comes here.
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

        <section class="step" aria-labelledby="story-step-prompt">
          <h3 id="story-step-prompt" class="caption">1 · give your agent the prompt</h3>
          <p>
            Start Claude Code, or another coding agent, in the folder that holds your repositories
            and paste the prompt in. It may ask you what came of the work.
          </p>
          <div class="flex flex-wrap items-center gap-4">
            <button type="button" class="btn btn-sm" (click)="copy()">
              {{ copied() ? 'Copied' : 'Copy prompt' }}
            </button>
            <button
              type="button"
              class="btn-link"
              [attr.aria-expanded]="showPrompt()"
              aria-controls="story-prompt"
              (click)="togglePrompt()"
            >
              {{ showPrompt() ? 'hide it' : 'read it first' }}
            </button>
          </div>
          @if (copyFailed()) {
            <p class="alert" role="alert">
              Your browser wouldn’t copy it. Select it below instead.
            </p>
          }
          @if (showPrompt()) {
            <pre id="story-prompt" #promptText class="story-prompt" tabindex="0">{{ prompt }}</pre>
          }
        </section>

        <section class="step" aria-labelledby="story-step-file">
          <h3 id="story-step-file" class="caption">2 · upload the file it writes</h3>
          @if (rows().length) {
            <p class="flex flex-wrap items-baseline gap-x-3">
              <span class="mono text-[13px] text-[var(--color-text-2)]">{{ source() }}</span>
              <span class="meta"
                >{{ rows().length }} {{ rows().length === 1 ? 'story' : 'stories' }}</span
              >
              <button type="button" class="btn-link" (click)="choose()">choose another</button>
            </p>
          } @else {
            <button type="button" class="sent-drop" [class.is-over]="over()" (click)="choose()">
              <app-icon name="upload" [size]="18" />
              <span class="font-medium">Choose {{ fileName }}</span>
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
                  placeholder='{ "format": "offtheloop-stories/1", "stories": [ … ] }'
                  (input)="pasted($any($event.target).value)"
                ></textarea>
              </label>
            } @else {
              <button type="button" class="btn-link self-start" (click)="paste()">
                or paste the JSON
              </button>
            }
          }
          @if (fileError(); as error) {
            <p class="alert" role="alert">{{ error }}</p>
          }
        </section>

        @if (rows().length) {
          <section class="step" aria-labelledby="story-step-pick">
            <h3 id="story-step-pick" class="caption">3 · tick the ones to keep</h3>
            <ul class="m-0 flex list-none flex-col p-0">
              @for (row of rows(); track row.number) {
                <li>
                  <label class="finding story-pick">
                    <input
                      type="checkbox"
                      class="finding-check"
                      [checked]="kept().has(row.number)"
                      [disabled]="!row.note"
                      (change)="toggle(row.number)"
                    />
                    <span class="finding-value">
                      <span class="font-medium">{{ row.title }}</span>
                      <span class="meta">
                        {{ row.quality || 'no quality' }} · {{ row.commits }}
                        {{ row.commits === 1 ? 'commit' : 'commits' }}
                        @if (row.repos.length) {
                          · {{ row.repos.join(', ') }}
                        }
                      </span>
                      @if (row.duplicate) {
                        <span class="story-flag">You have a story with this title already.</span>
                      }
                      @for (problem of row.problems; track $index) {
                        <span class="story-problem">{{ problem }}</span>
                      }
                    </span>
                  </label>
                </li>
              }
            </ul>
          </section>
        }

        @if (error()) {
          <p class="alert" role="alert">{{ error() }}</p>
        }
      </div>

      <div class="sheet-foot">
        <button
          type="button"
          class="btn btn-primary"
          [disabled]="!kept().size || busy()"
          (click)="save()"
        >
          @if (busy()) {
            <span class="spinner" aria-hidden="true"></span>
          }
          Import {{ kept().size || '' }} {{ kept().size === 1 ? 'story' : 'stories' }}
        </button>
        <button type="button" class="btn" (click)="close()">Cancel</button>
        @if (rows().length) {
          <span class="flex-1"></span>
          <span class="mono text-[11.5px] text-[var(--color-dim)]"
            >{{ kept().size }} of {{ rows().length }} ticked</span
          >
        }
      </div>
    </dialog>
  `,
})
export class StoryImportSheet {
  private readonly store = inject(LoopStore);
  private readonly router = inject(Router);
  private readonly sheet = viewChild.required<ElementRef<HTMLDialogElement>>('sheet');
  private readonly chooser = viewChild.required<ElementRef<HTMLInputElement>>('chooser');
  private readonly promptText = viewChild<ElementRef<HTMLElement>>('promptText');
  private readonly pasteBox = viewChild<ElementRef<HTMLTextAreaElement>>('pasteBox');

  readonly closed = output<void>();

  protected readonly prompt = storyPrompt();
  protected readonly fileName = STORY_FILE;
  protected readonly copied = signal(false);
  protected readonly copyFailed = signal(false);
  protected readonly showPrompt = signal(false);
  protected readonly pasting = signal(false);
  protected readonly over = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  /** The file's name, or "pasted". */
  protected readonly source = signal('');
  private readonly read = signal<StoriesRead | null>(null);
  protected readonly kept = signal<ReadonlySet<number>>(new Set());

  protected readonly fileError = computed(() => {
    const read = this.read();
    return read && 'error' in read ? read.error : '';
  });

  protected readonly rows = computed(() => {
    const read = this.read();
    if (!read || 'error' in read) {
      return [];
    }
    const titles = new Set(
      this.store
        .notes()
        .filter((n) => n.kind === 'story')
        .map((n) => titleKey(n.title)),
    );
    return read.stories.map((story) => ({
      ...story,
      duplicate: titles.has(titleKey(story.title)),
    }));
  });

  constructor() {
    afterNextRender(() => this.sheet().nativeElement.showModal());
  }

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.prompt);
      this.copyFailed.set(false);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      this.copyFailed.set(true);
      this.showPrompt.set(true);
      setTimeout(() => {
        const text = this.promptText()?.nativeElement;
        if (text) {
          getSelection()?.selectAllChildren(text);
          text.focus();
        }
      });
    }
  }

  protected togglePrompt(): void {
    this.showPrompt.set(!this.showPrompt());
    this.copyFailed.set(false);
  }

  protected choose(): void {
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
    this.take('pasted', text.trim() ? readStories(text) : null);
  }

  protected toggle(number: number): void {
    const kept = new Set(this.kept());
    if (!kept.delete(number)) {
      kept.add(number);
    }
    this.kept.set(kept);
  }

  protected close(): void {
    this.sheet().nativeElement.close();
  }

  protected closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.sheet().nativeElement) {
      this.close();
    }
  }

  protected async save(): Promise<void> {
    const kept = this.kept();
    const notes = this.rows().flatMap((row) =>
      row.note && kept.has(row.number) ? [row.note] : [],
    );
    if (!notes.length || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      const [first] = await this.store.importStories(notes);
      this.close();
      void this.router.navigate(['/app/notes'], {
        queryParams: { kind: 'story', id: first?.id ?? null },
      });
    } catch (err) {
      this.error.set(messageOf(err, 'The stories could not be imported.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(file: File): Promise<void> {
    this.take(
      file.name,
      file.size > MAX_FILE_BYTES
        ? { error: 'That file is too big to be a stories file.' }
        : readStories(await file.text()),
    );
  }

  /** Ticks every story that can be saved and isn't there already. */
  private take(source: string, read: StoriesRead | null): void {
    this.source.set(source);
    this.read.set(read);
    this.error.set('');
    this.kept.set(
      new Set(this.rows().flatMap((row) => (row.note && !row.duplicate ? [row.number] : []))),
    );
  }
}
