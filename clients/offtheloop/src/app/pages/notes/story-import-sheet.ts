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
import { AgentPrompt } from '../../shared/agent-prompt';
import { AgentFile, AgentUpload } from '../../shared/agent-upload';
import { Icon } from '../../shared/icon';

const titleKey = (title: string) => title.trim().toLowerCase();

/** Stories the user's own agent drew from their git history, ticked to keep; nothing saves before. */
@Component({
  selector: 'app-story-import',
  imports: [AgentPrompt, AgentUpload, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
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
    <dialog
      #sheet
      class="sheet"
      aria-labelledby="story-import-title"
      (close)="closed.emit()"
      (click)="closeOnBackdrop($event)"
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

        <section class="sheet-step" aria-labelledby="story-step-prompt">
          <h3 id="story-step-prompt" class="caption">1 · give your agent the prompt</h3>
          <p>
            Start Claude Code, or another coding agent, in the folder that holds your repositories
            and paste the prompt in. It may ask you what came of the work.
          </p>
          <app-agent-prompt [prompt]="prompt" />
        </section>

        <section class="sheet-step" aria-labelledby="story-step-file">
          <h3 id="story-step-file" class="caption">2 · upload the file it writes</h3>
          <app-agent-upload
            [fileName]="fileName"
            example='{ "format": "offtheloop-stories/1", "stories": [ … ] }'
            [summary]="summary()"
            (read)="take($event)"
          />
          @if (fileError(); as error) {
            <p class="alert" role="alert">{{ error }}</p>
          }
        </section>

        @if (rows().length) {
          <section class="sheet-step" aria-labelledby="story-step-pick">
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

  readonly closed = output<void>();

  protected readonly prompt = storyPrompt();
  protected readonly fileName = STORY_FILE;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
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
    const titles = new Set(this.store.stories().map((n) => titleKey(n.title)));
    return read.stories.map((story) => ({
      ...story,
      duplicate: titles.has(titleKey(story.title)),
    }));
  });

  protected readonly summary = computed(() => {
    const count = this.rows().length;
    return count ? `${count} ${count === 1 ? 'story' : 'stories'}` : '';
  });

  constructor() {
    afterNextRender(() => this.sheet().nativeElement.showModal());
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
      void this.router.navigate(['/app/resume'], {
        queryParams: { view: 'stories', id: first?.id ?? null },
      });
    } catch (err) {
      this.error.set(messageOf(err, 'The stories could not be imported.'));
    } finally {
      this.busy.set(false);
    }
  }

  /** Ticks every story that can be saved and isn't there already. */
  protected take(file: AgentFile): void {
    this.read.set(!file ? null : 'error' in file ? { error: file.error } : readStories(file.text));
    this.error.set('');
    this.kept.set(
      new Set(this.rows().flatMap((row) => (row.note && !row.duplicate ? [row.number] : []))),
    );
  }
}
