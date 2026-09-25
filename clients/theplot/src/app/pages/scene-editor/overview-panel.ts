import { ChangeDetectionStrategy, Component, computed, inject, output } from '@angular/core';
import { Avatar } from './avatar';
import { initials, plural, settingPrefix } from './format';
import { SceneEditorStore } from './scene-editor-store';

@Component({
  selector: 'app-overview-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar],
  host: { class: 'se-overview', 'aria-label': 'Scene overview' },
  template: `
    <section>
      <div class="se-kicker se-overview-title">Scene flow</div>
      @for (step of flow(); track step.id) {
        @if (step.transition) {
          <div class="se-flow-trans">↓ {{ step.transition }}</div>
        }
        <button type="button" class="se-flow-item" (click)="jump.emit(step.id)">
          @if (step.hue !== null) {
            <span class="hue-fill" [style.--hue]="step.hue"></span>
          } @else {
            <span class="se-flow-bar"></span>
          }
          <span>
            <span class="se-flow-slug" [class.is-missing]="step.hue === null">{{
              step.label
            }}</span>
            <span class="se-flow-meta">{{ step.time }} · {{ step.beats }}</span>
          </span>
        </button>
      } @empty {
        <p class="se-flow-meta m-0">No headings yet.</p>
      }
    </section>

    <section>
      <div class="se-kicker se-overview-title">
        <span>Characters in scene</span
        ><span class="text-[var(--se-muted-2)]"
          >{{ cast().length }} of {{ store.characters().length }}</span
        >
      </div>
      @for (entry of cast(); track entry.character.id) {
        <div class="se-stat">
          <app-avatar
            [shape]="store.kindOf(entry.character).avatarShape"
            [hue]="entry.character.hue"
            [initials]="entry.initials"
            [size]="24"
          />
          <span class="se-stat-name">{{ entry.character.name }}</span>
          <span class="se-stat-meta">{{ entry.meta }}</span>
        </div>
      } @empty {
        <p class="se-flow-meta m-0">Nobody speaks or is mentioned yet.</p>
      }
    </section>

    <section>
      <div class="se-kicker se-overview-title">Props in play</div>
      @for (entry of props(); track entry.name) {
        <div class="se-stat">
          <span class="se-stat-name se-script font-bold text-[13px]">◆ {{ entry.name }}</span>
          <span class="se-stat-meta" [class.is-unused]="entry.unused">{{ entry.meta }}</span>
        </div>
      } @empty {
        <p class="se-flow-meta m-0">The library has no props.</p>
      }
    </section>
  `,
})
export class OverviewPanel {
  protected readonly store = inject(SceneEditorStore);

  readonly jump = output<string>();

  protected readonly flow = computed(() => {
    const locations = this.store.locationById();
    let transition: string | null = null;
    const steps: {
      id: string;
      transition: string | null;
      hue: number | null;
      label: string;
      time: string;
      beats: string;
    }[] = [];
    for (const row of this.store.rows()) {
      const element = row.element;
      if (element.type === 'Transition') {
        transition = element.transition ?? 'Transition';
      } else if (element.type === 'Heading') {
        const location = element.locationId ? locations.get(element.locationId) : undefined;
        steps.push({
          id: element.id,
          transition,
          hue: location?.hue ?? null,
          label: location ? `${settingPrefix(location.setting)} ${location.name}` : 'No location',
          time: element.timeOfDay ?? 'no time',
          beats: plural(row.beats, 'beat'),
        });
        transition = null;
      }
    }
    return steps;
  });

  protected readonly cast = computed(() => {
    const { lines, mentions } = this.store.stats();
    return this.store
      .characters()
      .map((character) => ({
        character,
        lines: lines.get(character.id) ?? 0,
        mentions: mentions.get(character.id) ?? 0,
      }))
      .filter((c) => c.lines + c.mentions > 0)
      .sort((a, b) => b.lines - a.lines || b.mentions - a.mentions)
      .map((c) => ({
        character: c.character,
        initials: initials(c.character.name),
        meta: `${plural(c.lines, 'line')} · ${c.mentions} @`,
      }));
  });

  protected readonly props = computed(() => {
    const mentions = this.store.stats().mentions;
    return this.store.props().map((prop) => {
      const count = mentions.get(prop.id) ?? 0;
      return {
        name: prop.name,
        unused: count === 0,
        meta: count ? plural(count, 'reference') : 'unused',
      };
    });
  });
}
