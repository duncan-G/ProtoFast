import { addDays, daysBetween, when } from './days';
import { Application } from './model/application';
import { statusOf } from './model/status';

export interface DayEvent {
  applicationId: string;
  companyId: string;
  company: string;
  what: string;
  offer: boolean;
}

export interface UpcomingDay {
  day: string;
  events: DayEvent[];
}

/** Day 1 is the day of the first application. */
export function searchDay(applications: Application[], now: string): number | null {
  const first = applications
    .map((a) => a.appliedOn)
    .filter((day) => day !== null)
    .sort()[0];
  return first ? Math.max(1, daysBetween(first, now) + 1) : null;
}

/** Each day from today on, with the open applications whose next step falls on it. */
export function upcoming(applications: Application[], now: string, count: number): UpcomingDay[] {
  const days = Array.from(
    { length: count },
    (_, i): UpcomingDay => ({ day: addDays(now, i), events: [] }),
  );
  const byDay = new Map(days.map((d) => [d.day, d]));
  for (const application of applications) {
    if (application.closed || !application.nextOn) {
      continue;
    }
    const offer = application.stage === 'offer';
    byDay.get(application.nextOn)?.events.push({
      applicationId: application.id,
      companyId: application.companyId,
      company: shortCompany(application.company),
      what: offer ? 'offer ends' : application.stage,
      offer,
    });
  }
  return days;
}

export function roundsAhead(applications: Application[], now: string, count: number): number {
  return upcoming(applications, now, count).reduce(
    (total, day) => total + day.events.filter((e) => !e.offer).length,
    0,
  );
}

export function openOffers(applications: Application[]): Application[] {
  return applications.filter((a) => statusOf(a) === 'offer');
}

/** What the loop's last column says: time left on an offer, the day of the next step, or a dash. */
export function nextLabel(application: Application, now: string): string {
  const status = statusOf(application);
  if (status === 'closed') {
    return '';
  }
  if (!application.nextOn) {
    return status === 'offer' ? 'offer' : '—';
  }
  if (status === 'offer') {
    const left = daysBetween(now, application.nextOn);
    return left >= 0 ? `${left}d left` : 'lapsed';
  }
  return when(application.nextOn, now);
}

export function stageLabel(application: Application): string {
  const status = statusOf(application);
  return status === 'closed' ? 'closed' : status === 'none' ? '—' : application.stage;
}

export function shortCompany(company: string): string {
  return company.trim().split(/\s+/)[0] ?? company;
}

export function initials(name: string): string {
  return name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((word) => word[0]?.toUpperCase() ?? '')
    .join('');
}

export function plural(count: number, one: string, many = `${one}s`): string {
  return `${count} ${count === 1 ? one : many}`;
}
