import { readResumeFile, RESUME_FORMAT, resumePrompt } from './resume-import';
import { monthLabel, rangeLabel, readMonth, resumeText } from './resume-text';

function read(value: unknown) {
  const result = readResumeFile(typeof value === 'string' ? value : JSON.stringify(value));
  if ('error' in result) {
    throw new Error(result.error);
  }
  return result;
}

describe('resume import', () => {
  it('reads the file the prompt asks for', () => {
    const { resume, rejected } = read({
      format: RESUME_FORMAT,
      resume: {
        name: ' Ada Park ',
        links: [{ label: 'GitHub', url: 'https://github.com/ada' }],
        experience: [
          {
            company: 'Old Co',
            start: '2015-01',
            end: '2018-06',
            highlights: ['Wrote the importer'],
          },
          {
            company: 'Halcyon',
            title: 'Senior engineer',
            start: 'Mar 2021',
            end: 'present',
            highlights: ['Moved billing onto a queue', 'Moved billing onto a queue'],
            evidence: [
              { repo: 'billing', commit: 'A1B2C3D', date: '2025-03-14', what: 'Split the run' },
            ],
          },
        ],
        skills: [{ name: 'languages', items: ['Go'] }],
      },
    });

    expect(rejected).toEqual([]);
    expect(resume.name).toBe('Ada Park');
    expect(resume.experience.map((r) => [r.company, r.start, r.end])).toEqual([
      ['Halcyon', '2021-03', ''],
      ['Old Co', '2015-01', '2018-06'],
    ]);
    expect(resume.experience[0].highlights).toEqual(['Moved billing onto a queue']);
    expect(resume.experience[0].evidence[0].commit).toBe('a1b2c3d');
  });

  it('sets aside entries with problems, saying what they are', () => {
    const { resume, rejected } = read(
      'Here it is:\n```json\n' +
        JSON.stringify({
          resume: {
            projects: [
              { name: 'tinyurl', link: 'github.com/ada/tinyurl' },
              { summary: 'no name' },
              { name: 'ok', evidence: [{ repo: 'r', commit: 'HEAD', what: 'x' }] },
              { name: 'fine' },
            ],
          },
        }) +
        '\n```',
    );

    expect(resume.projects.map((p) => p.name)).toEqual(['fine']);
    expect(rejected).toEqual([
      { section: 'projects', title: 'tinyurl', problems: ['“link” isn’t a web address.'] },
      { section: 'projects', title: 'Project 2', problems: ['“name” is missing.'] },
      { section: 'projects', title: 'ok', problems: ['Commit 1 has no hash.'] },
    ]);
  });

  it('refuses a file that is not one', () => {
    expect(readResumeFile('{"format": "offtheloop-stories/1", "stories": []}')).toEqual({
      error: 'This is a “offtheloop-stories/1” file, not offtheloop-resume/1.',
    });
    expect(readResumeFile('{}')).toEqual({ error: 'There’s nothing in it to add.' });
    expect('error' in readResumeFile('not json')).toBe(true);
  });

  it('names the file and format in the prompt', () => {
    expect(resumePrompt()).toContain('offtheloop-resume.json');
    expect(resumePrompt()).toContain(`"format": "${RESUME_FORMAT}"`);
  });
});

describe('resume text', () => {
  it('reads months the ways people write them', () => {
    expect(
      ['2023-04', '2023', 'Apr 2023', 'april 2023', '4/2023', '2023-04-17', 'Present', ''].map(
        readMonth,
      ),
    ).toEqual(['2023-04', '2023', '2023-04', '2023-04', '2023-04', '2023-04', '', '']);
    expect(readMonth('2023-13')).toBeNull();
    expect(readMonth('spring')).toBeNull();
  });

  it('labels months and ranges', () => {
    expect(monthLabel('2023-04')).toBe('Apr 2023');
    expect(rangeLabel('2021-03', '')).toBe('Mar 2021 – now');
    expect(rangeLabel('2014', '2018')).toBe('2014 – 2018');
    expect(rangeLabel('2014', '', null)).toBe('2014');
    expect(rangeLabel('', '')).toBe('');
  });

  it('writes the resume as plain text', () => {
    const text = read({
      resume: {
        name: 'Ada Park',
        email: 'ada@example.com',
        experience: [
          { company: 'Halcyon', title: 'Engineer', start: '2021', highlights: ['Did a thing'] },
        ],
        skills: [{ name: 'languages', items: ['Go', 'SQL'] }],
      },
    }).resume;

    expect(resumeText(text)).toBe(
      'Ada Park\nada@example.com\n\nEXPERIENCE\n\nEngineer, Halcyon\n2021 – present\n- Did a thing\n\nSKILLS\n\nlanguages: Go, SQL\n',
    );
  });
});
