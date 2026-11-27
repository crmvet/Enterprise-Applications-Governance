// Audit trail and evidence pack (screen "Audit trail and evidence pack", priority 2): answer from the record who did
// what, as which actor, through which client, for whom (playbook 15), and the evidence pack of an application or a
// decision.
//   /app/audit?record=<id>&person=<objectId>&app=<id>&actor=<kind>&client=<name>
//                                                                    RouteOf<'audit'>, and the page's own op=<operation>
//                                                                    (repeatable), from=<date> and to=<date>, so Back
//                                                                    returns to the same entries. The menu shows it only
//                                                                    to a person whose roles give get_audit_trail (the
//                                                                    auditor and the platform admin, document 26).
//   /app/evidence/<business-application|decision>/<id>               the evidence pack, for any role, with the page's own
//                                                                    from=<date>, to=<date> and ai=1 (AI-created rows
//                                                                    only). /app/records/<kind>/<id>/evidence opens it.
// Reads: get_audit_trail (the entries, newest first, page by page; four more reads of one row each count refused calls,
//   AI entries, approval answers and acceptances under the same scope), get_evidence (decisions with every approval
//   answer, changes, deployments with flags, reviews, documents, evidence links; each row's configuration version next
//   to the version in force), get_application_card (an application's name for the scope chip), find_applications and
//   find_records (the evidence pack finder).
// Writes: save_link (add an evidence link to a decision, change, deployment or review in the pack), in the shared side
//   sheet. "Print the pack" is the browser's print; the page's print rules leave out the menu and the buttons.
// Design choices (D-41) for AI activity, Agents and Audit:
//   - A person who may not read the audit trail who opens /app/audit sees why, and the evidence pack finder, which every
//     role may use.
//   - The figures count under the record, person, application, client and dates only, so each one opens its own rows.
//   - Wave 4: client=<name> keeps the entries made through one client app (get_audit_trail clientApp, B24-21). Each
//     entry's client is a link to that filter, and AI activity's clients view and the Agents page link in with it.
//   - Wave 4: q= searches the entries read so far (what was done, the record, the people, the client, the call
//     reference); get_audit_trail takes no words, so the list says when more entries exist than it read. The figures
//     stay the tool's counts under the filters.
import { Bot, CircleSlash, FileStack, Gavel, GitPullRequest, History, Link2, ListChecks, Printer, ShieldCheck, UserRound } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import { evidenceHref, guidelineHref, hrefTo, recordHref, recordHrefOf, useQueryParam, withParams } from '../app/links';
import { navigate, useLocationKey } from '../app/navigation';
import { AUDIT_ACTOR_KINDS, type AuditActorKind, type EvidenceKind, type RouteOf } from '../app/routes';
import { DataStamp, Empty, KpiTile, LinkButton, Muted, PageHeader, Panel, PanelFailure, StatusBadge, TextLink, ToneBadge } from '../components/blocks';
import { PersonSearchField } from '../components/people';
import { ListSearch, useListText } from '../components/listSearch';
import { filterByText } from '../data/textSearch';
import { ExternalLink, Row, RowList } from '../components/rows';
import { AiMark, StampLine } from '../components/stamps';
import { Alert, AlertDescription, AlertTitle } from '../components/ui/alert';
import { Button } from '../components/ui/button';
import { Input } from '../components/ui/input';
import { Label } from '../components/ui/label';
import { ToggleGroup, ToggleGroupItem } from '../components/ui/toggle-group';
import type { AuditItem, AuditTrail, EvidenceDecision, PrincipalRef } from '../data/contracts';
import { fmtDate, fmtDateTime, fmtWindow, forSomeoneElse, humanizeKey } from '../data/format';
import { usePagedRead, useRead } from '../data/reads';
import { useSession } from '../data/session';
import { dataOf, type ToolState } from '../data/toolClient';
import { cn } from '../lib/utils';
import {
  approvalCountText,
  AUDIT_OPERATIONS,
  auditInput,
  datesReversed,
  entryHeadline,
  isIsoDate,
  OPERATION_TEXT,
  parseOperations,
  scopeOnly,
  statusAfterText,
  versionNote,
  versionsText,
  type AuditFilters,
  type AuditOperation,
} from './ai-audit/auditLogic';
import { refusalCodeWords, sameId } from './ai-audit/aiActivityLogic';
import { EvidenceFinder } from './ai-audit/EvidenceFinder';
import { ActorBadge, filterBarClass, LeadIcon, MoreButton, useActorLabel } from './ai-audit/parts';
import { FlagBadges, SaveLinkSheet } from './changes-records/parts';
import { Chip, kpiRowClass, kpiRowClass4, twoColumnClass } from './owners-reviews/people';

const PAGE_SIZE = 50;
const DATE = /^\d{4}-\d{2}-\d{2}$/;
const AI_KINDS: AuditActorKind[] = ['ai-client-for-person', 'agent-identity'];

function useDateParam(name: string): string | undefined {
  const value = useQueryParam(name, DATE);
  return isIsoDate(value) ? value : undefined;
}

// =====================================================================================================================
// The audit trail: /app/audit
// =====================================================================================================================

export function Audit({ route }: { route: RouteOf<'audit'> }) {
  const session = useSession();
  const location = useLocationKey();
  const search = location.includes('?') ? location.slice(location.indexOf('?')) : '';
  const operations = parseOperations(search);
  const from = useDateParam('from');
  const to = useDateParam('to');
  const filters: AuditFilters = {
    recordId: route.recordId,
    principalObjectId: route.principalObjectId,
    applicationId: route.applicationId,
    actorKinds: route.actorKinds,
    operations,
    clientApp: route.clientApp,
    from,
    to,
  };
  const mayRead = !session.known || session.failed || session.can('get_audit_trail');
  // The entries' search (q=) stays while the other filters change.
  const words = useListText();

  const go = (next: Partial<AuditFilters>) => {
    const f = { ...filters, ...next };
    const href = hrefTo({
      page: 'audit',
      ...(f.recordId ? { recordId: f.recordId } : {}),
      ...(f.principalObjectId ? { principalObjectId: f.principalObjectId } : {}),
      ...(f.applicationId ? { applicationId: f.applicationId } : {}),
      ...(f.actorKinds?.length ? { actorKinds: f.actorKinds } : {}),
      ...(f.clientApp ? { clientApp: f.clientApp } : {}),
    });
    const params = new URLSearchParams(href.includes('?') ? href.slice(href.indexOf('?') + 1) : '');
    for (const op of f.operations ?? []) params.append('op', op);
    if (f.from) params.set('from', f.from);
    if (f.to) params.set('to', f.to);
    if (words) params.set('q', words);
    const path = href.includes('?') ? href.slice(0, href.indexOf('?')) : href;
    const query = params.toString();
    navigate(query ? `${path}?${query}` : path, true);
  };

  if (!mayRead) return <NoAuditAccess />;

  return (
    <>
      <PageHeader
        eyebrow="Record"
        title="Audit trail"
        subtitle="Who did what, as which kind of actor, through which client, and for whom. Every composed call, refused call, approval answer, acceptance and effect leaves one entry. Entries are never changed."
        actions={<Button asChild variant="outline" size="sm"><a href={hrefTo({ page: 'ai-activity' })}>AI activity</a></Button>}
      />
      <AuditFigures filters={filters} go={go} />
      <AuditFilterBar filters={filters} go={go} />
      <div className={twoColumnClass}>
        <AuditList key={JSON.stringify(auditInput(filters, PAGE_SIZE))} filters={filters} go={go} />
        <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
          <Panel title="Evidence pack" subtitle="Every decision and approval answer, the changes, deployments, reviews and documents of one application or decision, ready to print.">
            <EvidenceFinder />
          </Panel>
          <AboutPanel />
        </div>
      </div>
    </>
  );
}

function NoAuditAccess() {
  return (
    <>
      <PageHeader eyebrow="Record" title="Audit trail" subtitle="Who did what, as which kind of actor, through which client, and for whom." />
      <div className={twoColumnClass}>
        <Panel title="Only the auditor and the platform admin read the audit trail">
          <p className="m-0 text-sm leading-relaxed text-navy-2">
            The audit trail holds every call people and AI clients made, including the calls the product refused. Your roles do not
            include reading it. The evidence pack of an application or a decision is open to every role: it shows the decisions with
            each approval answer, the changes, deployments, reviews and documents, and who drafted each one.
          </p>
        </Panel>
        <Panel title="Open an evidence pack">
          <EvidenceFinder autoFocus />
        </Panel>
      </div>
    </>
  );
}

// ---------- the figures ----------

function AuditFigures({ filters, go }: { filters: AuditFilters; go: (f: Partial<AuditFilters>) => void }) {
  const scope = scopeOnly(filters);
  const all = useRead('get_audit_trail', auditInput(scope, 1));
  const refused = useRead('get_audit_trail', auditInput({ ...scope, operations: ['call-refused'] }, 1));
  const ai = useRead('get_audit_trail', auditInput({ ...scope, actorKinds: AI_KINDS }, 1));
  const answers = useRead('get_audit_trail', auditInput({ ...scope, operations: ['approval-outcome-written'] }, 1));
  const accepted = useRead('get_audit_trail', auditInput({ ...scope, operations: ['decision-accepted'] }, 1));
  const total = (s: ToolState<AuditTrail>) => dataOf(s)?.totalCount ?? null;
  if (all.status === 'done' && !all.envelope.ok) return <div className="mb-4"><PanelFailure failure={all.envelope} /></div>;
  return (
    <div className={kpiRowClass} role="group" aria-label="Key figures">
      <KpiTile className="narrow:col-span-full" label="Entries" value={total(all)} caption="In this scope, every kind" onClick={() => go({ actorKinds: undefined, operations: undefined })} />
      <KpiTile label="Refused calls" value={total(refused)} caption="Nothing was written" attention={!!total(refused)} onClick={() => go({ actorKinds: undefined, operations: ['call-refused'] })} />
      <KpiTile label="By AI clients" value={total(ai)} caption="For a person, or an agent identity" onClick={() => go({ actorKinds: AI_KINDS, operations: undefined })} />
      <KpiTile label="Approval answers" value={total(answers)} caption="Each answer written" onClick={() => go({ actorKinds: undefined, operations: ['approval-outcome-written'] })} />
      <KpiTile label="Decisions accepted" value={total(accepted)} caption="And their effects" onClick={() => go({ actorKinds: undefined, operations: ['decision-accepted'] })} />
    </div>
  );
}

// ---------- the filters ----------

function AuditFilterBar({ filters, go }: { filters: AuditFilters; go: (f: Partial<AuditFilters>) => void }) {
  const actorLabel = useActorLabel();
  const fromId = useId();
  const toId = useId();
  const actorId = useId();
  const opId = useId();
  const [picking, setPicking] = useState(false);
  const app = useRead('get_application_card', filters.applicationId ? { businessApplicationId: filters.applicationId, sections: ['links'] } : null);
  const appName = dataOf(app)?.application.name ?? 'This application';
  const people = usePeopleNames(filters);
  const any = !!(filters.recordId || filters.principalObjectId || filters.applicationId || filters.clientApp || filters.actorKinds?.length || filters.operations?.length || filters.from || filters.to);
  return (
    <div className="mb-4 flex flex-col gap-3">
      <div className={filterBarClass} role="group" aria-label="Filters">
        <div className="flex min-w-0 flex-col gap-1 narrow:w-full">
          <span id={actorId} className="text-xs font-medium text-navy-2">Actor</span>
          <ToggleGroup
            type="multiple"
            variant="outline"
            size="sm"
            spacing={1}
            className="flex-wrap"
            aria-labelledby={actorId}
            value={filters.actorKinds ?? []}
            onValueChange={v => go({ actorKinds: v.length ? (v as AuditActorKind[]) : undefined })}
          >
            {AUDIT_ACTOR_KINDS.map(k => <ToggleGroupItem key={k} value={k} className="text-navy data-[state=on]:bg-secondary data-[state=on]:text-brand-dark">{actorLabel(k)}</ToggleGroupItem>)}
          </ToggleGroup>
        </div>
        <div className="flex min-w-0 flex-col gap-1 narrow:w-full">
          <span id={opId} className="text-xs font-medium text-navy-2">What</span>
          <ToggleGroup
            type="multiple"
            variant="outline"
            size="sm"
            spacing={1}
            className="flex-wrap"
            aria-labelledby={opId}
            value={filters.operations ?? []}
            onValueChange={v => go({ operations: v.length ? (v as AuditOperation[]) : undefined })}
          >
            {AUDIT_OPERATIONS.map(o => <ToggleGroupItem key={o} value={o} className="text-navy data-[state=on]:bg-secondary data-[state=on]:text-brand-dark">{OPERATION_TEXT[o]}</ToggleGroupItem>)}
          </ToggleGroup>
        </div>
        <div className="flex min-w-0 flex-col gap-1">
          <Label htmlFor={fromId} className="text-xs text-navy-2">From</Label>
          <Input id={fromId} type="date" className="w-[160px]" value={filters.from ?? ''} onChange={e => go({ from: isIsoDate(e.target.value) ? e.target.value : undefined })} />
        </div>
        <div className="flex min-w-0 flex-col gap-1">
          <Label htmlFor={toId} className="text-xs text-navy-2">To</Label>
          <Input id={toId} type="date" className="w-[160px]" value={filters.to ?? ''} onChange={e => go({ to: isIsoDate(e.target.value) ? e.target.value : undefined })} />
        </div>
        <div className="flex min-w-0 flex-col gap-1 narrow:w-full">
          {picking ? (
            <div className="w-[300px] narrow:w-full">
              <PersonSearchField
                label="Person"
                name="principalObjectId"
                value={null}
                onChange={p => { setPicking(false); if (p) go({ principalObjectId: p.objectId.toLowerCase() }); }}
                hint="What they did, and what AI clients did for them."
              />
            </div>
          ) : (
            <Button variant="outline" size="sm" className="mb-0.5" onClick={() => setPicking(true)}>
              <UserRound aria-hidden />
              Choose a person
            </Button>
          )}
        </div>
      </div>
      {datesReversed(filters.from, filters.to) && <Muted>The From date is after the To date, so no entry can match.</Muted>}
      {any && (
        <div className="flex flex-wrap items-center gap-2" aria-label="Filters in use">
          {filters.recordId && <Chip label={`Record: ${people.recordLabel ?? 'the record chosen'}`} onRemove={() => go({ recordId: undefined })} />}
          {filters.principalObjectId && <Chip label={`Person: ${people.personLabel ?? 'the person chosen'}`} onRemove={() => go({ principalObjectId: undefined })} />}
          {filters.applicationId && <Chip label={`Application: ${appName}`} onRemove={() => go({ applicationId: undefined })} />}
          {filters.clientApp && <Chip label={`Through ${filters.clientApp}`} onRemove={() => go({ clientApp: undefined })} />}
          {filters.actorKinds?.map(k => <Chip key={k} label={actorLabel(k)} onRemove={() => go({ actorKinds: filters.actorKinds!.filter(x => x !== k) })} />)}
          {filters.operations?.map(o => <Chip key={o} label={OPERATION_TEXT[o]} onRemove={() => go({ operations: filters.operations!.filter(x => x !== o) })} />)}
          {filters.from && <Chip label={`From ${fmtDate(filters.from)}`} onRemove={() => go({ from: undefined })} />}
          {filters.to && <Chip label={`To ${fmtDate(filters.to)}`} onRemove={() => go({ to: undefined })} />}
          <LinkButton className="inline-flex min-h-6 items-center text-xs" onClick={() => go({ recordId: undefined, principalObjectId: undefined, applicationId: undefined, clientApp: undefined, actorKinds: undefined, operations: undefined, from: undefined, to: undefined })}>
            Clear the filters
          </LinkButton>
          {filters.applicationId && (
            <TextLink className="text-xs" href={evidenceHref('business-application', filters.applicationId)}>Evidence pack of {appName}</TextLink>
          )}
        </div>
      )}
    </div>
  );
}

// The name of the record and of the person the filters name, from the first entries that carry them.
function usePeopleNames(filters: AuditFilters): { recordLabel: string | null; personLabel: string | null } {
  const state = useRead('get_audit_trail', filters.recordId || filters.principalObjectId
    ? auditInput({ recordId: filters.recordId, principalObjectId: filters.principalObjectId }, 20)
    : null);
  const items = dataOf(state)?.items ?? [];
  const record = filters.recordId ? items.find(i => sameId(i.record?.recordId, filters.recordId))?.record?.label ?? null : null;
  let person: string | null = null;
  if (filters.principalObjectId) {
    for (const i of items) {
      const p = [i.actor, i.onBehalfOf].find(x => sameId(x?.objectId, filters.principalObjectId));
      if (p) { person = p.displayLabel || null; break; }
    }
  }
  return { recordLabel: record, personLabel: person };
}

// ---------- the entries ----------

function AuditList({ filters, go }: { filters: AuditFilters; go: (f: Partial<AuditFilters>) => void }) {
  const list = usePagedRead('get_audit_trail', { ...auditInput(filters, PAGE_SIZE) }, d => d.items);
  const actorLabel = useActorLabel();
  const text = useListText();
  let body: ReactNode;
  let count: number | undefined;
  if (list.status === 'loading') body = <Muted>Reading the audit trail.</Muted>;
  else if (list.status === 'failed') body = <PanelFailure failure={list.failure} />;
  else {
    const shown = filterByText(list.items, text, item => [
      entryHeadline(item),
      item.record?.label,
      item.actor?.displayLabel,
      item.onBehalfOf?.displayLabel,
      item.clientApp,
      item.correlationId,
    ]);
    count = text ? shown.length : list.first.data.totalCount;
    body = shown.length ? (
      <>
        <RowList label="Audit entries">
          {shown.map(item => <AuditRow key={item.auditEntryId} item={item} filters={filters} go={go} actorLabel={actorLabel} />)}
        </RowList>
        {text && list.skipToken && <Muted>The search covers the entries read so far. Show more to search further.</Muted>}
        <MoreButton skipToken={list.skipToken} loading={list.loadingMore} onMore={list.more} what="entries" laterFailure={list.laterFailure} />
      </>
    ) : text && list.items.length ? (
      <span className="flex flex-col items-start gap-2">
        <Empty>{`No entry read so far matches "${text}".`}</Empty>
        <MoreButton skipToken={list.skipToken} loading={list.loadingMore} onMore={list.more} what="entries" laterFailure={list.laterFailure} />
      </span>
    ) : (
      <Empty>
        {filters.recordId || filters.principalObjectId || filters.applicationId || filters.clientApp || filters.actorKinds || filters.operations || filters.from || filters.to
          ? 'No entry matches these filters.'
          : 'The audit trail holds no entry yet. Entries appear as people and AI clients act through the product.'}
      </Empty>
    );
  }
  return (
    <Panel title="Entries" subtitle="Newest first. Entries hold IDs; the names shown are read from the records today." count={count} id="audit-entries">
      <ListSearch label="Search the entries read by what was done, record, person, client or call reference" placeholder="What, record, person or client" className="mb-1 w-full" />
      {body}
    </Panel>
  );
}

const who = (p: PrincipalRef | null | undefined) => (p ? p.displayLabel || p.objectId : null);

function AuditRow({ item, filters, go, actorLabel }: { item: AuditItem; filters: AuditFilters; go: (f: Partial<AuditFilters>) => void; actorLabel: (k: string) => string }) {
  const refused = item.operation === 'call-refused';
  const ai = item.actorKind === 'ai-client-for-person' || item.actorKind === 'agent-identity';
  const recordUrl = recordHref(item.record?.recordUrl);
  const forWhom = forSomeoneElse(item.actor, item.onBehalfOf);
  const byLine = [
    `By ${who(item.actor) ?? actorLabel(item.actorKind).toLowerCase()}`,
    forWhom ? `for ${who(forWhom)}` : null,
    item.clientApp ? `through ${item.clientApp}` : null,
  ].filter(Boolean).join(' ');
  const person = item.onBehalfOf ?? item.actor;
  const evidenceKind: EvidenceKind | null = item.record?.entity === 'business-application' || item.record?.entity === 'decision' ? item.record.entity : null;
  return (
    <Row
      lead={(
        <LeadIcon tone={refused ? 'danger' : ai ? 'brand' : item.actorKind === 'person' ? 'success' : 'subtle'}>
          {refused ? <CircleSlash /> : ai ? <Bot /> : item.actorKind === 'person' ? <UserRound /> : <ShieldCheck />}
        </LeadIcon>
      )}
      primary={entryHeadline(item)}
      secondary={(
        <>
          {item.record && <RecordOn record={item.record} href={recordUrl} />}
          {byLine}. {item.occurredOn ? fmtDateTime(item.occurredOn) : 'Time not recorded'}.
        </>
      )}
      tertiary={refused && item.refusal ? <span className="text-navy-2">The rule: {item.refusal.rule}</span> : statusAfterText(item.statusAfter) ?? undefined}
      badges={(
        <>
          <ActorBadge kind={item.actorKind} label={actorLabel(item.actorKind)} />
          {refused && <ToneBadge tone="danger" icon={<CircleSlash aria-hidden />}>Refused, nothing written</ToneBadge>}
          {item.refusal && <ToneBadge tone="subtle">{refusalCodeWords(item.refusal.code, item.refusal.knownTool)}</ToneBadge>}
          {item.refusal?.guideline && <TextLink className="text-xs" href={guidelineHref(item.refusal.guideline)}>{item.refusal.guideline}{item.refusal.guidelineTitle ? ` ${item.refusal.guidelineTitle}` : ''}</TextLink>}
          {item.refusal && (item.refusal.ruleIds.length > 0 || item.refusal.reasonCode) && (
            <span className="w-full text-[11px] text-navy-3">
              For the audit: code <span className="font-mono">{item.refusal.code}</span>{item.refusal.reasonCode ? <>, reason <span className="font-mono">{item.refusal.reasonCode}</span></> : null}{item.refusal.ruleIds.length ? <>, engine rules {item.refusal.ruleIds.join(', ')}</> : null}.
            </span>
          )}
          <span className="w-full text-[11px] text-navy-3 [overflow-wrap:anywhere]">
            Call <span className="font-mono">{item.correlationId}</span>{item.hasRequestKey ? ', sent with a retry key, so a retry returns the first answer' : ''}
          </span>
        </>
      )}
      endSub={(
        <span className="mt-1 flex flex-col items-end gap-0 text-xs narrow:items-start print:hidden">
          {item.record && !sameId(filters.recordId, item.record.recordId) && (
            <LinkButton className="inline-flex min-h-6 items-center text-xs" onClick={() => go({ recordId: item.record!.recordId.toLowerCase(), principalObjectId: undefined })}>
              <History className="mr-1 inline size-3" aria-hidden />History of this record
            </LinkButton>
          )}
          {person && !sameId(filters.principalObjectId, person.objectId) && (
            <LinkButton className="inline-flex min-h-6 items-center text-xs" onClick={() => go({ principalObjectId: person.objectId.toLowerCase(), recordId: undefined })}>
              Everything for {who(person)}
            </LinkButton>
          )}
          {item.clientApp && (filters.clientApp ?? '').toLowerCase() !== item.clientApp.toLowerCase() && (
            <LinkButton className="inline-flex min-h-6 items-center text-xs" onClick={() => go({ clientApp: item.clientApp! })}>
              Everything through {item.clientApp}
            </LinkButton>
          )}
          {evidenceKind && item.record && <TextLink className="text-xs" href={evidenceHref(evidenceKind, item.record.recordId.toLowerCase())}>Evidence pack</TextLink>}
        </span>
      )}
      wideEnd
    />
  );
}

// What the evidence pack says about a change's authorization decision, by that decision's status: an auditor never reads
// a proposed decision as an authorization (e2e step 15).
function authorizationWords(meaning: string | null): string {
  if (meaning === 'accepted') return 'Authorized on';
  if (meaning === 'proposed') return 'Waiting for authorization on';
  if (meaning === 'rejected') return 'Authorization refused on';
  if (meaning === 'withdrawn' || meaning === 'superseded') return 'Authorization withdrawn on';
  return 'Authorization recorded on';
}

// "On the decision DEC-0182: Change the owners of Claims Triage Agent." A record whose name cannot be read shows its kind.
function RecordOn({ record, href }: { record: NonNullable<AuditItem['record']>; href: string | null }) {
  const kind = record.entity ? humanizeKey(record.entity).toLowerCase() : 'record';
  const label = record.label?.replace(/\.\s*$/, '') ?? null;
  if (!label) return <>On {/^[aeiou]/.test(kind) ? 'an' : 'a'} {kind}{href ? <> (<TextLink href={href}>open it</TextLink>)</> : null}. </>;
  return <>On the {kind} {href ? <TextLink href={href}>{label}</TextLink> : label}. </>;
}

function AboutPanel() {
  return (
    <Panel title="Reading the trail">
      <ul className="m-0 flex list-disc flex-col gap-1.5 pl-5 text-sm leading-5 text-navy-2">
        <li>The actor kind comes from the way in and the sign-in token, never from what a caller says.</li>
        <li>An AI client always acts for a person: the entry names both, and the client.</li>
        <li>A refused call writes nothing. Its entry names the rule that refused it.</li>
        <li>Entries of one call share its call reference.</li>
      </ul>
    </Panel>
  );
}

// =====================================================================================================================
// The evidence pack: /app/evidence/<kind>/<id>
// =====================================================================================================================

const PRINT_RULES = `@media print {
  header.sticky, body footer, a[href="#main"], [data-print="hide"] { display: none !important; }
  body { background: #ffffff !important; }
  main { padding-top: 0 !important; }
  section { box-shadow: none !important; break-inside: avoid-page; }
}`;

const SETTLED_TEXT: Record<NonNullable<EvidenceDecision['settledBy']>, string> = {
  answers: 'Settled by the approval answers',
  engine: 'Accepted by the engine under the rules, with no approval needed',
  person: 'Decided by a person',
  recorded: 'Recorded as decided',
};

export function EvidencePack({ subjectKind, subjectId }: { subjectKind: EvidenceKind; subjectId: string }) {
  const session = useSession();
  const from = useDateParam('from');
  const to = useDateParam('to');
  const aiOnly = useQueryParam('ai', /^1$/) === '1';
  const state = useRead('get_evidence', {
    ...(subjectKind === 'decision' ? { decisionId: subjectId } : { businessApplicationId: subjectId }),
    ...(from ? { from } : {}),
    ...(to ? { to } : {}),
  });
  const back = subjectKind === 'decision' ? hrefTo({ page: 'decision', id: subjectId }) : hrefTo({ page: 'app', id: subjectId });
  const kindText = subjectKind === 'decision' ? 'decision' : 'application';
  const setParams = (p: Record<string, string | undefined>) => {
    navigate(withParams(evidenceHref(subjectKind, subjectId), { from, to, ai: aiOnly ? '1' : undefined, ...p }), true);
  };

  if (state.status === 'loading' || !state.envelope.ok) {
    const failure = state.status === 'done' && !state.envelope.ok ? state.envelope : null;
    return (
      <>
        <PageHeader eyebrow="Evidence pack" title="Evidence pack" meta={<span className="font-mono [overflow-wrap:anywhere]">{subjectId}</span>} />
        {failure
          ? <PanelFailure failure={failure} />
          : <Panel title={`Reading the ${kindText}'s evidence`} state={state}>{() => null}</Panel>}
        <p className="mt-4 text-sm text-navy-2"><TextLink href={back}>Back to the {kindText}</TextLink></p>
      </>
    );
  }

  const envelope = state.envelope;
  const pack = envelope.data;
  const inForce = pack.configurationInForce.configurationSetVersion;
  const canLink = session.can('save_link');
  const keep = <T extends { isAiCreated: boolean }>(rows: T[]) => (aiOnly ? rows.filter(r => r.isAiCreated) : rows);
  const decisions = keep(pack.decisions);
  const changes = keep(pack.changes);
  const deployments = keep(pack.deployments);
  const reviews = aiOnly ? [] : pack.reviews;
  const documents = keep(pack.documents);
  const links = keep(pack.evidenceLinks);
  const scrollTo = (id: string) => {
    if (aiOnly) setParams({ ai: undefined });
    window.setTimeout(() => {
      const el = document.getElementById(id);
      el?.scrollIntoView({ behavior: 'smooth', block: 'start' });
      const heading = document.getElementById(`${id}-title`);
      if (heading) { heading.setAttribute('tabindex', '-1'); heading.focus({ preventScroll: true }); }
    }, 0);
  };
  const subjectHref = recordHref(pack.subject.recordUrl) ?? back;

  return (
    <>
      <style>{PRINT_RULES}</style>
      <PageHeader
        eyebrow={`Evidence pack, ${kindText}`}
        title={pack.subject.label || `This ${kindText}`}
        subtitle={`Every decision with each approval answer, the changes, the deployments with their flags, the reviews, the documents and the evidence links of this ${kindText}, each with the configuration version it was checked under.`}
        meta={(
          <span className="flex flex-col gap-0.5">
            <span><DataStamp envelope={envelope} /></span>
            {(from || to) && <span>Rows dated {from ? `from ${fmtDate(from)}` : ''}{from && to ? ' ' : ''}{to ? `to ${fmtDate(to)}` : ''} only.</span>}
            {aiOnly && <span>Only the rows an AI client drafted.</span>}
          </span>
        )}
        actions={(
          <span className="flex flex-wrap gap-2" data-print="hide">
            <Button size="sm" onClick={() => window.print()}><Printer aria-hidden />Print the pack</Button>
            <Button asChild size="sm" variant="outline"><a href={subjectHref}>Open the {kindText}</a></Button>
            {session.can('get_audit_trail') && (
              <Button asChild size="sm" variant="outline">
                <a href={subjectKind === 'decision' ? hrefTo({ page: 'audit', recordId: subjectId }) : hrefTo({ page: 'audit', applicationId: subjectId })}>Audit trail</a>
              </Button>
            )}
            {canLink && subjectKind === 'decision' && <SaveLinkSheet subjectKind="decision" subjectId={subjectId} category="evidence" />}
          </span>
        )}
      />

      <PackRange from={from} to={to} onChange={p => setParams(p)} />

      {pack.truncated && (
        <Alert variant="warning" className="mb-4">
          <ShieldCheck aria-hidden />
          <AlertTitle>The pack is long</AlertTitle>
          <AlertDescription><p className="m-0">A list stopped at 500 rows. Narrow the dates to see the rest.</p></AlertDescription>
        </Alert>
      )}

      <div className={kpiRowClass4} role="group" aria-label="What the pack holds">
        <KpiTile label="Decisions" value={pack.counts.decisions} caption={`${pack.counts.approvalAnswers} approval answers`} onClick={() => scrollTo('pack-decisions')} />
        <KpiTile label="Changes" value={pack.counts.changes} caption="With their authorization" onClick={() => scrollTo('pack-changes')} />
        <KpiTile label="Deployments" value={pack.counts.deployments} caption={`${pack.counts.flaggedDeployments} flagged`} attention={pack.counts.flaggedDeployments > 0} onClick={() => scrollTo('pack-deployments')} />
        <KpiTile label="Reviews" value={pack.counts.reviews} caption="Due and done" onClick={() => scrollTo('pack-reviews')} />
        <KpiTile label="Documents" value={pack.counts.documents} caption="Document links" onClick={() => scrollTo('pack-documents')} />
        <KpiTile label="Evidence links" value={pack.counts.evidenceLinks} caption="Proof held elsewhere" onClick={() => scrollTo('pack-evidence-links')} />
        <KpiTile label="Drafted by AI" value={pack.counts.aiCreated} caption={aiOnly ? 'Showing these only' : 'Taken forward by a person'} onClick={() => setParams({ ai: aiOnly ? undefined : '1' })} />
        <KpiTile label="Configuration" value={inForce} caption="Version in force now" onClick={() => scrollTo('pack-configuration')} />
      </div>

      {aiOnly && (
        <div className="mb-4 flex flex-wrap items-center gap-2" aria-label="Filters in use" data-print="hide">
          <Chip label="Drafted by AI only" onRemove={() => setParams({ ai: undefined })} />
        </div>
      )}

      <div className="flex flex-col gap-4 narrow:gap-3">
        <Panel title="Configuration versions" id="pack-configuration" className="print:break-inside-avoid">
          <p className="m-0 text-sm text-navy-2">{versionsText(pack.versionsStamped, inForce)}</p>
          <p className="m-0 text-xs text-navy-3 [overflow-wrap:anywhere]">
            Version {inForce} in force now, content hash <span className="font-mono">{pack.configurationInForce.contentHash}</span>.
          </p>
        </Panel>

        <Panel title="Decisions and approval answers" count={decisions.length} id="pack-decisions">
          {decisions.length ? (
            <ol className="m-0 flex list-none flex-col gap-4 p-0">
              {decisions.map(d => <DecisionBlock key={d.decisionId} d={d} inForce={inForce} canLink={canLink && subjectKind !== 'decision'} />)}
            </ol>
          ) : <Empty>{aiOnly ? 'No decision here was drafted by an AI client.' : 'No decision in this pack.'}</Empty>}
        </Panel>

        <Panel title="Changes" count={changes.length} id="pack-changes">
          {changes.length ? (
            <RowList label="Changes">
              {changes.map(c => {
                const href = recordHref(c.recordUrl);
                const auth = c.authorizationDecisionId ? recordHrefOf('decision', c.authorizationDecisionId.toLowerCase()) : null;
                return (
                  <Row
                    key={c.changeRequestId}
                    lead={<LeadIcon><ListChecks /></LeadIcon>}
                    primary={<>{href ? <TextLink href={href}>{c.recordNumber ? `${c.recordNumber}: ${c.title}` : c.title}</TextLink> : c.title}</>}
                    secondary={<>{c.changeType.label}. {fmtWindow(c.windowStart, c.windowEnd)}.{c.result ? ` Result: ${c.result.label}.` : ''}</>}
                    tertiary={auth ? <>{authorizationWords(decisions.find(d => d.decisionId.toLowerCase() === c.authorizationDecisionId!.toLowerCase())?.status?.meaning ?? null)} <TextLink href={auth}>its decision</TextLink>. <VersionText stamped={c.configurationSetVersion} inForce={inForce} /></> : <VersionText stamped={c.configurationSetVersion} inForce={inForce} />}
                    badges={<><AiMark isAiCreated={c.isAiCreated} performedBy={c.performedBy} /><StampLine performedBy={c.performedBy} className="w-full" /></>}
                    end={c.status ? <StatusBadge status={c.status} /> : undefined}
                    endSub={canLink ? <span className="mt-1" data-print="hide"><SaveLinkSheet subjectKind="change-request" subjectId={c.changeRequestId} category="evidence" /></span> : undefined}
                    wideEnd
                  />
                );
              })}
            </RowList>
          ) : <Empty>{aiOnly ? 'No change here was drafted by an AI client.' : 'No change in this pack.'}</Empty>}
        </Panel>

        <Panel title="Deployments" count={deployments.length} id="pack-deployments">
          {deployments.length ? (
            <RowList label="Deployments">
              {deployments.map(d => {
                const href = recordHref(d.recordUrl);
                const label = `${d.artifactName} ${d.artifactVersion}`;
                return (
                  <Row
                    key={d.deploymentRecordId}
                    lead={<LeadIcon tone={d.flags.missingApproval || d.flags.otherPath || d.flags.outsideWindow || d.flags.aiOnlyCodeApproval ? 'warning' : 'subtle'}><GitPullRequest /></LeadIcon>}
                    primary={href ? <TextLink href={href}>{label}</TextLink> : label}
                    secondary={<>To {d.targetLabel ?? 'a target no longer found'}, {fmtDateTime(d.finishedOn)}. Route: {humanizeKey(d.route.key)}.{d.startedByAiClient ? ` Started by ${d.startedByAiClient}.` : ''}</>}
                    tertiary={<VersionText stamped={d.configurationSetVersion} inForce={inForce} />}
                    badges={<><FlagBadges flags={d.flags} /><AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} /></>}
                    end={<StatusBadge status={d.result} />}
                    endSub={canLink ? <span className="mt-1" data-print="hide"><SaveLinkSheet subjectKind="deployment-record" subjectId={d.deploymentRecordId} category="evidence" /></span> : undefined}
                    wideEnd
                  />
                );
              })}
            </RowList>
          ) : <Empty>{aiOnly ? 'No deployment record here was made by an AI client.' : 'No deployment in this pack.'}</Empty>}
        </Panel>

        <Panel title="Reviews" count={reviews.length} id="pack-reviews">
          {reviews.length ? (
            <RowList label="Reviews">
              {reviews.map(r => {
                const href = recordHref(r.recordUrl);
                const outcome = r.outcomeDecisionId ? recordHrefOf('decision', r.outcomeDecisionId.toLowerCase()) : null;
                const name = `${r.recordNumber ? `${r.recordNumber}: ` : ''}${r.reviewType.label}`;
                return (
                  <Row
                    key={r.reviewId}
                    lead={<LeadIcon><History /></LeadIcon>}
                    primary={href ? <TextLink href={href}>{name}</TextLink> : name}
                    secondary={<>{r.doneOn ? `Done ${fmtDate(r.doneOn)}` : r.dueOn ? `Due ${fmtDate(r.dueOn)}` : 'No date'}{r.reviewer ? `. Reviewer: ${who(r.reviewer)}` : ''}.</>}
                    tertiary={<>{outcome && <>Outcome on <TextLink href={outcome}>its decision</TextLink>. </>}<VersionText stamped={r.configurationSetVersion} inForce={inForce} /></>}
                    end={<StatusBadge status={r.status} />}
                    endSub={canLink ? <span className="mt-1" data-print="hide"><SaveLinkSheet subjectKind="review" subjectId={r.reviewId} category="evidence" /></span> : undefined}
                    wideEnd
                  />
                );
              })}
            </RowList>
          ) : <Empty>{aiOnly ? 'Reviews are left out: only rows an AI client drafted show.' : 'No review in this pack.'}</Empty>}
        </Panel>

        <Panel title="Documents" count={documents.length} id="pack-documents">
          {documents.length ? (
            <RowList label="Documents">
              {documents.map(d => {
                const href = recordHref(d.recordUrl);
                return (
                  <Row
                    key={d.documentLinkId}
                    lead={<LeadIcon><FileStack /></LeadIcon>}
                    primary={href ? <TextLink href={href}>{d.documentType.label}</TextLink> : d.documentType.label}
                    secondary={<>{d.owner ? `Owner: ${who(d.owner)}. ` : ''}{d.lastCheckedOn ? `Last checked ${fmtDate(d.lastCheckedOn)}.` : 'Never checked.'}{d.url ? <> <ExternalLink href={d.url}>The document</ExternalLink></> : null}</>}
                    tertiary={<VersionText stamped={d.configurationSetVersion} inForce={inForce} />}
                    badges={d.isAiCreated ? <AiMark isAiCreated performedBy={null} /> : undefined}
                    end={d.status ? <StatusBadge status={d.status} /> : undefined}
                    wideEnd
                  />
                );
              })}
            </RowList>
          ) : <Empty>{aiOnly ? 'No document link here was recorded by an AI client.' : 'No document in this pack.'}</Empty>}
        </Panel>

        <Panel title="Evidence links" count={links.length} id="pack-evidence-links">
          {links.length ? (
            <RowList label="Evidence links">
              {links.map(l => {
                const subject = recordHrefOf(l.subjectKind, l.subjectId.toLowerCase());
                return (
                  <Row
                    key={l.evidenceLinkId}
                    lead={<LeadIcon><Link2 /></LeadIcon>}
                    primary={l.url ? <ExternalLink href={l.url}>{l.label}</ExternalLink> : l.label}
                    secondary={<>{l.linkKind.label}{l.evidenceLevel ? `, ${l.evidenceLevel.label}` : ''}. On {subject ? <TextLink href={subject}>the {humanizeKey(l.subjectKind).toLowerCase()}</TextLink> : `the ${humanizeKey(l.subjectKind).toLowerCase()}`}{l.capturedOn ? `, captured ${fmtDateTime(l.capturedOn)}` : ''}.</>}
                    tertiary={<VersionText stamped={l.configurationSetVersion} inForce={inForce} />}
                    badges={l.isAiCreated ? <AiMark isAiCreated performedBy={null} /> : undefined}
                    wideEnd
                  />
                );
              })}
            </RowList>
          ) : <Empty>{aiOnly ? 'No evidence link here was added by an AI client.' : 'No evidence link in this pack yet.'}</Empty>}
        </Panel>
      </div>
    </>
  );
}

function PackRange({ from, to, onChange }: { from: string | undefined; to: string | undefined; onChange: (p: Record<string, string | undefined>) => void }) {
  const fromId = useId();
  const toId = useId();
  return (
    <div className={cn(filterBarClass, 'mb-4')} role="group" aria-label="Dates" data-print="hide">
      <div className="flex min-w-0 flex-col gap-1">
        <Label htmlFor={fromId} className="text-xs text-navy-2">Rows dated from</Label>
        <Input id={fromId} type="date" className="w-[160px]" value={from ?? ''} onChange={e => onChange({ from: isIsoDate(e.target.value) ? e.target.value : undefined })} />
      </div>
      <div className="flex min-w-0 flex-col gap-1">
        <Label htmlFor={toId} className="text-xs text-navy-2">To</Label>
        <Input id={toId} type="date" className="w-[160px]" value={to ?? ''} onChange={e => onChange({ to: isIsoDate(e.target.value) ? e.target.value : undefined })} />
      </div>
      {(from || to) && <LinkButton className="mb-2 text-xs" onClick={() => onChange({ from: undefined, to: undefined })}>Every date</LinkButton>}
      {datesReversed(from, to) && <Muted className="w-full">The first date is after the second, so no row can match.</Muted>}
    </div>
  );
}

function VersionText({ stamped, inForce }: { stamped: number | null; inForce: number }) {
  const note = versionNote(stamped, inForce);
  return <span className={cn(note.older && 'font-semibold text-warning')}>{note.text}.</span>;
}

function DecisionBlock({ d, inForce, canLink }: { d: EvidenceDecision; inForce: number; canLink: boolean }) {
  const href = recordHref(d.recordUrl);
  const name = `${d.recordNumber ? `${d.recordNumber}: ` : ''}${d.statement}`;
  return (
    <li className="flex flex-col gap-2 border-t border-line pt-3 first:border-t-0 first:pt-0 print:break-inside-avoid">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="flex min-w-0 flex-col gap-0.5">
          <span className="flex items-center gap-2 text-sm font-semibold text-navy [overflow-wrap:anywhere]">
            <Gavel className="size-4 shrink-0 text-navy-3" aria-hidden />
            {href ? <TextLink href={href}>{name}</TextLink> : name}
          </span>
          <span className="text-xs text-navy-3">
            {d.decisionType.label}{d.outcome ? `, outcome ${d.outcome.label}` : ''}.
            {d.proposedBy ? ` Proposed by ${who(d.proposedBy)}.` : ''}
            {d.decidedOn ? ` Decided ${fmtDateTime(d.decidedOn)}${d.decider ? ` by ${who(d.decider)}` : ''}.` : ''}
            {d.settledBy ? ` ${SETTLED_TEXT[d.settledBy]}.` : ''}
          </span>
          {d.reason && <span className="text-xs text-navy-2 [overflow-wrap:anywhere]">Why: {d.reason}</span>}
          <span className="text-xs text-navy-3"><VersionText stamped={d.configurationSetVersion} inForce={inForce} /></span>
          <span className="flex flex-wrap gap-1"><AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} /></span>
          <StampLine performedBy={d.performedBy} />
        </div>
        <span className="flex flex-col items-end gap-1.5">
          <StatusBadge status={d.status} />
          {canLink && <span data-print="hide"><SaveLinkSheet subjectKind="decision" subjectId={d.decisionId} category="evidence" /></span>}
        </span>
      </div>
      {d.approvals.length ? (
        <ul className="m-0 flex list-none flex-col gap-2 rounded-md bg-background p-2.5 pl-3" aria-label={`Approval requests of ${name}`}>
          {d.approvals.map(a => {
            const counted = approvalCountText(a);
            const pending = a.outcome.meaning === 'pending';
            const asked = a.askedRole?.label ?? a.askedOwnerRole?.label ?? null;
            return (
              <li key={a.approvalRecordId} className="flex flex-col gap-1 text-sm">
                <span className="flex flex-wrap items-center gap-1.5">
                  <span className="font-medium text-navy">{who(a.approver) ?? (pending ? 'Waiting for an answer' : 'No answer recorded')}</span>
                  {asked && <span className="text-xs text-navy-3">asked as {asked}{a.isConsultation ? ', consulted' : ''}</span>}
                  <StatusBadge status={a.outcome} />
                  {!pending && <ToneBadge tone={counted.tone}>{counted.text}</ToneBadge>}
                  {a.approverActorKind && a.approverActorKind !== 'person' && <ActorBadge kind={a.approverActorKind} label={humanizeKey(a.approverActorKind)} />}
                </span>
                {a.typedReason && <span className="border-l-2 border-line-strong pl-2 text-xs whitespace-pre-wrap text-navy-2 [overflow-wrap:anywhere]">{a.typedReason}</span>}
                <span className="text-xs text-navy-3">
                  {a.requestedOn ? `Asked ${fmtDateTime(a.requestedOn)}` : 'Asked'}
                  {a.respondedOn ? `; answered ${fmtDateTime(a.respondedOn)}` : ''}. <VersionText stamped={a.configurationSetVersion} inForce={inForce} />
                </span>
              </li>
            );
          })}
        </ul>
      ) : <Muted>No approval request: {d.settledBy === 'engine' ? 'the rules needed none.' : 'none was asked.'}</Muted>}
    </li>
  );
}
