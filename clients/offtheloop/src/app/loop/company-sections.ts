import { Application } from './model/application';
import { Company } from './model/company';
import { STAGES } from './model/stage';
import { Status, statusOf } from './model/status';

export interface CompanyEntry {
  company: Company;
  /** In loop order, so the first is the one the company is listed by. */
  applications: Application[];
  lead: Application | null;
  status: Status;
  /** Starred and not applied for; a star stops counting once you apply. */
  starred: boolean;
}

export interface CompanySection {
  status: Status;
  entries: CompanyEntry[];
}

const SECTIONS: readonly Status[] = ['offer', 'active', 'applied', 'none', 'closed'];

/**
 * Companies grouped by their lead application's status, each group starred first, then by the
 * furthest stage, the nearest next date and the latest change. Applications come in loop order.
 */
export function companySections(
  companies: Company[],
  applications: Application[],
): CompanySection[] {
  const byCompany = new Map<string, Application[]>();
  for (const application of applications) {
    const list = byCompany.get(application.companyId) ?? [];
    list.push(application);
    byCompany.set(application.companyId, list);
  }
  const entries = companies.map((company): CompanyEntry => {
    const all = byCompany.get(company.id) ?? [];
    const lead = all[0] ?? null;
    const status = lead ? statusOf(lead) : 'none';
    return {
      company,
      applications: all,
      lead,
      status,
      starred: status === 'none' && company.starred,
    };
  });
  return SECTIONS.map((status) => ({
    status,
    entries: entries.filter((e) => e.status === status).sort(compare),
  })).filter((section) => section.entries.length);
}

function compare(a: CompanyEntry, b: CompanyEntry): number {
  return (
    Number(b.starred) - Number(a.starred) ||
    stageRank(b) - stageRank(a) ||
    compareDays(deadline(a), deadline(b)) ||
    lastModified(b) - lastModified(a)
  );
}

/** A company with no application sits below one not yet applied for. */
function stageRank(entry: CompanyEntry): number {
  return entry.lead ? STAGES.indexOf(entry.lead.stage) : -1;
}

/** A closed application's next date is stale. */
function deadline(entry: CompanyEntry): string | null {
  return entry.status === 'closed' ? null : (entry.lead?.nextOn ?? null);
}

function compareDays(a: string | null, b: string | null): number {
  if (a === b) {
    return 0;
  }
  if (a === null || b === null) {
    return a === null ? 1 : -1;
  }
  return a < b ? -1 : 1;
}

function lastModified(entry: CompanyEntry): number {
  return Math.max(entry.company.lastModified, ...entry.applications.map((a) => a.lastModified));
}
