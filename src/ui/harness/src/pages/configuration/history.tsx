// The Versions and Drafts views of the Configuration page, and the configuration's health.
//   Versions: every version, newest first: when it was published, who instructed it, the decision behind it, who
//             accepted it and when, the engine's change summary, and every row that changed (get_configuration with
//             compareToVersion).
//   Drafts:   every draft with its state, its author, whether an AI client drafted it, its rule check and its decision;
//             submit or withdraw it. The filter is in the address (?state=), so Back keeps it.
// Wave 4: both lists search by text (?q=, components/listSearch.tsx): a version by its number, who instructed and
// accepted it, its decision and its change summary; a draft by its number, author, state, decision and change summary.
// list_configuration_changes takes no text, so the page searches the rows it read; the drafts list says so when more
// drafts wait to be read (D-41: one search box and one address form on every list).
import { FileDiff, GitCommitVertical, ShieldAlert } from 'lucide-react';
import { useState } from 'react';
import { hrefTo, recordHref, recordHrefOf, useQueryParam, withParams } from '../../app/links';
import { Empty, LinkButton, Muted, Panel, PanelFailure, StatusBadge, SwitchTabs, TextLink, ToneBadge } from '../../components/blocks';
import { ListSearch, setListText, useListText } from '../../components/listSearch';
import { AiMark } from '../../components/stamps';
import { Button } from '../../components/ui/button';
import type { ConfigurationChanges, ConfigurationDiffEntry, ConfigurationDraftItem, ConfigurationHealth, ConfigurationRows } from '../../data/contracts';
import { fmtDate, fmtDateTime, plural } from '../../data/format';
import { usePagedRead, useRead } from '../../data/reads';
import { useSession } from '../../data/session';
import { filterByText } from '../../data/textSearch';
import type { ToolState } from '../../data/toolClient';
import { plainEngineText } from '../../data/writes';
import { MoreButton } from '../ai-audit/parts';
import { twoColumnClass } from '../owners-reviews/people';
import { Sticky, SubmitSheet, WithdrawSheet } from '../work-approvals/parts';
import { acceptedHowText, DRAFT_FILTER_TEXT, DRAFT_FILTERS, DRAFT_STATE_TONE, draftSearchFields, draftStatesOf, versionSearchFields, type DraftFilter } from './logic';
import { DiffList, useSetParams } from './parts';
import { useConfigRowsInForce } from './rows';
import type { OpenBuilder } from './rules';

// The first characters of a content hash: enough to tell two versions apart.
export const fingerprint = (hash: string | null | undefined) => (hash ? hash.slice(0, 12) : null);

// ---------- versions ----------

type Version = ConfigurationChanges['versions'][number];

export function VersionsView({ health }: { health: ToolState<ConfigurationHealth> }) {
  const state = useRead('list_configuration_changes', { view: 'versions' });
  const text = useListText();
  const versions = state.status === 'done' && state.envelope.ok ? filterByText(state.envelope.data.versions, text, versionSearchFields) : null;
  return (
    <div className={twoColumnClass}>
      <Panel title="Versions" state={state} count={versions?.length} subtitle="Newest first. A version comes into force only when a person accepts its decision.">
        {data => (
          <>
            <ListSearch label="Search the versions by number, person, decision or change" placeholder="Version, person, decision or change" />
            {!data.versions.length ? <Empty>No version is recorded.</Empty> : !versions?.length ? (
              <Empty>
                {`No version matches "${text ?? ''}". `}
                <LinkButton onClick={() => setListText(undefined)}>Clear the search</LinkButton>
              </Empty>
            ) : (
              <ol className="m-0 list-none p-0">
                {versions.map(v => <VersionItem key={v.version} v={v} />)}
              </ol>
            )}
          </>
        )}
      </Panel>
      <HealthPanel health={health} />
    </div>
  );
}

function VersionItem({ v }: { v: Version }) {
  const [open, setOpen] = useState(false);
  const decisionHref = v.decision ? recordHref(v.decision.recordUrl) ?? recordHrefOf('decision', v.decision.decisionId) : null;
  const names = v.acceptedBy.map(a => `${a.principal?.displayLabel ?? 'a person'}${a.answeredOn ? ` on ${fmtDate(a.answeredOn)}` : ''}`);
  const draftHref = v.sourceDraftId ? recordHrefOf('configuration-draft', v.sourceDraftId) : null;
  return (
    <li className="flex flex-col gap-1.5 border-t border-line py-3 first:border-t-0 first:pt-0.5">
      <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
        <GitCommitVertical className="size-4 text-navy-3" aria-hidden />
        <span className="text-sm font-semibold text-navy">{`Version ${v.version}`}</span>
        {v.isActive && <ToneBadge tone="success">In force</ToneBadge>}
        {v.publishedOn && <span className="text-xs text-navy-3">{`Published ${fmtDateTime(v.publishedOn)}`}</span>}
      </div>
      <span className="text-sm text-navy-2">{`${acceptedHowText(v.acceptedHow, names)}.`}</span>
      <span className="flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-navy-3">
        {v.instructedBy && <span>{`Instructed by ${v.instructedBy.displayLabel}`}</span>}
        {v.baseVersion !== null && <span>{`Changed version ${v.baseVersion}`}</span>}
        {fingerprint(v.contentHash) && <span>{`Fingerprint ${fingerprint(v.contentHash)}`}</span>}
      </span>
      {(v.decision || draftHref) && (
        <span className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
          {v.decision && (
            <span className="flex flex-wrap items-center gap-1.5">
              {decisionHref ? <TextLink href={decisionHref}>{v.decision.recordNumber ?? 'The decision'}</TextLink> : <span>{v.decision.recordNumber ?? 'The decision'}</span>}
              <StatusBadge status={v.decision.status} />
            </span>
          )}
          {draftHref && <TextLink href={draftHref}>The draft it came from</TextLink>}
        </span>
      )}
      {v.changeSummary.length > 0 && (
        <ul className="m-0 flex list-disc flex-col gap-0.5 pl-5 text-sm text-navy">
          {v.changeSummary.map((l, i) => <li key={i}>{l}</li>)}
        </ul>
      )}
      {v.version > 1 && (
        <details className="text-sm" onToggle={e => setOpen((e.target as HTMLDetailsElement).open)}>
          <summary className="w-fit cursor-pointer rounded-sm text-sm font-medium text-primary hover:underline focus-visible:outline-2 focus-visible:outline-ring">
            <FileDiff className="mr-1 inline size-4 align-[-3px]" aria-hidden />
            Every row that changed
          </summary>
          {open && <VersionDiff version={v.version} base={v.baseVersion ?? v.version - 1} />}
        </details>
      )}
    </li>
  );
}

// The rows a version changed from the one before it. top 1: the read returns the version's rows too, and only the diff
// is wanted here.
function VersionDiff({ version, base }: { version: number; base: number }) {
  const { lookup } = useConfigRowsInForce();
  const state = useRead('get_configuration', { version, compareToVersion: base, top: 1 });
  if (state.status === 'loading') return <Muted className="mt-2 block">Reading the rows that changed.</Muted>;
  if (!state.envelope.ok) return <div className="mt-2"><PanelFailure failure={state.envelope} /></div>;
  // get_configuration returns diff with compareToVersion; the typed ConfigurationRows in data/contracts.ts leaves it out.
  const diff = (state.envelope.data as ConfigurationRows & { diff?: ConfigurationDiffEntry[] }).diff ?? [];
  return <div className="mt-2"><DiffList diff={diff} lookup={lookup} empty={`No row changed from version ${base}.`} /></div>;
}

// ---------- health ----------

export function HealthPanel({ health }: { health: ToolState<ConfigurationHealth> }) {
  const config = useConfigRowsInForce();
  return (
    <Panel title="Health" state={health} id="configuration-health" subtitle="Whether the rules in force load, and what the engine found in them.">
      {h => (
        <div className="flex flex-col gap-3">
          <dl className="m-0 flex flex-col gap-2">
            <div className="flex flex-col gap-0.5">
              <dt className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">In force</dt>
              <dd className="m-0 text-sm text-navy">{h.active ? `Version ${h.active.version}, since ${fmtDateTime(h.active.activatedOn)}` : 'No version is in force'}</dd>
            </div>
            <div className="flex flex-col gap-0.5">
              <dt className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">The engine</dt>
              <dd className="m-0 flex flex-wrap items-center gap-2 text-sm text-navy">
                <ToneBadge tone={h.engine.loaded ? 'success' : 'danger'}>{h.engine.loaded ? 'Loaded the rules' : 'Could not load the rules'}</ToneBadge>
                <span className="text-xs text-navy-3">{`Engine ${h.engine.engineVersion}`}</span>
              </dd>
            </div>
            <div className="flex flex-col gap-0.5">
              <dt className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">Next review of the rules</dt>
              <dd className="m-0 text-sm text-navy">{h.reviewDueOn ? fmtDate(h.reviewDueOn) : 'Not set'}</dd>
            </div>
          </dl>
          <div className="flex flex-col gap-1.5">
            <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{`Findings (${h.findings.length})`}</span>
            {!h.findings.length ? <Muted>The engine found nothing to fix.</Muted> : (
              <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
                {h.findings.map((f, i) => (
                  <li key={i} className="flex items-start gap-1.5 text-sm text-navy">
                    <ShieldAlert className="mt-0.5 size-4 shrink-0 text-warning" aria-hidden />
                    <span>{plainEngineText(f.message, config.labelOfAnyKey)}</span>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      )}
    </Panel>
  );
}

// ---------- drafts ----------

const FILTER = /^(open|all|draft|submitted|activated|discarded)$/;

export function DraftsView({ canPropose, openBuilder }: { canPropose: boolean; openBuilder: OpenBuilder }) {
  const setParams = useSetParams();
  const filter = (useQueryParam('state', FILTER) as DraftFilter | undefined) ?? 'open';
  const states = draftStatesOf(filter);
  const counts = useRead('list_configuration_changes', { view: 'drafts', top: 1 });
  const read = usePagedRead('list_configuration_changes', { view: 'drafts', top: 25, ...(states ? { draftStates: states } : {}) }, d => d.drafts);
  const total = counts.status === 'done' && counts.envelope.ok ? counts.envelope.data.counts : null;
  const text = useListText();
  const drafts = read.status === 'done' ? filterByText(read.items, text, draftSearchFields) : [];
  return (
    <div className="flex flex-col gap-4 narrow:gap-3">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <ListSearch label="Search the drafts by number, person, state, decision or change" placeholder="Draft, person, decision or change" />
        <SwitchTabs
          label="Show"
          ariaLabel="Which drafts"
          value={filter}
          onChange={v => setParams({ state: v === 'open' ? undefined : v })}
          options={DRAFT_FILTERS.map(f => ({
            value: f,
            label: f === 'open' && total ? `${DRAFT_FILTER_TEXT[f]} (${total.openDrafts})` : f === 'all' && total ? `${DRAFT_FILTER_TEXT[f]} (${total.drafts})` : DRAFT_FILTER_TEXT[f],
          }))}
        />
        {canPropose && <Button size="sm" onClick={() => openBuilder({ title: 'Propose a change to the rules' })}>Propose a change</Button>}
      </div>
      <Panel title={DRAFT_FILTER_TEXT[filter] === 'Open' ? 'Open drafts' : `Drafts: ${DRAFT_FILTER_TEXT[filter].toLowerCase()}`} count={read.status === 'done' ? drafts.length : undefined}>
        {read.status === 'loading' ? <Muted>Reading the drafts.</Muted> : read.status === 'failed' ? <PanelFailure failure={read.failure} /> : !read.items.length ? (
          <Empty>
            {filter === 'open'
              ? `No draft is open. ${canPropose ? 'Propose a change to start one.' : 'A configuration admin proposes changes.'}`
              : 'No draft in this state.'}
          </Empty>
        ) : (
          <>
            {drafts.length === 0 ? (
              <Empty>
                {`No draft read so far matches "${text ?? ''}". `}
                <LinkButton onClick={() => setListText(undefined)}>Clear the search</LinkButton>
              </Empty>
            ) : (
              <ul className="m-0 list-none p-0">
                {drafts.map(d => <DraftRow key={d.configurationDraftId} d={d} />)}
              </ul>
            )}
            {text && read.skipToken && <Muted>{`The search covers the ${read.items.length} drafts read so far. Read more to search the rest.`}</Muted>}
            <MoreButton skipToken={read.skipToken} loading={read.loadingMore} onMore={read.more} what="drafts" laterFailure={read.laterFailure} />
          </>
        )}
      </Panel>
      <Muted>
        A draft changes nothing. Submitted, it waits on the decision page for the people the rules name; once a person
        accepts it, the engine checks it again and it becomes the next version.
      </Muted>
    </div>
  );
}

export function draftTitle(d: Pick<ConfigurationDraftItem, 'recordNumber' | 'changeSummary'>): string {
  return d.recordNumber ? `Configuration draft ${d.recordNumber}` : 'Configuration draft';
}

function DraftRow({ d }: { d: ConfigurationDraftItem }) {
  const session = useSession();
  const href = recordHref(d.recordUrl) ?? recordHrefOf('configuration-draft', d.configurationDraftId);
  const decisionHref = d.decision ? recordHref(d.decision.recordUrl) ?? recordHrefOf('decision', d.decision.decisionId) : null;
  const canSubmit = d.state.key === 'draft' && session.can('submit_record') && !!d.instructedBy && d.instructedBy.objectId.toLowerCase() === session.objectId;
  const canWithdraw = (d.state.key === 'draft' || d.state.key === 'submitted') && session.can('withdraw_record') && !!d.instructedBy && d.instructedBy.objectId.toLowerCase() === session.objectId;
  return (
    <li className="flex flex-col gap-1.5 border-t border-line py-3 first:border-t-0 first:pt-0.5">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <span className="flex min-w-0 flex-col gap-1">
          <span className="flex flex-wrap items-center gap-2">
            {href ? <TextLink href={href} className="text-sm font-semibold">{draftTitle(d)}</TextLink> : <span className="text-sm font-semibold text-navy">{draftTitle(d)}</span>}
            <ToneBadge tone={DRAFT_STATE_TONE[d.state.key]}>{d.state.label}</ToneBadge>
            {d.isValid === false && <ToneBadge tone="danger">{plural(d.errorCount || 1, 'rule problem', 'rule problems')}</ToneBadge>}
            {d.warningCount > 0 && <ToneBadge tone="warning">{plural(d.warningCount, 'warning', 'warnings')}</ToneBadge>}
          </span>
          <AiMark isAiCreated={d.isAiCreated} performedBy={d.performedBy} />
        </span>
        <span className="flex flex-wrap gap-1.5">
          <Sticky show={canSubmit}>
            <SubmitSheet subjectKind="configuration-draft" subjectId={d.configurationDraftId} title={draftTitle(d)} isAiDraft={d.isAiCreated} performedBy={d.performedBy} />
          </Sticky>
          <Sticky show={canWithdraw}>
            <WithdrawSheet subjectKind="configuration-draft" subjectId={d.configurationDraftId} title={draftTitle(d)} />
          </Sticky>
        </span>
      </div>
      {d.changeSummary.length > 0 && (
        <ul className="m-0 flex list-disc flex-col gap-0.5 pl-5 text-sm text-navy">
          {d.changeSummary.slice(0, 4).map((l, i) => <li key={i}>{l}</li>)}
          {d.changeSummary.length > 4 && <li className="list-none text-xs text-navy-3">{`And ${d.changeSummary.length - 4} more.`}</li>}
        </ul>
      )}
      <span className="flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-navy-3">
        {d.instructedBy && <span>{`Instructed by ${d.instructedBy.displayLabel}`}</span>}
        <span>{`From version ${d.baseVersion}`}</span>
        <span>{plural(d.operationCount, 'row change', 'row changes')}</span>
        {d.submittedOn && <span>{`Submitted ${fmtDateTime(d.submittedOn)}`}</span>}
        {d.expiresOn && d.state.key === 'submitted' && <span>{`Expires ${fmtDateTime(d.expiresOn)}`}</span>}
        {d.resultingVersion !== null && <span>{`Became version ${d.resultingVersion}`}</span>}
        {d.discardedOn && <span>{`Ended ${fmtDate(d.discardedOn)}${d.discardReason ? `: ${d.discardReason}` : ''}`}</span>}
      </span>
      {d.decision && (
        <span className="flex flex-wrap items-center gap-1.5 text-sm">
          <span className="text-navy-2">Decision</span>
          {decisionHref ? <TextLink href={decisionHref}>{d.decision.recordNumber ?? 'Open the decision'}</TextLink> : <span>{d.decision.recordNumber ?? 'The decision'}</span>}
          <StatusBadge status={d.decision.status} />
        </span>
      )}
    </li>
  );
}

// The address of the drafts list, for a link from elsewhere on the page.
export const draftsHref = (filter?: DraftFilter) => withParams(hrefTo({ page: 'configuration', view: 'drafts' }), { state: filter && filter !== 'open' ? filter : undefined });
