// Date and text helpers. Every date is shown in UTC so the harness renders the same on every machine.
// The product pages would show the viewer's time zone and say which one; the layout does not change.
import type { IsoDate, IsoDateTime } from './contracts';

const dayMonthYear = new Intl.DateTimeFormat('en-GB', { timeZone: 'UTC', day: 'numeric', month: 'short', year: 'numeric' });
const dayMonth = new Intl.DateTimeFormat('en-GB', { timeZone: 'UTC', day: 'numeric', month: 'short' });
const weekdayDayMonth = new Intl.DateTimeFormat('en-GB', { timeZone: 'UTC', weekday: 'short', day: 'numeric', month: 'short' });
const time = new Intl.DateTimeFormat('en-GB', { timeZone: 'UTC', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
const monthShort = new Intl.DateTimeFormat('en-GB', { timeZone: 'UTC', month: 'short' });
const weekdayShort = new Intl.DateTimeFormat('en-GB', { timeZone: 'UTC', weekday: 'short' });

const toDate = (value: IsoDate | IsoDateTime) => new Date(value.length === 10 ? `${value}T00:00:00Z` : value);

export const fmtDate = (value: IsoDate | IsoDateTime | null | undefined, fallback = 'Not set') =>
  value ? dayMonthYear.format(toDate(value)) : fallback;

export const fmtDayMonth = (value: IsoDate | IsoDateTime) => dayMonth.format(toDate(value));
export const fmtWeekday = (value: IsoDate | IsoDateTime) => weekdayDayMonth.format(toDate(value));
export const fmtTime = (value: IsoDateTime) => time.format(toDate(value));
export const fmtMonth = (value: IsoDate | IsoDateTime) => monthShort.format(toDate(value));
export const fmtWeekdayOnly = (value: IsoDate | IsoDateTime) => weekdayShort.format(toDate(value));
export const dayOfMonth = (value: IsoDate | IsoDateTime) => toDate(value).getUTCDate();

export const fmtDateTime = (value: IsoDateTime | null | undefined) =>
  value ? `${dayMonthYear.format(toDate(value))}, ${time.format(toDate(value))} UTC` : 'Not set';

// "27 Nov, 18:00 to 21:00 UTC" or "4 Dec, 22:00 to 5 Dec, 01:00 UTC".
export function fmtWindow(start: IsoDateTime | null, end: IsoDateTime | null): string {
  if (!start || !end) return 'No window yet';
  const sameDay = start.slice(0, 10) === end.slice(0, 10);
  return sameDay
    ? `${fmtDayMonth(start)}, ${fmtTime(start)} to ${fmtTime(end)} UTC`
    : `${fmtDayMonth(start)}, ${fmtTime(start)} to ${fmtDayMonth(end)}, ${fmtTime(end)} UTC`;
}

// Whole days between two ISO dates (b minus a).
export function daysBetween(a: IsoDate, b: IsoDate): number {
  return Math.round((toDate(b).getTime() - toDate(a).getTime()) / 86400000);
}

export function addDays(date: IsoDate, days: number): IsoDate {
  const d = toDate(date);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

// A configuration key shown when its label could not be read: "reads-data-from" becomes "Reads data from".
export function humanizeKey(key: string | null | undefined): string {
  if (!key) return 'Not set';
  const words = key.replace(/[-_]+/g, ' ').trim();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

export const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;
