import { SceneElement } from '../stories/model/scene-element';
import { Panel } from './model/panel';

/** Where a panel sits in the scene: 0-based indexes into the elements, inclusive. */
export interface PanelSpan {
  panel: Panel;
  first: number;
  last: number;
}

/** Panels in scene order with their spans; a panel whose rows are gone is left out. */
export function panelSpans(
  panels: readonly Panel[],
  elements: readonly SceneElement[],
): PanelSpan[] {
  const index = new Map(elements.map((e, i) => [e.id, i]));
  return panels
    .flatMap((panel) => {
      const first = index.get(panel.firstElementId);
      const last = index.get(panel.lastElementId);
      return first === undefined || last === undefined
        ? []
        : [{ panel, first: Math.min(first, last), last: Math.max(first, last) }];
    })
    .sort((a, b) => a.first - b.first);
}

/** The span covering an element, by its index. */
export function spanAt(spans: readonly PanelSpan[], index: number): PanelSpan | null {
  return spans.find((s) => s.first <= index && index <= s.last) ?? null;
}

/** Indexes of elements no panel covers. */
export function uncovered(spans: readonly PanelSpan[], count: number): number[] {
  const gaps: number[] = [];
  for (let i = 0; i < count; i++) {
    if (!spanAt(spans, i)) {
      gaps.push(i);
    }
  }
  return gaps;
}

export function blankPanel(elements: readonly SceneElement[], first: number, last: number): Panel {
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

/** A new panel starts at `index`, taking the rest of the panel that covered it; null when it already starts one. */
export function splitAt(
  panels: readonly Panel[],
  elements: readonly SceneElement[],
  index: number,
): Panel[] | null {
  const spans = panelSpans(panels, elements);
  const span = spanAt(spans, index);
  if (!span || span.first === index) {
    return null;
  }
  const head = { ...span.panel, lastElementId: elements[index - 1].id, edited: true };
  const tail = blankPanel(elements, index, span.last);
  return renumber([...panels.filter((p) => p !== span.panel), head, tail], elements);
}

/** The panel at `index` joins the panel before it; null when there is none to join. */
export function mergeUp(
  panels: readonly Panel[],
  elements: readonly SceneElement[],
  index: number,
): Panel[] | null {
  const spans = panelSpans(panels, elements);
  const span = spanAt(spans, index);
  const previous = span && span.first > 0 ? spanAt(spans, span.first - 1) : null;
  if (!span || !previous) {
    return null;
  }
  const merged = { ...previous.panel, lastElementId: elements[span.last].id, edited: true };
  return renumber(
    [...panels.filter((p) => p !== span.panel && p !== previous.panel), merged],
    elements,
  );
}

/** Covers an uncovered element and the uncovered run around it with a new panel. */
export function cover(
  panels: readonly Panel[],
  elements: readonly SceneElement[],
  index: number,
): Panel[] | null {
  const spans = panelSpans(panels, elements);
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
  return renumber([...panels, blankPanel(elements, first, last)], elements);
}

export function removePanel(
  panels: readonly Panel[],
  elements: readonly SceneElement[],
  panel: Panel,
): Panel[] {
  return renumber(
    panels.filter((p) => p !== panel),
    elements,
  );
}

/** Panels in scene order, positions renumbered. */
export function renumber(panels: readonly Panel[], elements: readonly SceneElement[]): Panel[] {
  return panelSpans(panels, elements).map((s, position) => ({ ...s.panel, position }));
}

export function headingAt(elements: readonly SceneElement[], index: number): SceneElement | null {
  for (let i = index; i >= 0; i--) {
    if (elements[i].type === 'Heading') {
      return elements[i];
    }
  }
  return null;
}
