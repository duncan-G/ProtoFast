import { isObject, parseAgentJson } from './agent-json';
import { blankResume, Resume } from './model/resume';
import { ResumeEvidence } from './model/resume-evidence';
import { ResumeGroup } from './model/resume-group';
import { ResumeLink } from './model/resume-link';
import { ResumeProject } from './model/resume-project';
import { ResumeRole } from './model/resume-role';
import { ResumeSchool } from './model/resume-school';
import { Rejected, RESUME_LIMITS } from './resume-merge';
import { inOrder, readMonth } from './resume-text';

export const RESUME_FORMAT = 'offtheloop-resume/1';
export const RESUME_FILE = 'offtheloop-resume.json';

/** The server's lengths. */
const LIMITS = { name: 200, line: 300, label: 80, summary: 600, text: 4000, url: 2048 };

export type ResumeRead = { error: string } | { resume: Resume; rejected: Rejected[] };

/** The prompt the user gives their own agent, which reads their git history on their computer. */
export function resumePrompt(): string {
  const evidence = [
    {
      repo: 'billing-service',
      commit: 'a1b2c3d',
      date: '2025-03-14',
      what: 'Split the nightly run into per-account jobs',
    },
  ];
  const example = {
    format: RESUME_FORMAT,
    resume: {
      name: 'My Name',
      headline: 'Backend engineer · payments and data pipelines',
      email: 'me@example.com',
      phone: '',
      location: 'Austin, TX',
      links: [{ label: 'GitHub', url: 'https://github.com/me' }],
      summary: 'A few sentences on who I am as an engineer.',
      experience: [
        {
          company: 'Acme Payments',
          title: 'Senior Software Engineer',
          location: 'Remote',
          start: '2022-04',
          end: '',
          summary: 'One line on the team or what it does.',
          highlights: [
            'Moved nightly billing onto a queue, so a failed account no longer stopped the run',
          ],
          stack: ['Go', 'PostgreSQL', 'SQS'],
          evidence,
        },
      ],
      projects: [
        {
          name: 'tinyurl-rs',
          link: 'https://github.com/me/tinyurl-rs',
          start: '2024-01',
          end: '2024-03',
          summary: 'A URL shortener in Rust.',
          highlights: ['Built a lock-free counter for short codes'],
          stack: ['Rust', 'Redis'],
          evidence: [],
        },
      ],
      skills: [
        { name: 'languages', items: ['Go', 'Rust', 'TypeScript'] },
        { name: 'infrastructure', items: ['AWS', 'Terraform'] },
      ],
      education: [
        {
          school: 'State University',
          degree: 'BSc Computer Science',
          start: '2014',
          end: '2018',
          details: [],
        },
      ],
      extras: [{ name: 'open source', items: ['Contributor to sqlx (2023)'] }],
    },
  };

  return `Help me build my general resume from my real work. Read my git history and write everything that could go on a resume as a JSON file I'll import into OffTheLoop, my job-search companion. It isn't the resume I send anyone: it's the full list I tailor each resume from, so put in everything that's true and leave the choosing for later.

## 1. Find my work
- Look for git repositories in this folder and its subfolders, plus any I name.
- Work out which names and emails I commit under: \`git config user.email\` and \`git shortlog -sne --all\` in each repository. If you can't tell which are mine, ask me before going on.
- Read my commits with \`git log --all --author=<me> --stat\`, then the diffs of the substantial ones. If the GitHub CLI (\`gh\`) is signed in, my pull requests, their descriptions and their review threads help too.

## 2. Collect everything
- Projects: each repository or substantial piece of work of mine: what it is, what I built, the stack, when (my first and last commit), and its link if it's public.
- Highlights: what I did and what came of it, one bullet each, as many as the history backs: features shipped, systems designed, migrations, performance and reliability wins, tooling, and reviewing or mentoring that shows in pull requests.
- Experience: which employer each piece of work was for. Remotes, organisations and work emails are clues. Ask me for the employers, titles and dates you can't tell, or leave the job out. A job's work goes under that job; personal and open-source work goes under projects.
- Skills: the languages, frameworks, databases, infrastructure and tools I actually used, in groups.
- Ask me, in one short batch of questions, for what git can't show: my name and how to reach me, my education, certifications, talks and awards. Leave out whatever I don't give.

## 3. Rules
- Everything rests on my real work. Cite the commits behind each job and project under "evidence" with their short hashes. Never invent a commit, a number, an employer, a title or an outcome; use numbers only when the history or I back them up.
- Start each highlight with a strong past-tense verb (present tense for a current job), in plain words.
- Leave out secrets, keys, internal hostnames, customer names and anything confidential. Describe internal systems in general terms ("the billing service").

## 4. Write the file
Write \`${RESUME_FILE}\` in this folder holding only this JSON, with no comments, then tell me where it is:

${JSON.stringify(example, null, 2)}

- "experience" and "projects" go most recent first. Months are YYYY-MM, or YYYY when only the year is known; "end" is empty while it's current.
- "evidence" lists up to ${RESUME_LIMITS.items} of my commits per job or project; "date" is the commit date as YYYY-MM-DD.
- At most ${RESUME_LIMITS.items} highlights per entry, each under ${LIMITS.line} characters; at most ${RESUME_LIMITS.entries} jobs, projects and schools.
- Every field is plain text and may be left empty, except a job's "company", a project's "name", a school's "school" and a list's "name".
`;
}

/** Reads what the user's agent wrote; an entry with problems is listed with them, not added. */
export function readResumeFile(text: string): ResumeRead {
  const parsed = parseAgentJson(text);
  if ('error' in parsed) {
    return parsed;
  }
  const file = parsed.value;
  if (!isObject(file)) {
    return { error: 'There’s no resume in it.' };
  }
  if (file['format'] !== undefined && file['format'] !== RESUME_FORMAT) {
    return { error: `This is a “${String(file['format'])}” file, not ${RESUME_FORMAT}.` };
  }
  const value = isObject(file['resume']) ? file['resume'] : file;
  const rejected: Rejected[] = [];
  const resume = blankResume();
  const reader = new Reader(value);

  resume.name = reader.text('name', LIMITS.name);
  resume.headline = reader.text('headline', LIMITS.line);
  resume.email = reader.text('email', LIMITS.name);
  resume.phone = reader.text('phone', LIMITS.label);
  resume.location = reader.text('location', LIMITS.label);
  resume.summary = reader.text('summary', LIMITS.text);
  if (reader.problems.length) {
    rejected.push({ section: 'about you', title: 'name and details', problems: reader.problems });
  }

  resume.links = entries(
    value,
    'links',
    'about you',
    rejected,
    (r): ResumeLink => ({
      label: r.text('label', LIMITS.label),
      url: r.url('url', true),
    }),
    (link) => link.label || 'a link',
  );
  resume.experience = entries(
    value,
    'experience',
    'experience',
    rejected,
    (r): ResumeRole => ({
      company: r.text('company', LIMITS.name, true),
      title: r.text('title', LIMITS.name),
      location: r.text('location', LIMITS.label),
      start: r.month('start'),
      end: r.month('end'),
      summary: r.text('summary', LIMITS.summary),
      highlights: r.items('highlights', LIMITS.line),
      stack: r.items('stack', LIMITS.label),
      evidence: r.evidence(),
    }),
    (role, n) => [role.title, role.company].filter(Boolean).join(' · ') || `Job ${n}`,
  );
  resume.projects = entries(
    value,
    'projects',
    'projects',
    rejected,
    (r): ResumeProject => ({
      name: r.text('name', LIMITS.name, true),
      link: r.url('link', false),
      start: r.month('start'),
      end: r.month('end'),
      summary: r.text('summary', LIMITS.summary),
      highlights: r.items('highlights', LIMITS.line),
      stack: r.items('stack', LIMITS.label),
      evidence: r.evidence(),
    }),
    (project, n) => project.name || `Project ${n}`,
  );
  resume.skills = entries(
    value,
    'skills',
    'skills',
    rejected,
    (r) => group(r, LIMITS.label),
    (g, n) => g.name || `Skills ${n}`,
  );
  resume.education = entries(
    value,
    'education',
    'education',
    rejected,
    (r): ResumeSchool => ({
      school: r.text('school', LIMITS.name, true),
      degree: r.text('degree', LIMITS.name),
      start: r.month('start'),
      end: r.month('end'),
      details: r.items('details', LIMITS.line),
    }),
    (school, n) => school.school || `School ${n}`,
  );
  resume.extras = entries(
    value,
    'extras',
    'other',
    rejected,
    (r) => group(r, LIMITS.line),
    (g, n) => g.name || `List ${n}`,
  );

  const found =
    resume.links.length +
    resume.experience.length +
    resume.projects.length +
    resume.skills.length +
    resume.education.length +
    resume.extras.length;
  if (!found && !resume.name && !resume.summary && !rejected.length) {
    return { error: 'There’s nothing in it to add.' };
  }
  return { resume: inOrder(resume), rejected };
}

function group(r: Reader, max: number): ResumeGroup {
  return { name: r.text('name', LIMITS.label, true), items: r.items('items', max) };
}

/** A section's entries; one with problems goes to rejected instead. */
function entries<T>(
  value: Record<string, unknown>,
  name: string,
  section: string,
  rejected: Rejected[],
  read: (reader: Reader) => T,
  title: (entry: T, number: number) => string,
): T[] {
  const list = value[name];
  if (list === undefined || list === null) {
    return [];
  }
  if (!Array.isArray(list)) {
    rejected.push({ section, title: name, problems: [`“${name}” isn’t a list.`] });
    return [];
  }
  const kept: T[] = [];
  list.forEach((item, i) => {
    const reader = new Reader(isObject(item) ? item : {});
    const entry = read(reader);
    const problems = isObject(item) ? reader.problems : ['It isn’t an object.'];
    if (problems.length) {
      rejected.push({ section, title: title(entry, i + 1), problems });
    } else if (kept.length >= RESUME_LIMITS.entries) {
      rejected.push({
        section,
        title: title(entry, i + 1),
        problems: [`Only ${RESUME_LIMITS.entries} fit.`],
      });
    } else {
      kept.push(entry);
    }
  });
  return kept;
}

/** Reads one object's fields, noting what's wrong with them. */
class Reader {
  readonly problems: string[] = [];

  constructor(private readonly value: Record<string, unknown>) {}

  text(name: string, max: number, required = false): string {
    const raw = this.value[name];
    if (raw === undefined || raw === null || raw === '') {
      if (required) {
        this.problems.push(`“${name}” is missing.`);
      }
      return '';
    }
    if (typeof raw !== 'string') {
      this.problems.push(`“${name}” isn’t text.`);
      return '';
    }
    const text = raw.trim();
    if (required && !text) {
      this.problems.push(`“${name}” is empty.`);
    }
    if (text.length > max) {
      this.problems.push(`“${name}” is longer than ${max} characters.`);
    }
    return text;
  }

  month(name: string): string {
    const text = this.text(name, LIMITS.label);
    const month = readMonth(text);
    if (month === null) {
      this.problems.push(`“${name}” isn’t a month like 2023-04.`);
      return '';
    }
    return month;
  }

  url(name: string, required: boolean): string {
    const text = this.text(name, LIMITS.url, required);
    if (text && !/^https?:\/\/\S+$/i.test(text)) {
      this.problems.push(`“${name}” isn’t a web address.`);
    }
    return text;
  }

  items(name: string, max: number): string[] {
    const raw = this.value[name];
    if (raw === undefined || raw === null) {
      return [];
    }
    if (!Array.isArray(raw)) {
      this.problems.push(`“${name}” isn’t a list.`);
      return [];
    }
    const items = raw.map((item) => (typeof item === 'string' ? item.trim() : null));
    if (items.some((item) => item === null)) {
      this.problems.push(`An entry in “${name}” isn’t text.`);
    }
    const kept = items.filter((item): item is string => !!item);
    if (kept.length > RESUME_LIMITS.items) {
      this.problems.push(`“${name}” can hold at most ${RESUME_LIMITS.items}.`);
    }
    if (kept.some((item) => item.length > max)) {
      this.problems.push(`An entry in “${name}” is longer than ${max} characters.`);
    }
    return [...new Set(kept)];
  }

  evidence(): ResumeEvidence[] {
    const raw = this.value['evidence'];
    if (raw === undefined || raw === null) {
      return [];
    }
    if (!Array.isArray(raw)) {
      this.problems.push('“evidence” isn’t a list.');
      return [];
    }
    if (raw.length > RESUME_LIMITS.items) {
      this.problems.push(`“evidence” can hold at most ${RESUME_LIMITS.items} commits.`);
    }
    return raw.map((item, i) => {
      const reader = new Reader(isObject(item) ? item : {});
      const evidence = {
        repo: reader.text('repo', LIMITS.name),
        commit: reader.text('commit', 40).toLowerCase(),
        date: reader.text('date', 10),
        what: reader.text('what', LIMITS.line),
      };
      const at = `Commit ${i + 1}`;
      if (!/^[0-9a-f]{7,40}$/.test(evidence.commit)) {
        this.problems.push(`${at} has no hash.`);
      }
      if (evidence.date && !/^\d{4}-\d{2}-\d{2}$/.test(evidence.date)) {
        this.problems.push(`${at}’s date isn’t YYYY-MM-DD.`);
      }
      this.problems.push(...reader.problems.map((p) => `${at}: ${p}`));
      return evidence;
    });
  }
}
