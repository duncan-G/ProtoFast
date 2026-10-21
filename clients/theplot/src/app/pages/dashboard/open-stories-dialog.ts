import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { coverStripe, relativeTime } from '../../documents/format';
import { OpenStory } from '../../stories/model/open-story';

/**
 * The open stories a writer can copy onto their desk. A view over state the page owns: the list,
 * which one is importing, and what went wrong.
 */
@Component({
  selector: 'app-open-stories-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'close.emit()' },
  template: `
    <div class="dialog-backdrop" (click)="close.emit()">
      <div
        class="dialog rise"
        role="dialog"
        aria-modal="true"
        aria-labelledby="open-stories-title"
        (click)="$event.stopPropagation()"
      >
        <div class="flex items-start gap-4 px-6 pt-6">
          <div class="min-w-0 flex-1">
            <p class="section-label mb-0">Write · Open stories</p>
            <h2 id="open-stories-title" class="mt-2.5 mb-1.5 text-[32px] tracking-[-0.01em]">
              Open stories
            </h2>
            <p class="m-0 text-muted text-[14px] leading-[1.5] [text-wrap:pretty]">
              Screenplays ThePlot offers every writer. Import one and a copy lands on your desk to
              read and rework as your own; the original stays as it is.
            </p>
          </div>
          <button
            type="button"
            class="icon-btn h-8 w-8 flex-none border border-[var(--color-divider)]"
            aria-label="Close"
            (click)="close.emit()"
          >
            ×
          </button>
        </div>

        <div class="flex flex-col gap-2.5 px-6 pt-5 pb-6">
          @if (error()) {
            <div class="panel-danger flex flex-wrap items-center gap-3" role="alert">
              <p class="m-0 min-w-0 flex-1 text-[13px]">{{ error() }}</p>
            </div>
          }

          @for (story of stories(); track story.id) {
            <div class="file-card">
              <div class="desk-cover" [style.background]="cover(story.id)" aria-hidden="true"></div>
              <div class="flex min-w-0 flex-1 flex-col gap-1">
                <span class="font-[family-name:var(--font-heading)] text-[21px] leading-[1.1]">{{
                  story.title
                }}</span>
                @if (story.opening) {
                  <span class="text-muted text-[13px] leading-[1.4] [text-wrap:pretty]">{{
                    story.opening
                  }}</span>
                }
                <span class="meta text-[11px]">
                  {{ story.sceneCount }} {{ story.sceneCount === 1 ? 'scene' : 'scenes' }} ·
                  {{ story.characterCount }}
                  {{ story.characterCount === 1 ? 'character' : 'characters' }} ·
                  {{ published(story) }}
                </span>
              </div>
              @if (story.importedStoryId; as copyId) {
                <div class="flex flex-none flex-col items-end gap-1.5">
                  <span class="tag tag-imported">On your desk</span>
                  <button
                    type="button"
                    class="btn btn-secondary px-2.5 py-1.5 text-[12px]"
                    (click)="open.emit(copyId)"
                  >
                    Open
                  </button>
                </div>
              } @else {
                <button
                  type="button"
                  class="btn btn-primary flex-none px-3 py-1.5 text-[13px]"
                  [disabled]="importingId() !== null"
                  [attr.aria-label]="'Import “' + story.title + '”'"
                  (click)="import.emit(story.id)"
                >
                  @if (importingId() === story.id) {
                    <span class="spinner" aria-hidden="true"></span>Importing…
                  } @else {
                    Import
                  }
                </button>
              }
            </div>
          } @empty {
            <p class="meta m-0">There are no open stories yet.</p>
          }
        </div>
      </div>
    </div>
  `,
})
export class OpenStoriesDialog {
  readonly stories = input.required<OpenStory[]>();
  /** The open story being copied, which holds every Import button until it lands. */
  readonly importingId = input<string | null>(null);
  readonly error = input('');

  readonly import = output<string>();
  /** The writer's copy to open. */
  readonly open = output<string>();
  readonly close = output<void>();

  protected cover(id: string): string {
    return coverStripe(id);
  }

  protected published(story: OpenStory): string {
    return `Added ${relativeTime(story.publishedAt).toLowerCase()}`;
  }
}
