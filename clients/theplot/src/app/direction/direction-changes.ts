import { SceneElement } from '../stories/model/scene-element';
import { SceneElementType } from '../stories/model/scene-element-type';
import { Cue } from './model/cue';
import { SceneDirection } from './model/scene-direction';
import { frameSpans, uncovered } from './frames';
import { sameWords } from './performance';

/**
 * What the story text has outgrown in the director layer, found by reading both: a line whose
 * words no longer match its cue, a line without a cue, a row no frame pictures.
 */
export type DirectionChange =
  | { kind: 'line-changed'; elementId: string; cue: Cue }
  | { kind: 'cue-missing'; elementId: string }
  | { kind: 'uncovered'; elementId: string };

/** Read aloud: dialogue by its speaker, narration and action by a narrator. */
export function isSpoken(type: SceneElementType): boolean {
  return type === 'Dialogue' || type === 'Narration' || type === 'Action';
}

export function directionChanges(
  direction: SceneDirection,
  elements: readonly SceneElement[],
): DirectionChange[] {
  const changes: DirectionChange[] = [];
  const cues = new Map(direction.cues.map((c) => [c.elementId, c]));
  const directed = direction.cues.length > 0 || direction.frames.length > 0;
  for (const element of elements) {
    if (!isSpoken(element.type)) {
      continue;
    }
    const cue = cues.get(element.id);
    if (!cue) {
      if (directed) {
        changes.push({ kind: 'cue-missing', elementId: element.id });
      }
    } else if (!sameWords(cue.performance, element.text ?? '')) {
      changes.push({ kind: 'line-changed', elementId: element.id, cue });
    }
  }
  if (direction.frames.length > 0) {
    for (const index of uncovered(frameSpans(direction.frames, elements), elements.length)) {
      changes.push({ kind: 'uncovered', elementId: elements[index].id });
    }
  }
  const order = new Map(elements.map((e, i) => [e.id, i]));
  return changes.sort((a, b) => order.get(a.elementId)! - order.get(b.elementId)!);
}
