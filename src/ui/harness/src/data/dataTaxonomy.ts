// The data taxonomy in words (D-37; docs/architecture/data-taxonomy.md), with no React in it, so
// every data page, the action sheet's engine answer and the tests read the same words:
//   dataUseStateText(state, given, needed)   "1 of 2 approvals", "Decision needed", "Breaks a rule"
//   DATA_USE_STATE_TONE[state]               the badge tone of a state (a label always shows; never color alone)
//   DATA_RULE_TEXT / DATA_RULE_TONE          a handling rule chip: Allowed, Decision, Masked only, Never
//   ruleSourceText(rule)                     where a rule comes from: "set on People / Payroll", "from Restricted"
//   DATA_ORIGIN_TEXT / DATA_ORIGIN_SHORT     a mapping's origin: "Copy of production", matrix cell "Copy"
//   DATA_ACCESS_TEXT / DATA_ACCESS_SHORT     "Reads and writes", matrix cell "RW"
//   DATA_CHECKS[code]                        a data check's title and its severity as designed (DC-01 to DC-14)
//   inheritedText(person)                    "Hana Lindqvist, from People"
//   needsAttention(state)                    true for the states someone must act on
// The engine computes every state; the page only names it.
import type {
  DataAccess,
  DataCheckCode,
  DataGoverningRule,
  DataHandlingRule,
  DataOrigin,
  DataPersonValue,
  DataRuleValue,
  DataSeverity,
  DataUseState,
} from './contracts';
import type { Tone } from './meanings';

export const DATA_USE_STATES: readonly DataUseState[] = ['not-needed', 'needed', 'proposed', 'allowed', 'denied', 're-decide', 'breaks-rule', 'no-one-to-ask'];

// The state chip's words (docs/architecture/data-taxonomy.md, Screens: the Uses tab). A proposed use shows its approvals instead when they are known.
export const DATA_USE_STATE_TEXT: Record<DataUseState, string> = {
  'not-needed': 'Not needed',
  needed: 'Decision needed',
  proposed: 'Waiting for approval',
  allowed: 'Allowed',
  denied: 'Denied',
  're-decide': 'Decide again',
  'breaks-rule': 'Breaks a rule',
  'no-one-to-ask': 'No one to ask',
};

// A longer sentence for a state, for a record page or a tooltip.
export const DATA_USE_STATE_HELP: Record<DataUseState, string> = {
  'not-needed': 'The rule in force allows this use, so it needs no decision.',
  needed: 'The rule in force asks for a data-use decision, and no decision allows it yet.',
  proposed: 'A data-use decision is proposed and waits for its approvals.',
  allowed: 'An accepted decision allows this use.',
  denied: 'An accepted decision denies this use.',
  're-decide': 'It was allowed, but the data or the rule has tightened since. It needs a new decision.',
  'breaks-rule': 'The rule in force does not allow this use, and no decision can allow it. End the use or change the data.',
  'no-one-to-ask': 'A decision is needed, but an approver it needs resolves to nobody. Appoint a data owner first.',
};

export const DATA_USE_STATE_TONE: Record<DataUseState, Tone> = {
  'not-needed': 'subtle',
  needed: 'warning',
  proposed: 'informative',
  allowed: 'success',
  denied: 'danger',
  're-decide': 'warning',
  'breaks-rule': 'danger',
  'no-one-to-ask': 'danger',
};

// The words of a state, with the approvals of a proposed decision when the read gives them: "1 of 2 approvals".
export function dataUseStateText(state: DataUseState, approvalsGiven?: number | null, approvalsNeeded?: number | null): string {
  if (state === 'proposed' && typeof approvalsNeeded === 'number' && approvalsNeeded > 0 && typeof approvalsGiven === 'number') {
    return `${Math.min(approvalsGiven, approvalsNeeded)} of ${approvalsNeeded} ${approvalsNeeded === 1 ? 'approval' : 'approvals'}`;
  }
  return DATA_USE_STATE_TEXT[state];
}

// The states someone must act on: a decision to make or propose, or a use that must change.
export function needsAttention(state: DataUseState): boolean {
  return state === 'needed' || state === 're-decide' || state === 'breaks-rule' || state === 'no-one-to-ask';
}

export const DATA_RULE_TEXT: Record<DataHandlingRule, string> = {
  allowed: 'Allowed',
  decision: 'Decision',
  'masked-only': 'Masked only',
  never: 'Never',
};

// "Never" is the attention color the design asks for; the label always shows.
export const DATA_RULE_TONE: Record<DataHandlingRule, Tone> = {
  allowed: 'subtle',
  decision: 'informative',
  'masked-only': 'warning',
  never: 'danger',
};

export const DATA_RULE_NAME: Record<'use' | 'ai' | 'copy', string> = {
  use: 'Use',
  ai: 'AI use',
  copy: 'Copy',
};

// The rule's name inside a sentence: the use rule, the AI use rule, the copy rule.
const RULE_IN_TEXT: Record<'use' | 'ai' | 'copy', string> = { use: 'use', ai: 'AI use', copy: 'copy' };

// Where a rule comes from: the domain that sets it, else the classification whose default applies.
export function ruleSourceText(rule: Pick<DataRuleValue, 'setOn' | 'fromClassification'>): string {
  if (rule.setOn) return `set on ${rule.setOn.label}`;
  if (rule.fromClassification) return `from ${rule.fromClassification.label}`;
  return 'the default';
}

// The governing rule in one sentence: "The AI use rule is Decision, set on People / Payroll."
export function governingRuleText(rule: DataGoverningRule): string {
  return `The ${RULE_IN_TEXT[rule.name]} rule is ${DATA_RULE_TEXT[rule.value]}, ${ruleSourceText(rule)}.`;
}

export const DATA_ORIGIN_TEXT: Record<DataOrigin, string> = {
  'system-of-record': 'System of record',
  replica: 'Replica',
  'production-copy': 'Copy of production',
  'masked-copy': 'Masked copy',
  synthetic: 'Synthetic',
};

// The mapping matrix's cell words (docs/architecture/data-taxonomy.md, Screens): SoR, Replica, Copy (a warning), Masked, Synthetic.
export const DATA_ORIGIN_SHORT: Record<DataOrigin, string> = {
  'system-of-record': 'SoR',
  replica: 'Replica',
  'production-copy': 'Copy',
  'masked-copy': 'Masked',
  synthetic: 'Synthetic',
};

export const DATA_ORIGIN_TONE: Record<DataOrigin, Tone> = {
  'system-of-record': 'brand',
  replica: 'subtle',
  'production-copy': 'warning',
  'masked-copy': 'informative',
  synthetic: 'subtle',
};

export const DATA_ACCESS_TEXT: Record<DataAccess, string> = {
  read: 'Reads',
  write: 'Writes',
  'read-write': 'Reads and writes',
};

export const DATA_ACCESS_SHORT: Record<DataAccess, string> = {
  read: 'R',
  write: 'W',
  'read-write': 'RW',
};

export const DATA_SEVERITY_TONE: Record<DataSeverity, Tone> = {
  high: 'danger',
  medium: 'warning',
  low: 'subtle',
};

export const DATA_CHECK_CODES: readonly DataCheckCode[] = [
  'DC-01', 'DC-02', 'DC-03', 'DC-04', 'DC-05', 'DC-06', 'DC-07', 'DC-08', 'DC-09', 'DC-10', 'DC-11', 'DC-12', 'DC-13', 'DC-14',
];

// The data checks as designed (docs/architecture/data-taxonomy.md, Data checks): the finding's title and its usual severity. A finding carries its own
// severity (DC-05 is high when the use reaches AI), so a page shows the finding's, and this one only to group.
export const DATA_CHECKS: Record<DataCheckCode, { title: string; severity: DataSeverity }> = {
  'DC-01': { title: 'No owner', severity: 'high' },
  'DC-02': { title: 'Own owner missing', severity: 'medium' },
  'DC-03': { title: 'No deputy', severity: 'medium' },
  'DC-04': { title: 'Owner or deputy inactive', severity: 'high' },
  'DC-05': { title: 'Decision missing', severity: 'medium' },
  'DC-06': { title: 'No one to ask', severity: 'high' },
  'DC-07': { title: 'Breaks a rule', severity: 'high' },
  'DC-08': { title: 'Copy outside production', severity: 'high' },
  'DC-09': { title: 'Sensitivity answer below the data', severity: 'medium' },
  'DC-10': { title: 'Undeclared data', severity: 'low' },
  'DC-11': { title: 'Mapping missing', severity: 'low' },
  'DC-12': { title: 'Location unowned or stale', severity: 'medium' },
  'DC-13': { title: 'Retention open', severity: 'high' },
  'DC-14': { title: 'Rows on a retired domain', severity: 'high' },
};

// A check code as a finding carries it ("DC-05"), or null for one this release does not know.
export function dataCheckOf(check: string): DataCheckCode | null {
  return (DATA_CHECK_CODES as readonly string[]).includes(check) ? (check as DataCheckCode) : null;
}

// An owner or steward with where it comes from: "Hana Lindqvist", or "Hana Lindqvist, from People" when inherited.
export function inheritedText(person: Pick<DataPersonValue, 'principal' | 'inheritedFrom'> | null | undefined, none = 'No one'): string {
  if (!person) return none;
  const name = person.principal.displayLabel || person.principal.objectId;
  return person.inheritedFrom ? `${name}, from ${person.inheritedFrom.label}` : name;
}
