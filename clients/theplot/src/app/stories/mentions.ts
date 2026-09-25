import { MentionedText } from './model/mentioned-text';
import { Referable } from './model/referable';
import { ReferenceInsertion } from './model/reference-insertion';
import { ReferenceKind } from './model/reference-kind';
import { ReferenceTarget } from './model/reference-target';
import { SceneElementMention } from './model/scene-element-mention';
import { TextEdit } from './model/text-edit';
import { TextSegment } from './model/text-segment';

const WORD_CHAR = /[\p{L}\p{N}]/u;

/** Tie-break when a character, location and prop share a name: the autocomplete's order. */
export const REFERENCE_ORDER: Record<ReferenceKind, number> = {
  character: 0,
  location: 1,
  prop: 2,
};

export function mentionTarget(mention: SceneElementMention): ReferenceTarget {
  if (mention.characterId) {
    return { kind: 'character', id: mention.characterId };
  }
  if (mention.locationId) {
    return { kind: 'location', id: mention.locationId };
  }
  return { kind: 'prop', id: mention.propId ?? '' };
}

export function mentions(mention: SceneElementMention, target: ReferenceTarget): boolean {
  switch (target.kind) {
    case 'character':
      return mention.characterId === target.id;
    case 'location':
      return mention.locationId === target.id;
    case 'prop':
      return mention.propId === target.id;
  }
}

export function createMention(
  target: ReferenceTarget,
  offset: number,
  length: number,
): SceneElementMention {
  return {
    id: crypto.randomUUID(),
    characterId: target.kind === 'character' ? target.id : null,
    locationId: target.kind === 'location' ? target.id : null,
    propId: target.kind === 'prop' ? target.id : null,
    offset,
    length,
  };
}

function commonPrefix(a: string, b: string): number {
  const max = Math.min(a.length, b.length);
  let n = 0;
  while (n < max && a[n] === b[n]) {
    n++;
  }
  return n;
}

function commonSuffix(a: string, b: string, max = Math.min(a.length, b.length)): number {
  let n = 0;
  while (n < max && a[a.length - 1 - n] === b[b.length - 1 - n]) {
    n++;
  }
  return n;
}

/** Repeated characters make the split ambiguous ("aa" → "aaa"); the caret, when known, decides. */
export function editedRange(before: string, after: string, caret?: number): TextEdit {
  if (caret !== undefined) {
    const suffix = after.length - caret;
    if (suffix >= 0 && suffix <= before.length && commonSuffix(before, after) >= suffix) {
      const start = Math.min(commonPrefix(before, after), caret, before.length - suffix);
      return { start, oldEnd: before.length - suffix, newEnd: caret };
    }
  }
  const start = commonPrefix(before, after);
  const suffix = commonSuffix(before, after, Math.min(before.length, after.length) - start);
  return {
    start,
    oldEnd: before.length - suffix,
    newEnd: after.length - suffix,
  };
}

/** Mentions the edit cuts into are dropped; their text stays as plain text. */
export function shiftMentions(
  list: readonly SceneElementMention[],
  edit: TextEdit,
  after: string,
): SceneElementMention[] {
  const delta = edit.newEnd - edit.oldEnd;
  const kept: SceneElementMention[] = [];
  for (const mention of list) {
    const end = mention.offset + mention.length;
    if (end <= edit.start) {
      kept.push(mention);
    } else if (mention.offset >= edit.oldEnd) {
      kept.push(delta === 0 ? mention : { ...mention, offset: mention.offset + delta });
    }
  }
  return kept.filter((m) => after[m.offset] === '@');
}

/** The longest name ending on a word boundary; ties go by `REFERENCE_ORDER`. */
export function matchReference(
  text: string,
  at: number,
  referables: readonly Referable[],
): Referable | null {
  if (text[at] !== '@' || (at > 0 && WORD_CHAR.test(text[at - 1]))) {
    return null;
  }
  let best: Referable | null = null;
  for (const candidate of referables) {
    const length = candidate.name.length;
    if (length === 0) {
      continue;
    }
    const spelled = text.slice(at + 1, at + 1 + length);
    if (
      spelled.localeCompare(candidate.name, undefined, {
        sensitivity: 'accent',
      }) !== 0
    ) {
      continue;
    }
    const next = text[at + 1 + length];
    if (next !== undefined && WORD_CHAR.test(next)) {
      continue;
    }
    if (
      !best ||
      length > best.name.length ||
      (length === best.name.length && REFERENCE_ORDER[candidate.kind] < REFERENCE_ORDER[best.kind])
    ) {
      best = candidate;
    }
  }
  return best;
}

function overlaps(list: readonly SceneElementMention[], start: number, end: number): boolean {
  return list.some((m) => m.offset < end && m.offset + m.length > start);
}

/** `skipAt` is the `@` still being typed: it waits for a boundary or a pick. */
export function linkTypedReferences(
  value: MentionedText,
  referables: readonly Referable[],
  from: number,
  to: number,
  skipAt: number | null = null,
): MentionedText {
  const added: SceneElementMention[] = [];
  const all = () => [...value.mentions, ...added];
  for (let at = value.text.indexOf('@'); at >= 0; at = value.text.indexOf('@', at + 1)) {
    if (at === skipAt || at > to) {
      continue;
    }
    const match = matchReference(value.text, at, referables);
    if (!match) {
      continue;
    }
    const end = at + 1 + match.name.length;
    if (end < from || overlaps(all(), at, end)) {
      continue;
    }
    added.push(createMention(match, at, end - at));
  }
  if (added.length === 0) {
    return value;
  }
  return { text: value.text, mentions: sortMentions(all()) };
}

export function applyTyping(
  before: MentionedText,
  after: string,
  referables: readonly Referable[],
  caret?: number,
  typingAt: number | null = null,
): MentionedText {
  const edit = editedRange(before.text, after, caret);
  const moved = {
    text: after,
    mentions: shiftMentions(before.mentions, edit, after),
  };
  return linkTypedReferences(moved, referables, edit.start, edit.newEnd, typingAt);
}

/** Links to `target` directly, so a picked reference never depends on name resolution. */
export function insertReference(
  value: MentionedText,
  start: number,
  end: number,
  target: Referable,
): ReferenceInsertion {
  const { text } = value;
  const lead = start > 0 && WORD_CHAR.test(text[start - 1]) ? ' ' : '';
  const trail = /\s/.test(text[end] ?? '') ? '' : ' ';
  const reference = `@${target.name}`;
  const inserted = lead + reference + trail;
  const next = text.slice(0, start) + inserted + text.slice(end);
  const edit = { start, oldEnd: end, newEnd: start + inserted.length };
  const kept = shiftMentions(value.mentions, edit, next);
  const mention = createMention(target, start + lead.length, reference.length);
  return {
    value: { text: next, mentions: sortMentions([...kept, mention]) },
    caret: start + lead.length + reference.length + 1,
  };
}

export function renameReferences(
  value: MentionedText,
  target: ReferenceTarget,
  name: string,
): MentionedText {
  let text = value.text;
  let shift = 0;
  const out: SceneElementMention[] = [];
  for (const mention of sortMentions(value.mentions)) {
    const offset = mention.offset + shift;
    if (!mentions(mention, target)) {
      out.push(shift === 0 ? mention : { ...mention, offset });
      continue;
    }
    const reference = `@${name}`;
    text = text.slice(0, offset) + reference + text.slice(offset + mention.length);
    shift += reference.length - mention.length;
    out.push({ ...mention, offset, length: reference.length });
  }
  return { text, mentions: out };
}

export function forgetReferences(
  list: readonly SceneElementMention[],
  target: ReferenceTarget,
): SceneElementMention[] {
  return list.filter((m) => !mentions(m, target));
}

export function sortMentions(list: readonly SceneElementMention[]): SceneElementMention[] {
  return [...list].sort((a, b) => a.offset - b.offset);
}

export function segmentText(value: MentionedText): TextSegment[] {
  const out: TextSegment[] = [];
  let at = 0;
  for (const mention of sortMentions(value.mentions)) {
    if (mention.offset < at) {
      continue;
    }
    if (mention.offset > at) {
      out.push({ text: value.text.slice(at, mention.offset), mention: null });
    }
    out.push({
      text: value.text.slice(mention.offset, mention.offset + mention.length),
      mention,
    });
    at = mention.offset + mention.length;
  }
  if (at < value.text.length) {
    out.push({ text: value.text.slice(at), mention: null });
  }
  return out;
}
