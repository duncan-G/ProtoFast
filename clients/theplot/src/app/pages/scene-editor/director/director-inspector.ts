import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { overrideCount } from '../../../direction/inheritance';
import { FrameSpan } from '../../../direction/frames';
import { plural } from '../format';
import { ImageState } from '../image-state';
import { InspectorScope } from '../inspector-scope';
import { InspectorTab } from '../inspector-tab';
import { SceneEditorStore } from '../scene-editor-store';
import { InspectorCastTab } from './inspector-cast-tab';
import { InspectorLinesTab } from './inspector-lines-tab';
import { InspectorPlaceTab } from './inspector-place-tab';
import { InspectorShotTab } from './inspector-shot-tab';
import { PicturePreview } from './picture-preview';

const TABS: { tab: InspectorTab; label: string }[] = [
  { tab: 'shot', label: 'Shot' },
  { tab: 'cast', label: 'Cast & props' },
  { tab: 'place', label: 'Place' },
  { tab: 'lines', label: 'Lines' },
];

/** Edits the selected frame, or the defaults every frame of the scene follows. */
@Component({
  selector: 'app-director-inspector',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    InspectorCastTab,
    InspectorLinesTab,
    InspectorPlaceTab,
    InspectorShotTab,
    PicturePreview,
  ],
  host: { class: 'dr-inspector', 'aria-label': 'Inspector' },
  template: `
    <div class="dr-inspector-top">
      <div class="dr-scope" role="tablist" aria-label="Edit">
        @for (scope of scopes(); track scope.value) {
          <button
            type="button"
            role="tab"
            [attr.aria-selected]="store.inspectorScope() === scope.value"
            (click)="store.inspectorScope.set(scope.value)"
          >
            {{ scope.label }}
          </button>
        }
      </div>

      @if (scene()) {
        <div class="dr-inspector-title is-scene">
          <span class="dr-serif">All frames</span>
          <span class="dr-hint"
            >Scene defaults. Every frame follows these unless it overrides a field.</span
          >
        </div>
      } @else if (store.selectedFrame(); as span) {
        <div class="dr-inspector-title">
          <span class="dr-badge is-big">{{ store.selectedFrameNumber() }}</span>
          <span class="dr-serif">Frame {{ store.selectedFrameNumber() }}</span>
          <span class="dr-inspector-of"
            >of {{ store.frameSpans().length }} · {{ frameLines().length }} line{{
              frameLines().length === 1 ? '' : 's'
            }}</span
          >
          <span class="flex-1"></span>
          <button
            type="button"
            class="dr-lock"
            [class.is-locked]="span.frame.locked"
            [attr.aria-pressed]="span.frame.locked"
            [disabled]="frameDirecting(span) || frameImageState() === 'generating'"
            [title]="
              span.frame.locked
                ? 'Locked: directing and picturing leave this frame as it is. Click to unlock.'
                : 'Lock this frame once you are happy with it'
            "
            (click)="store.setFrameLocked(span.frame.firstElementId, !span.frame.locked)"
          >
            <svg class="dr-lock-icon" viewBox="0 0 16 16" aria-hidden="true">
              <path
                [attr.d]="span.frame.locked ? 'M5 7V5a3 3 0 0 1 6 0v2' : 'M5 7V5a3 3 0 0 1 5.8-1.1'"
              />
              <rect x="3" y="7" width="10" height="7" rx="1.5" />
            </svg>
            {{ span.frame.locked ? 'Locked' : 'Lock' }}
          </button>
          <button
            type="button"
            class="dr-step"
            aria-label="Previous frame"
            [disabled]="store.selectedFrameNumber() <= 1"
            (click)="store.stepFrame(-1)"
          >
            ‹
          </button>
          <button
            type="button"
            class="dr-step"
            aria-label="Next frame"
            [disabled]="store.selectedFrameNumber() >= store.frameSpans().length"
            (click)="store.stepFrame(1)"
          >
            ›
          </button>
        </div>
      }

      @if (scene()) {
        <div class="dr-media" [class.is-collapsed]="!summaryOpen()">
          <button
            type="button"
            class="dr-media-toggle"
            [attr.aria-expanded]="summaryOpen()"
            (click)="summaryOpen.set(!summaryOpen())"
          >
            <span class="dr-kicker">Generation</span>
            @if (!summaryOpen()) {
              <span class="dr-media-summary"
                >Images {{ imageCount('ready') }}/{{ store.frameSpans().length }} · Audio
                {{ readyLines(allLines()) }}/{{ allLines().length }}</span
              >
            }
            <span class="flex-1"></span>
            <span class="dr-chev" aria-hidden="true">{{ summaryOpen() ? '▴' : '▾' }}</span>
          </button>
          @if (summaryOpen()) {
            <div class="dr-thumbs">
              @for (span of store.frameSpans(); track span.frame.firstElementId; let i = $index) {
                @let state = store.frameImageStates().get(span.frame.firstElementId) ?? 'none';
                <button
                  type="button"
                  class="dr-thumb"
                  [class]="'is-' + state"
                  [title]="'Frame ' + (i + 1) + ' · ' + stateLabel(state)"
                  (click)="store.selectFrame(span.frame.firstElementId)"
                >
                  @if (span.frame.image; as image) {
                    <img [src]="image.url" alt="" />
                  }
                </button>
              }
            </div>
            @if (store.frameSpans().length > 0) {
              <div class="dr-legend">
                @if (imageCount('ready')) {
                  <span
                    ><span class="dr-legend-key is-ready"></span
                    >{{ imageCount('ready') }} ready</span
                  >
                }
                @if (imageCount('stale')) {
                  <span
                    ><span class="dr-legend-key is-stale"></span>{{ imageCount('stale') }} out of
                    date</span
                  >
                }
                @if (imageCount('none')) {
                  <span><span class="dr-legend-key"></span>{{ imageCount('none') }} missing</span>
                }
                @if (imageCount('generating')) {
                  <span
                    ><span class="dr-legend-key is-generating"></span
                    >{{ imageCount('generating') }} drawing</span
                  >
                }
              </div>
            }
            <div class="dr-media-row">
              <span class="dr-media-label">Images</span>
              <span class="flex-1"
                >{{ imageCount('ready') }} of
                {{ plural(store.frameSpans().length, 'frame') }} ready</span
              >
              <button
                type="button"
                class="dr-link"
                [title]="sceneImagesTitle"
                [disabled]="store.unlockedFrameIds().length === 0"
                (click)="pictureScene()"
              >
                {{ sceneImageAction() }}
              </button>
            </div>
            <div class="dr-media-row">
              <span class="dr-media-label">Audio</span>
              <span class="flex-1"
                >{{ readyLines(allLines()) }} of {{ plural(allLines().length, 'line') }} ready</span
              >
              <button
                type="button"
                class="dr-link"
                [disabled]="allLines().length === 0"
                (click)="generate(allLines())"
              >
                {{ audioAction(allLines()) }}
              </button>
            </div>
          }
        </div>
      } @else if (store.selectedFrame()) {
        <div class="dr-media is-frame">
          @if (store.selectedFrame()?.frame?.image; as image) {
            <app-picture-preview [url]="image.url" [alt]="'Frame ' + store.selectedFrameNumber()" />
          } @else {
            <div class="dr-picture">
              {{ frameImageState() === 'generating' ? 'Drawing…' : 'No image' }}
            </div>
          }
          <span class="dr-media-label">Image</span>
          <span
            class="dr-media-value"
            [class.is-warn]="frameImageState() === 'stale' && !frameLocked()"
            [title]="frameImageTitle()"
            >{{ stateLabel(frameImageState()) }}</span
          >
          @if (frameLocked()) {
            <span></span>
          } @else {
            <button
              type="button"
              class="dr-link"
              [class.is-done]="frameImageState() === 'ready'"
              [class.is-working]="frameImageState() === 'generating'"
              [disabled]="!store.selectedFrame()?.frame?.id || frameImageState() === 'generating'"
              [title]="
                !store.selectedFrame()?.frame?.id
                  ? 'Saving…'
                  : variationOf()
                    ? 'Edits frame ' + variationOf() + '’s picture with the change alone'
                    : 'Composed from the pictures that already exist around it'
              "
              (click)="pictureFrame()"
            >
              {{ frameImageAction() }}
            </button>
          }
          <span class="dr-media-label">Audio</span>
          <span
            class="dr-media-value"
            [class.is-warn]="readyLines(frameLines()) < frameLines().length"
            [title]="
              readyLines(frameLines()) + ' of ' + plural(frameLines().length, 'line') + ' ready'
            "
            >{{ readyLines(frameLines()) }} of {{ frameLines().length }} ready</span
          >
          <button
            type="button"
            class="dr-link"
            [class.is-done]="frameLines().length > 0 && missingAudio(frameLines()) === 0"
            [disabled]="frameLines().length === 0"
            (click)="generate(frameLines())"
          >
            {{ audioAction(frameLines(), 'Regenerate') }}
          </button>
          <button
            type="button"
            class="dr-pill-btn"
            [disabled]="!playingFrame() && readyLines(frameLines()) === 0"
            (click)="playFrame()"
          >
            {{ playingFrame() ? '❚❚ Stop' : '▶ Play frame' }}
          </button>
        </div>
        @if (variationOf() || nextFrame()) {
          <div class="dr-chain">
            @if (variationOf(); as previous) {
              <p class="dr-chain-note">
                <button type="button" class="dr-link" (click)="store.stepFrame(-1)">
                  Frame {{ previous }}
                </button>
                with one change: {{ store.selectedFrame()?.frame?.variation }}
              </p>
            }
            @if (nextFrame(); as next) {
              <label
                class="dr-check"
                [title]="
                  next.frame.locked
                    ? 'Frame ' + (store.selectedFrameNumber() + 1) + ' is locked'
                    : 'Its picture is made by editing this one, so everything else stays exactly as it is'
                "
              >
                <input
                  type="checkbox"
                  [checked]="chained()"
                  [disabled]="next.frame.locked"
                  (change)="chain($any($event.target).checked)"
                />
                Next frame is this picture with a small change
              </label>
              @if (chained()) {
                <textarea
                  class="dr-input"
                  rows="2"
                  maxlength="2000"
                  placeholder="What changes, as the viewer sees it. e.g. Mara has looked up from the radio, toward screen right"
                  [value]="next.frame.variation"
                  [disabled]="next.frame.locked"
                  (change)="
                    store.updateFrame(next.frame.firstElementId, {
                      variation: $any($event.target).value.trim(),
                    })
                  "
                ></textarea>
              }
            }
          </div>
        }
      }

      <div class="dr-tabs" role="tablist">
        @for (t of tabs; track t.tab) {
          <button
            type="button"
            role="tab"
            [attr.aria-selected]="store.inspectorTab() === t.tab"
            (click)="store.inspectorTab.set(t.tab)"
          >
            {{ t.label }}
          </button>
        }
      </div>
    </div>

    <div class="dr-inspector-body">
      @if (!scene() && !store.selectedFrame()) {
        <div class="dr-empty">
          <p>No frames yet. Let the model split the scene, or picture it all in one frame.</p>
          <div class="dr-row-actions">
            <button
              type="button"
              class="dr-btn"
              [class.is-working]="store.isDirecting({ kind: 'frames' })"
              [disabled]="!store.canDirect({ kind: 'frames' })"
              (click)="store.direct({ kind: 'frames' })"
            >
              {{ store.isDirecting({ kind: 'frames' }) ? 'Suggesting…' : 'Suggest frames' }}
            </button>
            <button
              type="button"
              class="dr-btn"
              [disabled]="store.elements().length === 0"
              (click)="store.coverRow(0)"
            >
              One frame
            </button>
          </div>
        </div>
      } @else {
        @if (frameLocked() && store.inspectorTab() !== 'lines') {
          <p class="dr-locked-note">Locked. Unlock the frame to change it.</p>
        }
        <fieldset
          class="dr-fieldset"
          [disabled]="frameLocked() && store.inspectorTab() !== 'lines'"
        >
          @switch (store.inspectorTab()) {
            @case ('shot') {
              <app-inspector-shot-tab />
            }
            @case ('cast') {
              <app-inspector-cast-tab />
            }
            @case ('place') {
              <app-inspector-place-tab />
            }
            @case ('lines') {
              <app-inspector-lines-tab />
            }
          }
        </fieldset>
      }
    </div>

    <div class="dr-inspector-foot">
      @if (scene()) {
        <button
          type="button"
          class="dr-btn"
          title="The model splits the scene into frames; frames you edited keep their rows and locked frames stay as they are"
          [disabled]="store.isDirecting({ kind: 'frames' })"
          (click)="store.direct({ kind: 'frames' })"
        >
          @if (store.isDirecting({ kind: 'frames' })) {
            <span class="spinner" aria-hidden="true"></span>
          }
          Direct all frames
        </button>
        <span class="flex-1"></span>
        <span class="dr-hint">{{ plural(overrides(), 'frame override') }}</span>
      } @else if (store.selectedFrame(); as span) {
        @if (!span.frame.locked) {
          <button
            type="button"
            class="dr-btn"
            [class.is-working]="store.isDirecting({ kind: 'frame', frameId: span.frame.id })"
            [disabled]="
              !span.frame.id || !store.canDirect({ kind: 'frame', frameId: span.frame.id })
            "
            [title]="span.frame.id ? 'Have the model direct this frame' : 'Saving…'"
            (click)="store.direct({ kind: 'frame', frameId: span.frame.id })"
          >
            {{
              store.isDirecting({ kind: 'frame', frameId: span.frame.id })
                ? 'Suggesting…'
                : 'Suggest frame'
            }}
          </button>
          <span class="flex-1"></span>
          <button
            type="button"
            class="dr-danger"
            [disabled]="span.first === 0 || joinBlocked(span)"
            [title]="joinTitle(span)"
            (click)="store.mergeFrameUp(span.first)"
          >
            Join with previous
          </button>
        }
      }
    </div>
  `,
})
export class DirectorInspector {
  protected readonly store = inject(SceneEditorStore);

  protected readonly tabs = TABS;
  protected readonly sceneImagesTitle =
    'Pictures the scene’s cast, places and props first when they have none, then each frame in order';

  /** The All frames generation summary, folded away until asked for. */
  protected readonly summaryOpen = signal(false);
  protected readonly scene = computed(() => this.store.inspectorScope() === 'scene');
  protected readonly scopes = computed<{ value: InspectorScope; label: string }[]>(() => [
    {
      value: 'frame',
      label: `Frame ${this.store.selectedFrameNumber() || ''}`.trim(),
    },
    { value: 'scene', label: 'All frames' },
  ]);

  protected readonly allLines = computed(() => this.store.spokenElements().map((e) => e.id));
  protected readonly frameLines = computed(() => {
    const span = this.store.selectedFrame();
    if (!span) {
      return [];
    }
    return this.store
      .elements()
      .slice(span.first, span.last + 1)
      .filter((e) => this.store.audioStates().has(e.id))
      .map((e) => e.id);
  });
  protected readonly playingFrame = computed(
    () =>
      this.store.player.context() !== null &&
      this.store.player.context() === this.store.selectedFrame()?.frame.firstElementId,
  );
  protected readonly overrides = computed(() =>
    this.store.frameSpans().reduce((n, s) => n + overrideCount(s.frame), 0),
  );

  protected readonly nextFrame = computed(() => {
    const span = this.store.selectedFrame();
    return span ? this.store.frameAt(span.last + 1) : null;
  });
  /** The number of the frame this one edits, when it is a variation. */
  protected readonly variationOf = computed(() => {
    const number = this.store.selectedFrameNumber();
    return number > 1 && this.store.selectedFrame()?.frame.variation ? number - 1 : null;
  });
  /** The frame whose box was ticked before its next frame's change was written. */
  private readonly chaining = signal<string | null>(null);
  protected readonly chained = computed(
    () =>
      !!this.nextFrame()?.frame.variation ||
      this.chaining() === this.store.selectedFrame()?.frame.firstElementId,
  );

  protected readonly frameLocked = computed(
    () => this.store.inspectorScope() === 'frame' && !!this.store.selectedFrame()?.frame.locked,
  );
  protected readonly frameImageState = computed<ImageState>(() => {
    const span = this.store.selectedFrame();
    return (span && this.store.frameImageStates().get(span.frame.firstElementId)) ?? 'none';
  });
  /** What the picture was offered to compose from, and how the image model says it used them. */
  protected readonly frameImageTitle = computed(() => {
    const image = this.store.selectedFrame()?.frame.image;
    if (!image) {
      return '';
    }
    const from = image.references.length
      ? `Composed from ${image.references.join(', ')}.`
      : 'Drawn from its description alone.';
    return image.notes ? `${from}\n\n${image.notes}` : from;
  });
  protected readonly frameImageAction = computed(() => {
    switch (this.frameImageState()) {
      case 'generating':
        return 'Drawing…';
      case 'ready':
        return 'Regenerate';
      case 'stale':
        return 'Update';
      default:
        return 'Generate';
    }
  });
  protected readonly sceneImageAction = computed(() => {
    const need = this.store.framesNeedingImages().length;
    return need > 0 ? `Generate ${need}` : 'Regenerate all';
  });

  protected imageCount(state: ImageState): number {
    return [...this.store.frameImageStates().values()].filter((s) => s === state).length;
  }

  protected stateLabel(state: ImageState): string {
    return {
      none: 'Missing',
      ready: 'Ready',
      stale: 'Out of date',
      generating: 'Drawing…',
    }[state];
  }

  protected chain(on: boolean): void {
    const next = this.nextFrame();
    this.chaining.set(on ? (this.store.selectedFrame()?.frame.firstElementId ?? null) : null);
    if (!on && next?.frame.variation) {
      this.store.updateFrame(next.frame.firstElementId, { variation: '' });
    }
  }

  protected pictureFrame(): void {
    const id = this.store.selectedFrame()?.frame.id;
    if (id) {
      void this.store.generateFrameImages([id], this.frameImageState() === 'ready');
    }
  }

  protected pictureScene(): void {
    const need = this.store.framesNeedingImages();
    void this.store.generateFrameImages(
      need.length > 0 ? need : this.store.unlockedFrameIds(),
      need.length === 0,
      true,
    );
  }

  protected plural(count: number, word: string): string {
    return plural(count, word);
  }

  protected readyLines(ids: readonly string[]): number {
    return ids.filter((id) => this.store.audioStates().get(id) === 'ready').length;
  }

  protected missingAudio(ids: readonly string[]): number {
    return ids.filter((id) => this.store.linesNeedingAudio().includes(id)).length;
  }

  protected audioAction(ids: readonly string[], regenerate = 'Regenerate all'): string {
    const need = this.missingAudio(ids);
    return need > 0 ? `Generate ${need}` : ids.length > 0 ? regenerate : 'Generate';
  }

  protected generate(ids: readonly string[]): void {
    const need = ids.filter((id) => this.store.linesNeedingAudio().includes(id));
    void this.store.generateAudio(need.length > 0 ? need : ids, need.length === 0);
  }

  protected joinBlocked(span: FrameSpan): boolean {
    const before = this.store.frameAt(span.first - 1);
    return (
      !!before?.frame.locked || [span, before].some((s) => s !== null && this.frameDirecting(s))
    );
  }

  protected joinTitle(span: FrameSpan): string {
    if (span.first === 0) {
      return 'The first frame has nothing before it';
    }
    return this.store.frameAt(span.first - 1)?.frame.locked ? 'The frame before is locked' : '';
  }

  protected frameDirecting(span: FrameSpan): boolean {
    return this.store.isDirecting({ kind: 'frame', frameId: span.frame.id });
  }

  protected playFrame(): void {
    const span = this.store.selectedFrame();
    if (this.playingFrame()) {
      this.store.player.stop();
    } else if (span) {
      this.store.playLines(this.frameLines(), span.frame.firstElementId);
    }
  }
}
