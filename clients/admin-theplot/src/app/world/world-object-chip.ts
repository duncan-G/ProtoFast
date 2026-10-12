import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import type { WorldFact } from '../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import { KIND_CHIP, KIND_HINT, KIND_LABEL, momentLabel } from './labels';
import { WorldSession } from './world-session';

@Component({
  selector: 'app-world-object',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'inline-flex max-w-full' },
  template: `
    @if (object(); as object) {
      <button
        type="button"
        class="inline-flex max-w-full flex-wrap items-center gap-x-1 rounded-md border px-1.5 py-0.5 text-left text-xs ring-1 ring-inset transition hover:brightness-95"
        [class]="kindClass()"
        [class.border-transparent]="!carried()"
        [class.border-dashed]="carried()"
        [class.border-gray-400]="carried()"
        [class.opacity-60]="carried() || gone()"
        [class.line-through]="gone()"
        [class.outline-2]="changed()"
        [class.outline-offset-1]="changed()"
        [class.outline-emerald-500]="changed()"
        [title]="tooltip()"
        (click)="session.inspect({ kind: 'object', id: object.id })"
      >
        <span class="font-medium">{{ object.name }}</span>
        @for (state of states(); track state.fact.key) {
          <span
            class="rounded px-1 text-[11px]"
            [class.bg-white/70]="!state.changed"
            [class.bg-emerald-100]="state.changed"
            [class.text-emerald-900]="state.changed"
            >{{ state.fact.property }}: {{ state.fact.value }}</span
          >
        }
      </button>
    }
  `,
})
export class WorldObjectChip {
  protected readonly session = inject(WorldSession);

  readonly id = input.required<string>();
  readonly fact = input<WorldFact>();
  readonly states = input<{ fact: WorldFact; changed: boolean }[]>([]);
  readonly changed = input(false);
  readonly carried = input(false);
  readonly gone = input(false);

  protected readonly object = computed(() => this.session.world()?.objects.get(this.id()));
  protected readonly kindClass = computed(() => KIND_CHIP[this.object()?.kind ?? 0]);
  protected readonly tooltip = computed(() => {
    const object = this.object();
    const world = this.session.world();
    if (!object || !world) {
      return '';
    }
    const kind = object.kind;
    const lines = [`${object.name} — ${KIND_LABEL[kind].toLowerCase()}, ${KIND_HINT[kind]}`];
    const fact = this.fact();
    if (fact) {
      lines.push(`Since ${momentLabel(world, fact.start)}`);
      if (this.carried()) {
        lines.push(
          fact.said > this.session.moment()
            ? `First mentioned at ${momentLabel(world, fact.said)}`
            : 'Not mentioned in this scene: carried forward',
        );
      }
    }
    return lines.join('\n');
  });
}
