import { ResumeProject } from './model/resume-project';
import { ResumeRole } from './model/resume-role';
import {
  entryMeta,
  findKey,
  linkLabel,
  markParts,
  readEntries,
  readGroups,
  spanLabel,
  trailingLink,
  yearsLabel,
} from './resume-reading';

const TODAY = new Date(2026, 10, 4);

function role(fields: Partial<ResumeRole> = {}): ResumeRole {
  return {
    company: 'Halcyon',
    title: 'Senior engineer',
    location: '',
    start: '2022-01',
    end: '',
    summary: 'Payments for marketplaces.',
    highlights: ['Moved billing onto a queue', 'Put Envoy in front of the api', 'Wrote the ledger'],
    stack: ['Go', 'Envoy', 'Postgres'],
    evidence: [
      { repo: 'billing', commit: 'a1b2c3d', date: '2023-02-01', what: 'Split the billing run' },
      { repo: 'edge', commit: 'e4f5a6b', date: '2024-06-10', what: 'Added the Envoy edge' },
    ],
    ...fields,
  };
}

function project(fields: Partial<ResumeProject> = {}): ResumeProject {
  return {
    name: 'tinyurl-rs',
    link: 'https://github.com/ada/tinyurl-rs',
    start: '2025-03',
    end: '2025-03',
    summary: 'A URL shortener in Rust.',
    highlights: [],
    stack: ['Rust'],
    evidence: [],
    ...fields,
  };
}

describe('resume reading', () => {
  it('finds nothing until there are two letters to match', () => {
    expect(findKey('  E ')).toBe('');
    expect(findKey(' EnVoy ')).toBe('envoy');
  });

  it('marks every match, whatever its case', () => {
    expect(markParts('Envoy in front; envoy behind', 'envoy')).toEqual([
      { text: 'Envoy', hit: true },
      { text: ' in front; ', hit: false },
      { text: 'envoy', hit: true },
      { text: ' behind', hit: false },
    ]);
    expect(markParts('No match here', '')).toEqual([{ text: 'No match here', hit: false }]);
  });

  it('counts a span in years and months, both end months included', () => {
    expect(spanLabel('2022-01', '', TODAY)).toBe('4 yrs 11 mo');
    expect(spanLabel('2018-03', '2018-10', TODAY)).toBe('8 mo');
    expect(spanLabel('2025-11', '2026-10', TODAY)).toBe('1 yr');
    expect(spanLabel('2011', '2015-12', TODAY)).toBe('');
  });

  it('shortens years for the outline', () => {
    expect(yearsLabel('2022-01', '')).toBe('2022–now');
    expect(yearsLabel('2019-02', '2021-12')).toBe('2019–21');
    expect(yearsLabel('2018-03', '2018-10')).toBe('2018');
  });

  it('lays out a job with its dates, numbered highlights and newest commits first', () => {
    const [entry] = readEntries('role', [role()], '', TODAY);

    expect(entry.key).toBe('role-0');
    expect([entry.title, entry.at, entry.label]).toEqual(['Senior engineer', 'Halcyon', 'Halcyon']);
    expect([entry.dates, entry.span, entry.years]).toEqual(['Jan 2022 – now', '4 yrs 11 mo', '2022–now']);
    expect(entry.highlights.map((h) => h.n)).toEqual(['01', '02', '03']);
    expect(entry.evidence.map((e) => e.commit)).toEqual(['e4f5a6b', 'a1b2c3d']);
    expect(entry.repos).toBe('billing, edge');
    expect(entryMeta(entry, '')).toBe('3 highlights · 3 in stack · 2 commits');
  });

  it('keeps only the highlights a find matches, numbered as they were', () => {
    const [entry] = readEntries('role', [role()], 'envoy', TODAY);

    expect(entry.highlights).toEqual([{ n: '02', text: 'Put Envoy in front of the api' }]);
    expect(entry.hits).toBe(3);
    expect(entry.shown).toBe(true);
    expect(entryMeta(entry, 'envoy')).toBe('1 of 3 highlights match');
  });

  it('shows an entry a find matches by name alone, and hides one it misses', () => {
    const [named, missed] = readEntries(
      'project',
      [project(), project({ name: 'ledger', summary: 'Double-entry books.', stack: ['Go'] })],
      'rust',
      TODAY,
    );

    expect([named.shown, named.hits]).toEqual([true, 1]);
    expect(missed.shown).toBe(false);
    expect([named.title, named.at, named.dates]).toEqual(['tinyurl-rs', '', 'Mar 2025']);
  });

  it('shows the skill groups a find matches', () => {
    const groups = readGroups(
      [
        { name: 'languages', items: ['Go', 'Rust'] },
        { name: 'cloud', items: ['AWS'] },
      ],
      'rust',
    );

    expect(groups.map((g) => [g.name, g.hits, g.shown])).toEqual([
      ['languages', 1, true],
      ['cloud', 0, false],
    ]);
  });

  it('splits a trailing address off a line to link it', () => {
    expect(trailingLink('Primes, Rose-Hulman Journal: https://scholar.example.edu/5/')).toEqual({
      text: 'Primes, Rose-Hulman Journal',
      url: 'https://scholar.example.edu/5/',
    });
    expect(trailingLink('AWS Solutions Architect (2024)')).toEqual({
      text: 'AWS Solutions Architect (2024)',
      url: '',
    });
    expect(linkLabel('https://www.github.com/ada/')).toBe('github.com/ada');
  });
});
