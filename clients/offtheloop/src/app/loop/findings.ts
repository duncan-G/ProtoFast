import { Application } from './model/application';
import { Company } from './model/company';
import { CompanyFindings } from './model/company-findings';
import { Contact } from './model/contact';
import { PostingFindings } from './model/posting-findings';
import { WebSource } from './model/web-source';

/** One thing found: a value for a field, or an item to add to a list. */
export interface Finding {
  target: 'company' | 'application';
  key: string;
  label: string;
  value: unknown;
  text: string;
  /** What the value would replace; empty for an empty field or a list item. */
  replaces: string;
  list: boolean;
  /** Clamped to a few lines. */
  long: boolean;
  /** Ticked: only what replaces nothing starts ticked. */
  keep: boolean;
}

/** What came back, laid out for review. */
export interface Review {
  findings: Finding[];
  sources: WebSource[];
  /** A line above the findings. */
  note?: string;
}

interface Spec {
  key: string;
  label: string;
  list?: boolean;
  long?: boolean;
  show?: (value: unknown) => string;
  /** What makes two list items the same. */
  match?: (value: unknown) => string;
}

const COMPANY: readonly Spec[] = [
  { key: 'about', label: 'about' },
  { key: 'location', label: 'based in' },
  { key: 'revenue', label: 'revenue' },
  { key: 'growth', label: 'growth target' },
  { key: 'headcount', label: 'people' },
  { key: 'funding', label: 'funding' },
  { key: 'lastRaised', label: 'last raised' },
  { key: 'competitors', label: 'competes with', list: true },
  { key: 'concerns', label: 'concerns', list: true },
  { key: 'values', label: 'values', list: true },
  { key: 'practices', label: 'how it works', list: true },
  {
    key: 'contacts',
    label: 'executives',
    list: true,
    show: (value) => {
      const { name, role } = value as Contact;
      return role ? `${name} · ${role}` : name;
    },
    match: (value) => (value as Contact).name,
  },
];

const POSTING_COMPANY = COMPANY.filter((spec) =>
  ['about', 'values', 'practices'].includes(spec.key),
);

const APPLICATION: readonly Spec[] = [
  { key: 'role', label: 'role' },
  { key: 'postingUrl', label: 'posting link' },
  { key: 'postedPay', label: 'posted range' },
  { key: 'team', label: 'team' },
  { key: 'reportsTo', label: 'reports to' },
  { key: 'arrangement', label: 'setup' },
  { key: 'location', label: 'where' },
  { key: 'hours', label: 'hours' },
  {
    key: 'sponsorsVisa',
    label: 'visa',
    show: (value) => (value ? 'sponsors' : "doesn't sponsor"),
  },
  { key: 'wants', label: 'they want', list: true },
  { key: 'stack', label: 'stack', list: true },
  { key: 'benefits', label: 'benefits', list: true },
  { key: 'questions', label: 'to ask', list: true },
  { key: 'posting', label: 'saved copy', long: true },
];

export function companyFindings(company: Company, found: CompanyFindings['company']): Finding[] {
  return compare('company', COMPANY, company, found);
}

/** Against no application, everything the posting says is new. */
export function postingFindings(
  application: Application | null,
  company: Company,
  found: PostingFindings,
): Finding[] {
  return [
    ...compare('application', APPLICATION, application ?? {}, found.application),
    ...compare('company', POSTING_COMPANY, company, found.company),
  ];
}

/** The kept findings as changes to the record: values replace, list items are appended. */
export function changesFrom<T extends object>(
  record: T,
  findings: readonly Finding[],
  target: Finding['target'],
): Partial<T> {
  const current = record as Record<string, unknown>;
  const changes: Record<string, unknown> = {};
  for (const finding of findings) {
    if (!finding.keep || finding.target !== target) {
      continue;
    }
    if (finding.list) {
      const list = (changes[finding.key] ?? current[finding.key] ?? []) as unknown[];
      changes[finding.key] = [...list, finding.value];
    } else {
      changes[finding.key] = finding.value;
    }
  }
  return changes as Partial<T>;
}

/** Whether a posting names another employer than the company it was read for. */
export function otherEmployer(employer: string, company: string): boolean {
  const a = squash(employer);
  const b = squash(company);
  return !!a && !!b && !a.includes(b) && !b.includes(a);
}

function compare(
  target: Finding['target'],
  specs: readonly Spec[],
  record: object,
  found: object,
): Finding[] {
  const current = record as Record<string, unknown>;
  const values = found as Record<string, unknown>;
  const findings: Finding[] = [];
  for (const spec of specs) {
    const value = values[spec.key];
    const show = spec.show ?? String;
    if (spec.list) {
      const match = spec.match ?? String;
      const have = new Set(
        ((current[spec.key] ?? []) as unknown[]).map((item) => squash(match(item))),
      );
      for (const item of (value ?? []) as unknown[]) {
        if (!have.has(squash(match(item)))) {
          findings.push(finding(target, spec, item, show(item), '', true));
        }
      }
      continue;
    }
    const before = current[spec.key];
    if (value === null || value === undefined || value === '' || same(value, before)) {
      continue;
    }
    const empty = before === null || before === undefined || before === '';
    findings.push(finding(target, spec, value, show(value), empty ? '' : show(before), empty));
  }
  return findings;
}

function finding(
  target: Finding['target'],
  spec: Spec,
  value: unknown,
  text: string,
  replaces: string,
  keep: boolean,
): Finding {
  return {
    target,
    key: spec.key,
    label: spec.label,
    value,
    text,
    replaces,
    list: !!spec.list,
    long: !!spec.long,
    keep,
  };
}

function same(a: unknown, b: unknown): boolean {
  return typeof a === 'string' && typeof b === 'string' ? squash(a) === squash(b) : a === b;
}

function squash(text: string): string {
  return text.toLowerCase().replace(/[^\p{L}\p{N}]+/gu, '');
}
