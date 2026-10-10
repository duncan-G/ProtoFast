import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { FrameSpan } from '../../../direction/frames';
import { SceneEditorStore } from '../scene-editor-store';

/** Which part of its place a frame is in: the location's establishing view or one of its variations. */
@Component({
  selector: 'app-location-variation-select',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'dr-field' },
  template: `
    @if (location(); as location) {
      <div class="dr-field-head">
        <span class="dr-kicker">Location variation</span>
        <span class="flex-1"></span>
        <button
          type="button"
          class="dr-link"
          title="Add or change {{ location.name }}'s variations in the library"
          (click)="store.directorLibraryEntry.set(location.id)"
        >
          Edit in library
        </button>
      </div>
      <select
        class="dr-input"
        aria-label="Location variation"
        [disabled]="span().frame.locked"
        [title]="span().frame.locked ? 'Locked. Unlock the frame to change it.' : ''"
        (change)="set($any($event.target).value)"
      >
        <option value="" [selected]="!part()">{{ location.name }} · establishing view</option>
        @for (v of location.variations; track v.id) {
          <option [value]="v.id" [selected]="part()?.id === v.id">
            {{ location.name }} · {{ v.name }}
          </option>
        }
      </select>
      <span class="dr-hint">{{
        part()
          ? part()!.description || 'No description yet.'
          : location.variations.length
            ? 'Where the location’s own picture stands. Pick a variation once the camera has moved on to another part of the place.'
            : 'Where the location’s own picture stands. Add variations in the library for the other parts of the place the camera moves to.'
      }}</span>
    }
  `,
})
export class LocationVariationSelect {
  protected readonly store = inject(SceneEditorStore);

  readonly span = input.required<FrameSpan>();

  protected readonly location = computed(() => this.store.frameLocation(this.span()));
  protected readonly part = computed(
    () =>
      this.location()?.variations.find((v) => v.id === this.span().frame.locationVariationId) ??
      null,
  );

  protected set(id: string): void {
    this.store.updateFrame(this.span().frame.firstElementId, { locationVariationId: id || null });
  }
}
