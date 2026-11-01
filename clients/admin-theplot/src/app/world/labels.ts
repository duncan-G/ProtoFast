import {
  WorldBasis,
  WorldObjectKind,
  WorldOrigin,
  type WorldPart,
  type WorldPlace,
  type WorldView,
} from '../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import type { World } from './world';

export function partName(part: WorldPart | undefined): string {
  if (!part) {
    return 'somewhere';
  }
  return part.name || part.description || 'an unnamed part';
}

export function placeName(
  world: World,
  place: Pick<WorldPlace, 'locationId' | 'partId'> | undefined,
): string {
  if (!place) {
    return 'nowhere known';
  }
  const location = world.locations.get(place.locationId)?.name ?? 'an unknown place';
  return place.partId ? `${location} › ${partName(world.parts.get(place.partId))}` : location;
}

/** A place seen from somewhere in `here`: a part of it by its name alone. */
export function placeFrom(world: World, place: WorldPlace, here: string): string {
  return place.partId && place.locationId === here
    ? partName(world.parts.get(place.partId))
    : placeName(world, place);
}

/** A later look of the same camera, after the place changed, is numbered: counter → door (2). */
export function viewLabel(world: World, view: WorldView): string {
  const here = view.camera?.locationId ?? '';
  const from = view.camera ? placeFrom(world, view.camera, here) : 'somewhere';
  const label = view.toward ? `${from} → ${placeFrom(world, view.toward, here)}` : from;
  return view.state ? `${label} (${view.state + 1})` : label;
}

/** The beats filmed at the moments, numbered in their scene, with runs shortened: 1–3, 7. */
export function beatRuns(world: World, moments: readonly number[]): string {
  const beats = moments.map((m) => (world.moments[m]?.beat ?? -1) + 1).filter((b) => b > 0);
  const runs: string[] = [];
  for (let i = 0; i < beats.length; i++) {
    let j = i;
    while (j + 1 < beats.length && beats[j + 1] === beats[j] + 1) {
      j++;
    }
    runs.push(j > i ? `${beats[i]}–${beats[j]}` : `${beats[i]}`);
    i = j;
  }
  return runs.join(', ');
}

export function heading(world: World, scene: number): string {
  const s = world.reply.scenes[scene];
  const location = s ? world.locations.get(s.locationId) : undefined;
  if (!location) {
    return 'No location';
  }
  const setting = location.exterior ? 'EXT.' : 'INT.';
  return s.timeOfDay
    ? `${setting} ${location.name} – ${s.timeOfDay}`
    : `${setting} ${location.name}`;
}

export function momentLabel(world: World, moment: number): string {
  const m = world.moments[moment];
  if (!m) {
    return 'The end';
  }
  return m.beat < 0
    ? `Scene ${m.scene + 1} · opening`
    : `Scene ${m.scene + 1} · beat ${m.beat + 1}`;
}

export function beatText(world: World, moment: number): string {
  const m = world.moments[moment];
  const beat = m && m.beat >= 0 ? world.reply.scenes[m.scene].beats[m.beat] : undefined;
  if (!beat) {
    return '';
  }
  return beat.speaker ? `${beat.speaker}: ${beat.text}` : beat.text;
}

export function hue(value: number, lightness = 0.72, chroma = 0.12): string {
  return `oklch(${lightness} ${chroma} ${value})`;
}

export function initials(name: string): string {
  const words = name
    .replace(/[^\p{L}\p{N} ]/gu, ' ')
    .trim()
    .split(/\s+/);
  return (
    (words[0]?.[0] ?? '') + (words.length > 1 ? words[words.length - 1][0] : (words[0]?.[1] ?? ''))
  ).toUpperCase();
}

export const KIND_LABEL: Record<WorldObjectKind, string> = {
  [WorldObjectKind.UNSPECIFIED]: 'Object',
  [WorldObjectKind.FIXTURE]: 'Fixture',
  [WorldObjectKind.PROP]: 'Prop',
  [WorldObjectKind.DRESSING]: 'Dressing',
  [WorldObjectKind.OCCUPANT]: 'Occupant',
};

export const KIND_HINT: Record<WorldObjectKind, string> = {
  [WorldObjectKind.UNSPECIFIED]: '',
  [WorldObjectKind.FIXTURE]: 'part of the set; there from the first scene in its place',
  [WorldObjectKind.PROP]: 'stays until a beat moves or removes it',
  [WorldObjectKind.DRESSING]: 'stays until a beat cleans or ends it',
  [WorldObjectKind.OCCUPANT]: 'leaves when its scene ends',
};

export const KIND_CHIP: Record<WorldObjectKind, string> = {
  [WorldObjectKind.UNSPECIFIED]: 'bg-gray-50 text-gray-800 ring-gray-300',
  [WorldObjectKind.FIXTURE]: 'bg-slate-100 text-slate-800 ring-slate-300',
  [WorldObjectKind.PROP]: 'bg-amber-50 text-amber-900 ring-amber-300',
  [WorldObjectKind.DRESSING]: 'bg-rose-50 text-rose-900 ring-rose-300',
  [WorldObjectKind.OCCUPANT]: 'bg-sky-50 text-sky-900 ring-sky-300',
};

export const KIND_SWATCH: Record<WorldObjectKind, string> = {
  [WorldObjectKind.UNSPECIFIED]: 'bg-gray-400',
  [WorldObjectKind.FIXTURE]: 'bg-slate-400',
  [WorldObjectKind.PROP]: 'bg-amber-400',
  [WorldObjectKind.DRESSING]: 'bg-rose-400',
  [WorldObjectKind.OCCUPANT]: 'bg-sky-400',
};

export function basisLabel(basis: WorldBasis, origin: WorldOrigin): string {
  if (origin === WorldOrigin.AUTHOR) {
    return 'author';
  }
  if (origin === WorldOrigin.RULE) {
    return 'rule';
  }
  return basis === WorldBasis.STATED ? 'stated' : 'implied';
}
