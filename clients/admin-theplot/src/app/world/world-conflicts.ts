import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { WorldEdge, type WorldPlace } from '../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import { basisLabel, beatText, momentLabel, placeName } from './labels';
import { WorldCharacterChip } from './world-character-chip';
import { WorldObjectChip } from './world-object-chip';
import { WorldSession } from './world-session';

@Component({
  selector: 'app-world-conflicts',
  imports: [WorldCharacterChip, WorldObjectChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="text-sm text-gray-600">
      A beat that disagrees with the standing fact, with nothing in between to explain the change,
      loses unless it outranks it: the author's word, then a stated observation, then an implied
      one.
    </p>
    <ol class="mt-3 space-y-2">
      @for (row of rows(); track $index) {
        <li
          class="rounded-xl border bg-white p-3 text-sm shadow-sm"
          [class.border-indigo-300]="row.moment === session.moment()"
          [class.border-gray-200]="row.moment !== session.moment()"
        >
          <div class="flex flex-wrap items-center gap-2">
            <button
              type="button"
              class="text-xs font-medium text-indigo-600 hover:underline"
              (click)="session.go(row.moment)"
            >
              {{ row.when }}
            </button>
            @if (row.objectId) {
              <app-world-object [id]="row.objectId" />
            } @else if (row.characterId) {
              <app-world-character [id]="row.characterId" />
            }
          </div>
          <p class="mt-1.5 text-gray-800">
            <span class="text-gray-500">Said ({{ row.basis }}):</span> {{ row.said }}
          </p>
          <p class="text-gray-800">
            <span class="text-gray-500">Kept ({{ row.keptBasis }}, from</span>
            <button
              type="button"
              class="text-gray-500 underline decoration-gray-300 hover:decoration-gray-600"
              (click)="session.go(row.keptAt)"
            >
              {{ row.keptWhen }}</button
            ><span class="text-gray-500">):</span>
            {{ row.kept }}
          </p>
          @if (row.text) {
            <p class="mt-1 line-clamp-2 text-xs text-gray-500 italic">“{{ row.text }}”</p>
          }
        </li>
      } @empty {
        <li
          class="rounded-xl border border-dashed border-gray-300 p-6 text-center text-sm text-gray-500"
        >
          No conflicts: every observation agreed with what stood, or outranked it.
        </li>
      }
    </ol>
  `,
})
export class WorldConflicts {
  protected readonly session = inject(WorldSession);

  protected readonly rows = computed(() => {
    const world = this.session.world();
    if (!world) {
      return [];
    }
    return [...world.reply.conflicts]
      .sort((a, b) => a.moment - b.moment)
      .map((conflict) => {
        const fact = world.facts[conflict.fact];
        const claim = (
          place: WorldPlace | undefined,
          other: WorldPlace | undefined,
          value: string,
          ends: boolean,
        ) => {
          switch (fact.edge) {
            case WorldEdge.STATE:
              return `${fact.property}: ${value}`;
            case WorldEdge.CONNECTS:
              return `${placeName(world, place)} ⇄ ${placeName(world, other)}${ends ? ' closed' : ''}`;
            default:
              return ends ? 'left' : `at ${placeName(world, place)}`;
          }
        };
        return {
          moment: conflict.moment,
          when: momentLabel(world, conflict.moment),
          objectId: fact.objectId,
          characterId: fact.characterId,
          basis: basisLabel(conflict.basis, conflict.origin),
          said: claim(conflict.place, conflict.other, conflict.value, conflict.ends),
          keptBasis: basisLabel(fact.basis, fact.origin),
          keptAt: fact.said,
          keptWhen: momentLabel(world, fact.said),
          kept: claim(fact.place, fact.other, fact.value, false),
          text: beatText(world, conflict.moment),
        };
      });
  });
}
