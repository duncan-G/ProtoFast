import { blankResume, Resume } from './model/resume';
import { ResumeEvidence } from './model/resume-evidence';
import { ResumeGroup } from './model/resume-group';
import { ResumeProject } from './model/resume-project';
import { ResumeRole } from './model/resume-role';
import { ResumeSchool } from './model/resume-school';
import { plural } from './loop-summary';
import { inOrder, rangeLabel, sameText, textKey } from './resume-text';

/** The server's limits. */
export const RESUME_LIMITS = { entries: 50, items: 30 };

/** Something an import or a read resume would add to the general resume, ticked to keep. */
export interface MergeRow {
  key: string;
  section: string;
  title: string;
  detail: string;
  /** What ticking it replaces in the general resume. */
  replaces: string;
  /** It adds to an entry already there. */
  adds: boolean;
  ticked: boolean;
  /** Why it can't be added; it can't be ticked then. */
  problems: string[];
}

/** An entry the file had that couldn't be read. */
export interface Rejected {
  section: string;
  title: string;
  problems: string[];
}

type Field = 'name' | 'headline' | 'email' | 'phone' | 'location';

const FIELDS: readonly [Field, string][] = [
  ['name', 'name'],
  ['headline', 'headline'],
  ['email', 'email'],
  ['phone', 'phone'],
  ['location', 'based in'],
];

interface Dated {
  title?: string;
  start: string;
  end: string;
}

const sameRole = (a: ResumeRole, b: ResumeRole) =>
  sameText(a.company, b.company) &&
  (sameText(a.title, b.title) || (!!a.start && a.start === b.start));

const sameProject = (a: ResumeProject, b: ResumeProject) => sameText(a.name, b.name);

const sameSchool = (a: ResumeSchool, b: ResumeSchool) => sameText(a.school, b.school);

const sameCommit = (a: ResumeEvidence, b: ResumeEvidence) =>
  a.commit.toLowerCase().startsWith(b.commit.toLowerCase()) ||
  b.commit.toLowerCase().startsWith(a.commit.toLowerCase());

const linkKey = (url: string) => url.trim().replace(/\/+$/, '').toLowerCase();

interface Added<K extends string = string> {
  highlights: string[];
  stack: string[];
  details: string[];
  evidence: ResumeEvidence[];
  fields: K[];
}

/** What's new in an incoming entry; only blank fields are filled. */
function additions<
  T extends Dated & {
    highlights?: string[];
    stack?: string[];
    details?: string[];
    evidence?: ResumeEvidence[];
  },
>(have: T, incoming: T, fields: readonly (keyof T & string)[]): Added<keyof T & string> {
  const fresh = (mine: string[] = [], theirs: string[] = []) =>
    theirs.filter((item) => !mine.some((m) => sameText(m, item)));
  return {
    highlights: fresh(have.highlights, incoming.highlights),
    stack: fresh(have.stack, incoming.stack),
    details: fresh(have.details, incoming.details),
    evidence: (incoming.evidence ?? []).filter(
      (e) => !(have.evidence ?? []).some((h) => sameCommit(h, e)),
    ),
    fields: fields.filter((f) => !have[f] && !!incoming[f]),
  };
}

function addsLine(added: Added): string {
  const fields = added.fields.map((f) => (f === 'start' || f === 'end' ? 'dates' : f));
  return [
    added.highlights.length && plural(added.highlights.length, 'highlight'),
    added.details.length && plural(added.details.length, 'detail'),
    added.stack.length && added.stack.join(', '),
    added.evidence.length && plural(added.evidence.length, 'commit'),
    fields.length && [...new Set(fields)].join(', '),
  ]
    .filter(Boolean)
    .join(' · ');
}

const hasAdditions = (added: Added) =>
  added.highlights.length +
    added.details.length +
    added.stack.length +
    added.evidence.length +
    added.fields.length >
  0;

const ROLE_FIELDS = ['title', 'location', 'start', 'end', 'summary'] as const;
const PROJECT_FIELDS = ['link', 'start', 'end', 'summary'] as const;
const SCHOOL_FIELDS = ['degree', 'start', 'end'] as const;

/** Everything the incoming resume has that the general one doesn't. */
export function mergeRows(
  base: Resume | null,
  incoming: Resume,
  rejected: readonly Rejected[] = [],
): MergeRow[] {
  const have = base ?? blankResume();
  const rows: MergeRow[] = [];
  const row = (fields: Partial<MergeRow> & Pick<MergeRow, 'key' | 'section' | 'title'>) =>
    rows.push({ detail: '', replaces: '', adds: false, ticked: true, problems: [], ...fields });

  for (const [field, label] of FIELDS) {
    const value = incoming[field].trim();
    if (value && !sameText(value, have[field])) {
      row({
        key: `field:${field}`,
        section: 'about you',
        title: label,
        detail: value,
        replaces: have[field],
        ticked: !have[field],
      });
    }
  }
  for (const link of incoming.links) {
    if (!have.links.some((l) => linkKey(l.url) === linkKey(link.url))) {
      row({
        key: `link:${linkKey(link.url)}`,
        section: 'about you',
        title: link.label || 'link',
        detail: link.url,
      });
    }
  }
  if (incoming.summary.trim() && !sameText(incoming.summary, have.summary)) {
    row({
      key: 'summary',
      section: 'summary',
      title: 'summary',
      detail: incoming.summary,
      replaces: have.summary,
      ticked: !have.summary,
    });
  }

  incoming.experience.forEach((role, i) => {
    const title = [role.title || 'a job', role.company].join(' · ');
    const match = have.experience.find((r) => sameRole(r, role));
    if (!match) {
      row({
        key: `experience:${i}`,
        section: 'experience',
        title,
        detail: entryLine(role.start, role.end, role.highlights.length, role.evidence.length),
      });
      return;
    }
    const added = additions(match, role, ROLE_FIELDS);
    if (hasAdditions(added)) {
      row({
        key: `experience:${i}`,
        section: 'experience',
        title,
        detail: addsLine(added),
        adds: true,
      });
    }
  });

  incoming.projects.forEach((project, i) => {
    const match = have.projects.find((p) => sameProject(p, project));
    if (!match) {
      row({
        key: `projects:${i}`,
        section: 'projects',
        title: project.name,
        detail: entryLine(
          project.start,
          project.end,
          project.highlights.length,
          project.evidence.length,
        ),
      });
      return;
    }
    const added = additions(match, project, PROJECT_FIELDS);
    if (hasAdditions(added)) {
      row({
        key: `projects:${i}`,
        section: 'projects',
        title: project.name,
        detail: addsLine(added),
        adds: true,
      });
    }
  });

  incoming.skills.forEach((group, i) => {
    const known = have.skills.flatMap((g) => g.items);
    const items = group.items.filter((item) => !known.some((k) => sameText(k, item)));
    if (items.length) {
      row({
        key: `skills:${i}`,
        section: 'skills',
        title: group.name,
        detail: items.join(', '),
        adds: have.skills.some((g) => sameText(g.name, group.name)),
      });
    }
  });

  incoming.education.forEach((school, i) => {
    const title = [school.degree, school.school].filter(Boolean).join(' · ');
    const match = have.education.find((s) => sameSchool(s, school));
    if (!match) {
      row({
        key: `education:${i}`,
        section: 'education',
        title,
        detail: rangeLabel(school.start, school.end, null),
      });
      return;
    }
    const added = additions(match, school, SCHOOL_FIELDS);
    if (hasAdditions(added)) {
      row({
        key: `education:${i}`,
        section: 'education',
        title,
        detail: addsLine(added),
        adds: true,
      });
    }
  });

  incoming.extras.forEach((group, i) => {
    const same = have.extras.find((g) => sameText(g.name, group.name));
    const items = group.items.filter((item) => !same?.items.some((k) => sameText(k, item)));
    if (items.length) {
      row({
        key: `extras:${i}`,
        section: 'other',
        title: group.name,
        detail: items.join(' · '),
        adds: !!same,
      });
    }
  });

  rejected.forEach((r, i) =>
    row({
      key: `rejected:${i}`,
      section: r.section,
      title: r.title,
      ticked: false,
      problems: r.problems,
    }),
  );
  return rows;
}

function entryLine(start: string, end: string, highlights: number, commits: number): string {
  return [
    rangeLabel(start, end),
    highlights && plural(highlights, 'highlight'),
    commits && plural(commits, 'commit'),
  ]
    .filter(Boolean)
    .join(' · ');
}

/** The general resume with the ticked rows added, in order and within the server's limits. */
export function applyMerge(
  base: Resume | null,
  incoming: Resume,
  keys: ReadonlySet<string>,
): Resume {
  const resume: Resume = structuredClone(base ?? blankResume());
  for (const [field] of FIELDS) {
    if (keys.has(`field:${field}`)) {
      resume[field] = incoming[field].trim();
    }
  }
  for (const link of incoming.links) {
    if (
      keys.has(`link:${linkKey(link.url)}`) &&
      !resume.links.some((l) => linkKey(l.url) === linkKey(link.url))
    ) {
      resume.links.push({ ...link });
    }
  }
  if (keys.has('summary')) {
    resume.summary = incoming.summary.trim();
  }

  incoming.experience.forEach((role, i) => {
    if (keys.has(`experience:${i}`)) {
      merge(resume.experience, role, sameRole, ROLE_FIELDS);
    }
  });
  incoming.projects.forEach((project, i) => {
    if (keys.has(`projects:${i}`)) {
      merge(resume.projects, project, sameProject, PROJECT_FIELDS);
    }
  });
  incoming.education.forEach((school, i) => {
    if (keys.has(`education:${i}`)) {
      merge(resume.education, school, sameSchool, SCHOOL_FIELDS);
    }
  });
  incoming.skills.forEach((group, i) => {
    if (keys.has(`skills:${i}`)) {
      const known = resume.skills.flatMap((g) => g.items);
      addToGroup(
        resume.skills,
        group,
        group.items.filter((item) => !known.some((k) => sameText(k, item))),
      );
    }
  });
  incoming.extras.forEach((group, i) => {
    if (keys.has(`extras:${i}`)) {
      addToGroup(resume.extras, group, group.items);
    }
  });

  const capped = inOrder(resume);
  return {
    ...capped,
    links: capped.links.slice(0, RESUME_LIMITS.items),
    experience: capped.experience.slice(0, RESUME_LIMITS.entries),
    projects: capped.projects.slice(0, RESUME_LIMITS.entries),
    education: capped.education.slice(0, RESUME_LIMITS.entries),
    skills: capped.skills.slice(0, RESUME_LIMITS.items),
    extras: capped.extras.slice(0, RESUME_LIMITS.items),
  };
}

function merge<
  T extends Dated & {
    highlights?: string[];
    stack?: string[];
    details?: string[];
    evidence?: ResumeEvidence[];
  },
>(
  list: T[],
  incoming: T,
  same: (a: T, b: T) => boolean,
  fields: readonly (keyof T & string)[],
): void {
  const match = list.find((entry) => same(entry, incoming));
  if (!match) {
    list.push(structuredClone(incoming));
    return;
  }
  const added = additions(match, incoming, fields);
  const cap = <V>(items: V[]) => items.slice(0, RESUME_LIMITS.items);
  if (match.highlights) {
    match.highlights = cap([...match.highlights, ...added.highlights]);
  }
  if (match.stack) {
    match.stack = cap([...match.stack, ...added.stack]);
  }
  if (match.details) {
    match.details = cap([...match.details, ...added.details]);
  }
  if (match.evidence) {
    match.evidence = cap([...match.evidence, ...added.evidence]);
  }
  for (const field of added.fields) {
    match[field] = incoming[field];
  }
}

function addToGroup(groups: ResumeGroup[], group: ResumeGroup, items: string[]): void {
  const same = groups.find((g) => sameText(g.name, group.name));
  if (!same) {
    groups.push({ name: group.name, items: items.slice(0, RESUME_LIMITS.items) });
    return;
  }
  const fresh = items.filter((item) => !same.items.some((k) => textKey(k) === textKey(item)));
  same.items = [...same.items, ...fresh].slice(0, RESUME_LIMITS.items);
}
