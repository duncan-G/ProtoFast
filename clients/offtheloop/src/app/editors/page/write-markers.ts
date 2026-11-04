export const CURSOR = '⟦cursor⟧';
export const SELECTION_START = '⟦selection⟧';
export const SELECTION_END = '⟦/selection⟧';

/** How much of the text on either side of a mark is matched, longest first. */
const REACH = [80, 30, 10];

/**
 * The note as it is now, marked where a write that started on an older version of it goes: found by
 * the text around the mark, or at the end when that text has gone.
 */
export function placeMarkers(body: string, document: string): string {
  const atEnd = `${body.trimEnd()}\n\n${CURSOR}`;
  const cursor = document.indexOf(CURSOR);
  if (cursor >= 0) {
    const at = anchor(body, document.slice(0, cursor), document.slice(cursor + CURSOR.length));
    return at === null ? atEnd : body.slice(0, at) + CURSOR + body.slice(at);
  }
  const start = document.indexOf(SELECTION_START);
  const end = document.indexOf(SELECTION_END);
  if (start < 0 || end < start) {
    return atEnd;
  }
  const selected = document.slice(start + SELECTION_START.length, end);
  const before = document.slice(0, start);
  const after = document.slice(end + SELECTION_END.length);
  for (const reach of [...REACH, 0]) {
    const lead = before.slice(before.length - reach);
    const at = unique(body, lead + selected + after.slice(0, reach));
    if (selected && at >= 0) {
      const from = at + lead.length;
      const to = from + selected.length;
      return `${body.slice(0, from)}${SELECTION_START}${selected}${SELECTION_END}${body.slice(to)}`;
    }
  }
  return atEnd;
}

/** Where `before` meets `after` in the text, by as much of either side as appears there once. */
function anchor(text: string, before: string, after: string): number | null {
  for (const reach of REACH) {
    const lead = before.slice(before.length - reach);
    const tail = after.slice(0, reach);
    const at = lead || tail ? unique(text, lead + tail) : -1;
    if (at >= 0) {
      return at + lead.length;
    }
  }
  for (const reach of REACH) {
    const lead = before.slice(before.length - reach);
    const at = lead ? unique(text, lead) : -1;
    if (at >= 0) {
      return at + lead.length;
    }
    const tail = after.slice(0, reach);
    const next = tail ? unique(text, tail) : -1;
    if (next >= 0) {
      return next;
    }
  }
  return null;
}

function unique(text: string, needle: string): number {
  const at = text.indexOf(needle);
  return at >= 0 && text.indexOf(needle, at + 1) < 0 ? at : -1;
}
