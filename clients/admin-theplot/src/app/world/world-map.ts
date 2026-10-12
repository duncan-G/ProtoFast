import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { WorldEdge } from '../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import { hue, initials, placeName } from './labels';
import { layoutMap } from './map-layout';
import { WorldSession } from './world-session';

const WIDTH = 960;
const HEIGHT = 560;
const MAX_FACES = 7;

@Component({
  selector: 'app-world-map',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="rounded-2xl border border-gray-200 bg-white p-2 shadow-sm">
      <svg
        [attr.viewBox]="'0 0 ' + width + ' ' + height"
        class="block h-auto w-full"
        role="img"
        aria-label="Map of the story's places"
      >
        <defs>
          <marker
            id="world-map-arrow"
            viewBox="0 0 8 8"
            refX="7"
            refY="4"
            markerWidth="7"
            markerHeight="7"
            orient="auto-start-reverse"
          >
            <path d="M0 0 L8 4 L0 8 z" class="fill-indigo-500" />
          </marker>
        </defs>

        @for (link of nesting(); track link.id) {
          <line
            [attr.x1]="link.x1"
            [attr.y1]="link.y1"
            [attr.x2]="link.x2"
            [attr.y2]="link.y2"
            class="stroke-gray-300"
            stroke-width="1"
            stroke-dasharray="1 3"
            stroke-linecap="round"
          />
        }

        @for (way of ways(); track way.key) {
          <line
            [attr.x1]="way.x1"
            [attr.y1]="way.y1"
            [attr.x2]="way.x2"
            [attr.y2]="way.y2"
            [class]="way.changed ? 'stroke-emerald-500' : 'stroke-gray-400'"
            [attr.stroke-width]="way.changed ? 2.5 : 1.25"
          >
            <title>{{ way.title }}</title>
          </line>
        }

        @if (cut(); as cut) {
          <line
            [attr.x1]="cut.x1"
            [attr.y1]="cut.y1"
            [attr.x2]="cut.x2"
            [attr.y2]="cut.y2"
            class="stroke-indigo-500"
            stroke-width="2"
            stroke-dasharray="6 4"
            marker-end="url(#world-map-arrow)"
          />
        }

        @for (node of nodes(); track node.id) {
          <g
            [attr.transform]="'translate(' + node.x + ' ' + node.y + ')'"
            class="cursor-pointer"
            (click)="session.focus(node.id)"
          >
            <title>{{ node.title }}</title>
            @if (node.current) {
              <circle [attr.r]="node.r + 7" class="animate-pulse fill-indigo-500/15" />
            }
            <circle
              [attr.r]="node.r"
              [attr.fill]="node.fill"
              [attr.stroke]="node.stroke"
              [attr.stroke-width]="node.current || node.pinned ? 3 : 1.25"
              [attr.stroke-dasharray]="node.known ? null : '2 2'"
            />
            <text
              [attr.y]="node.r + 12"
              text-anchor="middle"
              class="pointer-events-none select-none"
              [class]="
                node.current
                  ? 'fill-gray-900 font-semibold'
                  : node.known
                    ? 'fill-gray-700'
                    : 'fill-gray-400'
              "
              font-size="10"
            >
              {{ node.label }}
            </text>
            @for (face of node.faces; track face.id) {
              <g
                [attr.transform]="'translate(' + face.x + ' ' + -(node.r + 8) + ')'"
                [attr.opacity]="face.here ? 1 : 0.45"
              >
                <circle r="5.5" [attr.fill]="face.fill" class="stroke-white" stroke-width="1" />
                <text
                  y="2.2"
                  text-anchor="middle"
                  font-size="5.5"
                  class="pointer-events-none fill-gray-900 font-semibold select-none"
                >
                  {{ face.initials }}
                </text>
                <title>{{ face.name }}</title>
              </g>
            }
            @if (node.more) {
              <text
                [attr.x]="node.moreX"
                [attr.y]="-(node.r + 6)"
                font-size="8"
                class="pointer-events-none fill-gray-500"
              >
                +{{ node.more }}
              </text>
            }
          </g>
        }
      </svg>
    </div>
    <div class="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-gray-500">
      <span class="inline-flex items-center gap-1"
        ><span class="h-3 w-3 rounded-full bg-indigo-300 ring-2 ring-indigo-500"></span> this
        scene's place</span
      >
      <span class="inline-flex items-center gap-1"
        ><span class="h-3 w-3 rounded-full bg-linear-to-br from-amber-300 to-sky-300"></span> a
        place the story has reached</span
      >
      <span class="inline-flex items-center gap-1"
        ><span class="h-3 w-3 rounded-full border border-dashed border-gray-400 bg-white"></span>
        not reached yet</span
      >
      <span class="inline-flex items-center gap-1"
        ><span class="h-0.5 w-5 bg-gray-400"></span> a way between places</span
      >
      <span class="inline-flex items-center gap-1"
        ><span class="h-0.5 w-5 border-t-2 border-dotted border-gray-300"></span> inside</span
      >
      <span class="inline-flex items-center gap-1"
        ><span class="h-0.5 w-5 border-t-2 border-dashed border-indigo-500"></span> the cut from the
        last scene</span
      >
      <span>Faces: the cast, where each was last seen. Click a place to open it.</span>
    </div>
  `,
})
export class WorldMap {
  protected readonly session = inject(WorldSession);
  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;

  private readonly layout = computed(() => {
    const world = this.session.world();
    return world ? layoutMap(world, WIDTH, HEIGHT) : new Map();
  });

  private readonly sceneCounts = computed(() => {
    const counts = new Map<string, number>();
    for (const scene of this.session.world()?.reply.scenes ?? []) {
      counts.set(scene.locationId, (counts.get(scene.locationId) ?? 0) + 1);
    }
    return counts;
  });

  protected readonly nesting = computed(() => {
    const world = this.session.world();
    const at = this.layout();
    return (world?.reply.locations ?? []).flatMap((l) => {
      const a = at.get(l.id);
      const b = at.get(l.insideLocationId);
      return a && b ? [{ id: l.id, x1: a.x, y1: a.y, x2: b.x, y2: b.y }] : [];
    });
  });

  protected readonly ways = computed(() => {
    const world = this.session.world();
    const at = this.layout();
    const moment = this.session.moment();
    const seen = new Map<
      string,
      {
        key: string;
        x1: number;
        y1: number;
        x2: number;
        y2: number;
        changed: boolean;
        title: string;
      }
    >();
    if (!world) {
      return [];
    }
    for (const fact of this.session.holding()) {
      if (fact.edge !== WorldEdge.CONNECTS || !fact.place || !fact.other) {
        continue;
      }
      const from = fact.place.locationId;
      const to = fact.other.locationId;
      const a = at.get(from);
      const b = at.get(to);
      if (from === to || !a || !b) {
        continue;
      }
      const key = from < to ? `${from}:${to}` : `${to}:${from}`;
      const via = world.objects.get(fact.viaObjectId)?.name;
      const line = `${placeName(world, fact.place)} ⇄ ${placeName(world, fact.other)}${via ? ` through ${via}` : ''}`;
      const existing = seen.get(key);
      if (existing) {
        existing.changed ||= fact.start === moment;
        existing.title += `\n${line}`;
      } else {
        seen.set(key, {
          key,
          x1: a.x,
          y1: a.y,
          x2: b.x,
          y2: b.y,
          changed: fact.start === moment,
          title: line,
        });
      }
    }
    return [...seen.values()];
  });

  protected readonly cut = computed(() => {
    const world = this.session.world();
    const scene = this.session.scene();
    if (!world || scene === 0) {
      return null;
    }
    const at = this.layout();
    const a = at.get(world.reply.scenes[scene - 1].locationId);
    const b = at.get(world.reply.scenes[scene].locationId);
    if (!a || !b || a === b) {
      return null;
    }
    const d = Math.hypot(b.x - a.x, b.y - a.y) || 1;
    const r = this.radius(world.reply.scenes[scene].locationId) + 6;
    return { x1: a.x, y1: a.y, x2: b.x - ((b.x - a.x) / d) * r, y2: b.y - ((b.y - a.y) / d) * r };
  });

  protected readonly nodes = computed(() => {
    const world = this.session.world();
    if (!world) {
      return [];
    }
    const at = this.layout();
    const moment = this.session.moment();
    const current = this.session.sceneLocation();
    const pinned = this.session.pinned();
    const touched = new Set<string>();
    const objects = new Map<string, number>();
    for (const fact of this.session.holding()) {
      if (fact.place) {
        touched.add(fact.place.locationId);
      }
      if (fact.other) {
        touched.add(fact.other.locationId);
      }
      if (fact.edge === WorldEdge.AT && fact.place) {
        objects.set(fact.place.locationId, (objects.get(fact.place.locationId) ?? 0) + 1);
      }
    }
    const cast = new Map<
      string,
      { id: string; name: string; fill: string; initials: string; here: boolean }[]
    >();
    for (const member of this.session.cast()) {
      const place = member.fact?.place?.locationId;
      if (place) {
        cast.set(place, [
          ...(cast.get(place) ?? []),
          {
            id: member.character.id,
            name: member.character.name,
            fill: hue(member.character.hue, 0.85, 0.09),
            initials: initials(member.character.name),
            here: member.inScene,
          },
        ]);
      }
    }

    return world.reply.locations.flatMap((location) => {
      const point = at.get(location.id);
      if (!point) {
        return [];
      }
      const first = world.firstVisit.get(location.id);
      const visited = first !== undefined && first <= moment;
      const known = visited || touched.has(location.id);
      const r = this.radius(location.id);
      const people = cast.get(location.id) ?? [];
      const shown = people.slice(0, MAX_FACES);
      const scenes = this.sceneCounts().get(location.id) ?? 0;
      return [
        {
          id: location.id,
          x: point.x,
          y: point.y,
          r,
          known,
          current: location.id === current,
          pinned: location.id === pinned,
          fill: known ? hue(location.hue, visited ? 0.78 : 0.92, visited ? 0.12 : 0.04) : '#ffffff',
          stroke:
            location.id === current
              ? 'oklch(0.51 0.23 277)'
              : location.id === pinned
                ? '#111827'
                : known
                  ? hue(location.hue, 0.6, 0.12)
                  : '#9ca3af',
          label: location.name.length > 22 ? `${location.name.slice(0, 21)}…` : location.name,
          title: [
            location.name,
            visited
              ? `${scenes} ${scenes === 1 ? 'scene' : 'scenes'} here`
              : 'The story has not been here yet',
            `${objects.get(location.id) ?? 0} things here now`,
            people.length ? `Last seen here: ${people.map((p) => p.name).join(', ')}` : '',
          ]
            .filter(Boolean)
            .join('\n'),
          faces: shown.map((p, i) => ({ ...p, x: (i - (shown.length - 1) / 2) * 11 })),
          more: people.length - shown.length,
          moreX: ((shown.length - 1) / 2) * 11 + 8,
        },
      ];
    });
  });

  private radius(locationId: string): number {
    return 6 + 1.8 * Math.sqrt(this.sceneCounts().get(locationId) ?? 0);
  }
}
