import { Note } from './model/note';
import { NoteKind } from './model/note-kind';
import { byName, tagKey } from './note-tags';

export type NoteSort = 'recent' | 'title' | 'tag';

export const NOTE_SORTS: readonly NoteSort[] = ['recent', 'title', 'tag'];

export interface NoteFilter {
  query: string;
  kind: NoteKind | 'all';
  /** A tag key; empty for every tag. */
  tag: string;
}

/** Every word of the query must appear somewhere in the note. */
export function matches(note: Note, query: string): boolean {
  const words = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (words.length === 0) {
    return true;
  }
  const haystack = [note.title, note.tag, note.body, note.kind, ...note.badges, ...note.fits]
    .join(' ')
    .toLowerCase();
  return words.every((word) => haystack.includes(word));
}

/** The matching notes, in the order they came: the most recently changed first. */
export function search(notes: Note[], { query, kind, tag }: NoteFilter): Note[] {
  return notes.filter(
    (note) =>
      (kind === 'all' || note.kind === kind) &&
      (!tag || tagKey(note.tag) === tag) &&
      matches(note, query),
  );
}

/** By tag puts untagged notes last; ties go by title. */
export function sortNotes(notes: Note[], sort: NoteSort): Note[] {
  if (sort === 'recent') {
    return notes;
  }
  const byTitle = (a: Note, b: Note) => byName(a.title, b.title);
  return [...notes].sort(sort === 'title' ? byTitle : (a, b) => byTag(a, b) || byTitle(a, b));
}

function byTag(a: Note, b: Note): number {
  const [x, y] = [tagKey(a.tag), tagKey(b.tag)];
  return !x || !y ? Number(!x) - Number(!y) : byName(x, y);
}
