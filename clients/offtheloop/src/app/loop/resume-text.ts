import { Resume } from './model/resume';

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

const MONTH = /^\d{4}(-(0[1-9]|1[0-2]))?$/;

const OPEN_WORDS = ['present', 'current', 'now', 'ongoing', 'today'];

/** "2023-04" → "Apr 2023"; a year stays as it is. */
export function monthLabel(month: string): string {
  const [year, number] = month.split('-');
  return number ? `${MONTHS[Number(number) - 1]} ${year}` : year;
}

/** "Apr 2023 – now"; with no word for an open end, an unset end shows only the start. */
export function rangeLabel(start: string, end: string, open: string | null = 'now'): string {
  if (!start && !end) {
    return '';
  }
  if (!end) {
    return open === null ? monthLabel(start) : `${monthLabel(start)} – ${open}`;
  }
  return start && start !== end ? `${monthLabel(start)} – ${monthLabel(end)}` : monthLabel(end);
}

/** A month as people type it: "2023-04", "Apr 2023", "04/2023", "2023". Null when it isn't one; "" for now. */
export function readMonth(text: string): string | null {
  const value = text.trim();
  if (!value || OPEN_WORDS.includes(value.toLowerCase())) {
    return '';
  }
  if (MONTH.test(value)) {
    return value;
  }
  const day = /^(\d{4})-(\d{2})-\d{2}$/.exec(value);
  if (day) {
    return MONTH.test(`${day[1]}-${day[2]}`) ? `${day[1]}-${day[2]}` : null;
  }
  const slashed = /^(\d{1,2})\/(\d{4})$/.exec(value);
  if (slashed) {
    const month = `${slashed[2]}-${slashed[1].padStart(2, '0')}`;
    return MONTH.test(month) ? month : null;
  }
  const named = /^([a-z]{3})[a-z]*\.?\s+(\d{4})$/i.exec(value);
  const index = named ? MONTHS.findIndex((m) => m.toLowerCase() === named[1].toLowerCase()) : -1;
  return named && index >= 0 ? `${named[2]}-${String(index + 1).padStart(2, '0')}` : null;
}

/** Open ones first, then the latest end, then the latest start. */
export function byRecent(
  a: { start: string; end: string },
  b: { start: string; end: string },
): number {
  const ends = (b.end || '9999').localeCompare(a.end || '9999');
  return ends || b.start.localeCompare(a.start);
}

/** Jobs, projects and schools, most recent first. */
export function inOrder(resume: Resume): Resume {
  return {
    ...resume,
    experience: [...resume.experience].sort(byRecent),
    projects: [...resume.projects].sort(byRecent),
    education: [...resume.education].sort(byRecent),
  };
}

/** Matches ignoring case, spacing and a trailing full stop. */
export function sameText(a: string, b: string): boolean {
  return textKey(a) === textKey(b);
}

export function textKey(text: string): string {
  return text
    .trim()
    .toLowerCase()
    .replace(/\s+/g, ' ')
    .replace(/[.。]$/, '');
}

/** The resume as plain text, for an application form's box. */
export function resumeText(resume: Resume): string {
  const blocks: string[] = [];
  const head = [
    resume.name,
    resume.headline,
    [resume.location, resume.email, resume.phone].filter(Boolean).join(' · '),
    resume.links.map((l) => l.url).join(' · '),
  ].filter(Boolean);
  if (head.length) {
    blocks.push(head.join('\n'));
  }
  if (resume.summary) {
    blocks.push(resume.summary);
  }
  if (resume.experience.length) {
    blocks.push(
      'EXPERIENCE\n\n' +
        resume.experience
          .map((role) =>
            [
              [role.title, role.company].filter(Boolean).join(', '),
              [rangeLabel(role.start, role.end, 'present'), role.location]
                .filter(Boolean)
                .join(' · '),
              role.summary,
              ...role.highlights.map((h) => `- ${h}`),
            ]
              .filter(Boolean)
              .join('\n'),
          )
          .join('\n\n'),
    );
  }
  if (resume.projects.length) {
    blocks.push(
      'PROJECTS\n\n' +
        resume.projects
          .map((project) =>
            [
              [project.name, project.link].filter(Boolean).join(' · '),
              project.summary,
              ...project.highlights.map((h) => `- ${h}`),
            ]
              .filter(Boolean)
              .join('\n'),
          )
          .join('\n\n'),
    );
  }
  if (resume.skills.length) {
    blocks.push(
      'SKILLS\n\n' + resume.skills.map((g) => `${g.name}: ${g.items.join(', ')}`).join('\n'),
    );
  }
  if (resume.education.length) {
    blocks.push(
      'EDUCATION\n\n' +
        resume.education
          .map((school) =>
            [
              [school.degree, school.school].filter(Boolean).join(', '),
              rangeLabel(school.start, school.end, null),
              ...school.details.map((d) => `- ${d}`),
            ]
              .filter(Boolean)
              .join('\n'),
          )
          .join('\n\n'),
    );
  }
  for (const group of resume.extras) {
    blocks.push(`${group.name.toUpperCase()}\n\n${group.items.map((i) => `- ${i}`).join('\n')}`);
  }
  return blocks.join('\n\n') + '\n';
}
