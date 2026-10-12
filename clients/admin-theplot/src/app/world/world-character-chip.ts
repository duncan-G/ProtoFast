import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import type { WorldFact } from '../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import { hue, initials, momentLabel, placeName } from './labels';
import { WorldSession } from './world-session';

@Component({
  selector: 'app-world-character',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'inline-flex' },
  template: `
    @if (character(); as character) {
      <button
        type="button"
        class="inline-flex items-center gap-1 rounded-full bg-white py-0.5 pr-2 pl-0.5 text-xs text-gray-900 ring-1 ring-gray-200 transition ring-inset hover:bg-gray-50"
        [class.opacity-50]="carried() || gone()"
        [class.line-through]="gone()"
        [class.outline-2]="changed()"
        [class.outline-offset-1]="changed()"
        [class.outline-emerald-500]="changed()"
        [title]="tooltip()"
        (click)="session.inspect({ kind: 'character', id: character.id })"
      >
        <span
          class="grid h-5 w-5 place-items-center rounded-full text-[9px] font-semibold text-gray-900"
          [style.background]="avatar()"
          >{{ initials(character.name) }}</span
        >
        <span class="font-medium">{{ character.name }}</span>
      </button>
    }
  `,
})
export class WorldCharacterChip {
  protected readonly session = inject(WorldSession);
  protected readonly initials = initials;

  readonly id = input.required<string>();
  readonly fact = input<WorldFact>();
  readonly changed = input(false);
  readonly carried = input(false);
  readonly gone = input(false);

  protected readonly character = computed(() => this.session.world()?.characters.get(this.id()));
  protected readonly avatar = computed(() => hue(this.character()?.hue ?? 0, 0.85, 0.09));
  protected readonly tooltip = computed(() => {
    const world = this.session.world();
    const fact = this.fact();
    if (!world || !fact) {
      return this.character()?.name ?? '';
    }
    const lines = [`At ${placeName(world, fact.place)} since ${momentLabel(world, fact.start)}`];
    if (this.carried()) {
      lines.push('Not placed in this scene: where they were last seen');
    }
    return lines.join('\n');
  });
}
