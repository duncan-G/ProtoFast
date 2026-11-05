import { learningGroups, newestFirst, roundNow } from './learnings';

describe('learnings', () => {
  const learnings = [
    { text: 'Range is $230k', round: 'Recruiter call', on: '2026-10-08' },
    { text: 'Live Go', round: 'Tech screen', on: '2026-10-21' },
    { text: 'Series C closed', round: '', on: '2026-10-30' },
    { text: 'Table-driven tests', round: 'Tech screen', on: '2026-10-21' },
    { text: 'Undated', round: 'Recruiter call', on: null },
  ];

  it('puts the newest first, the later entry first on the same day', () => {
    expect(newestFirst(learnings).map((entry) => entry.text)).toEqual([
      'Series C closed',
      'Table-driven tests',
      'Live Go',
      'Range is $230k',
      'Undated',
    ]);
  });

  it('groups by round, latest round first and no round last', () => {
    expect(
      learningGroups(learnings).map((group) => [group.round, group.entries.map((e) => e.at)]),
    ).toEqual([
      ['Tech screen', [3, 1]],
      ['Recruiter call', [0, 4]],
      ['', [2]],
    ]);
  });

  it('files new entries under the round under way, else the last one done', () => {
    expect(
      roundNow([
        { label: 'Screen', state: 'done' },
        { label: 'Tech', state: 'current' },
      ]),
    ).toBe('Tech');
    expect(
      roundNow([
        { label: 'Screen', state: 'done' },
        { label: 'Tech', state: 'done' },
        { label: 'Onsite', state: 'upcoming' },
      ]),
    ).toBe('Tech');
    expect(roundNow([])).toBe('');
  });
});
