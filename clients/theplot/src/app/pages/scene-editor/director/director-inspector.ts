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
  imports: [InspectorCastTab, InspectorLinesTab, InspectorPlaceTab, InspectorShotTab],
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
                [disabled]="frameIds().length === 0"
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
            <a class="dr-picture is-image" [href]="image.url" target="_blank" rel="noopener">
              <img [src]="image.url" [alt]="'Frame ' + store.selectedFrameNumber()" />
            </a>
          } @else {
            <div class="dr-picture">
              {{ frameImageState() === 'generating' ? 'Drawing…' : 'No image' }}
            </div>
          }
          <span class="dr-media-label">Image</span>
          <span
            class="dr-media-value"
            [class.is-warn]="frameImageState() === 'stale'"
            [title]="frameImageTitle()"
            >{{ stateLabel(frameImageState()) }}</span
          >
          <button
            type="button"
            class="dr-link"
            [class.is-done]="frameImageState() === 'ready'"
            [class.is-working]="frameImageState() === 'generating'"
            [disabled]="!store.selectedFrame()?.frame?.id || frameImageState() === 'generating'"
            [title]="
              store.selectedFrame()?.frame?.id
                ? 'Composed from the pictures that already exist around it'
                : 'Saving…'
            "
            (click)="pictureFrame()"
          >
            {{ frameImageAction() }}
          </button>
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
      }
    </div>

    <div class="dr-inspector-foot">
      @if (scene()) {
        <button
          type="button"
          class="dr-btn"
          title="The model splits the scene into frames; frames you edited keep their rows"
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
        <button
          type="button"
          class="dr-btn"
          [class.is-working]="store.isDirecting({ kind: 'frame', frameId: span.frame.id })"
          [disabled]="!span.frame.id || !store.canDirect({ kind: 'frame', frameId: span.frame.id })"
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
          [title]="span.first === 0 ? 'The first frame has nothing before it' : ''"
          (click)="store.mergeFrameUp(span.first)"
        >
          Join with previous
        </button>
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

  protected readonly frameIds = computed(() =>
    this.store.frameSpans().flatMap((s) => (s.frame.id ? [s.frame.id] : [])),
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

  protected pictureFrame(): void {
    const id = this.store.selectedFrame()?.frame.id;
    if (id) {
      void this.store.generateFrameImages([id], this.frameImageState() === 'ready');
    }
  }

  protected pictureScene(): void {
    const need = this.store.framesNeedingImages();
    void this.store.generateFrameImages(
      need.length > 0 ? need : this.frameIds(),
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
    return [span, before].some(
      (s) => s !== null && this.store.isDirecting({ kind: 'frame', frameId: s.frame.id }),
    );
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
