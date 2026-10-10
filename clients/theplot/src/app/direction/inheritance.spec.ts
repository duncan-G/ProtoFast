import { Character } from '../stories/model/character';
import {
  extraOnScreenOf,
  framingOf,
  inViewOf,
  joinChanges,
  onScreenOf,
  overrideCount,
  wardrobeOf,
  withFrameExtra,
  withPlacement,
} from './inheritance';
import { Frame } from './model/frame';
import { blankFrameExtra } from './model/frame-extra';
import { blankAppearance } from './model/scene-appearance';
import { emptyDirection, SceneDirection } from './model/scene-direction';
import { blankPropState } from './model/scene-prop-state';
import { blankSceneExtra } from './model/scene-extra';

function frame(patch: Partial<Frame> = {}): Frame {
  return {
    id: 'p',
    position: 0,
    firstElementId: 'a',
    lastElementId: 'b',
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
    edited: true,
    locked: false,
    image: null,
    ...patch,
  };
}

const MARA: Character = {
  id: 'mara',
  storyId: 's',
  name: 'Mara',
  kind: 'Human',
  hue: 20,
  description: null,
  appearance: null,
  voice: null,
  wardrobes: [
    { id: 'field', label: 'Field gear', description: '' },
    { id: 'rain', label: 'Rain cloak', description: '' },
  ],
};

function scene(patch: Partial<SceneDirection> = {}): SceneDirection {
  return { ...emptyDirection('s'), shot: 'medium', ...patch };
}

describe('inheritance', () => {
  it('takes the scene framing until the frame sets its own', () => {
    expect(framingOf(scene(), frame(), 'shot')).toEqual({
      value: 'medium',
      overridden: false,
    });
    expect(framingOf(scene(), frame({ shot: 'wide' }), 'shot')).toEqual({
      value: 'wide',
      overridden: true,
    });
    expect(framingOf(scene(), null, 'angle')).toEqual({
      value: null,
      overridden: false,
    });
  });

  it('puts everyone on screen unless the scene or the frame takes them off', () => {
    const direction = scene({
      appearances: [{ ...blankAppearance('mara'), onScreen: false }],
    });
    expect(onScreenOf(scene(), frame(), 'bolt').value).toBe(true);
    expect(onScreenOf(direction, frame(), 'mara')).toEqual({
      value: false,
      overridden: false,
    });
    const shown = frame({
      placements: withPlacement(frame(), 'mara', { onScreen: true }),
    });
    expect(onScreenOf(direction, shown, 'mara')).toEqual({
      value: true,
      overridden: true,
    });
  });

  it('keeps props in view unless the scene or the frame hides them', () => {
    const direction = scene({
      propStates: [{ ...blankPropState('radio'), inView: false }],
    });
    expect(inViewOf(scene(), frame(), 'radio').value).toBe(true);
    expect(inViewOf(direction, frame(), 'radio').value).toBe(false);
    expect(
      inViewOf(direction, frame({ props: [{ propId: 'radio', inView: true }] }), 'radio'),
    ).toEqual({ value: true, overridden: true });
  });

  it('shows the scene’s extras unless the scene or the frame takes them off', () => {
    const direction = scene({ extras: [blankSceneExtra('crowd')] });
    expect(extraOnScreenOf(direction, frame(), 'crowd')).toEqual({
      value: true,
      overridden: false,
    });
    const off = frame({ extras: [{ ...blankFrameExtra('crowd'), onScreen: false }] });
    expect(extraOnScreenOf(direction, off, 'crowd')).toEqual({ value: false, overridden: true });
    const hidden = scene({ extras: [{ ...blankSceneExtra('crowd'), onScreen: false }] });
    expect(extraOnScreenOf(hidden, frame(), 'crowd').value).toBe(false);
  });

  it('wears the first wardrobe on the sheet, then the scene’s, then the frame’s', () => {
    expect(wardrobeOf(scene(), frame(), MARA)).toEqual({
      wardrobeId: 'field',
      source: 'library',
    });
    const direction = scene({
      appearances: [{ ...blankAppearance('mara'), wardrobeId: 'rain' }],
    });
    expect(wardrobeOf(direction, frame(), MARA)).toEqual({
      wardrobeId: 'rain',
      source: 'scene',
    });
    const own = frame({
      placements: withPlacement(frame(), 'mara', { wardrobeId: 'field' }),
    });
    expect(wardrobeOf(direction, own, MARA)).toEqual({
      wardrobeId: 'field',
      source: 'frame',
    });
  });

  it('drops a placement that no longer says anything', () => {
    const shown = frame({
      placements: withPlacement(frame(), 'mara', { onScreen: false }),
    });
    expect(shown.placements).toHaveLength(1);
    expect(withPlacement(shown, 'mara', { onScreen: null })).toEqual([]);
  });

  it('keeps an extra staged in a frame while it follows the scene for being on screen', () => {
    const direction = scene({ extras: [blankSceneExtra('crowd')] });
    const staged = frame({
      extras: withFrameExtra(frame(), 'crowd', { screen: 'right', pose: 'the nearest one' }),
    });
    expect(staged.extras).toHaveLength(1);
    expect(extraOnScreenOf(direction, staged, 'crowd')).toEqual({ value: true, overridden: false });
    const cleared = withFrameExtra(staged, 'crowd', { screen: '', pose: '' });
    expect(cleared).toEqual([]);
  });

  it('adds text changes up in order and counts what a frame overrides', () => {
    expect(joinChanges('Hood up.', '', ' Soaked. ')).toBe('Hood up. Soaked.');
    const busy = frame({
      shot: 'close',
      locationChange: 'Fog.',
      placements: [
        ...withPlacement(frame(), 'mara', { onScreen: false }),
        ...withPlacement(frame(), 'bolt', { appearanceChange: 'Dented.' }),
      ],
    });
    expect(overrideCount(busy)).toBe(4);
  });
});
