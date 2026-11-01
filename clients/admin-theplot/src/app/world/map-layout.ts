import { WorldEdge } from '../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import type { World } from './world';

export interface MapPoint {
  x: number;
  y: number;
}

interface Spring {
  a: number;
  b: number;
  strength: number;
}

const ITERATIONS = 400;
const GOLDEN_ANGLE = 2.399963;
const SPREAD_ITERATIONS = 150;
const LABEL_CHAR = 5.6;
const LABEL_HEIGHT = 30;

/**
 * Laid out once for the whole story so nothing moves with the playhead. Places pull together when
 * a way ever joins them, one is inside the other, or one scene cuts to the other.
 */
export function layoutMap(
  world: World,
  width: number,
  height: number,
  padding = 48,
): Map<string, MapPoint> {
  const ids = [...world.reply.locations]
    .sort(
      (a, b) =>
        (world.firstVisit.get(a.id) ?? Number.MAX_SAFE_INTEGER) -
          (world.firstVisit.get(b.id) ?? Number.MAX_SAFE_INTEGER) || a.name.localeCompare(b.name),
    )
    .map((l) => l.id);
  const index = new Map(ids.map((id, i) => [id, i]));
  const n = ids.length;
  if (!n) {
    return new Map();
  }

  const springs = new Map<string, Spring>();
  const join = (from: string, to: string, strength: number) => {
    const a = index.get(from);
    const b = index.get(to);
    if (a === undefined || b === undefined || a === b) {
      return;
    }
    const key = a < b ? `${a}:${b}` : `${b}:${a}`;
    const spring = springs.get(key);
    if (spring) {
      spring.strength = Math.max(spring.strength, strength);
    } else {
      springs.set(key, { a, b, strength });
    }
  };
  for (const fact of world.facts) {
    if (fact.edge === WorldEdge.CONNECTS && fact.place && fact.other) {
      join(fact.place.locationId, fact.other.locationId, 1);
    }
  }
  for (const location of world.reply.locations) {
    if (location.insideLocationId) {
      join(location.id, location.insideLocationId, 2);
    }
  }
  const scenes = world.reply.scenes;
  for (let i = 1; i < scenes.length; i++) {
    if (scenes[i - 1].locationId && scenes[i].locationId) {
      join(scenes[i - 1].locationId, scenes[i].locationId, 0.6);
    }
  }

  const k = Math.sqrt((width * height) / n) * 0.75;
  const x = ids.map((_, i) => Math.sqrt(i + 0.5) * k * 0.5 * Math.cos(i * GOLDEN_ANGLE));
  const y = ids.map((_, i) => Math.sqrt(i + 0.5) * k * 0.5 * Math.sin(i * GOLDEN_ANGLE));
  for (let step = 0; step < ITERATIONS; step++) {
    const temperature = k * (1 - step / ITERATIONS) * 0.5 + 0.5;
    const dx = new Array<number>(n).fill(0);
    const dy = new Array<number>(n).fill(0);
    for (let a = 0; a < n; a++) {
      for (let b = a + 1; b < n; b++) {
        const ex = x[a] - x[b] || 0.01;
        const ey = y[a] - y[b] || 0.01;
        const d2 = Math.max(ex * ex + ey * ey, 1);
        const push = (k * k) / d2;
        dx[a] += ex * push;
        dy[a] += ey * push;
        dx[b] -= ex * push;
        dy[b] -= ey * push;
      }
    }
    for (const { a, b, strength } of springs.values()) {
      const ex = x[a] - x[b];
      const ey = y[a] - y[b];
      const d = Math.max(Math.hypot(ex, ey), 0.01);
      const pull = (d / k) * strength;
      dx[a] -= ex * pull;
      dy[a] -= ey * pull;
      dx[b] += ex * pull;
      dy[b] += ey * pull;
    }
    for (let i = 0; i < n; i++) {
      dx[i] -= x[i] * 0.02;
      dy[i] -= y[i] * 0.02;
      const d = Math.max(Math.hypot(dx[i], dy[i]), 0.01);
      const move = Math.min(d, temperature);
      x[i] += (dx[i] / d) * move;
      y[i] += (dy[i] / d) * move;
    }
  }

  const fit = (values: number[], size: number) => {
    const min = Math.min(...values);
    const span = Math.max(...values) - min;
    return values.map((v) =>
      span ? padding + ((v - min) / span) * (size - padding * 2) : size / 2,
    );
  };
  const fx = fit(x, width);
  const fy = fit(y, height);
  const names = new Map(world.reply.locations.map((l) => [l.id, l.name]));
  const widths = ids.map((id) => Math.min(names.get(id)?.length ?? 0, 22) * LABEL_CHAR + 8);
  spread(fx, fy, widths, width, height, padding);
  return new Map(ids.map((id, i) => [id, { x: fx[i], y: fy[i] }]));
}

/** Nudges places apart until no two labels overlap, along whichever axis needs the smaller move. */
function spread(
  x: number[],
  y: number[],
  widths: number[],
  width: number,
  height: number,
  padding: number,
): void {
  for (let step = 0; step < SPREAD_ITERATIONS; step++) {
    let moved = false;
    for (let a = 0; a < x.length; a++) {
      for (let b = a + 1; b < x.length; b++) {
        const dx = x[b] - x[a];
        const dy = y[b] - y[a];
        const overlapX = (widths[a] + widths[b]) / 2 - Math.abs(dx);
        const overlapY = LABEL_HEIGHT - Math.abs(dy);
        if (overlapX <= 0 || overlapY <= 0) {
          continue;
        }
        moved = true;
        if (overlapY < overlapX) {
          const push = (overlapY / 2) * (dy < 0 ? -1 : 1);
          y[a] -= push;
          y[b] += push;
        } else {
          const push = (overlapX / 2) * (dx < 0 ? -1 : 1);
          x[a] -= push;
          x[b] += push;
        }
      }
    }
    for (let i = 0; i < x.length; i++) {
      x[i] = Math.min(Math.max(x[i], padding), width - padding);
      y[i] = Math.min(Math.max(y[i], padding), height - padding);
    }
    if (!moved) {
      return;
    }
  }
}
