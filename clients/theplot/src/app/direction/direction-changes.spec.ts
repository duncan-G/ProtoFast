import { SceneElement } from '../stories/model/scene-element';
import { SceneElementType } from '../stories/model/scene-element-type';
import { directionChanges } from './direction-changes';
import { emptyDirection } from './model/scene-direction';
import { blankPanel } from './panels';

function rows(...spec: [string, SceneElementType, string?][]): SceneElement[] {
  return spec.map(([id, type, text], position) => ({
    id,
    sceneId: 's',
    position,
    type,
    text: text ?? null,
    locationId: null,
    timeOfDay: null,
    speakerId: null,
    extension: null,
    parenthetical: null,
    transition: null,
    mentions: [],
  }));
}

const SCENE = rows(
  ['H', 'Heading'],
  ['a', 'Action', 'Rain.'],
  ['b', 'Dialogue', "Don't move."],
  ['c', 'Dialogue', 'I never do.'],
);

const cue = (elementId: string, performance: string) => ({
  id: elementId + '-cue',
  elementId,
  performance,
  gapBeforeMs: 0,
  interrupted: false,
  narratorId: null,
  edited: false,
  audio: null,
});

describe('directionChanges', () => {
  it('finds nothing in an undirected scene', () => {
    expect(directionChanges(emptyDirection('s'), SCENE)).toEqual([]);
  });

  it('reports changed lines, missing cues and uncovered rows in scene order, action included', () => {
    const direction = {
      ...emptyDirection('s'),
      cues: [cue('b', "[whispers] Don't you move.")],
      panels: [blankPanel(SCENE, 0, 1)],
    };
    expect(directionChanges(direction, SCENE).map((c) => [c.kind, c.elementId])).toEqual([
      ['cue-missing', 'a'],
      ['line-changed', 'b'],
      ['uncovered', 'b'],
      ['cue-missing', 'c'],
      ['uncovered', 'c'],
    ]);
  });

  it('accepts a cue whose words match through tags and pause punctuation', () => {
    const direction = {
      ...emptyDirection('s'),
      cues: [cue('a', 'Rain.'), cue('b', "[sad] Don't… move."), cue('c', 'I never do.')],
    };
    expect(directionChanges(direction, SCENE)).toEqual([]);
  });
});
