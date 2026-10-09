import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { headingAt } from '../../../direction/panels';
import { Location } from '../../../stories/model/location';
import { SceneEditorStore } from '../scene-editor-store';
import { ChangeLayers } from './change-layers';

interface Place {
  location: Location;
  time: string;
  sceneChange: string;
  own: string;
  /** Panels that add their own changes to this location. */
  panels: number[];
}

/** Where the panel or the scene is, and what is different about the place here. */
@Component({
  selector: 'app-inspector-place-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChangeLayers],
  host: { class: 'dr-tab-body' },
  template: `
    @for (place of places(); track place.location.id) {
      <div class="dr-place">
        <span class="dr-kicker">Location</span>
        <span class="dr-kicker">Time</span>
        <span class="dr-place-name">{{ place.location.name }}</span>
        <span class="dr-place-time">{{ place.time || '—' }}</span>
      </div>
      <app-change-layers
        title="Location changes"
        [base]="place.location.description ?? ''"
        [sceneText]="panel() ? place.sceneChange : ''"
        [hereTag]="hereTag()"
        [text]="place.own"
        [placeholder]="placeholder()"
        [canPromote]="!!panel() && !!place.own"
        [panelNote]="note(place.panels)"
        (edit)="store.setLocationChange(place.location.id, $event, panelKey())"
        (clear)="store.setLocationChange(place.location.id, '', panelKey())"
        (promote)="promote(place.location.id)"
        (clearPanels)="store.clearPanelLocationChanges(place.location.id)"
      />
    } @empty {
      <p class="dr-empty">
        {{
          panel()
            ? 'No location heads this panel yet. Add a heading above it in the story.'
            : 'This scene has no location yet. Add a heading in the story.'
        }}
      </p>
    }
    <div class="dr-tab-foot">
      <span class="flex-1">The place’s own sheet lives in the library.</span>
      <button
        type="button"
        class="dr-link"
        [disabled]="store.isDirecting({ kind: 'continuity' })"
        (click)="store.direct({ kind: 'continuity' })"
      >
        Suggest
      </button>
    </div>
  `,
})
export class InspectorPlaceTab {
  protected readonly store = inject(SceneEditorStore);

  protected readonly span = computed(() =>
    this.store.inspectorScope() === 'panel' ? this.store.selectedPanel() : null,
  );
  protected readonly panel = computed(() => this.span()?.panel ?? null);
  protected readonly panelKey = computed(() => this.panel()?.firstElementId ?? null);
  protected readonly hereTag = computed(() =>
    this.panel() ? `Panel ${this.store.selectedPanelNumber()}` : 'Scene',
  );

  protected readonly places = computed<Place[]>(() => {
    const elements = this.store.elements();
    const spans = this.store.panelSpans();
    const states = this.store.locationStateByLocation();
    const panelsAt = (locationId: string) =>
      spans.flatMap((s, i) =>
        s.panel.locationChange && headingAt(elements, s.first)?.locationId === locationId
          ? [i + 1]
          : [],
      );
    const span = this.span();
    if (span) {
      const heading = headingAt(elements, span.first);
      const location = heading?.locationId
        ? this.store.locationById().get(heading.locationId)
        : undefined;
      return location
        ? [
            {
              location,
              time: heading?.timeOfDay ?? '',
              sceneChange: states.get(location.id)?.change ?? '',
              own: span.panel.locationChange,
              panels: [],
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
      panels: panelsAt(location.id),
    }));
  });

  protected placeholder(): string {
    return this.panel()
      ? `What changes in panel ${this.store.selectedPanelNumber()}? e.g. crane silhouetted`
      : 'What changes in this scene? e.g. fog rolling in';
  }

  protected note(panels: readonly number[]): string {
    if (this.panel() || panels.length === 0) {
      return '';
    }
    const one = panels.length === 1;
    return `${one ? 'Panel' : 'Panels'} ${panels.join(', ')} add${one ? 's' : ''} further location changes`;
  }

  protected promote(locationId: string): void {
    const panelKey = this.panelKey();
    if (panelKey) {
      this.store.promoteLocationChange(locationId, panelKey);
    }
  }
}
