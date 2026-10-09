import { SceneElement } from '../stories/model/scene-element';
import { SceneElementType } from '../stories/model/scene-element-type';
import { Frame } from './model/frame';
import { blankFrame, cover, mergeUp, frameSpans, splitAt, uncovered } from './frames';

function rows(...types: [string, SceneElementType][]): SceneElement[] {
  return types.map(([id, type], position) => ({
    id,
    sceneId: 's',
    position,
    type,
    text: null,
    locationId: type === 'Heading' ? 'docks' : null,
    timeOfDay: type === 'Heading' ? 'NIGHT' : null,
    speakerId: null,
    extension: null,
    parenthetical: null,
    transition: null,
    mentions: [],
  }));
}

const SCENE = rows(
  ['H', 'Heading'],
  ['a', 'Action'],
  ['b', 'Dialogue'],
  ['c', 'Dialogue'],
  ['T', 'Transition'],
);

const frame = (first: string, last: string, extra: Partial<Frame> = {}): Frame => ({
  ...blankFrame(SCENE, 0, 0),
  firstElementId: first,
  lastElementId: last,
  edited: false,
  ...extra,
});

const ranges = (frames: Frame[] | null) =>
  frames?.map((p) => `${p.firstElementId}-${p.lastElementId}:${p.position}`);

describe('frame spans', () => {
  it('orders frames by their first row and finds the gaps', () => {
    const spans = frameSpans([frame('c', 'T'), frame('H', 'a')], SCENE);
    expect(spans.map((s) => [s.first, s.last])).toEqual([
      [0, 1],
      [3, 4],
    ]);
    expect(uncovered(spans, SCENE.length)).toEqual([2]);
  });

  it('takes the heading in force for a new frame and follows the scene for the rest', () => {
    expect(blankFrame(SCENE, 2, 3)).toMatchObject({
      firstElementId: 'b',
      lastElementId: 'c',
      locationId: 'docks',
      timeOfDay: 'NIGHT',
      shot: null,
      angle: null,
      movement: null,
      placements: [],
      props: [],
      edited: true,
    });
  });
});

describe('frame operations', () => {
  const frames = [frame('H', 'c', { beat: 'All of it' }), frame('T', 'T')];

  it('splits a frame so a new one starts at the row', () => {
    const split = splitAt(frames, SCENE, 2);
    expect(ranges(split)).toEqual(['H-a:0', 'b-c:1', 'T-T:2']);
    expect(split![0].beat).toBe('All of it');
    expect(split![0].edited).toBe(true);
    expect(splitAt(frames, SCENE, 0)).toBeNull();
  });

  it('merges a frame into the one before it', () => {
    expect(ranges(mergeUp(frames, SCENE, 4))).toEqual(['H-T:0']);
    expect(mergeUp(frames, SCENE, 1)).toBeNull();
  });

  it('covers the uncovered run around a row', () => {
    const gappy = [frame('H', 'H'), frame('T', 'T')];
    expect(ranges(cover(gappy, SCENE, 2))).toEqual(['H-H:0', 'a-c:1', 'T-T:2']);
    expect(cover(gappy, SCENE, 0)).toBeNull();
  });
});
