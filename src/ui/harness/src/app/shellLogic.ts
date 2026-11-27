// The top bar's logic with no React in it, tested in shellLogic.test.ts:
//   myWorkCount      the number on the My work tab: the same items My work lists
//   parseSearch      what the global search box holds: words, or a record number such as DEC-0123
import type { QueueItem } from '../data/contracts';
import { groupQueue } from '../pages/work-approvals/logic';

// The queue kinds My work lists from get_my_queue. Approvals come from list_approvals (view mine), as on the page.
export const MY_WORK_QUEUE_KINDS = ['draft', 'review', 'document', 'owner-gap', 'claim'] as const;
export const MY_WORK_QUEUE_TOP = 100;

// The number on the My work tab: the approval requests the person may answer now (list_approvals answerableCount, wave
// 4b), plus the queue items My work shows in its sections. Null until both reads answered. "more" says the queue holds more rows than were read, so the tab shows the
// number with a plus.
export function myWorkCount(approvalsTotal: number | null, queue: QueueItem[] | null, queueMore: boolean): { count: number; more: boolean } | null {
  if (approvalsTotal === null || queue === null) return null;
  const groups = groupQueue(queue);
  const items = Object.values(groups).reduce((n, list) => n + list.length, 0);
  return { count: approvalsTotal + items, more: queueMore };
}

// The tab's badge text: "7", "100+", or "99+" above 99.
export function countText(value: { count: number; more: boolean }): string {
  if (value.count > 99) return '99+';
  return value.more ? `${value.count}+` : String(value.count);
}

// ---------- the global search ----------

// The record number prefixes (engine RecordNumbers, document 25, B25-9) and the entity each one numbers.
export const RECORD_PREFIXES = {
  APP: 'business-application',
  IR: 'intake-request',
  DEC: 'decision',
  CHG: 'change-request',
  REV: 'review',
  CFG: 'configuration-draft',
  DU: 'data-use',
} as const;

export type RecordPrefix = keyof typeof RECORD_PREFIXES;

export interface SearchQuery {
  // The words as typed, trimmed, at most 100 characters (the longest text the search reads take).
  text: string;
  // Set when the text is one record number: "dec-123", "DEC 0123" and "DEC0123" all read as DEC-0123.
  recordNumber: { prefix: RecordPrefix; entity: (typeof RECORD_PREFIXES)[RecordPrefix]; number: string } | null;
  // True when the text is long enough to search people by name (find_people takes two letters or more).
  people: boolean;
}

const NUMBER = /^(app|ir|dec|chg|rev|cfg|du)[\s-]*(\d{1,9})$/i;

export function parseSearch(raw: string): SearchQuery {
  const text = raw.trim().replace(/\s+/g, ' ').slice(0, 100);
  const match = NUMBER.exec(text);
  let recordNumber: SearchQuery['recordNumber'] = null;
  if (match) {
    const prefix = match[1].toUpperCase() as RecordPrefix;
    const digits = match[2].replace(/^0+(?=\d)/, '');
    recordNumber = { prefix, entity: RECORD_PREFIXES[prefix], number: `${prefix}-${digits.padStart(4, '0')}` };
  }
  return { text, recordNumber, people: !recordNumber && /\p{L}.*\p{L}/u.test(text) };
}
