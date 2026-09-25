import { findByName, labelProblem, nameProblem, sameName } from './library-names';

const CHARACTERS = [
  { id: '1', name: 'Mara' },
  { id: '2', name: 'Old Fen' },
];

describe('sameName', () => {
  it('ignores case and surrounding space', () => {
    expect(sameName('mara', 'MARA')).toBe(true);
    expect(sameName('  Old Fen ', 'old fen')).toBe(true);
    expect(sameName('Mara', 'Maria')).toBe(false);
  });
});

describe('nameProblem', () => {
  it('refuses a name another entry has in any case', () => {
    expect(nameProblem(CHARACTERS, 'MARA', 'character')).toBe(
      'There’s already a character called “Mara”.',
    );
    expect(nameProblem(CHARACTERS, ' old fen', 'character')).toBe(
      'There’s already a character called “Old Fen”.',
    );
  });

  it('lets an entry keep or recase its own name', () => {
    expect(nameProblem(CHARACTERS, 'MARA', 'character', '1')).toBeNull();
  });

  it('refuses a blank name and accepts a new one', () => {
    expect(nameProblem(CHARACTERS, '   ', 'character')).toBe('A character needs a name.');
    expect(nameProblem(CHARACTERS, 'Bolt', 'character')).toBeNull();
  });

  it('only compares within one kind, so a character and a location can share a name', () => {
    const locations = [{ id: 'l', name: 'Harbor' }];
    expect(nameProblem(CHARACTERS, 'Harbor', 'character')).toBeNull();
    expect(findByName(locations, 'harbor')?.id).toBe('l');
  });
});

describe('labelProblem', () => {
  it('refuses a label already on the list in any case', () => {
    expect(labelProblem(['DAY', 'NIGHT'], 'night')).toBe('“NIGHT” is already on the list.');
    expect(labelProblem(['DAY', 'NIGHT'], 'MORNING')).toBeNull();
  });
});
