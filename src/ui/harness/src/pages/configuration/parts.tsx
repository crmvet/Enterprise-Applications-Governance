// Pieces the Configuration page, the draft record and the draft builder share: a diff in words, the approvals a change
// needs, the rule check's findings, what a change touches, and the data uses on the domains it touches.
import { CircleAlert, CircleCheck, Database, TriangleAlert } from 'lucide-react';
import type { ReactNode } from 'react';
import { hrefTo, recordHref, recordHrefOf, withParams } from '../../app/links';
import { navigate, useLocationKey } from '../../app/navigation';
import { Muted, TextLink, ToneBadge } from '../../components/blocks';
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert';
import type { ApprovalMode, ApprovalNeeded, ConfigurationDiffEntry, DataUseImpactItem, GateLevel, Id, LabelledKey } from '../../data/contracts';
import { plural } from '../../data/format';
import { useLabels } from '../../data/labels';
import { useRead } from '../../data/reads';
import { useSession } from '../../data/session';
import { dataOf } from '../../data/toolClient';
import { plainEngineText } from '../../data/writes';
import { cn } from '../../lib/utils';
import { choiceText, diffWords, GATE_LEVEL_TONE, inSentence, touchedDomains, touchesDataUses, type LabelLookup } from './logic';

const capitalize = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

// ---------- the page's own address parameters ----------

// Sets or removes the page's own query parameters, without moving the page. A filter typed or toggled replaces the
// address in place; a move to another part of the page (push) adds a history entry, so Back returns to the part before.
export function useSetParams(): (params: Record<string, string | undefined>, options?: { push?: boolean }) => void {
  const location = useLocationKey();
  return (params, options) => {
    const y = window.scrollY;
    navigate(withParams(location, params), !options?.push);
    window.scrollTo(0, y);
  };
}

// ---------- a diff ----------

export function DiffList({ diff, lookup, empty }: { diff: readonly ConfigurationDiffEntry[]; lookup: LabelLookup; empty?: string }) {
  if (!diff.length) return <Muted>{empty ?? 'No row changes.'}</Muted>;
  return (
    <ul className="m-0 flex list-none flex-col gap-2 p-0" aria-label="Row changes">
      {diff.map((entry, i) => {
        const w = diffWords(entry, lookup);
        return (
          <li key={`${entry.entity}-${entry.key}-${i}`} className="flex flex-col gap-1 rounded-md border border-line bg-card px-3 py-2">
            <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
              <ToneBadge tone={w.tone} icon={null}>{w.verb}</ToneBadge>
              <span className="text-xs text-navy-3">{w.entity}</span>
              <span className="text-sm font-semibold text-navy [overflow-wrap:anywhere]">{w.title}</span>
            </span>
            {w.lines.length > 0 && (
              <dl className="m-0 grid grid-cols-[minmax(0,180px)_minmax(0,1fr)] gap-x-3 gap-y-0.5 text-xs narrow:grid-cols-1">
                {w.lines.map(l => (
                  <div key={l.label} className="contents">
                    <dt className="font-semibold text-navy-2">{l.label}</dt>
                    <dd className="m-0 text-navy [overflow-wrap:anywhere]">
                      {l.before !== null
                        ? <><span className="text-navy-3 line-through decoration-navy-3/60">{l.before}</span><span className="text-navy-3"> becomes </span>{l.after}</>
                        : l.after}
                    </dd>
                  </div>
                ))}
              </dl>
            )}
          </li>
        );
      })}
    </ul>
  );
}

// ---------- approvals ----------

const MODE_TEXT: Record<ApprovalMode, string> = {
  'everyone-must-approve': 'everyone must approve',
  'first-to-respond': 'the first answer counts',
  sequential: 'in order',
};

export function ApprovalsNeededList({ items }: { items: readonly ApprovalNeeded[] }) {
  const roles = useLabels('role');
  const ownerRoles = useLabels('owner-role');
  if (!items.length) return <Muted>The rules in force name no approval for this change.</Muted>;
  return (
    <ul className="m-0 flex list-disc flex-col gap-0.5 pl-5 text-sm text-navy">
      {items.map((a, i) => {
        const who = a.approverRoleKey ? roles.label(a.approverRoleKey) : a.approverOwnerRoleKey ? `The ${inSentence(ownerRoles.label(a.approverOwnerRoleKey))}` : 'Each proposed owner, by name';
        const notes = [MODE_TEXT[a.approvalMode], a.minimumCount > 1 ? `${a.minimumCount} approvals` : null].filter(Boolean).join(', ');
        return <li key={i}><span className="font-medium">{who}</span><span className="text-navy-3"> ({notes})</span></li>;
      })}
    </ul>
  );
}

// ---------- the rule check ----------

export interface Issue {
  entity: string | null;
  key: string | null;
  message: string;
}

export function IssueList({ issues, labelOfAnyKey, tone }: { issues: readonly Issue[]; labelOfAnyKey: (key: string) => string | null; tone: 'error' | 'warning' }) {
  if (!issues.length) return null;
  return (
    <ul className={cn('m-0 flex list-disc flex-col gap-1 pl-5 text-sm', tone === 'error' ? 'text-danger' : 'text-warning')}>
      {issues.map((i, n) => <li key={n}><span className="text-navy">{plainEngineText(i.message, labelOfAnyKey)}</span></li>)}
    </ul>
  );
}

// The rule check's answer: passed, or the errors that keep the draft from being submitted, and the warnings.
export function RuleCheck({ valid, errors, warnings, labelOfAnyKey, saved }: {
  valid: boolean | null;
  errors: readonly Issue[];
  warnings: readonly Issue[];
  labelOfAnyKey: (key: string) => string | null;
  // True for a stored draft (the engine checked it when it was saved), false for a check that wrote nothing.
  saved: boolean;
}) {
  if (valid === false || errors.length) {
    return (
      <Alert variant="error" role="alert">
        <CircleAlert aria-hidden />
        <AlertTitle>{`The rules check found ${plural(errors.length || 1, 'problem', 'problems')}`}</AlertTitle>
        <AlertDescription>
          <div className="flex flex-col gap-2">
            <p className="m-0">{saved ? 'The draft cannot be accepted until they are fixed. Change the draft, or withdraw it.' : 'Fix them before you save the draft: a draft with problems cannot be accepted.'}</p>
            <IssueList issues={errors} labelOfAnyKey={labelOfAnyKey} tone="error" />
            <IssueList issues={warnings} labelOfAnyKey={labelOfAnyKey} tone="warning" />
          </div>
        </AlertDescription>
      </Alert>
    );
  }
  return (
    <Alert variant={valid === null ? 'info' : 'success'} role="status">
      {valid === null ? <TriangleAlert aria-hidden /> : <CircleCheck aria-hidden />}
      <AlertTitle>{valid === null ? 'The rules check has not run on this draft' : 'The rules check passed'}</AlertTitle>
      <AlertDescription>
        <div className="flex flex-col gap-2">
          <p className="m-0">{saved ? 'The engine checked every rule of the configuration when the draft was saved, and checks again when it is accepted.' : 'Every rule of the configuration ran. Nothing was saved yet.'}</p>
          {warnings.length > 0 && <span className="text-xs font-semibold text-navy-2">{`${plural(warnings.length, 'Warning', 'Warnings')} to read, which do not stop it:`}</span>}
          {warnings.length > 0 && <IssueList issues={warnings} labelOfAnyKey={labelOfAnyKey} tone="warning" />}
        </div>
      </AlertDescription>
    </Alert>
  );
}

// ---------- what a change touches ----------

export interface GateChange {
  tierKey: string;
  changeTypeKey: string;
  targetStageKey: string;
  from: string | null;
  to: string | null;
}

function GateLevelBadge({ level }: { level: string | null }) {
  if (!level) return <ToneBadge tone="subtle" icon={null}>No policy</ToneBadge>;
  return <ToneBadge tone={GATE_LEVEL_TONE[level as GateLevel] ?? 'subtle'} icon={null}>{choiceText(level)}</ToneBadge>;
}

export function GateChangeList({ changes: all, lookup }: { changes: readonly GateChange[]; lookup: LabelLookup }) {
  // A changed gate policy row whose level stays (a new label, say) moves no gate level.
  const changes = all.filter(g => g.from !== g.to);
  if (!changes.length) return <Muted>No gate level moves.</Muted>;
  const label = (entity: string, key: string) => (key === '*' ? 'any' : lookup(entity, key) ?? choiceText(key));
  return (
    <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
      {changes.map((g, i) => (
        <li key={i} className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-navy">
          <span>{capitalize(`${label('tier', g.tierKey)}, ${inSentence(label('change-type', g.changeTypeKey))} change, ${inSentence(label('target-stage', g.targetStageKey))}:`)}</span>
          <GateLevelBadge level={g.from} />
          <span className="text-xs text-navy-3">becomes</span>
          <GateLevelBadge level={g.to} />
        </li>
      ))}
    </ul>
  );
}

// The applications a change touches, by change, when the read names them by ID only: each one links to its page.
export function ByChangeList({ byChange, lookup, names }: {
  byChange: readonly { entity: string; key: string; applicationsAffected: number; sampleApplicationIds: Id[] }[];
  lookup: LabelLookup;
  names?: Map<string, string>;
}) {
  const touching = byChange.filter(b => b.applicationsAffected > 0);
  if (!touching.length) return <Muted>No live application's rules change.</Muted>;
  return (
    <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
      {touching.map(b => (
        <li key={`${b.entity}-${b.key}`} className="flex flex-col gap-0.5 text-sm text-navy">
          <span>
            <span className="font-medium">{lookup(b.entity, b.key) ?? choiceText(b.key.split('/').pop() ?? b.key)}</span>
            {`: ${plural(b.applicationsAffected, 'application', 'applications')}`}
          </span>
          {names && b.sampleApplicationIds.some(id => names.has(id.toLowerCase())) && (
            <span className="flex flex-wrap gap-x-3 gap-y-0.5 text-xs">
              {b.sampleApplicationIds.filter(id => names.has(id.toLowerCase())).map(id => {
                const href = recordHrefOf('business-application', id);
                return href ? <TextLink key={id} href={href}>{names.get(id.toLowerCase())}</TextLink> : <span key={id}>{names.get(id.toLowerCase())}</span>;
              })}
            </span>
          )}
        </li>
      ))}
    </ul>
  );
}

// ---------- the data uses on the domains a change touches ----------

// The data uses that sit today on the domains a change touches, each domain linked to its uses on the Data page. With
// the draft read's impact (get_configuration_draft impact.dataUses), each use or copy is listed with its state now and
// under the change, so the reader sees which ones would need a new decision or break a rule (data taxonomy, 8.5).
// Wave 4: the proposal's own check (propose_configuration_change with validateOnly) gives the same list
// (impact.dataUses, B24-22), so the sheet's check shows it before the draft is saved (builder.tsx CheckResult).
export function DataUsesTouched({ diff, uses: impacted }: { diff: readonly ConfigurationDiffEntry[]; uses?: readonly DataUseImpactItem[] }) {
  const session = useSession();
  const relevant = touchesDataUses(diff);
  const canRead = session.can('find_data_domains');
  const state = useRead('find_data_domains', relevant && canRead ? { top: 100 } : null);
  if (!relevant) return <Muted>The change touches no data domain, classification or level, so no data use changes state.</Muted>;
  if (!canRead) return <Muted>Your roles do not read the data domains, so the data uses this change touches are not shown.</Muted>;
  if (state.status === 'loading') return <Muted>Reading the data domains.</Muted>;
  if (!state.envelope.ok) return <Muted>{`The data domains could not be read: ${state.envelope.error.message}`}</Muted>;
  const domains = touchedDomains(diff, dataOf(state)?.items ?? []);
  const uses = domains.reduce((n, d) => n + d.counts.uses, 0);
  const open = domains.reduce((n, d) => n + d.counts.openDecisions, 0);
  const onlyNew = diff.filter(e => e.entity === 'data-domain').every(e => e.op === 'added') && !diff.some(e => e.entity === 'data-classification');
  return (
    <div className="flex flex-col gap-2">
      <p className="m-0 flex items-start gap-1.5 text-sm text-navy">
        <Database className="mt-0.5 size-4 shrink-0 text-navy-3" aria-hidden />
        <span>
          {domains.length === 0
            ? onlyNew ? 'A new domain holds no data uses yet, so no use changes state.' : 'No data use sits on the domains this change touches.'
            : `${plural(uses, 'data use sits', 'data uses sit')} on the ${plural(domains.length, 'domain', 'domains')} this change touches, with ${plural(open, 'open decision', 'open decisions')}.`}
        </span>
      </p>
      {domains.length > 0 && (
        <ul className="m-0 flex list-none flex-col gap-1 p-0">
          {domains.slice(0, 12).map(d => (
            <li key={d.key} className="flex flex-wrap items-baseline gap-x-2 text-sm">
              <TextLink href={hrefTo({ page: 'data', tab: 'uses', domainKey: d.key })}>{d.path || d.label}</TextLink>
              <span className="text-xs text-navy-3">{`${plural(d.counts.uses, 'use', 'uses')}, ${plural(d.counts.openDecisions, 'open decision', 'open decisions')}`}</span>
            </li>
          ))}
          {domains.length > 12 && <li><Muted>{`And ${plural(domains.length - 12, 'more domain', 'more domains')}.`}</Muted></li>}
        </ul>
      )}
      {impacted && impacted.length > 0 && (
        <ul className="m-0 flex list-none flex-col gap-1 p-0" aria-label="Data uses and copies this change touches">
          {impacted.map(u => (
            <li key={u.id} className="flex flex-wrap items-baseline gap-x-2 text-sm">
              <TextLink href={recordHref(u.recordUrl) ?? recordHrefOf(u.kind, u.id.toLowerCase()) ?? undefined}>
                {`${u.recordNumber ? `${u.recordNumber}: ` : ''}${u.application?.name ?? (u.kind === 'data-mapping' ? 'A copy' : 'A data use')}, ${u.domain.path}`}
              </TextLink>
              <span className={u.breaksRule || u.needsNewDecision ? 'text-xs font-semibold text-gold-strong' : 'text-xs text-navy-3'}>
                {u.stateBefore === u.stateAfter
                  ? `Stays ${stateWords(u.stateAfter)}`
                  : `${stateWords(u.stateBefore)} now, ${stateWords(u.stateAfter)} once accepted`}
              </span>
            </li>
          ))}
        </ul>
      )}
      {impacted && impacted.length === 0 && uses > 0 && (
        <Muted>No data use or copy on these domains changes state under the change.</Muted>
      )}
      {uses > 0 && !impacted && (
        <Muted>
          Once the change is accepted, the engine works out each use's state again under the new rules: a use can come to
          need a decision, need a new one, or break a rule.
        </Muted>
      )}
    </div>
  );
}

// A data use state (OP-16) in words.
function stateWords(state: string): string {
  switch (state) {
    case 'not-needed': return 'no decision needed';
    case 'needed': return 'needs a decision';
    case 're-decide': return 'needs a new decision';
    case 'no-one-to-ask': return 'has no one to ask';
    case 'breaks-rule': return 'breaks a rule';
    default: return state.replace(/-/g, ' ');
  }
}

// ---------- small pieces ----------

export function Section({ title, children, className }: { title: string; children: ReactNode; className?: string }) {
  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{title}</span>
      {children}
    </div>
  );
}

export function GatesTouched({ gates }: { gates: readonly LabelledKey[] }) {
  if (!gates.length) return <Muted>No gate's requirements change.</Muted>;
  return <span className="flex flex-wrap gap-1">{gates.map(g => <ToneBadge key={g.key} tone="brand" icon={null}>{g.label}</ToneBadge>)}</span>;
}
