/**
 * A cue's performance is the line's own words with delivery tags in square brackets, e.g.
 * `[sad] I'm sorry.` These helpers keep the words and the tags apart; the API's `Performances`
 * mirrors them.
 */

/** Two tags plus one non-verbal; more sounds overacted. */
export const MAX_TAGS = 3;

const TAG = /\[([^\[\]\n]{1,40})\]/g;
const WORD_CHAR = /[\p{L}\p{N}'’]/gu;

/** A performance as the editor shows it: words with tags before them, plus tags at the end. */
export interface PerformanceToken {
  kind: 'word' | 'tag';
  text: string;
}

export function tagsOf(performance: string): string[] {
  return [...performance.matchAll(TAG)].map((m) => m[1].trim().toLowerCase());
}

/** The performance without its tags, spacing collapsed. */
export function stripTags(performance: string): string {
  return collapse(performance.replace(TAG, ' '));
}

/** Words lowercased and without surrounding punctuation, so pause punctuation does not count. */
export function words(text: string): string[] {
  return text
    .split(/\s+/)
    .map((token) => (token.match(WORD_CHAR) ?? []).join('').toLowerCase())
    .filter((word) => word.length > 0);
}

export function sameWords(performance: string, text: string): boolean {
  const a = words(stripTags(performance));
  const b = words(text);
  return a.length === b.length && a.every((word, i) => word === b[i]);
}

export function tokenize(performance: string): PerformanceToken[] {
  const tokens: PerformanceToken[] = [];
  for (const part of performance.split(/\s+/).filter(Boolean)) {
    // A tag glued to a word, "[sad]I'm", still reads as a tag then a word.
    let rest = part;
    while (rest.length > 0) {
      const match = /^\[([^\[\]\n]{1,40})\]/.exec(rest);
      if (match) {
        tokens.push({ kind: 'tag', text: match[1].trim().toLowerCase() });
        rest = rest.slice(match[0].length);
      } else {
        const end = rest.indexOf('[');
        const word = end > 0 ? rest.slice(0, end) : rest;
        tokens.push({ kind: 'word', text: word });
        rest = end > 0 ? rest.slice(end) : '';
      }
    }
  }
  return tokens;
}

export function fromTokens(tokens: readonly PerformanceToken[]): string {
  return tokens.map((t) => (t.kind === 'tag' ? `[${t.text}]` : t.text)).join(' ');
}

/** The line's words with tags placed: `leading` first, each inline tag before its 0-based word. */
export function assemble(
  text: string,
  leading: readonly string[],
  inline: readonly { beforeWord: number; tag: string }[] = [],
): string {
  const tokens = text.split(/\s+/).filter(Boolean);
  const used = new Set<string>();
  let budget = MAX_TAGS;
  const start: string[] = [];
  for (const raw of leading) {
    const tag = clean(raw);
    if (tag && budget > 0 && !used.has(tag)) {
      used.add(tag);
      start.push(tag);
      budget--;
    }
  }
  const before = new Map<number, string[]>();
  for (const { beforeWord, tag: raw } of [...inline].sort((a, b) => a.beforeWord - b.beforeWord)) {
    const tag = clean(raw);
    if (!tag || budget === 0 || used.has(tag)) {
      continue;
    }
    used.add(tag);
    const at = Math.max(0, Math.min(beforeWord, tokens.length));
    before.set(at, [...(before.get(at) ?? []), tag]);
    budget--;
  }
  const parts = start.map((t) => `[${t}]`);
  for (let i = 0; i <= tokens.length; i++) {
    for (const tag of before.get(i) ?? []) {
      parts.push(`[${tag}]`);
    }
    if (i < tokens.length) {
      parts.push(tokens[i]);
    }
  }
  return parts.join(' ');
}

/** Inserts a tag before the word at `beforeWord` (the word count for the end); refused over the cap. */
export function insertTag(performance: string, beforeWord: number, tag: string): string | null {
  const cleaned = clean(tag);
  const tokens = tokenize(performance);
  const tags = tokens.filter((t) => t.kind === 'tag');
  if (!cleaned || tags.length >= MAX_TAGS || tags.some((t) => t.text === cleaned)) {
    return null;
  }
  let seen = 0;
  const next: PerformanceToken[] = [];
  let placed = false;
  for (const token of tokens) {
    if (token.kind === 'word') {
      if (seen === beforeWord && !placed) {
        next.push({ kind: 'tag', text: cleaned });
        placed = true;
      }
      seen++;
    }
    next.push(token);
  }
  if (!placed) {
    next.push({ kind: 'tag', text: cleaned });
  }
  return fromTokens(next);
}

export function removeTag(performance: string, tag: string): string {
  const cleaned = clean(tag);
  return fromTokens(tokenize(performance).filter((t) => t.kind !== 'tag' || t.text !== cleaned));
}

/**
 * Carries a performance's tags onto a changed line. A tag at the start stays at the start; a tag
 * before a word follows that word when it survives, and is dropped when it does not.
 */
export function reanchor(performance: string, text: string): string {
  const tokens = tokenize(performance);
  const leading: string[] = [];
  const inline: { word: string; tag: string }[] = [];
  let seenWord = false;
  let pending: string[] = [];
  for (const token of tokens) {
    if (token.kind === 'tag') {
      if (!seenWord) {
        leading.push(token.text);
      } else {
        pending.push(token.text);
      }
    } else {
      for (const tag of pending) {
        inline.push({ word: token.text, tag });
      }
      pending = [];
      seenWord = true;
    }
  }
  // A tag the old line ended with stays at the end.
  const trailing = pending;
  const newWords = text.split(/\s+/).filter(Boolean);
  const placed: { beforeWord: number; tag: string }[] = [];
  let from = 0;
  for (const { word, tag } of inline) {
    const at = newWords.findIndex((w, i) => i >= from && words(w)[0] === words(word)[0]);
    if (at >= 0) {
      placed.push({ beforeWord: at, tag });
      from = at;
    }
  }
  for (const tag of trailing) {
    placed.push({ beforeWord: newWords.length, tag });
  }
  return assemble(text, leading, placed);
}

export function collapse(text: string): string {
  return text.replace(/\s+/g, ' ').trim();
}

function clean(tag: string): string {
  return collapse(tag.replace(/^\[|\]$/g, '')).toLowerCase();
}
