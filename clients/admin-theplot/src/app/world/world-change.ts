import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { WorldEdge, WorldObjectKind } from '../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import { basisLabel, momentLabel } from './labels';
import type { Change } from './world';
import { WorldCharacterChip } from './world-character-chip';
import { WorldObjectChip } from './world-object-chip';
import { WorldPlaceLink } from './world-place-link';
import { WorldSession } from './world-session';

const MARK = {
  added: { glyph: '+', class: 'bg-emerald-100 text-emerald-800' },
  changed: { glyph: '~', class: 'bg-amber-100 text-amber-800' },
  removed: { glyph: '−', class: 'bg-gray-200 text-gray-700' },
};

@Component({
  selector: 'app-world-change',
  imports: [WorldCharacterChip, WorldObjectChip, WorldPlaceLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let c = change();
    @let fact = c.after ?? c.before!;
    <div class="flex items-start gap-2 text-sm leading-6 text-gray-700">
      <span
        class="mt-1 grid h-4 w-4 shrink-0 place-items-center rounded text-xs font-bold"
        [class]="mark().class"
        >{{ mark().glyph }}</span
      >
      <div class="min-w-0 flex-1">
        @switch (c.edge) {
          @case (edge.AT) {
            <app-world-object [id]="fact.objectId" [gone]="c.kind === 'removed'" />
            @switch (c.kind) {
              @case ('added') {
                is at <app-world-place [place]="c.after?.place" />
              }
              @case ('changed') {
                moved from <app-world-place [place]="c.before?.place" /> to
                <app-world-place [place]="c.after?.place" />
              }
              @case ('removed') {
                left <app-world-place [place]="c.before?.place" />
              }
            }
          }
          @case (edge.CHARACTER_AT) {
            <app-world-character [id]="fact.characterId" [gone]="c.kind === 'removed'" />
            @switch (c.kind) {
              @case ('added') {
                is at <app-world-place [place]="c.after?.place" />
              }
              @case ('changed') {
                moved from <app-world-place [place]="c.before?.place" /> to
                <app-world-place [place]="c.after?.place" />
              }
              @case ('removed') {
                is no longer at <app-world-place [place]="c.before?.place" />
              }
            }
          }
          @case (edge.STATE) {
            <app-world-object [id]="fact.objectId" />
            <span class="text-gray-500">{{ fact.property }}:</span>
            @if (c.kind === 'changed') {
              <span class="text-gray-400 line-through">{{ c.before?.value }}</span> →
            }
            <span class="font-medium text-gray-900">{{ fact.value }}</span>
          }
          @case (edge.CONNECTS) {
            <app-world-place [place]="fact.place" />
            <span class="text-gray-400">⇄</span>
            <app-world-place [place]="fact.other" />
            @if (c.kind === 'removed') {
              closed
            } @else if (fact.viaObjectId) {
              through <app-world-object [id]="fact.viaObjectId" />
            } @else {
              connect
            }
          }
          @case (edge.BEAT_AT) {
            <span class="text-gray-500">The action is at</span>
            <app-world-place [place]="fact.place" />
          }
        }
        <span class="ml-1 text-[11px] text-gray-400">{{ note() }}</span>
      </div>
    </div>
  `,
})
export class WorldChange {
  private readonly session = inject(WorldSession);

  readonly change = input.required<Change>();

  protected readonly edge = WorldEdge;
  protected readonly mark = computed(() => MARK[this.change().kind]);

  protected readonly note = computed(() => {
    const world = this.session.world();
    const c = this.change();
    if (!world) {
      return '';
    }
    if (c.kind === 'removed') {
      const object = world.objects.get(c.before?.objectId ?? '');
      if (c.edge === WorldEdge.AT && object?.kind === WorldObjectKind.OCCUPANT) {
        return 'occupants leave with their scene';
      }
      return '';
    }
    const fact = c.after!;
    const basis = basisLabel(fact.basis, fact.origin);
    return fact.said > fact.start ? `${basis} later, at ${momentLabel(world, fact.said)}` : basis;
  });
}
