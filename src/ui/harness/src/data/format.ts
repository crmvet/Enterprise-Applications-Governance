// Date and text helpers.
//
// Date-only values (2026-11-22) are calendar dates and never move between zones. Date-time values are shown in the
// display zone, which the build chooses once at startup with configureDisplayZone:
//   the harness shows UTC, so every render is the same on every machine;
//   the web app shows the viewer's own zone (the browser's local zone) and names it, for example "GMT+1" or "EST".
// Wave 4b: in the viewer's zone a date and time carries the zone's name at that moment, so a July
// time in New York reads EDT and a December one EST; zoneLabel() keeps today's name, for headings.
import type { IsoDate, IsoDateTime } from './contracts';

export type DisplayZone = 'utc' | 'local';

interface Formatters {
  dayMonthYear: Intl.DateTimeFormat;
  dayMonth: Intl.DateTimeFormat;
  weekdayDayMonth: Intl.DateTimeFormat;
  time: Intl.DateTimeFormat;
  month: Intl.DateTimeFormat;
  weekday: Intl.DateTimeFormat;
  dayKey: Intl.DateTimeFormat;
}

function formatters(timeZone: string | undefined): Formatters {
  return {
    dayMonthYear: new Intl.DateTimeFormat('en-GB', { timeZone, day: 'numeric', month: 'short', year: 'numeric' }),
    dayMonth: new Intl.DateTimeFormat('en-GB', { timeZone, day: 'numeric', month: 'short' }),
    weekdayDayMonth: new Intl.DateTimeFormat('en-GB', { timeZone, weekday: 'short', day: 'numeric', month: 'short' }),
    time: new Intl.DateTimeFormat('en-GB', { timeZone, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }),
    month: new Intl.DateTimeFormat('en-GB', { timeZone, month: 'short' }),
    weekday: new Intl.DateTimeFormat('en-GB', { timeZone, weekday: 'short' }),
    // en-CA writes dates as YYYY-MM-DD.
    dayKey: new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }),
  };
}

// Calendar dates always format in UTC, because they are parsed as UTC midnight.
const dates = formatters('UTC');
let zoneMode: DisplayZone = 'utc';
let times = dates;
let zoneName = 'UTC';
// The zone's short name at a given moment, in the viewer's zone; null in UTC display.
let zoneNames: Intl.DateTimeFormat | null = null;

// Chooses the zone for date-time values. Call it once, before the first render.
export function configureDisplayZone(mode: DisplayZone, now: Date = new Date()): void {
  zoneMode = mode;
  if (mode === 'utc') {
    times = dates;
    zoneName = 'UTC';
    zoneNames = null;
    return;
  }
  times = formatters(undefined);
  zoneNames = new Intl.DateTimeFormat('en-US', { timeZoneName: 'short' });
  zoneName = zoneAt(now);
}

// The short name of the display zone today, for headings such as "Times in EST".
export const zoneLabel = (): string => zoneName;

// The zone's short name at the moment a value names: EDT for a July time in New York, EST for a December one. In UTC
// display it is UTC.
function zoneAt(value: Date): string {
  if (!zoneNames) return zoneName;
  const part = zoneNames.formatToParts(value).find(p => p.type === 'timeZoneName');
  return part?.value ?? (Intl.DateTimeFormat().resolvedOptions().timeZone || 'local time');
}

const isDateOnly = (value: string) => value.length === 10;
const toDate = (value: IsoDate | IsoDateTime) => new Date(isDateOnly(value) ? `${value}T00:00:00Z` : value);
const pick = (value: string) => (isDateOnly(value) ? dates : times);

// Every month in three letters: en-GB writes September as "Sept" (QA, fix round 1: "15 Sept 2026" beside "3 Oct").
const threeLetters = (text: string) => text.replace(/\bSept\b/g, 'Sep');

export const fmtDate = (value: IsoDate | IsoDateTime | null | undefined, fallback = 'Not set') =>
  value ? threeLetters(pick(value).dayMonthYear.format(toDate(value))) : fallback;

export const fmtDayMonth = (value: IsoDate | IsoDateTime) => threeLetters(pick(value).dayMonth.format(toDate(value)));
export const fmtWeekday = (value: IsoDate | IsoDateTime) => threeLetters(pick(value).weekdayDayMonth.format(toDate(value)));
export const fmtTime = (value: IsoDateTime) => times.time.format(toDate(value));
export const fmtMonth = (value: IsoDate | IsoDateTime) => threeLetters(pick(value).month.format(toDate(value)));
export const fmtWeekdayOnly = (value: IsoDate | IsoDateTime) => pick(value).weekday.format(toDate(value));
export const dayOfMonth = (value: IsoDate | IsoDateTime) => Number(dayKey(value).slice(8, 10));

// The calendar day of a value in the display zone, as YYYY-MM-DD.
export function dayKey(value: IsoDate | IsoDateTime): IsoDate {
  return isDateOnly(value) ? value : times.dayKey.format(toDate(value));
}

// Today in the display zone, as YYYY-MM-DD.
export function todayInZone(now: Date = new Date()): IsoDate {
  return times.dayKey.format(now);
}

// The first moment of a calendar day in the display zone, in epoch milliseconds.
export function dayStartMs(date: IsoDate): number {
  if (zoneMode === 'utc') return Date.parse(`${date}T00:00:00Z`);
  const [y, m, d] = date.split('-').map(Number);
  return new Date(y, m - 1, d).getTime();
}

export const fmtDateTime = (value: IsoDateTime | null | undefined) =>
  value ? `${fmtDate(value)}, ${fmtTime(value)} ${zoneAt(toDate(value))}` : 'Not set';

// "27 Nov, 18:00 to 21:00 UTC" or "4 Dec, 22:00 to 5 Dec, 01:00 UTC". A window across a clock change names the zone
// after each time: "1 Nov, 01:00 EDT to 01:30 EST".
export function fmtWindow(start: IsoDateTime | null, end: IsoDateTime | null): string {
  if (!start || !end) return 'No window yet';
  const startZone = zoneAt(toDate(start));
  const endZone = zoneAt(toDate(end));
  const from = startZone === endZone ? fmtTime(start) : `${fmtTime(start)} ${startZone}`;
  return dayKey(start) === dayKey(end)
    ? `${fmtDayMonth(start)}, ${from} to ${fmtTime(end)} ${endZone}`
    : `${fmtDayMonth(start)}, ${from} to ${fmtDayMonth(end)}, ${fmtTime(end)} ${endZone}`;
}

// "18:00 to 21:00" on one day, or "22:00 to 5 Dec, 01:00" across midnight. No zone name.
export function fmtTimeRange(start: IsoDateTime, end: IsoDateTime): string {
  return dayKey(start) === dayKey(end)
    ? `${fmtTime(start)} to ${fmtTime(end)}`
    : `${fmtTime(start)} to ${fmtDayMonth(end)}, ${fmtTime(end)}`;
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

// The from and to dates to send a tool for the calendar days from..to in the display zone. The tools filter by UTC
// calendar date, so outside UTC the local days start and end on other UTC dates: this range covers every moment of the
// local days, and inDays (or inWindowDays) drops what it brings in from the days around them. In UTC display it is
// from..to itself.
export function utcDayRange(from: IsoDate, to: IsoDate): { from: IsoDate; to: IsoDate } {
  const first = dayStartMs(from);
  const last = dayStartMs(addDays(to, 1)) - 1;
  return { from: new Date(first).toISOString().slice(0, 10), to: new Date(last).toISOString().slice(0, 10) };
}

// True when a date or moment falls on one of the calendar days from..to in the display zone.
export function inDays(value: IsoDate | IsoDateTime, from: IsoDate, to: IsoDate): boolean {
  const day = dayKey(value);
  return day >= from && day <= to;
}

// True when a window touches the calendar days from..to in the display zone: it ends on or after from and starts on or
// before to, as the change schedule tool selects windows. An open end counts as touching.
export function inWindowDays(start: IsoDateTime | null, end: IsoDateTime | null, from: IsoDate, to: IsoDate): boolean {
  return (!end || dayKey(end) >= from) && (!start || dayKey(start) <= to);
}

// A configuration key shown when its label could not be read: "reads-data-from" becomes "Reads data from".
export function humanizeKey(key: string | null | undefined): string {
  if (!key) return 'Not set';
  const words = key.replace(/[-_]+/g, ' ').trim();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

export const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

// The hint under a review request's due date (wave 4b review): an empty date takes the number of days the rules in force
// set (setting review-request-due-days). Before that setting is read, or where it is not a number, the hint says the
// rules set it, which holds either way.
export function reviewDueHint(days: unknown): string {
  if (typeof days === 'number' && Number.isFinite(days) && days >= 0) {
    return days === 0 ? 'Optional. Empty: due today, as the rules in force set.' : `Optional. Empty: due in ${plural(days, 'day', 'days')}, as the rules in force set.`;
  }
  return 'Optional. Empty: due on the day the rules in force set.';
}

// A link out of the product, or null. The tool schemas type every such URL as https, but the page does not rely on the
// server's checks: only an absolute https URL without user information becomes a link, so a stored value such as a
// javascript: or data: URL is shown as text and never navigated to.
export function externalHref(value: string | null | undefined): string | null {
  if (typeof value !== 'string' || value.length === 0 || value.length > 4096) return null;
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    return null;
  }
  return url.protocol === 'https:' && !url.username && !url.password ? url.href : null;
}

// The person a write was made for, only when that is someone other than who made it. A person's own write on the web
// app carries On Behalf Of as themselves, and "Nadia Ferreira for Nadia Ferreira" says nothing.
export function forSomeoneElse<P extends { objectId: string; displayLabel?: string | null }>(
  principal: { objectId: string } | null | undefined,
  onBehalfOf: P | null | undefined,
): P | null {
  if (!onBehalfOf) return null;
  if (principal && principal.objectId.toLowerCase() === onBehalfOf.objectId.toLowerCase()) return null;
  return onBehalfOf;
}

// Dates the engine writes in its own words (2027-01-15) shown as the page shows dates. A date inside a longer token,
// such as an ID or an address, is left alone.
export function humanizeDates(text: string): string {
  return text.replace(/(^|[\s(,;:"'])(\d{4}-\d{2}-\d{2})(?![\dT:/-])/g, (whole, before: string, date: string) => {
    try {
      return `${before}${fmtDate(date, date)}`;
    } catch {
      return whole;
    }
  });
}
