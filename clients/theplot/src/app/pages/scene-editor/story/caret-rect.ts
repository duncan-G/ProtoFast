/**
 * Where character `offset` of a textarea sits, read from the copy of its text drawn behind it:
 * the copy keeps every character, so its text nodes index like the field's value.
 */
export function caretRect(copy: HTMLElement, offset: number): DOMRect {
  const walker = document.createTreeWalker(copy, NodeFilter.SHOW_TEXT);
  let left = offset;
  let last: Text | null = null;
  for (let node = walker.nextNode() as Text | null; node; node = walker.nextNode() as Text | null) {
    last = node;
    if (left <= node.length) {
      return rectAt(node, left) ?? copy.getBoundingClientRect();
    }
    left -= node.length;
  }
  return (last && rectAt(last, last.length)) ?? copy.getBoundingClientRect();
}

function rectAt(node: Text, offset: number): DOMRect | null {
  const range = document.createRange();
  range.setStart(node, offset);
  range.setEnd(node, offset);
  const rects = range.getClientRects();
  return rects.length ? rects[rects.length - 1] : null;
}

function lineHeight(copy: HTMLElement): number {
  return parseFloat(getComputedStyle(copy).lineHeight) || 24;
}

export function onFirstLine(field: HTMLTextAreaElement, copy: HTMLElement): boolean {
  if (field.selectionStart === 0) {
    return true;
  }
  return (
    caretRect(copy, field.selectionStart).top <
    copy.getBoundingClientRect().top + lineHeight(copy) / 2
  );
}

export function onLastLine(field: HTMLTextAreaElement, copy: HTMLElement): boolean {
  if (field.selectionStart === field.value.length) {
    return true;
  }
  return (
    caretRect(copy, field.selectionStart).bottom >
    copy.getBoundingClientRect().bottom - lineHeight(copy) / 2
  );
}
