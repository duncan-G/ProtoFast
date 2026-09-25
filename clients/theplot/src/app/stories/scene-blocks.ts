import { SceneElement } from './model/scene-element';

/** A heading moves with its beats, up to the next heading or transition. */
export function blockLength(elements: readonly SceneElement[], from: number): number {
  if (elements[from]?.type !== 'Heading') {
    return 1;
  }
  let end = from + 1;
  while (
    end < elements.length &&
    elements[end].type !== 'Heading' &&
    elements[end].type !== 'Transition'
  ) {
    end++;
  }
  return end - from;
}

/** `dropAt` is a gap index, 0…length. */
export function canDrop(elements: readonly SceneElement[], from: number, dropAt: number): boolean {
  if (from < 0 || from >= elements.length || dropAt < 0 || dropAt > elements.length) {
    return false;
  }
  const length = blockLength(elements, from);
  return dropAt < from || dropAt > from + length;
}

/** Null when the drop would change nothing. */
export function moveBlock(
  elements: readonly SceneElement[],
  from: number,
  dropAt: number,
): SceneElement[] | null {
  if (!canDrop(elements, from, dropAt)) {
    return null;
  }
  const length = blockLength(elements, from);
  const next = [...elements];
  const block = next.splice(from, length);
  next.splice(dropAt > from ? dropAt - length : dropAt, 0, ...block);
  return next;
}

export function swapElement(
  elements: readonly SceneElement[],
  index: number,
  delta: -1 | 1,
): SceneElement[] | null {
  const other = index + delta;
  if (index < 0 || index >= elements.length || other < 0 || other >= elements.length) {
    return null;
  }
  const next = [...elements];
  [next[index], next[other]] = [next[other], next[index]];
  return next;
}
