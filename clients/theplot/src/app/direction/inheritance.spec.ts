import { Character } from '../stories/model/character';
import {
  framingOf,
  inViewOf,
  joinChanges,
  onScreenOf,
  overrideCount,
  wardrobeOf,
  withPlacement,
} from './inheritance';
import { Panel } from './model/panel';
import { blankAppearance } from './model/scene-appearance';
import { emptyDirection, SceneDirection } from './model/scene-direction';
import { blankPropState } from './model/scene-prop-state';

function panel(patch: Partial<Panel> = {}): Panel {
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
    holdMs: null,
    placements: [],
    props: [],
    edited: true,
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
  it('takes the scene framing until the panel sets its own', () => {
    expect(framingOf(scene(), panel(), 'shot')).toEqual({ value: 'medium', overridden: false });
    expect(framingOf(scene(), panel({ shot: 'wide' }), 'shot')).toEqual({
      value: 'wide',
      overridden: true,
    });
    expect(framingOf(scene(), null, 'angle')).toEqual({ value: null, overridden: false });
  });

  it('puts everyone on screen unless the scene or the panel takes them off', () => {
    const direction = scene({ appearances: [{ ...blankAppearance('mara'), onScreen: false }] });
    expect(onScreenOf(scene(), panel(), 'bolt').value).toBe(true);
    expect(onScreenOf(direction, panel(), 'mara')).toEqual({ value: false, overridden: false });
    const shown = panel({ placements: withPlacement(panel(), 'mara', { onScreen: true }) });
    expect(onScreenOf(direction, shown, 'mara')).toEqual({ value: true, overridden: true });
  });

  it('keeps props in view unless the scene or the panel hides them', () => {
    const direction = scene({ propStates: [{ ...blankPropState('radio'), inView: false }] });
    expect(inViewOf(scene(), panel(), 'radio').value).toBe(true);
    expect(inViewOf(direction, panel(), 'radio').value).toBe(false);
    expect(
      inViewOf(direction, panel({ props: [{ propId: 'radio', inView: true }] }), 'radio'),
    ).toEqual({ value: true, overridden: true });
  });

  it('wears the first wardrobe on the sheet, then the scene’s, then the panel’s', () => {
    expect(wardrobeOf(scene(), panel(), MARA)).toEqual({ wardrobeId: 'field', source: 'library' });
    const direction = scene({ appearances: [{ ...blankAppearance('mara'), wardrobeId: 'rain' }] });
    expect(wardrobeOf(direction, panel(), MARA)).toEqual({ wardrobeId: 'rain', source: 'scene' });
    const own = panel({ placements: withPlacement(panel(), 'mara', { wardrobeId: 'field' }) });
    expect(wardrobeOf(direction, own, MARA)).toEqual({ wardrobeId: 'field', source: 'panel' });
  });

  it('drops a placement that no longer says anything', () => {
    const shown = panel({ placements: withPlacement(panel(), 'mara', { onScreen: false }) });
    expect(shown.placements).toHaveLength(1);
    expect(withPlacement(shown, 'mara', { onScreen: null })).toEqual([]);
  });

  it('adds text changes up in order and counts what a panel overrides', () => {
    expect(joinChanges('Hood up.', '', ' Soaked. ')).toBe('Hood up. Soaked.');
    const busy = panel({
      shot: 'close',
      locationChange: 'Fog.',
      placements: [
        ...withPlacement(panel(), 'mara', { onScreen: false }),
        ...withPlacement(panel(), 'bolt', { appearanceChange: 'Dented.' }),
      ],
    });
    expect(overrideCount(busy)).toBe(4);
  });
});
