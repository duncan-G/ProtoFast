import { SceneElement } from '../stories/model/scene-element';
import { Frame } from './model/frame';

/** Where a frame sits in the scene: 0-based indexes into the elements, inclusive. */
export interface FrameSpan {
  frame: Frame;
  first: number;
  last: number;
}

/** Frames in scene order with their spans; a frame whose rows are gone is left out. */
export function frameSpans(
  frames: readonly Frame[],
  elements: readonly SceneElement[],
): FrameSpan[] {
  const index = new Map(elements.map((e, i) => [e.id, i]));
  return frames
    .flatMap((frame) => {
      const first = index.get(frame.firstElementId);
      const last = index.get(frame.lastElementId);
      return first === undefined || last === undefined
        ? []
        : [{ frame, first: Math.min(first, last), last: Math.max(first, last) }];
    })
    .sort((a, b) => a.first - b.first);
}

/** The span covering an element, by its index. */
export function spanAt(spans: readonly FrameSpan[], index: number): FrameSpan | null {
  return spans.find((s) => s.first <= index && index <= s.last) ?? null;
}

/** Indexes of elements no frame covers. */
export function uncovered(spans: readonly FrameSpan[], count: number): number[] {
  const gaps: number[] = [];
  for (let i = 0; i < count; i++) {
    if (!spanAt(spans, i)) {
      gaps.push(i);
    }
  }
  return gaps;
}

export function blankFrame(elements: readonly SceneElement[], first: number, last: number): Frame {
  const heading = headingAt(elements, first);
  return {
    id: '',
    position: 0,
    firstElementId: elements[first].id,
    lastElementId: elements[last].id,
    locationId: heading?.locationId ?? null,
    timeOfDay: heading?.timeOfDay ?? null,
    beat: '',
    shot: null,
    angle: null,
    movement: null,
    locationChange: '',
    holdMs: null,
    placements: [],
    props: [],
    edited: true,
  };
}

/** A new frame starts at `index`, taking the rest of the frame that covered it; null when it already starts one. */
export function splitAt(
  frames: readonly Frame[],
  elements: readonly SceneElement[],
  index: number,
): Frame[] | null {
  const spans = frameSpans(frames, elements);
  const span = spanAt(spans, index);
  if (!span || span.first === index) {
    return null;
  }
  const head = { ...span.frame, lastElementId: elements[index - 1].id, edited: true };
  const tail = blankFrame(elements, index, span.last);
  return renumber([...frames.filter((p) => p !== span.frame), head, tail], elements);
}

/** The frame at `index` joins the frame before it; null when there is none to join. */
export function mergeUp(
  frames: readonly Frame[],
  elements: readonly SceneElement[],
  index: number,
): Frame[] | null {
  const spans = frameSpans(frames, elements);
  const span = spanAt(spans, index);
  const previous = span && span.first > 0 ? spanAt(spans, span.first - 1) : null;
  if (!span || !previous) {
    return null;
  }
  const merged = { ...previous.frame, lastElementId: elements[span.last].id, edited: true };
  return renumber(
    [...frames.filter((p) => p !== span.frame && p !== previous.frame), merged],
    elements,
  );
}

/** Covers an uncovered element and the uncovered run around it with a new frame. */
export function cover(
  frames: readonly Frame[],
  elements: readonly SceneElement[],
  index: number,
): Frame[] | null {
  const spans = frameSpans(frames, elements);
  if (spanAt(spans, index)) {
    return null;
  }
  let first = index;
  while (first > 0 && !spanAt(spans, first - 1)) {
    first--;
  }
  let last = index;
  while (last < elements.length - 1 && !spanAt(spans, last + 1)) {
    last++;
  }
  return renumber([...frames, blankFrame(elements, first, last)], elements);
}

export function removeFrame(
  frames: readonly Frame[],
  elements: readonly SceneElement[],
  frame: Frame,
): Frame[] {
  return renumber(
    frames.filter((p) => p !== frame),
    elements,
  );
}

/** Frames in scene order, positions renumbered. */
export function renumber(frames: readonly Frame[], elements: readonly SceneElement[]): Frame[] {
  return frameSpans(frames, elements).map((s, position) => ({ ...s.frame, position }));
}

export function headingAt(elements: readonly SceneElement[], index: number): SceneElement | null {
  for (let i = index; i >= 0; i--) {
    if (elements[i].type === 'Heading') {
      return elements[i];
    }
  }
  return null;
}
