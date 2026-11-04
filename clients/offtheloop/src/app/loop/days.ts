/** Calendar days as YYYY-MM-DD in the user's own time zone, and how the design writes them. */

const WEEKDAYS = ['sun', 'mon', 'tue', 'wed', 'thu', 'fri', 'sat'];
const MONTHS = ['jan', 'feb', 'mar', 'apr', 'may', 'jun', 'jul', 'aug', 'sep', 'oct', 'nov', 'dec'];
const DAY_MS = 86_400_000;

export function toDay(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

export function today(): string {
  return toDay(new Date());
}

/** Local noon, so a daylight-saving change can't move it to another day. */
export function fromDay(day: string): Date {
  const [year, month, date] = day.split('-').map(Number);
  return new Date(year, month - 1, date, 12);
}

export function addDays(day: string, count: number): string {
  const date = fromDay(day);
  date.setDate(date.getDate() + count);
  return toDay(date);
}

export function daysBetween(from: string, to: string): number {
  return Math.round((fromDay(to).getTime() - fromDay(from).getTime()) / DAY_MS);
}

export function weekday(day: string): string {
  return WEEKDAYS[fromDay(day).getDay()];
}

export function dayOfMonth(day: string): number {
  return fromDay(day).getDate();
}

export function isWeekend(day: string): boolean {
  const index = fromDay(day).getDay();
  return index === 0 || index === 6;
}

/** "nov 12" */
export function shortDate(day: string): string {
  const date = fromDay(day);
  return `${MONTHS[date.getMonth()]} ${date.getDate()}`;
}

/** "tue 3 nov" */
export function longDate(day: string): string {
  const date = fromDay(day);
  return `${WEEKDAYS[date.getDay()]} ${date.getDate()} ${MONTHS[date.getMonth()]}`;
}

/** "today", a weekday within the week ahead, else "nov 12". */
export function when(day: string, now: string): string {
  const ahead = daysBetween(now, day);
  if (ahead === 0) {
    return 'today';
  }
  if (ahead > 0 && ahead < 7) {
    return weekday(day);
  }
  return shortDate(day);
}

/** "fri 6" within the week ahead, else "nov 12". */
export function whenWithDate(day: string, now: string): string {
  const ahead = daysBetween(now, day);
  return ahead >= 0 && ahead < 7 ? `${weekday(day)} ${dayOfMonth(day)}` : shortDate(day);
}

/** "today", "tomorrow", "in 3 days", "2 days ago". */
export function fromNow(day: string, now: string): string {
  const ahead = daysBetween(now, day);
  if (ahead === 0) {
    return 'today';
  }
  if (ahead === 1) {
    return 'tomorrow';
  }
  if (ahead === -1) {
    return 'yesterday';
  }
  return ahead > 0 ? `in ${ahead} days` : `${-ahead} days ago`;
}
