import { create, type MessageInitShape } from '@bufbuild/protobuf';
import {
  GetWorldReplySchema,
  WorldEdge,
  WorldFactSchema,
  WorldObjectKind,
} from '../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import { layoutMap } from './map-layout';
import { castAt, changeCounts, changesAt, holdingAt, stageOf, worldOf } from './world';

// Moments: 0 opens scene 1 in the wing, 1–2 are its beats; 3 opens scene 2 in the lot, 4 is its beat.
const reply = create(GetWorldReplySchema, {
  storyTitle: 'Trauma',
  scenes: [
    {
      id: 's1',
      title: 'Wing',
      locationId: 'wing',
      beats: [
        { id: 'b1', text: 'A gurney blocks the hallway.' },
        { id: 'b2', text: 'Bryan wheels it to the end.' },
      ],
    },
    { id: 's2', title: 'Lot', locationId: 'lot', beats: [{ id: 'b3', text: 'Snow.' }] },
  ],
  locations: [
    { id: 'wing', name: 'TRAUMA WING', hue: 20 },
    { id: 'lot', name: 'PARKING LOT', hue: 200, exterior: true },
  ],
  parts: [
    { id: 'end', locationId: 'wing', name: 'hallway: end', position: 2 },
    { id: 'middle', locationId: 'wing', name: 'hallway: middle', position: 1 },
  ],
  objects: [
    { id: 'gurney', name: 'gurney', kind: WorldObjectKind.PROP },
    { id: 'doors', name: 'doors', kind: WorldObjectKind.FIXTURE },
  ],
  characters: [
    { id: 'bryan', name: 'Bryan' },
    { id: 'ana', name: 'Ana' },
  ],
  facts: [
    fact(WorldEdge.AT, 'at:gurney', 1, 2, {
      objectId: 'gurney',
      place: { locationId: 'wing', partId: 'middle' },
    }),
    fact(WorldEdge.AT, 'at:gurney', 2, 5, {
      objectId: 'gurney',
      place: { locationId: 'wing', partId: 'end' },
    }),
    fact(WorldEdge.AT, 'at:doors', 0, 5, {
      objectId: 'doors',
      place: { locationId: 'wing' },
      said: 2,
    }),
    fact(WorldEdge.STATE, 'state:doors:open', 2, 5, {
      objectId: 'doors',
      property: 'open',
      value: 'shut',
    }),
    fact(WorldEdge.CHARACTER_AT, 'character:bryan', 1, 3, {
      characterId: 'bryan',
      place: { locationId: 'wing', partId: 'middle' },
    }),
    fact(WorldEdge.CHARACTER_AT, 'character:bryan', 3, 5, {
      characterId: 'bryan',
      place: { locationId: 'lot' },
    }),
    fact(WorldEdge.CONNECTS, 'connects:end|lot', 2, 5, {
      place: { locationId: 'wing', partId: 'end' },
      other: { locationId: 'lot' },
      viaObjectId: 'doors',
    }),
    fact(WorldEdge.BEAT_AT, 'beat:wing', 2, 5, { place: { locationId: 'wing', partId: 'end' } }),
  ],
});

function fact(
  edge: WorldEdge,
  key: string,
  start: number,
  end: number,
  rest: MessageInitShape<typeof WorldFactSchema>,
): MessageInitShape<typeof WorldFactSchema> {
  return { edge, key, start, end, said: start, ...rest };
}

describe('world', () => {
  const world = worldOf(reply);

  it('gives each scene an opening moment followed by its beats', () => {
    expect(world.moments.length).toBe(5);
    expect(world.sceneStarts).toEqual([0, 3]);
    expect(world.moments[4]).toEqual({ scene: 1, beat: 0 });
    expect(world.firstVisit.get('lot')).toBe(3);
  });

  it('pairs a fact that ends with the one that replaces it as a change', () => {
    const changes = changesAt(world, 2);
    const moved = changes.find((c) => c.edge === WorldEdge.AT);
    expect(moved?.kind).toBe('changed');
    expect(moved?.before?.place?.partId).toBe('middle');
    expect(moved?.after?.place?.partId).toBe('end');
    expect(changes.filter((c) => c.kind === 'added').map((c) => c.edge)).toEqual([
      WorldEdge.STATE,
      WorldEdge.CONNECTS,
      WorldEdge.BEAT_AT,
    ]);
    expect(changeCounts(world)[2]).toBe(3);
  });

  it('lays a location out by its parts, marking what changed, what carried over and where the beat is', () => {
    const stage = stageOf(world, 'wing', 2, holdingAt(world, 2))!;
    expect(stage.tiles.map((t) => t.part?.name)).toEqual(['hallway: middle', 'hallway: end']);
    const [middle, end] = stage.tiles;
    expect(end.action).toBe(true);
    expect(end.objects.map((o) => [o.entity.name, o.changed])).toEqual([['gurney', true]]);
    expect(end.ways.map((w) => w.to.locationId)).toEqual(['lot']);
    expect(middle.characters.map((c) => c.entity.name)).toEqual(['Bryan']);
    const doors = stage.whole.objects[0];
    expect([doors.entity.name, doors.states[0].fact.value, doors.carried]).toEqual([
      'doors',
      'shut',
      false,
    ]);
  });

  it('dashes a fixture the scene has not mentioned yet, and names who just left and for where', () => {
    const opening = stageOf(world, 'wing', 0, holdingAt(world, 0))!;
    expect(opening.whole.objects[0].carried).toBe(true);

    const left = stageOf(world, 'wing', 3, holdingAt(world, 3))!;
    expect(left.departures.map((d) => [d.character?.name, d.to?.locationId])).toEqual([
      ['Bryan', 'lot'],
    ]);

    const lot = stageOf(world, 'lot', 3, holdingAt(world, 3))!;
    expect(lot.whole.ways.map((w) => w.to.partId)).toEqual(['end']);
  });

  it('puts the scene’s cast first and the unplaced last', () => {
    const cast = castAt(world, 4, holdingAt(world, 4));
    expect(cast.map((m) => [m.character.name, m.inScene, m.lastSeenScene])).toEqual([
      ['Bryan', true, 1],
      ['Ana', false, -1],
    ]);
  });

  it('places every location on the map the same way each time, inside the bounds', () => {
    const first = layoutMap(world, 400, 300, 20);
    const second = layoutMap(world, 400, 300, 20);
    expect([...first.entries()]).toEqual([...second.entries()]);
    for (const { x, y } of first.values()) {
      expect(x).toBeGreaterThanOrEqual(20);
      expect(x).toBeLessThanOrEqual(380);
      expect(y).toBeGreaterThanOrEqual(20);
      expect(y).toBeLessThanOrEqual(280);
    }
  });
});
