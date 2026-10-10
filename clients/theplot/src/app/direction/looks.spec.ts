import { heldIn, inViewOf, lookAt, wardrobeOf } from './inheritance';
import { Frame } from './model/frame';
import { LookChange } from './model/look-change';
import { blankAppearance } from './model/scene-appearance';
import { emptyDirection, SceneDirection } from './model/scene-direction';

const ORDER = new Map(['e0', 'e1', 'e2', 'e3', 'e4'].map((id, i) => [id, i]));

function frame(first: number, last: number): { frame: Frame; first: number; last: number } {
  return {
    first,
    last,
    frame: {
      id: `f${first}`,
      position: first,
      firstElementId: `e${first}`,
      lastElementId: `e${last}`,
      locationId: null,
      timeOfDay: null,
      beat: '',
      shot: null,
      angle: null,
      movement: null,
      locationChange: '',
      camera: '',
      holdMs: null,
      placements: [],
      props: [],
      extras: [],
      edited: false,
      locked: false,
      image: null,
    },
  };
}

function change(characterId: string, elementId: string, holding: string[]): LookChange {
  return {
    id: '',
    characterId,
    elementId,
    wardrobeId: null,
    wardrobeChange: '',
    appearanceChange: '',
    holding,
    reason: null,
    edited: false,
    image: null,
  };
}

/** Bryan opens carrying the cooler; the doctor takes it on e3. */
function handover(): SceneDirection {
  return {
    ...emptyDirection('s'),
    appearances: [
      { ...blankAppearance('bryan'), holding: ['cooler'] },
      { ...blankAppearance('doctor'), onScreen: false },
    ],
    lookChanges: [change('doctor', 'e3', ['cooler']), change('bryan', 'e3', [])],
  };
}

describe('looks', () => {
  it('holds the opening look until the line that changes it', () => {
    const direction = handover();
    expect(lookAt(direction, 'bryan', ORDER, null).holding).toEqual(['cooler']);
    expect(lookAt(direction, 'bryan', ORDER, 2).holding).toEqual(['cooler']);
    expect(lookAt(direction, 'bryan', ORDER, 3).holding).toEqual([]);
    expect(lookAt(direction, 'doctor', ORDER, 4).holding).toEqual(['cooler']);
    expect(lookAt(direction, 'doctor', ORDER, 4).change?.elementId).toBe('e3');
  });

  it('keeps a carried prop in view with its holder and out of view without one', () => {
    const direction = handover();
    const before = frame(0, 2);
    expect(
      inViewOf(direction, before.frame, 'cooler', heldIn(direction, before, ORDER)).value,
    ).toBe(true);
    // The doctor is off screen, so once they hold it the cooler leaves with them.
    const after = frame(3, 4);
    expect(inViewOf(direction, after.frame, 'cooler', heldIn(direction, after, ORDER)).value).toBe(
      false,
    );
  });

  it('wears the wardrobe a change puts on', () => {
    const direction: SceneDirection = {
      ...emptyDirection('s'),
      lookChanges: [{ ...change('mara', 'e2', []), wardrobeId: 'rain' }],
    };
    const mara = {
      id: 'mara',
      storyId: 's',
      name: 'Mara',
      kind: 'Human',
      hue: 0,
      description: null,
      appearance: null,
      voice: null,
      wardrobes: [
        { id: 'field', label: 'Field gear', description: '' },
        { id: 'rain', label: 'Rain cloak', description: '' },
      ],
    };
    const look = lookAt(direction, 'mara', ORDER, 3);
    expect(wardrobeOf(direction, frame(3, 3).frame, mara, look)).toEqual({
      wardrobeId: 'rain',
      source: 'scene',
    });
  });
});
