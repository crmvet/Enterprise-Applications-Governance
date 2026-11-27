// Pure helpers for the Changes pages and the deployment, document and claim record pages. No React here, so the unit
// tests run them in Node (logic.test.ts).
import type {
  DeploymentFlags,
  DeploymentItem,
  GateLevel,
  IsoDate,
  IsoDateTime,
  RecordStatusMeaning,
  SaveChangeRequestDraftInput,
  ScheduledChange,
  StatusMeaning,
} from '../../data/contracts';

// A problem the page finds in its own form, in the action sheet's shape (components/actions.tsx FormProblem).
export interface Problem {
  problem: string;
  field?: string;
}

const isProblem = (v: unknown): v is Problem => typeof v === 'object' && v !== null && typeof (v as Problem).problem === 'string';
export { isProblem };

// ---------- dates and times ----------

// A datetime-local value, "2026-12-02T18:00" in the viewer's own zone, as an ISO moment in UTC ending in Z with no
// milliseconds (the tool schemas ask for Z). Null for an empty or unreadable value.
export function localInputToUtc(value: string): IsoDateTime | null {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2})?$/.test(value)) return null;
  // A date and time with no offset is read in the local zone.
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return null;
  return d.toISOString().replace(/\.\d{3}Z$/, 'Z');
}

// An ISO moment as a datetime-local value in the viewer's own zone, for a form that edits it.
export function utcToLocalInput(iso: IsoDateTime | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

// Whole minutes in a window, or null when either end is missing.
export function windowMinutes(start: IsoDateTime | null, end: IsoDateTime | null): number | null {
  if (!start || !end) return null;
  const ms = Date.parse(end) - Date.parse(start);
  return Number.isFinite(ms) ? Math.round(ms / 60000) : null;
}

// "1 hour 30 minutes", "45 minutes", "2 days 3 hours".
export function durationText(minutes: number): string {
  if (minutes < 0) return 'ends before it starts';
  const days = Math.floor(minutes / 1440);
  const hours = Math.floor((minutes % 1440) / 60);
  const mins = minutes % 60;
  const part = (n: number, one: string, many: string) => (n ? `${n} ${n === 1 ? one : many}` : '');
  return [part(days, 'day', 'days'), part(hours, 'hour', 'hours'), part(mins, 'minute', 'minutes')].filter(Boolean).join(' ') || '0 minutes';
}

// The first day of the month a date falls in.
export function firstOfMonth(date: IsoDate): IsoDate {
  return `${date.slice(0, 7)}-01`;
}

// The first day of the month n months after (or before) a month's first day.
export function addMonths(month: IsoDate, n: number): IsoDate {
  const [y, m] = month.split('-').map(Number);
  const d = new Date(Date.UTC(y, m - 1 + n, 1));
  return d.toISOString().slice(0, 10);
}

// The last day of a month.
export function lastOfMonth(month: IsoDate): IsoDate {
  const next = addMonths(firstOfMonth(month), 1);
  const d = new Date(`${next}T00:00:00Z`);
  d.setUTCDate(0);
  return d.toISOString().slice(0, 10);
}

// The weeks of a month for a calendar, Monday first. Each week has seven dates; days of the months around it fill the
// first and last weeks.
export function monthWeeks(month: IsoDate): IsoDate[][] {
  const first = new Date(`${firstOfMonth(month)}T00:00:00Z`);
  const offset = (first.getUTCDay() + 6) % 7;
  const start = new Date(first);
  start.setUTCDate(first.getUTCDate() - offset);
  const last = lastOfMonth(month);
  const weeks: IsoDate[][] = [];
  const cursor = new Date(start);
  while (weeks.length < 6) {
    const week: IsoDate[] = [];
    for (let i = 0; i < 7; i++) {
      week.push(cursor.toISOString().slice(0, 10));
      cursor.setUTCDate(cursor.getUTCDate() + 1);
    }
    weeks.push(week);
    if (week[6] >= last) break;
  }
  return weeks;
}

// ---------- the change draft form ----------

export interface ChangeFormValues {
  title: string;
  changeTypeKey: string;
  standardChangeModelKey: string;
  description: string;
  // datetime-local values in the viewer's zone, or ''.
  windowStart: string;
  windowEnd: string;
  plannedTargetId: string;
  affected: { businessApplicationId: string; impactRoleKey: string }[];
  externalChangeNumber: string;
  externalChangeUrl: string;
}

export const emptyChangeForm = (): ChangeFormValues => ({
  title: '',
  changeTypeKey: '',
  standardChangeModelKey: '',
  description: '',
  windowStart: '',
  windowEnd: '',
  plannedTargetId: '',
  affected: [],
  externalChangeNumber: '',
  externalChangeUrl: '',
});

export type ChangeDraftInput = Omit<SaveChangeRequestDraftInput, 'clientRequestId' | 'validateOnly'>;

// An absolute https URL with no user information, as the tool schemas ask.
export function isHttpsUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return url.protocol === 'https:' && !url.username && !url.password;
  } catch {
    return false;
  }
}

// The save_change_request_draft input for the form, or the first problem the page can see itself. The engine checks the
// rest (the type, the model, the target, the linked mode) and names the field it refuses.
export function buildChangeDraft(form: ChangeFormValues, businessApplicationId: string, changeRequestId?: string): ChangeDraftInput | Problem {
  const title = form.title.trim();
  if (!title) return { problem: 'Give the change a short title.', field: 'title' };
  if (title.length > 200) return { problem: 'The title is longer than 200 characters.', field: 'title' };
  if (!form.changeTypeKey) return { problem: 'Choose the change type.', field: 'changeTypeKey' };
  const description = form.description.trim();
  if (!description) return { problem: 'Say what changes, and why.', field: 'description' };
  if (description.length > 4000) return { problem: 'The description is longer than 4,000 characters.', field: 'description' };

  const start = form.windowStart ? localInputToUtc(form.windowStart) : null;
  const end = form.windowEnd ? localInputToUtc(form.windowEnd) : null;
  if (form.windowStart && !start) return { problem: 'The window start is not a date and time.', field: 'windowStart' };
  if (form.windowEnd && !end) return { problem: 'The window end is not a date and time.', field: 'windowEnd' };
  if ((start && !end) || (!start && end)) return { problem: 'Give both the start and the end of the window, or leave both empty.', field: start ? 'windowEnd' : 'windowStart' };
  if (start && end && Date.parse(end) <= Date.parse(start)) return { problem: 'The window ends before it starts.', field: 'windowEnd' };

  const number = form.externalChangeNumber.trim();
  const url = form.externalChangeUrl.trim();
  if (number.length > 100) return { problem: 'The ITSM change number is longer than 100 characters.', field: 'externalChangeNumber' };
  if (url && !isHttpsUrl(url)) return { problem: 'The ITSM change link must be a full https address.', field: 'externalChangeUrl' };
  if ((number && !url) || (!number && url)) return { problem: 'Give both the ITSM change number and its link, or leave both empty.', field: number ? 'externalChangeUrl' : 'externalChangeNumber' };

  const seen = new Set<string>();
  const affected = form.affected.filter(a => {
    const id = a.businessApplicationId.toLowerCase();
    if (id === businessApplicationId.toLowerCase() || seen.has(id) || !a.impactRoleKey) return false;
    seen.add(id);
    return true;
  });

  const input: ChangeDraftInput = { title, businessApplicationId, changeTypeKey: form.changeTypeKey, description };
  if (changeRequestId) input.changeRequestId = changeRequestId;
  if (form.standardChangeModelKey) input.standardChangeModelKey = form.standardChangeModelKey;
  if (start && end) {
    input.windowStart = start;
    input.windowEnd = end;
  }
  if (form.plannedTargetId) input.plannedTargetId = form.plannedTargetId;
  if (affected.length) input.affected = affected;
  if (number && url) {
    input.externalChangeNumber = number;
    input.externalChangeUrl = url;
  }
  return input;
}

// ---------- the change list ----------

const CLOSED: StatusMeaning[] = ['completed', 'rejected', 'withdrawn', 'cancelled'];
export const isClosed = (meaning: StatusMeaning) => CLOSED.includes(meaning);

export type StatusFilter = 'all' | 'open' | RecordStatusMeaning;
export type AuthorizationFilter = 'all' | 'authorized' | 'not-authorized';
export type TimingFilter = 'all' | 'coming' | 'past';

export interface ChangeFilter {
  status: StatusFilter;
  type: string;
  authorization: AuthorizationFilter;
  timing: TimingFilter;
}

export const NO_FILTER: ChangeFilter = { status: 'all', type: 'all', authorization: 'all', timing: 'all' };

export function filterChanges(items: ScheduledChange[], filter: ChangeFilter, nowMs: number): ScheduledChange[] {
  return items.filter(c => {
    const m = c.status.meaning;
    if (filter.status === 'open' && isClosed(m)) return false;
    if (filter.status !== 'all' && filter.status !== 'open' && m !== filter.status) return false;
    if (filter.type !== 'all' && c.changeType.key !== filter.type) return false;
    if (filter.authorization === 'authorized' && !c.authorized) return false;
    if (filter.authorization === 'not-authorized' && c.authorized) return false;
    const start = c.windowStart ? Date.parse(c.windowStart) : null;
    const end = c.windowEnd ? Date.parse(c.windowEnd) : start;
    if (filter.timing === 'coming' && (end === null || end < nowMs)) return false;
    if (filter.timing === 'past' && (end === null || end >= nowMs)) return false;
    return true;
  });
}

// The list's filters in the address (wave 4: filters in the address, so Back keeps them): status=, type=, auth=,
// timing=. "all" is left out.
export const STATUS_PARAM = /^(open|draft|submitted|in-progress|completed|rejected|withdrawn|cancelled)$/;
export const AUTH_PARAM = /^(authorized|not-authorized)$/;
export const TIMING_PARAM = /^(coming|past)$/;

export function changeFilterParams(filter: ChangeFilter): Record<string, string | undefined> {
  return {
    status: filter.status === 'all' ? undefined : filter.status,
    type: filter.type === 'all' ? undefined : filter.type,
    auth: filter.authorization === 'all' ? undefined : filter.authorization,
    timing: filter.timing === 'all' ? undefined : filter.timing,
  };
}

// The words a change holds for the page's text search: its title, application, target and type.
export function changeText(c: ScheduledChange): (string | null | undefined)[] {
  return [c.title, c.applicationName, c.target?.label, c.changeType.label, c.status.label];
}

export function activeFilterCount(filter: ChangeFilter): number {
  return (filter.status !== 'all' ? 1 : 0) + (filter.type !== 'all' ? 1 : 0) + (filter.authorization !== 'all' ? 1 : 0) + (filter.timing !== 'all' ? 1 : 0);
}

// The figures above the list, each with the filter that shows its rows.
export interface ChangeFigure {
  key: 'waiting' | 'coming' | 'in-progress' | 'emergency' | 'drafts';
  value: number;
  filter: ChangeFilter;
}

export function changeFigures(items: ScheduledChange[], nowMs: number): ChangeFigure[] {
  const figures: Omit<ChangeFigure, 'value'>[] = [
    { key: 'waiting', filter: { ...NO_FILTER, status: 'submitted', authorization: 'not-authorized' } },
    { key: 'coming', filter: { ...NO_FILTER, status: 'open', authorization: 'authorized', timing: 'coming' } },
    { key: 'in-progress', filter: { ...NO_FILTER, status: 'in-progress' } },
    { key: 'emergency', filter: { ...NO_FILTER, status: 'open', type: 'emergency' } },
    { key: 'drafts', filter: { ...NO_FILTER, status: 'draft' } },
  ];
  return figures.map(f => ({ ...f, value: filterChanges(items, f.filter, nowMs).length }));
}

// ---------- results and deployments ----------

// The result the linked deployments suggest, as a change result value key, or null when they suggest none. A person
// still chooses the result; the suggestion only fills the form.
export function suggestResult(results: StatusMeaning[]): 'succeeded' | 'partly-succeeded' | 'failed' | null {
  const counted = results.filter(r => r === 'succeeded' || r === 'partly-succeeded' || r === 'failed');
  if (!counted.length) return null;
  if (counted.every(r => r === 'succeeded')) return 'succeeded';
  if (counted.every(r => r === 'failed')) return 'failed';
  return 'partly-succeeded';
}

// The changes of the deployment's application whose window is near the moment it finished, closest first: a window that
// holds the moment first, then by distance. Same target only, when both sides name one. Used to show which change a
// deployment may belong to; the change request record says for sure.
export function nearbyChanges(items: ScheduledChange[], deployment: Pick<DeploymentItem, 'businessApplicationId' | 'deploymentTargetId' | 'finishedOn'>): ScheduledChange[] {
  const at = Date.parse(deployment.finishedOn);
  const app = deployment.businessApplicationId?.toLowerCase();
  const target = deployment.deploymentTargetId?.toLowerCase();
  const distance = (c: ScheduledChange) => {
    const start = c.windowStart ? Date.parse(c.windowStart) : NaN;
    const end = c.windowEnd ? Date.parse(c.windowEnd) : start;
    if (!Number.isFinite(start)) return Number.MAX_SAFE_INTEGER;
    if (at >= start && at <= end) return 0;
    return Math.min(Math.abs(at - start), Math.abs(at - end));
  };
  return items
    .filter(c => (!app || c.businessApplicationId.toLowerCase() === app) && (!target || !c.target || c.target.deploymentTargetId.toLowerCase() === target))
    .map(c => ({ c, d: distance(c) }))
    .filter(x => x.d !== Number.MAX_SAFE_INTEGER)
    .sort((a, b) => a.d - b.d || a.c.title.localeCompare(b.c.title))
    .map(x => x.c);
}

// What each gate level does (document 21, section 7.2). The ladder is engine contract; which level applies is configuration.
export const gateLevelText: Record<GateLevel, string> = {
  off: 'Not governed. No approval is asked and no missing approval is flagged.',
  record: 'Flags are worked out and stored. Nobody is told.',
  flag: 'Flags are worked out and stored, and the owners are told.',
  advise: 'Flags are stored and the owners are told. The pipeline\'s own check step warns before it deploys.',
  enforce: 'The pipeline\'s own check step stops the deployment unless the approvals are complete or the emergency rule applies.',
};

export const gateLevelLabel: Record<GateLevel, string> = {
  off: 'Off',
  record: 'Record',
  flag: 'Flag',
  advise: 'Advise',
  enforce: 'Enforce',
};

// The guideline each deployment flag puts into practice, for the guideline chip next to the flag's explanation.
export const flagGuideline: Record<keyof DeploymentFlags, string> = {
  missingApproval: 'G12',
  outsideWindow: 'G12',
  otherPath: 'G9',
  aiOnlyCodeApproval: 'G17',
};

// What a raised flag asks of the people who follow it up.
export const flagFollowUp: Record<keyof DeploymentFlags, string> = {
  missingApproval: 'Find the change this deployment belongs to and its authorization, or propose an exception.',
  outsideWindow: 'Check why it ran outside the planned window. If it was right, propose an exception.',
  otherPath: 'Check how it reached the target. Releases go through the pipeline the target expects.',
  aiOnlyCodeApproval: 'A person reviews the code change. An AI approval never counts as authorization.',
};

// ---------- people ----------

export function samePrincipal(a: { objectId: string } | null | undefined, objectId: string | null | undefined): boolean {
  return !!a && !!objectId && a.objectId.toLowerCase() === objectId.toLowerCase();
}

// ---------- record lookups ----------

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

// The application hint a record link may carry (?app=<id>), lowercased, or null. The deployment, document and claim reads
// have no lookup by ID, so the hint narrows the search to one application.
export function appHintOf(search: string): string | null {
  const value = new URLSearchParams(search).get('app');
  return value && GUID.test(value) ? value.toLowerCase() : null;
}

// A record page's address with the application hint added.
export function withAppHint(href: string | null, applicationId: string | null | undefined): string | null {
  if (!href) return null;
  return applicationId && GUID.test(applicationId) ? `${href}?app=${applicationId.toLowerCase()}` : href;
}

export const sameId = (a: string | null | undefined, b: string | null | undefined) => !!a && !!b && a.toLowerCase() === b.toLowerCase();
