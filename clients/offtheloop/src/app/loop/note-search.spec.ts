import { Note } from './model/note';
import { search, sortNotes } from './note-search';
import { tagChoices, tagsOf } from './note-tags';

function note(id: string, fields: Partial<Note> = {}): Note {
  return {
    id,
    kind: 'pattern',
    companyId: null,
    title: id,
    tag: '',
    body: '',
    badges: [],
    fits: [],
    ...fields,
  };
}

const ids = (notes: Note[]) => notes.map((n) => n.id);

describe('note search', () => {
  const notes = [
    note('Sliding window', { tag: 'Window', body: 'longest substring with at most k distinct' }),
    note('Monotonic stack', {
      tag: 'stack',
      body: '## The tell\n\nNext greater, previous smaller',
    }),
    note('Rate limiter', { kind: 'design', tag: 'Scaling' }),
    note('Two sum'),
  ];
  const all = { query: '', kind: 'all', tag: '' } as const;

  it('keeps the order the notes came in', () => {
    expect(ids(search(notes, all))).toEqual([
      'Sliding window',
      'Monotonic stack',
      'Rate limiter',
      'Two sum',
    ]);
  });

  it('matches every word anywhere in the note, within the kind', () => {
    expect(ids(search(notes, { ...all, query: 'next greater' }))).toEqual(['Monotonic stack']);
    expect(ids(search(notes, { ...all, query: 'k distinct', kind: 'pattern' }))).toEqual([
      'Sliding window',
    ]);
    expect(ids(search(notes, { ...all, kind: 'design' }))).toEqual(['Rate limiter']);
  });

  it('filters by tag whatever its case', () => {
    expect(ids(search(notes, { ...all, tag: 'window' }))).toEqual(['Sliding window']);
    expect(ids(search(notes, { ...all, kind: 'design', tag: 'window' }))).toEqual([]);
  });

  it('sorts by title, or by tag with untagged notes last', () => {
    expect(ids(sortNotes(notes, 'title'))).toEqual([
      'Monotonic stack',
      'Rate limiter',
      'Sliding window',
      'Two sum',
    ]);
    expect(ids(sortNotes(notes, 'tag'))).toEqual([
      'Rate limiter',
      'Monotonic stack',
      'Sliding window',
      'Two sum',
    ]);
    expect(sortNotes(notes, 'recent')).toBe(notes);
  });

  it('offers a kind’s common tags with the ones already used, once each', () => {
    const tags = tagChoices('design', [
      ...notes,
      note('Ledger', { kind: 'design', tag: 'Ledgers' }),
    ]);
    expect(tags).toContain('caching');
    expect(tags).toContain('ledgers');
    expect(tags).not.toContain('window');
    expect(tags.filter((t) => t === 'scaling')).toHaveLength(1);
    expect(tagsOf(notes)).toEqual(['scaling', 'stack', 'window']);
  });
});
