import {
  WorldEdge,
  WorldObjectKind,
  type GetWorldReply,
  type WorldCharacter,
  type WorldFact,
  type WorldLocation,
  type WorldObject,
  type WorldPart,
  type WorldPlace,
  type WorldView,
} from '../../lib/gen/Admin/Theplot/theplot_world_models_pb';

export interface Moment {
  scene: number;
  /** -1 for the scene's opening. */
  beat: number;
}

export interface World {
  readonly reply: GetWorldReply;
  readonly moments: readonly Moment[];
  readonly sceneStarts: readonly number[];
  readonly locations: ReadonlyMap<string, WorldLocation>;
  readonly parts: ReadonlyMap<string, WorldPart>;
  readonly objects: ReadonlyMap<string, WorldObject>;
  readonly characters: ReadonlyMap<string, WorldCharacter>;
  readonly facts: readonly WorldFact[];
  readonly startsAt: readonly (readonly number[])[];
  readonly endsAt: readonly (readonly number[])[];
  /** The opening of the first scene set in each location. */
  readonly firstVisit: ReadonlyMap<string, number>;
  /** Each scene's views, as indexes into reply.views. */
  readonly sceneViews: readonly (readonly number[])[];
  /** The view each moment is filmed from, -1 for none; an opening shows its scene's first view. */
  readonly viewAt: readonly number[];
}

export type WorldRef =
  | { kind: 'location'; id: string }
  | { kind: 'part'; id: string }
  | { kind: 'object'; id: string }
  | { kind: 'character'; id: string };

export function worldOf(reply: GetWorldReply): World {
  const moments: Moment[] = [];
  const sceneStarts: number[] = [];
  const firstVisit = new Map<string, number>();
  reply.scenes.forEach((scene, index) => {
    sceneStarts.push(moments.length);
    if (scene.locationId && !firstVisit.has(scene.locationId)) {
      firstVisit.set(scene.locationId, moments.length);
    }
    moments.push({ scene: index, beat: -1 });
    scene.beats.forEach((_, beat) => moments.push({ scene: index, beat }));
  });

  const startsAt: number[][] = moments.map(() => []);
  const endsAt: number[][] = moments.map(() => []);
  reply.facts.forEach((fact, index) => {
    if (fact.start >= fact.end) {
      return;
    }
    startsAt[fact.start]?.push(index);
    endsAt[fact.end]?.push(index);
  });

  const sceneViews: number[][] = reply.scenes.map(() => []);
  const viewAt = moments.map(() => -1);
  reply.views.forEach((view, index) => {
    sceneViews[view.scene]?.push(index);
    for (const moment of view.moments) {
      viewAt[moment] = index;
    }
  });
  sceneStarts.forEach((start, scene) => {
    viewAt[start] = sceneViews[scene][0] ?? -1;
  });

  return {
    reply,
    moments,
    sceneStarts,
    locations: new Map(reply.locations.map((l) => [l.id, l])),
    parts: new Map(reply.parts.map((p) => [p.id, p])),
    objects: new Map(reply.objects.map((o) => [o.id, o])),
    characters: new Map(reply.characters.map((c) => [c.id, c])),
    facts: reply.facts,
    startsAt,
    endsAt,
    firstVisit,
    sceneViews,
    viewAt,
  };
}

/** Matches a view across world reads while its parts keep their ids. */
export function viewKey(view: WorldView): string {
  const place = (p: WorldPlace | undefined) => (p ? `${p.locationId}/${p.partId}` : '');
  return `${view.scene}|${place(view.camera)}|${place(view.toward)}|${view.state}`;
}

/** A fact about a place itself: its fixtures and dressing, their states, its ways. Characters, props and occupants are action. */
export function isPhysical(world: World, fact: WorldFact): boolean {
  switch (fact.edge) {
    case WorldEdge.AT:
    case WorldEdge.STATE: {
      const kind = world.objects.get(fact.objectId)?.kind;
      return (
        !fact.characterId && (kind === WorldObjectKind.FIXTURE || kind === WorldObjectKind.DRESSING)
      );
    }
    case WorldEdge.CONNECTS:
      return true;
    default:
      return false;
  }
}

export function holdingAt(world: World, moment: number): WorldFact[] {
  return world.facts.filter((f) => f.start <= moment && moment < f.end);
}

/** The last beat up to the moment that said the fact, or said it again; -1 when none has yet. */
export function lastMention(fact: WorldFact, moment: number): number {
  let last = fact.said <= moment ? fact.said : -1;
  for (const again of fact.confirmed) {
    if (again <= moment && again > last) {
      last = again;
    }
  }
  return last;
}

export type ChangeKind = 'added' | 'changed' | 'removed';

export interface Change {
  kind: ChangeKind;
  edge: WorldEdge;
  before?: WorldFact;
  after?: WorldFact;
}

const EDGE_ORDER: Record<WorldEdge, number> = {
  [WorldEdge.CHARACTER_AT]: 0,
  [WorldEdge.AT]: 1,
  [WorldEdge.STATE]: 2,
  [WorldEdge.CONNECTS]: 3,
  [WorldEdge.BEAT_AT]: 4,
  [WorldEdge.CAMERA]: 5,
  [WorldEdge.UNSPECIFIED]: 6,
};

/** What the moment changed: facts that start or end there, paired up by subject and edge. */
export function changesAt(world: World, moment: number): Change[] {
  const before = new Map<string, WorldFact>();
  const after = new Map<string, WorldFact>();
  for (const index of world.endsAt[moment] ?? []) {
    before.set(world.facts[index].key, world.facts[index]);
  }
  for (const index of world.startsAt[moment] ?? []) {
    after.set(world.facts[index].key, world.facts[index]);
  }

  const changes: Change[] = [];
  for (const [key, fact] of after) {
    const old = before.get(key);
    changes.push({ kind: old ? 'changed' : 'added', edge: fact.edge, before: old, after: fact });
  }
  for (const [key, fact] of before) {
    if (!after.has(key)) {
      changes.push({ kind: 'removed', edge: fact.edge, before: fact });
    }
  }
  return changes.sort((a, b) => EDGE_ORDER[a.edge] - EDGE_ORDER[b.edge]);
}

/** Counted as changesAt pairs them; only changes to a place count, not the action in it. */
export function changeCounts(world: World): number[] {
  return world.moments.map((_, moment) => {
    const keys = new Set<string>();
    for (const index of [...world.startsAt[moment], ...world.endsAt[moment]]) {
      const fact = world.facts[index];
      if (isPhysical(world, fact)) {
        keys.add(fact.key);
      }
    }
    return keys.size;
  });
}

export interface Placed<T> {
  entity: T;
  fact: WorldFact;
  /** It started at this moment. */
  changed: boolean;
  /** Not said in this scene yet: carried forward from before, or a fixture mentioned later. */
  carried: boolean;
}

export interface StageObject extends Placed<WorldObject> {
  states: { fact: WorldFact; changed: boolean }[];
}

export interface StageWay {
  fact: WorldFact;
  to: WorldPlace;
  via?: WorldObject;
  changed: boolean;
}

export interface StageTile {
  /** Unset for the location as a whole. */
  part?: WorldPart;
  children: StageTile[];
  objects: StageObject[];
  characters: Placed<WorldCharacter>[];
  ways: StageWay[];
  /** The beat at this moment happens here. */
  action: boolean;
  /** The camera stands here at this moment; its other place is where it looks. */
  camera?: WorldFact;
}

export interface Departure {
  fact: WorldFact;
  object?: WorldObject;
  character?: WorldCharacter;
  /** Where it went, when it went somewhere known. */
  to?: WorldPlace;
}

export interface Stage {
  location: WorldLocation;
  whole: StageTile;
  tiles: StageTile[];
  departures: Departure[];
  inside?: WorldLocation;
  within: WorldLocation[];
  scenes: number[];
}

export function partsOf(world: World, locationId: string): WorldPart[] {
  return world.reply.parts
    .filter((p) => p.locationId === locationId)
    .sort(
      (a, b) =>
        (a.position ?? Number.MAX_SAFE_INTEGER) - (b.position ?? Number.MAX_SAFE_INTEGER) ||
        a.name.localeCompare(b.name),
    );
}

/** A location at a moment: its parts with what is in each, its ways out, and what just left. */
export function stageOf(
  world: World,
  locationId: string,
  moment: number,
  holding: readonly WorldFact[],
): Stage | null {
  const location = world.locations.get(locationId);
  if (!location) {
    return null;
  }

  const sceneStart = world.sceneStarts[world.moments[moment]?.scene ?? 0] ?? 0;
  const placed = <T>(entity: T, fact: WorldFact): Placed<T> => ({
    entity,
    fact,
    changed: fact.start === moment,
    carried: lastMention(fact, moment) < sceneStart,
  });
  const tile = (part?: WorldPart): StageTile => ({
    part,
    children: [],
    objects: [],
    characters: [],
    ways: [],
    action: false,
  });

  const whole = tile();
  const tiles = new Map<string, StageTile>();
  const parts = partsOf(world, locationId);
  for (const part of parts) {
    tiles.set(part.id, tile(part));
  }
  const top: StageTile[] = [];
  for (const part of parts) {
    const parent = part.parentPartId ? tiles.get(part.parentPartId) : undefined;
    (parent ? parent.children : top).push(tiles.get(part.id)!);
  }
  const tileAt = (place: WorldPlace | undefined): StageTile =>
    (place?.partId && tiles.get(place.partId)) || whole;

  const states = new Map<string, WorldFact[]>();
  for (const fact of holding) {
    if (fact.edge === WorldEdge.STATE) {
      states.set(fact.objectId, [...(states.get(fact.objectId) ?? []), fact]);
    }
  }

  let action: WorldFact | undefined;
  for (const fact of holding) {
    const here = fact.place?.locationId === locationId;
    switch (fact.edge) {
      case WorldEdge.AT: {
        const object = world.objects.get(fact.objectId);
        if (here && object) {
          tileAt(fact.place).objects.push({
            ...placed(object, fact),
            states: (states.get(object.id) ?? [])
              .sort((a, b) => a.property.localeCompare(b.property))
              .map((s) => ({ fact: s, changed: s.start === moment })),
          });
        }
        break;
      }
      case WorldEdge.CHARACTER_AT: {
        const character = world.characters.get(fact.characterId);
        if (here && character) {
          tileAt(fact.place).characters.push(placed(character, fact));
        }
        break;
      }
      case WorldEdge.CONNECTS: {
        const via = fact.viaObjectId ? world.objects.get(fact.viaObjectId) : undefined;
        const changed = fact.start === moment;
        if (here && fact.other) {
          tileAt(fact.place).ways.push({ fact, to: fact.other, via, changed });
        }
        if (fact.other?.locationId === locationId && fact.place) {
          tileAt(fact.other).ways.push({ fact, to: fact.place, via, changed });
        }
        break;
      }
      case WorldEdge.BEAT_AT:
        if (here && (!action || fact.start > action.start)) {
          action = fact;
        }
        break;
      case WorldEdge.CAMERA:
        if (here) {
          tileAt(fact.place).camera = fact;
        }
        break;
    }
  }
  if (action) {
    tileAt(action.place).action = true;
  }

  const byName = (a: { entity: { name: string } }, b: { entity: { name: string } }) =>
    a.entity.name.localeCompare(b.entity.name);
  for (const t of [whole, ...tiles.values()]) {
    t.objects.sort(byName);
    t.characters.sort(byName);
  }

  const departures: Departure[] = [];
  for (const index of world.endsAt[moment] ?? []) {
    const fact = world.facts[index];
    if (
      (fact.edge !== WorldEdge.AT && fact.edge !== WorldEdge.CHARACTER_AT) ||
      fact.place?.locationId !== locationId
    ) {
      continue;
    }
    const next = holding.find((f) => f.key === fact.key);
    if (next?.place?.locationId === locationId) {
      continue;
    }
    departures.push({
      fact,
      object: world.objects.get(fact.objectId),
      character: world.characters.get(fact.characterId),
      to: next?.place,
    });
  }

  return {
    location,
    whole,
    tiles: top,
    departures,
    inside: world.locations.get(location.insideLocationId),
    within: world.reply.locations.filter((l) => l.insideLocationId === locationId),
    scenes: world.reply.scenes.flatMap((s, i) => (s.locationId === locationId ? [i] : [])),
  };
}

/** Where the scene's beats last said they are, else its heading's place: a HOSPITAL scene's beats can be in its LOBBY. */
export function actionAt(
  world: World,
  moment: number,
  holding: readonly WorldFact[],
): string | null {
  const scene = world.moments[moment]?.scene ?? 0;
  const start = world.sceneStarts[scene] ?? 0;
  let at = -1;
  let location = world.reply.scenes[scene]?.locationId || null;
  for (const fact of holding) {
    const said = lastMention(fact, moment);
    if (fact.edge === WorldEdge.BEAT_AT && fact.place && said >= start && said > at) {
      at = said;
      location = fact.place.locationId;
    }
  }
  return location;
}

export interface CastMember {
  character: WorldCharacter;
  fact?: WorldFact;
  /** The scene the character was last said to be at the place, or -1. */
  lastSeenScene: number;
  /** Placed in the scene at this moment. */
  inScene: boolean;
}

/** Every character and where each was last seen at the moment: the current scene's first. */
export function castAt(world: World, moment: number, holding: readonly WorldFact[]): CastMember[] {
  const scene = world.moments[moment]?.scene ?? 0;
  const where = new Map<string, WorldFact>();
  for (const fact of holding) {
    if (fact.edge === WorldEdge.CHARACTER_AT) {
      where.set(fact.characterId, fact);
    }
  }
  return world.reply.characters
    .map((character) => {
      const fact = where.get(character.id);
      const last = fact ? lastMention(fact, moment) : -1;
      const lastSeenScene = last >= 0 ? world.moments[last].scene : -1;
      return { character, fact, lastSeenScene, inScene: lastSeenScene === scene };
    })
    .sort(
      (a, b) =>
        Number(b.inScene) - Number(a.inScene) ||
        b.lastSeenScene - a.lastSeenScene ||
        a.character.name.localeCompare(b.character.name),
    );
}

/** Every fact about one subject, in story order; a place's are only the physical ones. */
export function historyOf(world: World, ref: WorldRef): WorldFact[] {
  const at = (p: WorldPlace | undefined) =>
    ref.kind === 'location' ? p?.locationId === ref.id : p?.partId === ref.id;
  const things =
    ref.kind === 'location' || ref.kind === 'part'
      ? new Set(
          world.facts
            .filter((f) => f.edge === WorldEdge.AT && isPhysical(world, f) && at(f.place))
            .map((f) => f.objectId),
        )
      : new Set<string>();
  const about = (f: WorldFact): boolean => {
    switch (ref.kind) {
      case 'object':
        return f.objectId === ref.id || f.viaObjectId === ref.id;
      case 'character':
        return f.characterId === ref.id;
      case 'location':
      case 'part':
        return (
          isPhysical(world, f) &&
          (at(f.place) || at(f.other) || (f.edge === WorldEdge.STATE && things.has(f.objectId)))
        );
    }
  };
  return world.facts.filter((f) => about(f) && f.start < f.end).sort((a, b) => a.start - b.start);
}

export const KIND_ORDER = [
  WorldObjectKind.FIXTURE,
  WorldObjectKind.PROP,
  WorldObjectKind.DRESSING,
  WorldObjectKind.OCCUPANT,
] as const;
