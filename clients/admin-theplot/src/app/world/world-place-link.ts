import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import type { WorldPlace } from '../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import { hue, placeName } from './labels';
import { WorldSession } from './world-session';

@Component({
  selector: 'app-world-place',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'inline' },
  template: `
    @if (place(); as place) {
      <button
        type="button"
        class="inline-flex items-center gap-1 rounded text-left font-medium text-gray-900 underline decoration-gray-300 underline-offset-2 hover:decoration-gray-700"
        [title]="'Show ' + name() + ' on the stage'"
        (click)="session.focus(place.locationId)"
      >
        <span class="h-2 w-2 shrink-0 rounded-full" [style.background]="swatch()"></span>
        {{ name() }}
      </button>
    } @else {
      <span class="text-gray-500 italic">nowhere known</span>
    }
  `,
})
export class WorldPlaceLink {
  protected readonly session = inject(WorldSession);

  readonly place = input<Pick<WorldPlace, 'locationId' | 'partId'>>();

  protected readonly name = computed(() => {
    const world = this.session.world();
    return world ? placeName(world, this.place()) : '';
  });
  protected readonly swatch = computed(() =>
    hue(this.session.world()?.locations.get(this.place()?.locationId ?? '')?.hue ?? 0),
  );
}
