import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { overrideCount } from '../../../direction/inheritance';
import { FrameSpan } from '../../../direction/frames';
import { PictureRefinement } from '../../../direction/model/picture-refinement';
import { plural } from '../format';
import { ImageState } from '../image-state';
import { InspectorScope } from '../inspector-scope';
import { InspectorTab } from '../inspector-tab';
import { SceneEditorStore } from '../scene-editor-store';
import { InspectorCastTab } from './inspector-cast-tab';
import { InspectorLinesTab } from './inspector-lines-tab';
import { InspectorPlaceTab } from './inspector-place-tab';
import { InspectorShotTab } from './inspector-shot-tab';
import { Autofocus } from './autofocus';
import { LocationVariationSelect } from './location-variation-select';
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
    Autofocus,
    InspectorCastTab,
    InspectorLinesTab,
    InspectorPlaceTab,
    InspectorShotTab,
    LocationVariationSelect,
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
            <app-picture-preview
              [url]="image.url"
              [alt]="'Frame ' + store.selectedFrameNumber()"
              [refinable]="!frameLocked()"
              [busy]="frameImageState() === 'generating'"
              [references]="frameReferences()"
              (refine)="refineFrame($event)"
            />
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
            <span class="dr-media-actions">
              @if (store.selectedFrame()?.frame?.image) {
                <button
                  type="button"
                  class="dr-link"
                  [disabled]="frameImageState() === 'generating'"
                  title="Say what to change in this picture; the rest stays"
                  (click)="preview()?.open(true)"
                >
                  Refine
                </button>
              }
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
            </span>
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
        @if (store.selectedFrameNumber() > 1) {
          <div class="dr-variation">
            <label
              class="dr-check"
              [title]="
                frameLocked()
                  ? 'Locked. Unlock the frame to change it.'
                  : 'Its picture is made from frame ' +
                    (store.selectedFrameNumber() - 1) +
                    '’s, so the place, light and people stay as they are'
              "
            >
              <input
                type="checkbox"
                [checked]="chained()"
                [disabled]="frameLocked()"
                (change)="chain($any($event.target).checked)"
              />
              Based off previous frame
            </label>
            @if (chained()) {
              @let moves = movesCamera();
              <div class="dr-pills" role="radiogroup" aria-label="Camera">
                <button
                  type="button"
                  class="dr-pill"
                  role="radio"
                  [title]="
                    movedPart()
                      ? 'This frame is at another part of the place than the frame before, so the camera moves'
                      : 'Edits the previous picture: same camera, same people, only the change moves'
                  "
                  [attr.aria-checked]="!moves"
                  [disabled]="frameLocked() || !!movedPart()"
                  (click)="setMovesCamera(false)"
                >
                  Same shot
                </button>
                <button
                  type="button"
                  class="dr-pill"
                  role="radio"
                  title="The camera repositions and people and props come and go: set who is in view and where on the tabs below"
                  [attr.aria-checked]="moves"
                  [disabled]="frameLocked()"
                  (click)="setMovesCamera(true)"
                >
                  Camera moves
                </button>
              </div>
              @if (store.selectedFrame(); as span) {
                <app-location-variation-select [span]="span" />
              }
              @if (movedPart(); as moved) {
                <span class="dr-hint">{{ moved }}</span>
              }
              <textarea
                class="dr-input"
                [rows]="moves ? 5 : 9"
                maxlength="2000"
                [placeholder]="
                  moves
                    ? 'Where the camera has gone and what that shows. e.g. The camera has moved further down the hallway; the visitors from mid-corridor are now close at screen left and Bryan stands just past them'
                    : 'What changes, as the viewer sees it. e.g. Mara has looked up from the radio, toward screen right'
                "
                [value]="store.selectedFrame()?.frame?.variation ?? ''"
                [disabled]="frameLocked()"
                (change)="variation($any($event.target).value)"
              ></textarea>
            }
          </div>
        }
      }

      @if (frameFindings(); as findings) {
        @if (findings.length > 0) {
          <div class="dr-findings" role="status">
            <div class="dr-field-head">
              <span class="dr-kicker">Still to fix after the checks</span>
              <span class="flex-1"></span>
              <button
                type="button"
                class="dr-link is-quiet"
                (click)="store.dismissFindings(findings[0].frameId)"
              >
                dismiss
              </button>
            </div>
            <ul>
              @for (finding of findings; track $index) {
                <li>
                  <span class="dr-finding-check">{{
                    finding.check === 'consistency' ? 'Consistency' : 'Positioning'
                  }}</span>
                  {{ finding.issue }}
                </li>
              }
            </ul>
          </div>
        }
      }

      @if (!based()) {
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
      }
    </div>

    @if (!based()) {
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
    } @else {
      <span class="flex-1"></span>
    }

    @if (askingFor(); as span) {
      <div class="dr-inspector-foot is-asking">
        <label class="dr-field">
          <span class="dr-kicker">Instructions for the model</span>
          <textarea
            class="dr-input"
            rows="4"
            maxlength="2000"
            placeholder="What this frame should show. e.g. Close on Mara’s hands at the radio; Bolt stays out of shot"
            [value]="draft()"
            (input)="setDraft($any($event.target).value)"
            (keydown.escape)="asking.set(null)"
            (keydown.control.enter)="suggestFrame(span)"
            (keydown.meta.enter)="suggestFrame(span)"
            drAutofocus
          ></textarea>
        </label>
        <div class="dr-row-actions">
          <button
            type="button"
            class="dr-btn is-dark"
            [disabled]="!store.canDirect({ kind: 'frame', frameId: span.frame.id })"
            (click)="suggestFrame(span)"
          >
            Suggest frame
          </button>
          <button type="button" class="dr-link" (click)="asking.set(null)">Cancel</button>
        </div>
      </div>
    } @else {
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
              [title]="
                span.frame.id
                  ? 'Have the model direct this frame, with your instructions'
                  : 'Saving…'
              "
              (click)="asking.set(span.frame.id)"
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
    }
  `,
})
export class DirectorInspector {
  protected readonly store = inject(SceneEditorStore);

  protected readonly tabs = TABS;
  protected readonly preview = viewChild(PicturePreview);
  protected readonly sceneImagesTitle =
    'Pictures the scene’s cast, places and props first when they have none, then each frame in order';

  /** The All frames generation summary, folded away until asked for. */
  protected readonly summaryOpen = signal(false);
  protected readonly scene = computed(() => this.store.inspectorScope() === 'scene');
  protected readonly frameFindings = computed(() => {
    const id = this.scene() ? null : this.store.selectedFrame()?.frame?.id;
    return id ? this.store.findings().filter((f) => f.frameId === id) : [];
  });
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

  /** The number of the frame this one edits, when it is a variation. */
  protected readonly variationOf = computed(() => {
    const number = this.store.selectedFrameNumber();
    return number > 1 && this.store.selectedFrame()?.frame.variation ? number - 1 : null;
  });
  /** The frame whose box was ticked before its change was written. */
  private readonly chaining = signal<string | null>(null);
  protected readonly chained = computed(
    () =>
      !!this.variationOf() || this.chaining() === this.store.selectedFrame()?.frame.firstElementId,
  );
  /**
   * Said when this frame is at another part of the place than the one before: it can't be an edit
   * of that picture, so it is drawn with the camera moved and that part's own background.
   */
  protected readonly movedPart = computed(() => {
    const spans = this.store.frameSpans();
    const number = this.store.selectedFrameNumber();
    const here = spans[number - 1]?.frame.locationVariationId ?? null;
    const before = spans[number - 2]?.frame.locationVariationId ?? null;
    if (number < 2 || here === before) {
      return '';
    }
    const name = (id: string | null) =>
      id
        ? (this.store.locationVariationById().get(id)?.variation.name ?? 'a variation')
        : 'the establishing view';
    const source = here ? 'that variation’s picture' : 'the location’s picture';
    return (
      `Frame ${number - 1} is at ${name(before)} and this one at ${name(here)}: ` +
      `the place comes from ${source}, and frame ${number - 1} gives only the people.`
    );
  });
  protected readonly movesCamera = computed(
    () => !!this.store.selectedFrame()?.frame?.variationMovesCamera || !!this.movedPart(),
  );
  /** A same-shot variation is pictured from its change alone, so its tabs are hidden. */
  protected readonly based = computed(
    () =>
      !this.scene() &&
      this.store.selectedFrameNumber() > 1 &&
      this.chained() &&
      !this.movesCamera(),
  );

  /** The frame whose instructions box is open. */
  protected readonly asking = signal<string | null>(null);
  /** Instructions typed per frame id, kept for another try. */
  private readonly drafts = signal<ReadonlyMap<string, string>>(new Map());
  protected readonly askingFor = computed(() => {
    const span = this.store.selectedFrame();
    return !this.scene() && span && !span.frame.locked && span.frame.id === this.asking()
      ? span
      : null;
  });
  protected readonly draft = computed(() => this.drafts().get(this.asking() ?? '') ?? '');

  protected readonly frameLocked = computed(
    () => this.store.inspectorScope() === 'frame' && !!this.store.selectedFrame()?.frame.locked,
  );
  /** The library's pictures and the frames before this one. */
  protected readonly frameReferences = computed(() => {
    const number = this.store.selectedFrameNumber();
    return this.store
      .refineReferences()
      .filter((r) => r.frameNumber === null || r.frameNumber < number);
  });
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
    const span = this.store.selectedFrame();
    this.chaining.set(on ? (span?.frame.firstElementId ?? null) : null);
    if (!on && (span?.frame.variation || span?.frame.variationMovesCamera)) {
      this.store.updateFrame(span.frame.firstElementId, {
        variation: '',
        variationMovesCamera: false,
      });
    }
  }

  protected setMovesCamera(moves: boolean): void {
    const span = this.store.selectedFrame();
    if (span) {
      this.store.updateFrame(span.frame.firstElementId, { variationMovesCamera: moves });
    }
  }

  protected variation(text: string): void {
    const span = this.store.selectedFrame();
    if (span) {
      this.store.updateFrame(span.frame.firstElementId, { variation: text.trim() });
    }
  }

  protected setDraft(text: string): void {
    const id = this.asking();
    if (id) {
      this.drafts.update((drafts) => new Map(drafts).set(id, text));
    }
  }

  protected suggestFrame(span: FrameSpan): void {
    if (!this.store.canDirect({ kind: 'frame', frameId: span.frame.id })) {
      return;
    }
    this.asking.set(null);
    void this.store.direct({
      kind: 'frame',
      frameId: span.frame.id,
      instructions: this.drafts().get(span.frame.id)?.trim() ?? '',
    });
  }

  protected pictureFrame(): void {
    const id = this.store.selectedFrame()?.frame.id;
    if (id) {
      void this.store.generateFrameImages([id], this.frameImageState() === 'ready');
    }
  }

  protected refineFrame(refinement: PictureRefinement): void {
    const id = this.store.selectedFrame()?.frame.id;
    if (id) {
      void this.store.refineFrameImage(id, refinement);
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
