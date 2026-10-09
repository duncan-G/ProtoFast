import { NgTemplateOutlet } from '@angular/common';
import {
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
} from '@angular/core';
import { FrameSpan, spanAt } from '../../../direction/frames';
import { isSpoken } from '../../../direction/direction-changes';
import { SceneElement } from '../../../stories/model/scene-element';
import { ImageState } from '../image-state';
import { slugline, transitionText } from '../format';
import { SceneEditorStore } from '../scene-editor-store';

interface FrameCard {
  /** The first element's id, which is also the frame's key. */
  key: string;
  /** Null for a run of rows no frame covers. */
  span: FrameSpan | null;
  first: number;
  number: number;
  elements: SceneElement[];
}

/**
 * The scene as its frames: each picture (black until there is one) with its lines written on it.
 * While a frame plays its lines turn into a caption; while the scene plays, one frame fills the
 * view at a time and the pictures follow the voice.
 */
@Component({
  selector: 'app-director-frames',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet],
  host: { class: 'dr-scroll dr-frames', '[class.is-staging]': 'staging()' },
  template: `
    @if (staging()) {
      <div class="dr-stage">
        @for (card of cards(); track card.key) {
          @if (card.span?.frame?.image; as image) {
            <img
              class="dr-stage-image"
              [class.is-current]="card === current()"
              [src]="image.url"
              alt=""
            />
          }
        }
        @if (current(); as card) {
          <span class="dr-stage-count">
            {{ card.span ? 'Frame ' + card.number + ' / ' + frameCount() : 'No frame' }}
          </span>
        }
        @if (playing(); as line) {
          <ng-container
            [ngTemplateOutlet]="caption"
            [ngTemplateOutletContext]="{ $implicit: line }"
          />
        }
      </div>
    } @else {
      <div class="dr-reel">
        @for (card of cards(); track card.key) {
          @let image = card.span?.frame?.image ?? null;
          @let state = imageOf(card);
          @let captioning = playingIn(card);
          <div
            class="dr-shot"
            role="button"
            tabindex="0"
            [id]="'frame-' + card.key"
            [class.is-on]="isOn(card)"
            [class.is-loose]="!card.span"
            [class.is-captioning]="captioning"
            [attr.aria-label]="card.span ? 'Frame ' + card.number : 'Rows with no frame'"
            [title]="card.span ? '' : 'No frame pictures these rows — click to add one'"
            (click)="pick(card)"
            (keydown.enter)="pick(card)"
          >
            @if (image) {
              <img class="dr-shot-image" [src]="image.url" alt="" loading="lazy" />
            }
            <div class="dr-shot-top">
              @if (card.span) {
                <span class="dr-badge is-big">{{ card.number }}</span>
              } @else {
                <span class="dr-badge is-loose" aria-hidden="true">+</span>
              }
              @if (state === 'generating') {
                <span class="dr-shot-chip is-working">Drawing…</span>
              } @else if (state === 'stale') {
                <span class="dr-shot-chip is-warn">Out of date</span>
              }
              <span class="flex-1"></span>
              @if (playable(card)) {
                <button
                  type="button"
                  class="dr-shot-play"
                  [attr.aria-label]="playingCard(card) ? 'Stop' : 'Play this frame'"
                  [title]="playingCard(card) ? 'Stop' : 'Play this frame'"
                  (click)="$event.stopPropagation(); play(card)"
                >
                  {{ playingCard(card) ? '❚❚' : '▶' }}
                </button>
              }
            </div>
            @if (captioning) {
              <ng-container
                [ngTemplateOutlet]="caption"
                [ngTemplateOutletContext]="{ $implicit: playing() }"
              />
            } @else {
              <div class="dr-shot-text">
                @for (element of card.elements; track element.id) {
                  @switch (element.type) {
                    @case ('Heading') {
                      <p class="dr-shot-slug">{{ heading(element) }}</p>
                    }
                    @case ('Transition') {
                      <p class="dr-shot-transition">{{ transition(element) }}</p>
                    }
                    @case ('Dialogue') {
                      <p class="dr-shot-dialogue">
                        <span class="dr-shot-speaker">{{ speakerName(element) }}</span>
                        @if (element.parenthetical) {
                          <span class="dr-shot-paren">({{ element.parenthetical }})</span>
                        }
                        <span>{{ plain(element.text) }}</span>
                      </p>
                    }
                    @default {
                      <p
                        class="dr-shot-action"
                        [class.is-description]="element.type === 'Description'"
                      >
                        {{ plain(element.text) }}
                      </p>
                    }
                  }
                }
              </div>
            }
          </div>
        } @empty {
          <div class="dr-empty"><p>No lines in this scene yet.</p></div>
        }
      </div>
    }

    <ng-template #caption let-line>
      <p class="dr-caption" [class.is-narrated]="line.type !== 'Dialogue'">
        @if (line.type === 'Dialogue') {
          <span class="dr-caption-speaker">{{ speakerName(line) }}</span>
        }
        <span class="dr-caption-text">{{ plain(line.text) }}</span>
      </p>
    </ng-template>
  `,
})
export class DirectorFrames {
  protected readonly store = inject(SceneEditorStore);

  protected readonly cards = computed<FrameCard[]>(() => {
    const spans = this.store.frameSpans();
    const elements = this.store.elements();
    const cards: FrameCard[] = [];
    for (let i = 0; i < elements.length; ) {
      const span = spanAt(spans, i);
      const last = span ? span.last : this.looseEnd(spans, i, elements.length);
      cards.push({
        key: elements[i].id,
        span,
        first: i,
        number: span ? spans.indexOf(span) + 1 : 0,
        elements: elements.slice(i, last + 1),
      });
      i = last + 1;
    }
    return cards;
  });

  protected readonly frameCount = computed(() => this.store.frameSpans().length);

  protected readonly staging = computed(() => this.store.player.context() === 'scene');

  protected readonly playing = computed(() => {
    const id = this.store.player.playing();
    return id === null ? null : (this.store.elements().find((e) => e.id === id) ?? null);
  });

  /** The card whose line is playing; between lines it is still the last one's. */
  protected readonly current = computed(() => {
    const line = this.playing();
    return line ? (this.cards().find((c) => c.elements.includes(line)) ?? null) : null;
  });

  constructor() {
    // Back from the stage, the reel opens where the scene stopped.
    let staged: string | null = null;
    afterRenderEffect(() => {
      if (this.staging()) {
        staged = this.current()?.key ?? staged;
      } else if (staged !== null) {
        document.getElementById(`frame-${staged}`)?.scrollIntoView({ block: 'center' });
        staged = null;
      }
    });
  }

  protected isOn(card: FrameCard): boolean {
    return (
      card.span !== null &&
      (this.store.inspectorScope() === 'scene' || card.span === this.store.selectedFrame())
    );
  }

  protected pick(card: FrameCard): void {
    if (card.span) {
      this.store.selectFrame(card.key);
    } else {
      this.store.coverRow(card.first);
    }
  }

  protected imageOf(card: FrameCard): ImageState {
    return (card.span && this.store.frameImageStates().get(card.key)) || 'none';
  }

  protected playingIn(card: FrameCard): boolean {
    const line = this.playing();
    return line !== null && card.elements.includes(line);
  }

  protected playingCard(card: FrameCard): boolean {
    return this.store.player.context() === card.key;
  }

  protected playable(card: FrameCard): boolean {
    const cues = this.store.cueByElement();
    return card.elements.some((e) => isSpoken(e.type) && cues.get(e.id)?.audio);
  }

  protected play(card: FrameCard): void {
    if (this.playingCard(card)) {
      this.store.player.stop();
    } else {
      this.store.playLines(
        card.elements.filter((e) => isSpoken(e.type)).map((e) => e.id),
        card.key,
      );
    }
  }

  protected plain(text: string | null): string {
    return (text ?? '').replace(/@/g, '');
  }

  protected speakerName(element: SceneElement): string {
    const speaker = element.speakerId ? this.store.characterById().get(element.speakerId) : null;
    return speaker?.name?.toUpperCase() ?? 'UNKNOWN';
  }

  protected heading(element: SceneElement): string {
    const location = element.locationId ? this.store.locationById().get(element.locationId) : null;
    const slug = location ? slugline(location) : 'NO LOCATION';
    return element.timeOfDay ? `${slug} — ${element.timeOfDay.toUpperCase()}` : slug;
  }

  protected transition(element: SceneElement): string {
    return transitionText(element.transition ?? '');
  }

  /** The last index of the run of uncovered rows starting at `from`. */
  private looseEnd(spans: readonly FrameSpan[], from: number, count: number): number {
    let last = from;
    while (last + 1 < count && !spanAt(spans, last + 1)) {
      last++;
    }
    return last;
  }
}
