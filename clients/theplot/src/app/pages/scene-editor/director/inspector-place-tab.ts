import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { headingAt } from '../../../direction/frames';
import { Location } from '../../../stories/model/location';
import { SceneEditorStore } from '../scene-editor-store';
import { ChangeLayers } from './change-layers';
import { LocationVariationSelect } from './location-variation-select';

interface Place {
  location: Location;
  time: string;
  sceneChange: string;
  own: string;
  /** Frames that add their own changes to this location. */
  frames: number[];
}

/** Where the frame or the scene is, and what is different about the place here. */
@Component({
  selector: 'app-inspector-place-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChangeLayers, LocationVariationSelect],
  host: { class: 'dr-tab-body' },
  template: `
    @for (place of places(); track place.location.id) {
      <div class="dr-place">
        <span class="dr-kicker">Location</span>
        <span class="dr-kicker">Time</span>
        <span class="dr-place-name">{{ place.location.name }}</span>
        <span class="dr-place-time">{{ place.time || '—' }}</span>
      </div>
      @if (span(); as frameSpan) {
        <app-location-variation-select [span]="frameSpan" />
      }
      <app-change-layers
        title="Location changes"
        [base]="place.location.description ?? ''"
        [sceneText]="frame() ? place.sceneChange : ''"
        [hereTag]="hereTag()"
        [text]="place.own"
        [placeholder]="placeholder()"
        [canPromote]="!!frame() && !!place.own"
        [frameNote]="note(place.frames)"
        (edit)="store.setLocationChange(place.location.id, $event, frameKey())"
        (clear)="store.setLocationChange(place.location.id, '', frameKey())"
        (promote)="promote(place.location.id)"
        (clearFrames)="store.clearFrameLocationChanges(place.location.id)"
      />
    } @empty {
      <p class="dr-empty">
        {{
          frame()
            ? 'No location heads this frame yet. Add a heading above it in the story.'
            : 'This scene has no location yet. Add a heading in the story.'
        }}
      </p>
    }
    <div class="dr-tab-foot">
      <span class="flex-1">The place’s own sheet lives in the library.</span>
      <button
        type="button"
        class="dr-link"
        [class.is-working]="store.isDirecting({ kind: 'continuity' })"
        [disabled]="!store.canDirect({ kind: 'continuity' })"
        (click)="store.direct({ kind: 'continuity' })"
      >
        {{ store.isDirecting({ kind: 'continuity' }) ? 'Suggesting…' : 'Suggest' }}
      </button>
    </div>
  `,
})
export class InspectorPlaceTab {
  protected readonly store = inject(SceneEditorStore);

  protected readonly span = computed(() =>
    this.store.inspectorScope() === 'frame' ? this.store.selectedFrame() : null,
  );
  protected readonly frame = computed(() => this.span()?.frame ?? null);
  protected readonly frameKey = computed(() => this.frame()?.firstElementId ?? null);
  protected readonly hereTag = computed(() =>
    this.frame() ? `Frame ${this.store.selectedFrameNumber()}` : 'Scene',
  );

  protected readonly places = computed<Place[]>(() => {
    const elements = this.store.elements();
    const spans = this.store.frameSpans();
    const states = this.store.locationStateByLocation();
    // Rows no heading places are where the director layer puts the scene.
    const unheaded = this.store
      .locationsInScene()
      .find((l) => !elements.some((e) => e.type === 'Heading' && e.locationId === l.id));
    const placeAt = (index: number) => headingAt(elements, index)?.locationId ?? unheaded?.id;
    const framesAt = (locationId: string) =>
      spans.flatMap((s, i) =>
        s.frame.locationChange && placeAt(s.first) === locationId ? [i + 1] : [],
      );
    const span = this.span();
    if (span) {
      const heading = headingAt(elements, span.first);
      const placed = placeAt(span.first);
      const location = placed ? this.store.locationById().get(placed) : undefined;
      return location
        ? [
            {
              location,
              time: heading?.timeOfDay ?? '',
              sceneChange: states.get(location.id)?.change ?? '',
              own: span.frame.locationChange,
              frames: [],
            },
          ]
        : [];
    }
    return this.store.locationsInScene().map((location) => ({
      location,
      time:
        elements.find((e) => e.type === 'Heading' && e.locationId === location.id)?.timeOfDay ?? '',
      sceneChange: states.get(location.id)?.change ?? '',
      own: states.get(location.id)?.change ?? '',
      frames: framesAt(location.id),
    }));
  });

  protected placeholder(): string {
    return this.frame()
      ? `What changes in frame ${this.store.selectedFrameNumber()}? e.g. crane silhouetted`
      : 'What changes in this scene? e.g. fog rolling in';
  }

  protected note(frames: readonly number[]): string {
    if (this.frame() || frames.length === 0) {
      return '';
    }
    const one = frames.length === 1;
    return `${one ? 'Frame' : 'Frames'} ${frames.join(', ')} add${one ? 's' : ''} further location changes`;
  }

  protected promote(locationId: string): void {
    const frameKey = this.frameKey();
    if (frameKey) {
      this.store.promoteLocationChange(locationId, frameKey);
    }
  }
}
