import { plural } from './loop-summary';
import { ResumeEvidence } from './model/resume-evidence';
import { ResumeGroup } from './model/resume-group';
import { ResumeProject } from './model/resume-project';
import { ResumeRole } from './model/resume-role';
import { rangeLabel } from './resume-text';

export interface MarkPart {
  text: string;
  hit: boolean;
}

export type ReadingKind = 'role' | 'project';

/** A job or project as the reading view lays it out, cut down to what a find matches. */
export interface ReadingEntry {
  /** Also the id of the element it's read at. */
  key: string;
  kind: ReadingKind;
  index: number;
  source: ResumeRole | ResumeProject;
  title: string;
  /** The company, beside a job's title. */
  at: string;
  /** What the outline calls it. */
  label: string;
  link: string;
  dates: string;
  span: string;
  years: string;
  summary: string;
  total: number;
  /** Numbered as on the resume; while finding, only the ones that match. */
  highlights: { n: string; text: string }[];
  stack: string[];
  /** Newest first. */
  evidence: ResumeEvidence[];
  repos: string;
  /** Matching highlights, stack items and commits. */
  hits: number;
  shown: boolean;
}

export interface ReadingGroup {
  name: string;
  items: string[];
  hits: number;
  shown: boolean;
}

/** What a find box holds, as matched; one letter matches too much to be any use. */
export function findKey(query: string): string {
  const key = query.trim().toLowerCase();
  return key.length > 1 ? key : '';
}

export function hasText(text: string, key: string): boolean {
  return !!key && text.toLowerCase().includes(key);
}

/** The text cut around each match of the key. */
export function markParts(text: string, key: string): MarkPart[] {
  if (!key) {
    return [{ text, hit: false }];
  }
  const lower = text.toLowerCase();
  const parts: MarkPart[] = [];
  let from = 0;
  for (let at = lower.indexOf(key); at >= 0; at = lower.indexOf(key, from)) {
    if (at > from) {
      parts.push({ text: text.slice(from, at), hit: false });
    }
    parts.push({ text: text.slice(at, at + key.length), hit: true });
    from = at + key.length;
  }
  if (from < text.length) {
    parts.push({ text: text.slice(from), hit: false });
  }
  return parts;
}

/** "4 yrs 11 mo", both end months counted; empty unless the start has its month. */
export function spanLabel(start: string, end: string, today: Date): string {
  const [fromYear, fromMonth] = start.split('-').map(Number);
  const [toYear, toMonth] = end
    ? end.split('-').map(Number)
    : [today.getFullYear(), today.getMonth() + 1];
  if (!fromMonth || !toMonth) {
    return '';
  }
  const months = (toYear - fromYear) * 12 + toMonth - fromMonth + 1;
  if (months < 1) {
    return '';
  }
  const years = Math.floor(months / 12);
  return [years ? plural(years, 'yr') : '', months % 12 ? `${months % 12} mo` : '']
    .filter(Boolean)
    .join(' ');
}

/** "2022–now", "2019–21", "2018". */
export function yearsLabel(start: string, end: string): string {
  const from = start.slice(0, 4);
  const to = end.slice(0, 4);
  if (!from) {
    return to;
  }
  if (!end) {
    return `${from}–now`;
  }
  return to === from ? from : `${from}–${to.slice(2)}`;
}

export function readEntries(
  kind: ReadingKind,
  list: readonly (ResumeRole | ResumeProject)[],
  key: string,
  today: Date,
): ReadingEntry[] {
  return list.map((item, index) => {
    const role = kind === 'role' ? (item as ResumeRole) : null;
    const title = role ? role.title || role.company : (item as ResumeProject).name;
    const at = role?.title ? role.company : '';
    const numbered = item.highlights.map((text, i) => ({ n: String(i + 1).padStart(2, '0'), text }));
    const highlights = key ? numbered.filter((h) => hasText(h.text, key)) : numbered;
    const hits = key
      ? highlights.length +
        item.stack.filter((s) => hasText(s, key)).length +
        item.evidence.filter((e) => hasText(e.what, key)).length
      : 0;
    return {
      key: `${kind}-${index}`,
      kind,
      index,
      source: item,
      title,
      at,
      label: role ? role.company || role.title : title,
      link: role ? '' : (item as ResumeProject).link,
      dates: rangeLabel(item.start, item.end),
      span: spanLabel(item.start, item.end, today),
      years: yearsLabel(item.start, item.end),
      summary: item.summary,
      total: item.highlights.length,
      highlights,
      stack: item.stack,
      evidence: [...item.evidence].sort((a, b) => b.date.localeCompare(a.date)),
      repos: [...new Set(item.evidence.map((e) => e.repo).filter(Boolean))].join(', '),
      hits,
      shown: !key || hits > 0 || [title, at, item.summary].some((text) => hasText(text, key)),
    };
  });
}

export function readGroups(groups: readonly ResumeGroup[], key: string): ReadingGroup[] {
  return groups.map((group) => {
    const hits = key ? group.items.filter((item) => hasText(item, key)).length : 0;
    return {
      name: group.name,
      items: group.items,
      hits,
      shown: !key || hits > 0 || hasText(group.name, key),
    };
  });
}

/** The line under an entry's summary: how much is on it, or how much of it a find matched. */
export function entryMeta(entry: ReadingEntry, key: string): string {
  if (key) {
    return `${entry.highlights.length} of ${plural(entry.total, 'highlight')} match`;
  }
  return [
    entry.total ? plural(entry.total, 'highlight') : '',
    entry.stack.length ? `${entry.stack.length} in stack` : '',
    entry.evidence.length ? plural(entry.evidence.length, 'commit') : '',
  ]
    .filter(Boolean)
    .join(' · ');
}

/** "Paper title, Journal: https://…" → the words, and the address to link. */
export function trailingLink(text: string): { text: string; url: string } {
  const match = /^(.*?)[\s:]*(https?:\/\/\S+)$/.exec(text.trim());
  return match ? { text: match[1], url: match[2] } : { text, url: '' };
}

/** An address as a reader sees it: no scheme, no www, no trailing slash. */
export function linkLabel(url: string): string {
  return url.replace(/^https?:\/\/(www\.)?/, '').replace(/\/$/, '');
}
