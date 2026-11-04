import { addDays, daysBetween, fromNow, longDate, when, whenWithDate } from './days';

describe('days', () => {
  it('counts across a month end', () => {
    expect(addDays('2026-10-30', 3)).toBe('2026-11-02');
    expect(daysBetween('2026-10-30', '2026-11-12')).toBe(13);
  });

  it('writes a day the way the design does', () => {
    expect(longDate('2026-11-03')).toBe('tue 3 nov');
    expect(when('2026-11-03', '2026-11-03')).toBe('today');
    expect(when('2026-11-06', '2026-11-03')).toBe('fri');
    expect(when('2026-11-12', '2026-11-03')).toBe('nov 12');
    expect(when('2026-10-30', '2026-11-03')).toBe('oct 30');
    expect(whenWithDate('2026-11-06', '2026-11-03')).toBe('fri 6');
  });

  it('says how far off a day is', () => {
    expect(fromNow('2026-11-03', '2026-11-03')).toBe('today');
    expect(fromNow('2026-11-04', '2026-11-03')).toBe('tomorrow');
    expect(fromNow('2026-11-06', '2026-11-03')).toBe('in 3 days');
    expect(fromNow('2026-11-02', '2026-11-03')).toBe('yesterday');
    expect(fromNow('2026-10-30', '2026-11-03')).toBe('4 days ago');
  });
});
