import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { LabelChips } from './label-chips';
import { LocationPicker } from './location-picker';
import { RowActions } from './row-actions';
import { SceneEditorStore } from './scene-editor-store';
import { SceneRow } from './scene-row';
import { plural, slugline } from './format';

@Component({
  selector: 'app-heading-row',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LabelChips, LocationPicker, RowActions],
  template: `
    @if (!editing()) {
      <div
        class="se-grid se-heading"
        [class.has-hue]="location() !== null"
        [style.--hue]="location()?.hue"
        [style.margin-top.px]="row().index === 0 ? 0 : 22"
        (click)="store.editingId.set(row().element.id)"
      >
        <div class="se-kicker se-heading-kicker">
          @if (location()) {
            <span class="se-swatch hue-fill"></span>
          } @else {
            <span class="se-swatch se-swatch-none"></span>
          }
          Location
        </div>
        <div class="se-heading-line">
          @if (location(); as location) {
            <span class="se-slug hue-text">{{ slug() }}</span>
          } @else {
            <span class="se-slug se-missing">{{ missingLabel() }}</span>
          }
          @if (row().element.timeOfDay; as time) {
            <span class="se-time">— {{ time }}</span>
          } @else {
            <span class="se-time se-missing">NO TIME — PICK ONE</span>
          }
          <span class="se-count">{{ beats() }}</span>
        </div>
      </div>
    } @else {
      <div class="se-grid" [style.margin-top.px]="row().index === 0 ? 0 : 22">
        <div class="se-kicker se-editing-label">Location</div>
        <div class="se-card rise" (keydown.escape)="store.editingId.set(null)">
          <div class="se-card-head">
            <span class="se-kicker">Where</span>
            <app-row-actions
              (up)="store.moveElement(row().element.id, -1)"
              (down)="store.moveElement(row().element.id, 1)"
              (remove)="store.removeElement(row().element.id)"
              (done)="store.editingId.set(null)"
            />
          </div>
          <app-location-picker
            [element]="row().element"
            [autofocus]="!row().element.locationId && !store.titleRequest()"
            (escape)="store.editingId.set(null)"
          />
          <span class="se-kicker">When</span>
          <app-label-chips
            [labels]="store.timesOfDay()"
            [selected]="row().element.timeOfDay"
            noun="time of day"
            placeholder="e.g. MORNING"
            [add]="addTime"
            (pick)="store.updateElement(row().element.id, { timeOfDay: $event })"
          />
          <button
            type="button"
            class="se-btn se-btn-text self-start"
            (click)="store.libraryTab.set('locations')"
          >
            Manage locations in library →
          </button>
        </div>
      </div>
    }
  `,
})
export class HeadingRow {
  protected readonly store = inject(SceneEditorStore);

  readonly row = input.required<SceneRow>();
  readonly editing = input(false);

  protected readonly location = computed(() => {
    const id = this.row().element.locationId;
    return id ? (this.store.locationById().get(id) ?? null) : null;
  });
  protected readonly slug = computed(() => {
    const location = this.location();
    return location ? slugline(location) : '';
  });
  protected readonly missingLabel = computed(() =>
    this.row().element.locationId ? 'MISSING LOCATION — PICK ONE' : 'NO LOCATION — PICK ONE',
  );
  protected readonly beats = computed(() => plural(this.row().beats, 'beat'));

  protected readonly addTime = (label: string) => this.store.addTimeOfDay(label);
}
