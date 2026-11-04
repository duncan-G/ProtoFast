import { Note } from './model/note';
import { NoteKind } from './model/note-kind';
import { STORY_QUALITIES } from './story-import';

export const COMMON_TAGS: Record<NoteKind, readonly string[]> = {
  pattern: [
    'arrays',
    'strings',
    'hashing',
    'pointers',
    'window',
    'prefix sum',
    'binary search',
    'stack',
    'heap',
    'intervals',
    'linked list',
    'trees',
    'graph',
    'trie',
    'union find',
    'backtracking',
    'recursion',
    'dp',
    'greedy',
    'bits',
  ],
  design: [
    'apis',
    'storage',
    'caching',
    'scaling',
    'consistency',
    'reliability',
    'messaging',
    'streaming',
    'search',
    'rate limiting',
    'payments',
    'edge',
    'observability',
    'security',
  ],
  company: [
    'screen',
    'coding',
    'pairing',
    'system design',
    'behavioral',
    'take-home',
    'hiring manager',
    'onsite',
    'offer',
  ],
  story: [...STORY_QUALITIES.map((q) => q.tag), 'motivation'],
};

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true });

export const byName = (a: string, b: string): number => collator.compare(a, b);

/** Tags match whatever their case; the page shows them lower-cased. */
export function tagKey(tag: string): string {
  return tag.trim().toLowerCase();
}

/** The tags the notes carry, once each, A–Z. */
export function tagsOf(notes: readonly Note[]): string[] {
  return distinct(notes.map((note) => note.tag));
}

/** A kind's common tags and the ones its notes already carry, A–Z. */
export function tagChoices(kind: NoteKind, notes: readonly Note[]): string[] {
  return distinct([
    ...COMMON_TAGS[kind],
    ...notes.filter((note) => note.kind === kind).map((note) => note.tag),
  ]);
}

function distinct(tags: readonly string[]): string[] {
  return [...new Set(tags.map(tagKey).filter(Boolean))].sort(byName);
}
