// The data taxonomy's small marks, shared by the Data page, the data record pages, Application 360, My work and the
// decision page, so a state or a rule reads the same everywhere (words in data/dataTaxonomy.ts). Every badge carries
// its label; color is never the only signal (D-35).
//   <DataUseStateBadge state approvalsGiven approvalsNeeded />   "1 of 2 approvals", "Breaks a rule"
//   <DataRuleChip name="ai" rule={domain.rules.ai} />             "AI use: Never", with where it is set as its title
//   <DataOriginBadge origin="production-copy" short />            "Copy"
//   <DataSeverityBadge severity="high" />                         "High"
//   <ClassificationBadge classification={domain.classification} />
//   <DomainLink domain={use.domain} />                            the domain's path, linked to its record page
import { Ban, CircleCheck, Clock, Lock, ShieldAlert, TriangleAlert } from 'lucide-react';
import type { ReactElement } from 'react';
import { domainHref } from '../app/links';
import type { DataClassificationValue, DataDomainRef, DataOrigin, DataRuleValue, DataSeverity, DataUseState, LabelledKey } from '../data/contracts';
import {
  DATA_ORIGIN_SHORT,
  DATA_ORIGIN_TEXT,
  DATA_ORIGIN_TONE,
  DATA_RULE_NAME,
  DATA_RULE_TEXT,
  DATA_RULE_TONE,
  DATA_SEVERITY_TONE,
  DATA_USE_STATE_HELP,
  DATA_USE_STATE_TONE,
  dataUseStateText,
  ruleSourceText,
} from '../data/dataTaxonomy';
import { TextLink, ToneBadge } from './blocks';

const STATE_ICON: Partial<Record<DataUseState, ReactElement>> = {
  proposed: <Clock aria-hidden />,
  allowed: <CircleCheck aria-hidden />,
  denied: <Ban aria-hidden />,
  'breaks-rule': <ShieldAlert aria-hidden />,
  'no-one-to-ask': <TriangleAlert aria-hidden />,
};

export function DataUseStateBadge({ state, approvalsGiven, approvalsNeeded, className }: {
  state: DataUseState;
  approvalsGiven?: number | null;
  approvalsNeeded?: number | null;
  className?: string;
}) {
  return (
    <span title={DATA_USE_STATE_HELP[state]} className="inline-flex">
      <ToneBadge tone={DATA_USE_STATE_TONE[state]} icon={STATE_ICON[state]} className={className}>
        {dataUseStateText(state, approvalsGiven, approvalsNeeded)}
      </ToneBadge>
    </span>
  );
}

// One handling rule as a chip: its name and value. Where it is set goes in the title, and a page that has room shows
// ruleSourceText beside it.
export function DataRuleChip({ name, rule, className }: { name: 'use' | 'ai' | 'copy'; rule: DataRuleValue; className?: string }) {
  return (
    <span title={`${DATA_RULE_NAME[name]} rule: ${DATA_RULE_TEXT[rule.value]}, ${ruleSourceText(rule)}`} className="inline-flex">
      <ToneBadge tone={DATA_RULE_TONE[rule.value]} icon={rule.value === 'never' ? <Lock aria-hidden /> : null} className={className}>
        {DATA_RULE_NAME[name]}: {DATA_RULE_TEXT[rule.value]}
      </ToneBadge>
    </span>
  );
}

export function DataOriginBadge({ origin, short = false, className }: { origin: DataOrigin; short?: boolean; className?: string }) {
  return (
    <span title={DATA_ORIGIN_TEXT[origin]} className="inline-flex">
      <ToneBadge tone={DATA_ORIGIN_TONE[origin]} icon={origin === 'production-copy' ? undefined : null} className={className}>
        {short ? DATA_ORIGIN_SHORT[origin] : DATA_ORIGIN_TEXT[origin]}
      </ToneBadge>
    </span>
  );
}

const SEVERITY_TEXT: Record<DataSeverity, string> = { high: 'High', medium: 'Medium', low: 'Low' };

export function DataSeverityBadge({ severity, className }: { severity: DataSeverity; className?: string }) {
  return <ToneBadge tone={DATA_SEVERITY_TONE[severity]} className={className}>{SEVERITY_TEXT[severity]}</ToneBadge>;
}

// A classification: its label, and where it is set when that is not the domain itself (the page passes the domain key).
export function ClassificationBadge({ classification, domainKey, className }: {
  classification: DataClassificationValue | LabelledKey | null | undefined;
  domainKey?: string;
  className?: string;
}) {
  if (!classification) return <ToneBadge tone="subtle" icon={null} className={className}>Not classified</ToneBadge>;
  const setOn = 'setOn' in classification ? classification.setOn : null;
  const inherited = setOn && domainKey && setOn.key !== domainKey ? `, from ${setOn.label}` : '';
  return <ToneBadge tone="brand" icon={null} className={className}>{classification.label}{inherited}</ToneBadge>;
}

// A domain by its path ("People / Payroll"), linked to its record page.
export function DomainLink({ domain, className }: { domain: DataDomainRef; className?: string }) {
  return <TextLink href={domainHref(domain.key)} className={className}>{domain.path || domain.label}</TextLink>;
}
