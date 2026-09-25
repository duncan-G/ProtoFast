import { blockLength, canDrop, moveBlock, swapElement } from './scene-blocks';
import { SceneElement } from './model/scene-element';
import { SceneElementType } from './model/scene-element-type';

function rows(...types: [string, SceneElementType][]): SceneElement[] {
  return types.map(([id, type], position) => ({
    id,
    sceneId: 's',
    position,
    type,
    text: null,
    locationId: null,
    timeOfDay: null,
    speakerId: null,
    parenthetical: null,
    transition: null,
    mentions: [],
  }));
}

const ids = (elements: SceneElement[] | null) => elements?.map((e) => e.id);

// H1 a b T H2 c d
const SCENE = rows(
  ['H1', 'Heading'],
  ['a', 'Action'],
  ['b', 'Dialogue'],
  ['T', 'Transition'],
  ['H2', 'Heading'],
  ['c', 'Action'],
  ['d', 'Narration'],
);

describe('blockLength', () => {
  it('takes a heading with its beats, stopping at a transition or the next heading', () => {
    expect(blockLength(SCENE, 0)).toBe(3);
    expect(blockLength(SCENE, 4)).toBe(3);
  });

  it('moves a beat or a transition alone', () => {
    expect(blockLength(SCENE, 1)).toBe(1);
    expect(blockLength(SCENE, 3)).toBe(1);
  });
});

describe('moveBlock', () => {
  it('moves a heading and its beats to the end', () => {
    expect(ids(moveBlock(SCENE, 0, 7))).toEqual(['T', 'H2', 'c', 'd', 'H1', 'a', 'b']);
  });

  it('moves a later heading and its beats to the top', () => {
    expect(ids(moveBlock(SCENE, 4, 0))).toEqual(['H2', 'c', 'd', 'H1', 'a', 'b', 'T']);
  });

  it('moves a beat into another heading’s block', () => {
    expect(ids(moveBlock(SCENE, 1, 6))).toEqual(['H1', 'b', 'T', 'H2', 'c', 'a', 'd']);
  });

  it('refuses a drop inside or at the edges of the block itself', () => {
    for (const dropAt of [0, 1, 2, 3]) {
      expect(canDrop(SCENE, 0, dropAt)).toBe(false);
      expect(moveBlock(SCENE, 0, dropAt)).toBeNull();
    }
    expect(canDrop(SCENE, 0, 4)).toBe(true);
  });

  it('leaves the input untouched', () => {
    moveBlock(SCENE, 0, 7);
    expect(ids(SCENE)).toEqual(['H1', 'a', 'b', 'T', 'H2', 'c', 'd']);
  });
});

describe('swapElement', () => {
  it('swaps a row with its neighbour and stops at the ends', () => {
    expect(ids(swapElement(SCENE, 1, 1))).toEqual(['H1', 'b', 'a', 'T', 'H2', 'c', 'd']);
    expect(swapElement(SCENE, 0, -1)).toBeNull();
    expect(swapElement(SCENE, 6, 1)).toBeNull();
  });
});
