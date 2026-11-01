import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { WorldEdge, type WorldFact } from '../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import {
  basisLabel,
  beatText,
  hue,
  KIND_CHIP,
  KIND_HINT,
  KIND_LABEL,
  momentLabel,
  partName,
  placeName,
} from './labels';
import { historyOf, type Change, type World, type WorldRef } from './world';
import { WorldChange } from './world-change';
import { WorldSession } from './world-session';

const MAX_LANES = 24;

interface Bar {
  left: number;
  width: number;
  color: string;
  title: string;
  start: number;
}

interface Lane {
  label: string;
  bars: Bar[];
}

@Component({
  selector: 'app-world-inspector',
  imports: [WorldChange],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'session.inspect(null)' },
  template: `
    @if (view(); as view) {
      <aside
        class="fixed inset-y-0 right-0 z-40 flex w-full max-w-md flex-col border-l border-gray-200 bg-white shadow-2xl"
        role="dialog"
        aria-labelledby="world-inspector-title"
      >
        <header class="flex items-start gap-3 border-b border-gray-200 px-5 py-4">
          <div class="min-w-0 flex-1">
            <p class="text-xs tracking-wide text-gray-500 uppercase">{{ view.kind }}</p>
            <h2 id="world-inspector-title" class="truncate text-lg font-semibold text-gray-900">
              {{ view.name }}
            </h2>
            @if (view.subtitle) {
              <p class="mt-0.5 text-xs text-gray-500">{{ view.subtitle }}</p>
            }
          </div>
          <button
            type="button"
            class="rounded-lg border border-gray-300 px-2.5 py-1 text-sm text-gray-700 hover:bg-gray-50"
            (click)="session.inspect(null)"
          >
            Close
          </button>
        </header>

        <div class="flex-1 overflow-y-auto px-5 py-4">
          @if (view.chip) {
            <p class="text-sm">
              <span
                class="rounded-md px-1.5 py-0.5 text-xs ring-1 ring-inset"
                [class]="view.chip"
                >{{ view.kindLabel }}</span
              >
              <span class="ml-2 text-gray-600">{{ view.hint }}</span>
            </p>
          }
          @if (view.description) {
            <p class="mt-2 text-sm text-gray-700">{{ view.description }}</p>
          }
          @if (view.contents) {
            <p class="mt-2 text-xs font-medium text-gray-500">What is in it</p>
            <p class="mt-0.5 text-sm whitespace-pre-line text-gray-700">{{ view.contents }}</p>
          }
          @if (view.aliases.length) {
            <p class="mt-2 text-xs text-gray-500">Also called: {{ view.aliases.join(', ') }}</p>
          }

          <h3 class="mt-5 text-xs font-semibold tracking-wide text-gray-500 uppercase">
            Over the story
          </h3>
          @if (lanes().length) {
            <div class="mt-2 space-y-1.5">
              @for (lane of lanes(); track $index) {
                <div class="flex items-center gap-2">
                  <span class="w-28 shrink-0 truncate text-xs text-gray-600" [title]="lane.label">{{
                    lane.label
                  }}</span>
                  <div class="relative h-3.5 flex-1 rounded bg-gray-100">
                    @for (bar of lane.bars; track $index) {
                      <button
                        type="button"
                        class="absolute inset-y-0 rounded-sm hover:brightness-90"
                        [style.left.%]="bar.left"
                        [style.width.%]="bar.width"
                        [style.background]="bar.color"
                        [title]="bar.title"
                        (click)="session.go(bar.start)"
                      ></button>
                    }
                    <span
                      class="pointer-events-none absolute -inset-y-0.5 w-0.5 bg-indigo-600"
                      [style.left.%]="playhead()"
                    ></span>
                  </div>
                </div>
              }
              @if (hiddenLanes()) {
                <p class="text-xs text-gray-400">and {{ hiddenLanes() }} more</p>
              }
            </div>
            <p class="mt-1.5 text-[11px] text-gray-400">
              Each bar is a stretch of the story a fact held. Click one to go there.
            </p>
          } @else {
            <p class="mt-2 text-sm text-gray-500">No beat has said anything about it.</p>
          }

          <h3 class="mt-6 text-xs font-semibold tracking-wide text-gray-500 uppercase">
            History
            <span class="ml-1 font-normal text-gray-400 normal-case">{{ history().length }}</span>
          </h3>
          <ol class="mt-2 space-y-3">
            @for (row of history(); track $index) {
              <li
                class="rounded-lg border p-2.5"
                [class.border-indigo-300]="row.holds"
                [class.bg-indigo-50]="row.holds"
                [class.border-gray-200]="!row.holds"
              >
                <div class="flex items-center gap-2 text-xs">
                  <button
                    type="button"
                    class="font-medium text-indigo-600 hover:underline"
                    (click)="session.go(row.fact.start)"
                  >
                    {{ row.from }}
                  </button>
                  <span class="text-gray-400">→ {{ row.until }}</span>
                  @if (row.holds) {
                    <span class="ml-auto text-indigo-700">holds now</span>
                  }
                </div>
                <div class="mt-1">
                  <app-world-change [change]="row.change" />
                </div>
                @if (row.text) {
                  <p class="mt-1 line-clamp-3 text-xs text-gray-500 italic">
                    “{{ row.text }}”
                    @if (row.said !== row.fact.start) {
                      <span class="not-italic">({{ row.saidLabel }})</span>
                    }
                  </p>
                }
                @if (row.fact.confirmed.length) {
                  <p class="mt-1 text-[11px] text-gray-500">
                    Said again at
                    @for (again of row.fact.confirmed; track again; let last = $last) {
                      <button
                        type="button"
                        class="underline decoration-gray-300 hover:decoration-gray-600"
                        (click)="session.go(again)"
                      >
                        {{ label(again) }}</button
                      >{{ last ? '' : ', ' }}
                    }
                  </p>
                }
                @for (lost of row.conflicts; track $index) {
                  <p class="mt-1 text-[11px] text-amber-800">
                    Disagreed with at
                    <button type="button" class="underline" (click)="session.go(lost.moment)">
                      {{ label(lost.moment) }}
                    </button>
                    ({{ lost.basis }}), and lost
                  </p>
                }
              </li>
            } @empty {
              <li class="text-sm text-gray-500">Nothing yet.</li>
            }
          </ol>
        </div>
      </aside>
    }
  `,
})
export class WorldInspector {
  protected readonly session = inject(WorldSession);

  protected readonly view = computed(() => {
    const world = this.session.world();
    const ref = this.session.inspected();
    return world && ref ? describe(world, ref) : null;
  });

  private readonly facts = computed(() => {
    const world = this.session.world();
    const ref = this.session.inspected();
    return world && ref ? historyOf(world, ref) : [];
  });

  private readonly allLanes = computed(() => {
    const world = this.session.world();
    const ref = this.session.inspected();
    return world && ref ? lanesOf(world, ref, this.facts()) : [];
  });

  protected readonly lanes = computed(() => this.allLanes().slice(0, MAX_LANES));
  protected readonly hiddenLanes = computed(() => Math.max(this.allLanes().length - MAX_LANES, 0));

  protected readonly playhead = computed(() => {
    const total = this.session.world()?.moments.length ?? 1;
    return ((this.session.moment() + 0.5) / total) * 100;
  });

  protected readonly history = computed(() => {
    const world = this.session.world();
    if (!world) {
      return [];
    }
    const moment = this.session.moment();
    const conflicts = new Map<number, { moment: number; basis: string }[]>();
    for (const conflict of world.reply.conflicts) {
      conflicts.set(conflict.fact, [
        ...(conflicts.get(conflict.fact) ?? []),
        { moment: conflict.moment, basis: basisLabel(conflict.basis, conflict.origin) },
      ]);
    }
    return this.facts().map((fact) => ({
      fact,
      change: { kind: 'added', edge: fact.edge, after: fact } satisfies Change,
      from: momentLabel(world, fact.start),
      until: fact.end >= world.moments.length ? 'the end' : momentLabel(world, fact.end),
      holds: fact.start <= moment && moment < fact.end,
      said: fact.said,
      saidLabel: momentLabel(world, fact.said),
      text: beatText(world, fact.said),
      conflicts: conflicts.get(world.facts.indexOf(fact)) ?? [],
    }));
  });

  protected label(moment: number): string {
    const world = this.session.world();
    return world ? momentLabel(world, moment) : '';
  }
}

function describe(world: World, ref: WorldRef) {
  const base = {
    aliases: [] as string[],
    description: '',
    contents: '',
    chip: '',
    kindLabel: '',
    hint: '',
    subtitle: '',
  };
  switch (ref.kind) {
    case 'object': {
      const object = world.objects.get(ref.id);
      return {
        ...base,
        kind: 'Object',
        name: object?.name ?? 'Unknown object',
        aliases: object?.aliases ?? [],
        description: object?.description ?? '',
        chip: KIND_CHIP[object?.kind ?? 0],
        kindLabel: KIND_LABEL[object?.kind ?? 0],
        hint: KIND_HINT[object?.kind ?? 0],
      };
    }
    case 'character':
      return {
        ...base,
        kind: 'Character',
        name: world.characters.get(ref.id)?.name ?? 'Unknown character',
      };
    case 'location': {
      const location = world.locations.get(ref.id);
      const inside = world.locations.get(location?.insideLocationId ?? '');
      const scenes = world.reply.scenes.filter((s) => s.locationId === ref.id).length;
      return {
        ...base,
        kind: location?.exterior ? 'Place · exterior' : 'Place · interior',
        name: location?.name ?? 'Unknown place',
        description: location?.description ?? '',
        contents: location?.contents ?? '',
        subtitle: [
          inside ? `Inside ${inside.name}` : '',
          `${scenes} ${scenes === 1 ? 'scene' : 'scenes'} here`,
        ]
          .filter(Boolean)
          .join(' · '),
      };
    }
    case 'part': {
      const part = world.parts.get(ref.id);
      return {
        ...base,
        kind: 'Part of a place',
        name: partName(part),
        subtitle: world.locations.get(part?.locationId ?? '')?.name ?? '',
        description: part?.name ? part.description : '',
      };
    }
  }
}

function lanesOf(world: World, ref: WorldRef, facts: readonly WorldFact[]): Lane[] {
  const total = world.moments.length || 1;
  const bar = (fact: WorldFact, color: string, title: string): Bar => ({
    left: (fact.start / total) * 100,
    width: Math.max(((Math.min(fact.end, total) - fact.start) / total) * 100, 0.6),
    color,
    title: `${title}\n${momentLabel(world, fact.start)} → ${fact.end >= total ? 'the end' : momentLabel(world, fact.end)}`,
    start: fact.start,
  });
  const placeColor = (fact: WorldFact) =>
    hue(world.locations.get(fact.place?.locationId ?? '')?.hue ?? 0, 0.75, 0.12);
  const lanes = new Map<string, Lane>();
  const add = (key: string, label: string, b: Bar) => {
    const lane = lanes.get(key) ?? { label, bars: [] };
    lane.bars.push(b);
    lanes.set(key, lane);
  };

  if (ref.kind === 'location' || ref.kind === 'part') {
    if (ref.kind === 'location') {
      world.reply.scenes.forEach((scene, index) => {
        if (scene.locationId === ref.id) {
          const start = world.sceneStarts[index];
          add('scenes', 'Scenes here', {
            left: (start / total) * 100,
            width: Math.max(((scene.beats.length + 1) / total) * 100, 0.6),
            color: hue(world.locations.get(ref.id)?.hue ?? 0, 0.6, 0.14),
            title: `Scene ${index + 1} · ${scene.title}`,
            start,
          });
        }
      });
    }
    for (const fact of facts) {
      const here =
        ref.kind === 'location' ? fact.place?.locationId === ref.id : fact.place?.partId === ref.id;
      if (fact.edge === WorldEdge.AT && here) {
        const name = world.objects.get(fact.objectId)?.name ?? 'object';
        add(
          `o:${fact.objectId}`,
          name,
          bar(fact, placeColor(fact), `${name} at ${placeName(world, fact.place)}`),
        );
      } else if (fact.edge === WorldEdge.CHARACTER_AT && here) {
        const name = world.characters.get(fact.characterId)?.name ?? 'character';
        add(
          `c:${fact.characterId}`,
          name,
          bar(
            fact,
            hue(world.characters.get(fact.characterId)?.hue ?? 0, 0.8, 0.1),
            `${name} at ${placeName(world, fact.place)}`,
          ),
        );
      } else if (fact.edge === WorldEdge.STATE) {
        const name = `${world.objects.get(fact.objectId)?.name ?? 'object'}: ${fact.property}`;
        add(`s:${fact.key}`, name, bar(fact, valueColor(fact.value), `${name} ${fact.value}`));
      } else if (fact.edge === WorldEdge.CONNECTS) {
        const label = `⇄ ${placeName(world, fact.place?.locationId === ref.id || fact.place?.partId === ref.id ? fact.other : fact.place)}`;
        add(`w:${fact.key}`, label, bar(fact, '#9ca3af', label));
      }
    }
    return [...lanes.values()];
  }

  for (const fact of facts) {
    switch (fact.edge) {
      case WorldEdge.AT:
      case WorldEdge.CHARACTER_AT:
        add('where', 'Where', bar(fact, placeColor(fact), placeName(world, fact.place)));
        break;
      case WorldEdge.STATE:
        add(
          `state:${fact.property.toLowerCase()}`,
          fact.property,
          bar(fact, valueColor(fact.value), `${fact.property}: ${fact.value}`),
        );
        break;
      case WorldEdge.CONNECTS: {
        const label = `${placeName(world, fact.place)} ⇄ ${placeName(world, fact.other)}`;
        add('ways', 'A way between', bar(fact, '#9ca3af', label));
        break;
      }
    }
  }
  return [...lanes.values()];
}

function valueColor(value: string): string {
  let h = 0;
  for (const c of value.trim().toLowerCase()) {
    h = (h * 31 + c.charCodeAt(0)) % 360;
  }
  return hue(h, 0.78, 0.1);
}
