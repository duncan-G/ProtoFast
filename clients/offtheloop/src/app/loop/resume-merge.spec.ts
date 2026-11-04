import { blankResume, Resume } from './model/resume';
import { ResumeRole } from './model/resume-role';
import { applyMerge, mergeRows } from './resume-merge';

function role(fields: Partial<ResumeRole> = {}): ResumeRole {
  return {
    company: 'Halcyon',
    title: 'Senior engineer',
    location: '',
    start: '2021-03',
    end: '',
    summary: '',
    highlights: ['Moved billing onto a queue'],
    stack: ['Go'],
    evidence: [{ repo: 'billing', commit: 'a1b2c3d', date: '', what: 'Split the run' }],
    ...fields,
  };
}

function resume(fields: Partial<Resume> = {}): Resume {
  return { ...blankResume(), ...fields };
}

describe('resume merge', () => {
  it('offers everything when there is no general resume yet, all ticked', () => {
    const incoming = resume({
      name: 'Ada Park',
      links: [{ label: 'GitHub', url: 'https://github.com/ada' }],
      experience: [role()],
      skills: [{ name: 'languages', items: ['Go', 'SQL'] }],
    });

    const rows = mergeRows(null, incoming);

    expect(rows.map((r) => [r.section, r.title, r.ticked])).toEqual([
      ['about you', 'name', true],
      ['about you', 'GitHub', true],
      ['experience', 'Senior engineer · Halcyon', true],
      ['skills', 'languages', true],
    ]);
    expect(rows[2].detail).toBe('Mar 2021 – now · 1 highlight · 1 commit');
  });

  it('adds only what is new to a job already there, and leaves filled fields unticked', () => {
    const base = resume({
      name: 'Ada Park',
      experience: [role()],
      skills: [{ name: 'languages', items: ['Go'] }],
    });
    const incoming = resume({
      name: 'Ada K. Park',
      email: 'ada@example.com',
      experience: [
        role({
          title: 'senior engineer',
          location: 'Oakland, CA',
          highlights: ['moved billing onto a queue.', 'Cut deploys to 5 min'],
          stack: ['Go', 'Kafka'],
          evidence: [{ repo: 'billing', commit: 'a1b2c3d4e5', date: '', what: '' }],
        }),
        role({ title: 'Senior engineer', highlights: ['Moved billing onto a queue'] }),
      ],
      skills: [{ name: 'infrastructure', items: ['go', 'AWS'] }],
    });

    const rows = mergeRows(base, incoming);

    expect(rows.map((r) => [r.key, r.ticked, r.detail])).toEqual([
      ['field:name', false, 'Ada K. Park'],
      ['field:email', true, 'ada@example.com'],
      ['experience:0', true, '1 highlight · Kafka · location'],
      ['skills:0', true, 'AWS'],
    ]);
    expect(rows[0].replaces).toBe('Ada Park');
    expect(rows[2].adds).toBe(true);
  });

  it('applies the ticked rows, in order, without repeating what is there', () => {
    const base = resume({
      name: 'Ada Park',
      experience: [role({ company: 'Old Co', start: '2015-01', end: '2018-06' })],
      skills: [{ name: 'languages', items: ['Go'] }],
    });
    const incoming = resume({
      name: 'Someone',
      summary: 'Builds payment systems.',
      experience: [
        role(),
        role({ company: 'Old Co', start: '2015-01', highlights: ['Wrote the importer'] }),
      ],
      skills: [{ name: 'languages', items: ['Go', 'Rust'] }],
    });
    const keys = new Set(
      mergeRows(base, incoming)
        .filter((r) => r.ticked)
        .map((r) => r.key),
    );

    const merged = applyMerge(base, incoming, keys);

    expect(merged.name).toBe('Ada Park');
    expect(merged.summary).toBe('Builds payment systems.');
    expect(merged.experience.map((r) => r.company)).toEqual(['Halcyon', 'Old Co']);
    expect(merged.experience[1].highlights).toEqual([
      'Moved billing onto a queue',
      'Wrote the importer',
    ]);
    expect(merged.experience[1].end).toBe('2018-06');
    expect(merged.skills).toEqual([{ name: 'languages', items: ['Go', 'Rust'] }]);
    expect(base.experience[0].highlights).toEqual(['Moved billing onto a queue']);
  });

  it('lists entries it could not read with why, never ticked', () => {
    const rows = mergeRows(null, resume(), [
      { section: 'projects', title: 'Project 2', problems: ['“name” is missing.'] },
    ]);

    expect(rows).toEqual([
      expect.objectContaining({
        title: 'Project 2',
        ticked: false,
        problems: ['“name” is missing.'],
      }),
    ]);
  });
});
