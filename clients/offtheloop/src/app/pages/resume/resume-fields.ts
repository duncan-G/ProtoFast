import { FieldSpec } from '../../editors/fields-form';
import { LoopError } from '../../loop/errors';
import { Resume } from '../../loop/model/resume';
import { ResumeLink } from '../../loop/model/resume-link';
import { BLANK_ENTRY, Entry, entryChanges, EntryKind } from './resume-entry';

export const ENTRY_LISTS: Record<EntryKind, 'experience' | 'projects' | 'education'> = {
  role: 'experience',
  project: 'projects',
  school: 'education',
};

export const ENTRY_NOUNS: Record<EntryKind, string> = {
  role: 'job',
  project: 'project',
  school: 'school',
};

export const HEAD_FIELDS: readonly FieldSpec[] = [
  { key: 'name', label: 'name', placeholder: 'Ada Park', look: 'lead', wide: true },
  {
    key: 'headline',
    label: 'headline',
    hint: 'one line under your name',
    placeholder: 'Backend engineer · payments',
    wide: true,
  },
  { key: 'email', label: 'email', placeholder: 'ada@example.com' },
  { key: 'phone', label: 'phone', placeholder: '+1 555 0100' },
  { key: 'location', label: 'based in', placeholder: 'Austin, TX' },
  {
    key: 'links',
    label: 'links',
    hint: 'one per line: label then address',
    type: 'textarea',
    look: 'mono',
    placeholder: 'GitHub https://github.com/ada\nportfolio https://ada.dev',
  },
];

export const SUMMARY_FIELDS: readonly FieldSpec[] = [
  {
    key: 'summary',
    label: 'summary',
    hint: 'who you are as an engineer',
    type: 'textarea',
    placeholder: 'Backend engineer who…',
  },
];

/** "GitHub https://github.com/ada" per line; a bare address takes its site as the label. */
function readLinks(text: string): ResumeLink[] {
  return text
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      const url = line.split(/\s+/).find((part) => /^https?:\/\/\S+$/i.test(part));
      if (!url) {
        throw new LoopError(`“${line}” has no web address starting https://.`);
      }
      const label = line
        .replace(url, '')
        .replace(/[\s:·—-]+$/, '')
        .trim();
      return { label: label || new URL(url).hostname.replace(/^www\./, ''), url };
    });
}

/** The head form's value: links as lines of text. */
export function headValue(resume: Resume): object {
  return { ...resume, links: resume.links.map((l) => `${l.label} ${l.url}`).join('\n') };
}

export function trimmedChanges(changes: Record<string, unknown>): Partial<Resume> {
  return Object.fromEntries(
    Object.entries(changes).map(([key, value]) => [key, String(value ?? '').trim()]),
  );
}

export function headChanges(changes: Record<string, unknown>): Partial<Resume> {
  const { links, ...fields } = changes;
  return { ...trimmedChanges(fields), links: readLinks(String(links ?? '')) };
}

/** The entry at index changed, or taken off when null. */
export function entryChange(
  resume: Resume,
  kind: EntryKind,
  index: number,
  entry: Entry | null,
): Partial<Resume> {
  const key = ENTRY_LISTS[kind];
  const list = [...resume[key]] as Entry[];
  if (entry) {
    list[index] = entry;
  } else {
    list.splice(index, 1);
  }
  return { [key]: list };
}

export function addedEntry(
  resume: Resume,
  kind: EntryKind,
  changes: Record<string, unknown>,
): Partial<Resume> {
  const key = ENTRY_LISTS[kind];
  return { [key]: [...resume[key], entryChanges(BLANK_ENTRY[kind](), changes)] };
}
