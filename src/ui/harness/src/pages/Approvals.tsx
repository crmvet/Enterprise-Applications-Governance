// Approvals (/app/approvals?view=mine|asked-by-me|open|answered|rules&app=<id>&portfolio=<id>): every approval request
// in one place.
//   mine         requests that ask the person (list_approvals view mine); each opens its decision page, the one place an
//                answer is written (D-38). Wave 4b verify round: the requests the person may answer now come first, under the
//                title; the ones a separation rule keeps them from answering sit apart, under "Asked of you, but another
//                person answers", each with the rule that keeps them out. The tab, the header and the top bar count only
//                the first kind (answerableCount).
//   asked-by-me  requests pending on what the person proposed or requested, and who they wait for
//   open         every pending request, and how long it has waited
//   answered     the person's own answers, counted or not, and why
//   rules        who approves what: the approval rules, the accountable holder of each decision type and the separation
//                rules of the configuration in force (get_configuration), in plain words
// Each tab shows its count (list_approvals totalCount under the same filters; answerableCount on Waiting for me), and
// every count opens its rows. The page writes nothing: answers go on the decision page.
// Wave 4b review: portfolio=<id> (the Overview's "Approvals waiting" tile under a portfolio) narrows every read to that
// portfolio's applications (list_approvals portfolioId), with a "One portfolio" chip that clears it.
import { ListFilter, Scale, ShieldCheck, X } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import { hrefTo, withParams } from '../app/links';
import { navigate } from '../app/navigation';
import { ListSearch, setListText, useListText } from '../components/listSearch';
import { APPROVALS_VIEWS, type ApprovalsView, type RouteOf } from '../app/routes';
import { DataStamp, Empty, Muted, PageHeader, Panel, SwitchTabs, TextLink, ToneBadge } from '../components/blocks';
import { RowList } from '../components/rows';
import { Button } from '../components/ui/button';
import { Label } from '../components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../components/ui/select';
import type { ApprovalItem, ApprovalList, ApprovalListView, ConfigurationRows, ListApprovalsInput } from '../data/contracts';
import { useLabels } from '../data/labels';
import { dataOf, useTool, type ToolState } from '../data/toolClient';
import { answerableTotal, approvalRuleText, keptFromAnswering, matchesText, scopeText, separationRuleText, splitMine, type RuleLabels } from './work-approvals/logic';
import { ApprovalRow, KeptFromAnswering, useNow } from './work-approvals/parts';

const PAGE_SIZE = 50;

const VIEW_TEXT: Record<ApprovalsView, { tab: string; title: string; subtitle: string; empty: string; filteredEmpty: string }> = {
  mine: {
    tab: 'Waiting for me',
    title: 'Waiting for your answer',
    subtitle: 'Requests asked of you, through one of your roles or your owner role on the application. Open one to answer it on its decision page; your answer is final. A request a separation rule keeps you from answering shows apart: another person answers it.',
    empty: 'No approval request waits for you. When someone asks you, it shows here and on My work.',
    filteredEmpty: 'No request waiting for you matches these filters.',
  },
  'asked-by-me': {
    tab: 'Asked by me',
    title: 'Asked by you',
    subtitle: 'Requests still pending on decisions you proposed, or on the intake or change you requested, and who each one waits for.',
    empty: 'Nothing you proposed or requested waits for an answer.',
    filteredEmpty: 'Nothing you proposed or requested matches these filters.',
  },
  open: {
    tab: 'All open',
    title: 'All open requests',
    subtitle: 'Every pending request you may read, the oldest expiry first, with who it waits for and since when.',
    empty: 'No approval request is open.',
    filteredEmpty: 'No open request matches these filters.',
  },
  answered: {
    tab: 'Answered',
    title: 'Your answers',
    subtitle: 'The requests you answered, newest first: what you answered, your reason, and whether the engine counted it.',
    empty: 'You have not answered any approval request yet.',
    filteredEmpty: 'None of your answers matches these filters.',
  },
  rules: {
    tab: 'Who approves what',
    title: 'Who approves what',
    subtitle: 'The rules of the configuration in force, in plain words.',
    empty: '',
    filteredEmpty: '',
  },
};

export function Approvals({ route }: { route: RouteOf<'approvals'> }) {
  const view: ApprovalsView = route.view ?? 'mine';
  const now = useNow();
  // The type and the words are in the address (type=, q=), as on every other list (product review, fix round 1), so a filtered
  // list can be linked and survives a refresh.
  const typeKey = useListText('type') ?? 'all';
  const setTypeKey = (key: string) => setListText(key === 'all' ? undefined : key, 'type');
  const text = useListText() ?? '';
  const types = useLabels('decision-type');

  const filter: Pick<ListApprovalsInput, 'decisionTypeKeys' | 'businessApplicationId' | 'portfolioId'> = {
    ...(typeKey !== 'all' ? { decisionTypeKeys: [typeKey] } : {}),
    ...(route.applicationId ? { businessApplicationId: route.applicationId } : {}),
    ...(route.portfolioId ? { portfolioId: route.portfolioId } : {}),
  };
  // One light read per tab for its count, under the same filters as the list.
  const counts: Record<ApprovalListView, ToolState<ApprovalList>> = {
    mine: useTool('list_approvals', { view: 'mine', top: 1, ...filter }),
    'asked-by-me': useTool('list_approvals', { view: 'asked-by-me', top: 1, ...filter }),
    open: useTool('list_approvals', { view: 'open', top: 1, ...filter }),
    answered: useTool('list_approvals', { view: 'answered', top: 1, ...filter }),
  };
  // Waiting for me counts only the requests the person may answer now (V1); the other tabs count every row they list.
  const countOf = (v: ApprovalListView) => (v === 'mine' ? answerableTotal(dataOf(counts.mine)) : dataOf(counts[v])?.totalCount ?? null);

  const keep = { type: typeKey !== 'all' ? typeKey : undefined, q: text || undefined };
  const scoped = { ...(route.applicationId ? { applicationId: route.applicationId } : {}), ...(route.portfolioId ? { portfolioId: route.portfolioId } : {}) };
  const go = (next: ApprovalsView) => navigate(withParams(hrefTo({ page: 'approvals', ...(next !== 'mine' ? { view: next } : {}), ...scoped }), keep), true);
  const clearApp = () => navigate(withParams(hrefTo({ page: 'approvals', ...(route.view ? { view: route.view } : {}), ...(route.portfolioId ? { portfolioId: route.portfolioId } : {}) }), keep), true);
  const clearPortfolio = () => navigate(withParams(hrefTo({ page: 'approvals', ...(route.view ? { view: route.view } : {}), ...(route.applicationId ? { applicationId: route.applicationId } : {}) }), keep), true);

  const listInput: ListApprovalsInput | null = view === 'rules' ? null : { view, top: PAGE_SIZE, ...filter };
  const mineEnvelope = counts.mine.status === 'done' ? counts.mine.envelope : undefined;
  const mineCount = countOf('mine');
  const kept = keptFromAnswering(dataOf(counts.mine));
  const keptText = kept ? ` ${kept} more ${kept === 1 ? 'asks' : 'ask'} you, but a separation rule keeps you from answering.` : '';

  return (
    <>
      <PageHeader
        eyebrow="Work"
        title="Approvals"
        subtitle={view === 'rules'
          ? 'Who approves each kind of decision under the configuration in force, and the separation rules that keep an answer from counting.'
          : mineCount === null
            ? 'Approval requests across every decision you may read.'
            : mineCount === 0
              ? `Nothing waits for your answer right now.${keptText}`
              : `${mineCount} request${mineCount === 1 ? '' : 's'} wait${mineCount === 1 ? 's' : ''} for your answer. You answer each on its decision page.${keptText}`}
        meta={<DataStamp envelope={mineEnvelope} />}
      />

      <div className="mb-4 flex flex-col gap-3">
        <SwitchTabs
          label="Show"
          ariaLabel="Which approval requests"
          value={view}
          onChange={go}
          options={APPROVALS_VIEWS.map(v => ({
            value: v,
            label: (
              <span className="inline-flex items-center gap-1.5">
                {VIEW_TEXT[v].tab}
                {v !== 'rules' && countOf(v) !== null && (
                  <span className="rounded-full bg-muted px-1.5 text-xs leading-[18px] font-semibold text-navy-3 tabular-nums" aria-label={`${countOf(v)} requests`}>
                    {countOf(v)}
                  </span>
                )}
              </span>
            ),
          }))}
        />
        {view !== 'rules' && (
          <Filters
            typeKey={typeKey}
            setTypeKey={setTypeKey}
            typeOptions={types.keys().map(k => ({ key: k, label: types.label(k) }))}
            text={text}
            applicationId={route.applicationId}
            onClearApp={clearApp}
            portfolioId={route.portfolioId}
            onClearPortfolio={clearPortfolio}
          />
        )}
      </div>

      {view === 'rules' ? (
        <RulesView />
      ) : listInput ? (
        <ApprovalPages
          key={JSON.stringify(listInput)}
          view={view}
          input={listInput}
          text={text}
          now={now}
          filtered={typeKey !== 'all' || !!route.applicationId || !!route.portfolioId}
          onClear={() => {
            if (route.applicationId || route.portfolioId) navigate(hrefTo({ page: 'approvals', ...(route.view ? { view: route.view } : {}) }), true);
            else setListText(undefined, 'q', { type: undefined });
          }}
        />
      ) : null}
    </>
  );
}

// ---------- filters ----------

function Filters(props: {
  typeKey: string;
  setTypeKey: (key: string) => void;
  typeOptions: { key: string; label: string }[];
  text: string;
  applicationId: string | undefined;
  onClearApp: () => void;
  portfolioId: string | undefined;
  onClearPortfolio: () => void;
}) {
  const typeId = useId();
  return (
    <div className="flex flex-wrap items-end gap-x-3 gap-y-2.5 rounded-xl border bg-card px-4 py-3 shadow-xs narrow:px-3" role="group" aria-label="Filters">
      <ListFilter className="mb-2.5 size-4 shrink-0 text-navy-3 narrow:hidden" aria-hidden />
      <div className="flex min-w-0 flex-col gap-1 narrow:w-full">
        <Label htmlFor={typeId} className="text-xs text-navy-2">Decision type</Label>
        <Select value={props.typeKey} onValueChange={props.setTypeKey}>
          <SelectTrigger id={typeId} className="w-[220px] narrow:w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Every type</SelectItem>
            {props.typeOptions.map(o => <SelectItem key={o.key} value={o.key}>{o.label}</SelectItem>)}
          </SelectContent>
        </Select>
      </div>
      <div className="flex min-w-0 flex-1 flex-col gap-1 narrow:w-full">
        <span className="text-xs text-navy-2" aria-hidden>Search the requests shown</span>
        <ListSearch label="Search the requests shown" placeholder="Statement, application, role" className="w-[360px] narrow:w-full" />
      </div>
      {props.applicationId && (
        <div className="flex items-center gap-1.5 self-center rounded-full border border-line-strong bg-background py-0.5 pr-1 pl-3 text-sm text-navy">
          <span>One application only</span>
          <TextLink href={hrefTo({ page: 'app', id: props.applicationId })} className="text-xs">open it</TextLink>
          <Button variant="ghost" size="icon-xs" aria-label="Show every application" onClick={props.onClearApp}>
            <X aria-hidden />
          </Button>
        </div>
      )}
      {props.portfolioId && (
        <div className="flex items-center gap-1.5 self-center rounded-full border border-line-strong bg-background py-0.5 pr-1 pl-3 text-sm text-navy">
          <span>One portfolio</span>
          <TextLink href={hrefTo({ page: 'portfolio', id: props.portfolioId })} className="text-xs">open it</TextLink>
          <Button variant="ghost" size="icon-xs" aria-label="Show every portfolio" onClick={props.onClearPortfolio}>
            <X aria-hidden />
          </Button>
        </div>
      )}
    </div>
  );
}

// ---------- the list, page by page ----------

function ApprovalPages(props: { view: ApprovalListView; input: ListApprovalsInput; text: string; now: number; filtered: boolean; onClear: () => void }) {
  const first = useTool('list_approvals', props.input);
  const [tokens, setTokens] = useState<string[]>([]);
  const words = VIEW_TEXT[props.view];
  // Waiting for me counts what waits for the person's answer; the rows a separation rule keeps them from carry their own
  // heading and count below (V1).
  const total = props.view === 'mine' ? answerableTotal(dataOf(first)) ?? undefined : dataOf(first)?.totalCount;
  return (
    <Panel title={words.title} subtitle={words.subtitle} state={first} count={total} skeletonRows={6} id="approvals-list">
      {data => {
        if (!data.items.length) {
          return (
            <div className="flex flex-col items-start gap-2">
              <Empty>{props.filtered ? words.filteredEmpty : words.empty}</Empty>
              {props.filtered && <Button variant="outline" size="sm" onClick={props.onClear}>Clear the filters</Button>}
            </div>
          );
        }
        return (
          <>
            <ApprovalRows items={data.items} view={props.view} text={props.text} now={props.now} first kept={props.view === 'mine' ? keptFromAnswering(data) : 0} />
            {tokens.map((t, i) => (
              <MorePage
                key={t}
                input={{ ...props.input, skipToken: t }}
                view={props.view}
                text={props.text}
                now={props.now}
                onMore={i === tokens.length - 1 ? next => setTokens(list => [...list, next]) : undefined}
              />
            ))}
            {tokens.length === 0 && data.skipToken && (
              <ShowMore shown={data.items.length} total={data.totalCount} onClick={() => setTokens([data.skipToken!])} />
            )}
          </>
        );
      }}
    </Panel>
  );
}

function MorePage(props: { input: ListApprovalsInput; view: ApprovalListView; text: string; now: number; onMore?: (token: string) => void }) {
  const page = useTool('list_approvals', props.input);
  if (page.status === 'loading') return <Muted className="py-2">Loading more requests.</Muted>;
  if (!page.envelope.ok) return <Muted className="py-2">The next requests could not load: {page.envelope.error.message} Reference {page.envelope.correlationId}.</Muted>;
  const data = page.envelope.data;
  return (
    <>
      <ApprovalRows items={data.items} view={props.view} text={props.text} now={props.now} />
      {props.onMore && data.skipToken && <ShowMore shown={null} total={data.totalCount} onClick={() => props.onMore?.(data.skipToken!)} />}
    </>
  );
}

function ShowMore({ shown, total, onClick }: { shown: number | null; total: number; onClick: () => void }) {
  return (
    <div className="flex flex-wrap items-center gap-3 border-t border-line pt-3">
      <Button variant="outline" size="sm" onClick={onClick}>Show more requests</Button>
      <Muted>{shown !== null ? `${shown} of ${total} shown.` : `${total} in all.`}</Muted>
    </div>
  );
}

// One page of rows. In view mine the rows split (V1): what the person may answer now under the panel's title, then the
// requests a separation rule keeps them from answering, under their own heading. The server lists the answerable ones
// first, so the second group sits on the last pages. first marks the first page, which says when nothing waits for an
// answer; kept is the server's count of the second group, for its heading.
function ApprovalRows({ items, view, text, now, first = false, kept = 0 }: { items: ApprovalItem[]; view: ApprovalListView; text: string; now: number; first?: boolean; kept?: number }) {
  const shown = items.filter(i => matchesText(i, text));
  if (!shown.length) return <Empty>No request on this page matches "{text.trim()}".</Empty>;
  if (view !== 'mine') {
    return (
      <RowList label={VIEW_TEXT[view].title}>
        {shown.map(item => <ApprovalRow key={item.approvalRecordId} item={item} now={now} view={view} />)}
      </RowList>
    );
  }
  const { answerable, others } = splitMine(shown);
  return (
    <>
      {answerable.length > 0 ? (
        <RowList label={VIEW_TEXT.mine.title}>
          {answerable.map(item => <ApprovalRow key={item.approvalRecordId} item={item} now={now} view="mine" />)}
        </RowList>
      ) : first ? <Empty>No request waits for your answer right now.</Empty> : null}
      {others.length > 0 && <KeptFromAnswering items={others} now={now} count={text.trim() ? others.length : kept || others.length} />}
    </>
  );
}

// ---------- who approves what ----------

type ConfigRow = ConfigurationRows['rows'][number];

function useRuleLabels(): RuleLabels & { decisionType(key: string): string } {
  const role = useLabels('role');
  const ownerRole = useLabels('owner-role');
  const tier = useLabels('tier');
  const changeType = useLabels('change-type');
  const applicationKind = useLabels('application-kind');
  const lifecycleStage = useLabels('lifecycle-stage');
  const targetStage = useLabels('target-stage');
  const decisionType = useLabels('decision-type');
  return {
    role: k => role.label(k),
    ownerRole: k => ownerRole.label(k),
    tier: k => tier.label(k),
    changeType: k => changeType.label(k),
    applicationKind: k => applicationKind.label(k),
    lifecycleStage: k => lifecycleStage.label(k),
    targetStage: k => targetStage.label(k),
    decisionType: k => decisionType.label(k),
  };
}

const str = (v: unknown) => (typeof v === 'string' ? v : null);

function RulesView() {
  const approvalRules = useTool('get_configuration', { entity: 'approval-rule', top: 100 });
  const rights = useTool('get_configuration', { entity: 'decision-right', top: 100 });
  const separation = useTool('get_configuration', { entity: 'separation-rule', top: 100 });
  const types = useLabels('decision-type');
  const labels = useRuleLabels();
  const rules = dataOf(approvalRules)?.rows ?? [];
  const accountable = (dataOf(rights)?.rows ?? []).filter(r => r.fields.letter === 'accountable');
  const separationRows = dataOf(separation)?.rows ?? [];

  // Decision types in configured order, then any type a rule names that the type list did not return.
  const typeKeys = [...types.keys()];
  for (const r of [...rules, ...accountable]) {
    const k = str(r.fields.decisionTypeKey);
    if (k && !typeKeys.includes(k)) typeKeys.push(k);
  }

  return (
    <div className="grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3">
      <Panel
        title="By decision type"
        subtitle="Who is accountable for each kind of decision, and which approvals the rules add. The most specific rule group wins for a decision."
        state={approvalRules}
        count={typeKeys.length}
        skeletonRows={10}
      >
        {() => (
          <ul className="m-0 flex list-none flex-col p-0" aria-label="Decision types">
            {typeKeys.map(key => (
              <TypeRules
                key={key}
                label={labels.decisionType(key)}
                accountable={accountable.filter(r => r.fields.decisionTypeKey === key)}
                rules={rules.filter(r => r.fields.decisionTypeKey === key)}
                separation={separationRows.filter(r => r.fields.decisionTypeKey === key)}
                labels={labels}
                rightsFailed={rights.status === 'done' && !rights.envelope.ok}
              />
            ))}
          </ul>
        )}
      </Panel>
      <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
        <Panel
          title="Separation rules"
          subtitle="Rules that keep an answer from counting when the approver is too close to the work."
          state={separation}
          count={separationRows.length}
        >
          {data => data.rows.length ? (
            <ul className="m-0 flex list-none flex-col gap-3 p-0" aria-label="Separation rules">
              {data.rows.map(r => (
                <li key={r.key} className="flex flex-col gap-1">
                  <span className="flex flex-wrap items-center gap-2 text-sm font-semibold text-navy">
                    <ShieldCheck className="size-4 shrink-0 text-brand-dark" aria-hidden />
                    {r.label}
                    {r.fields.enforcement === 'refuse' && <ToneBadge tone="danger">Refuses</ToneBadge>}
                    {r.fields.enforcement === 'flag' && <ToneBadge tone="warning">Flags</ToneBadge>}
                  </span>
                  <span className="text-sm text-navy-2">{separationRuleText(r.fields, labels)}</span>
                </li>
              ))}
            </ul>
          ) : <Empty>The configuration in force has no separation rule.</Empty>}
        </Panel>
        <Panel title="When you proposed it yourself">
          <p className="m-0 text-sm leading-relaxed text-navy-2">
            Where no separation rule applies, the person who proposed a decision may also answer it, as the configuration in
            force allows. Where one applies, the engine refuses the answer, or records it and flags it, and names the rule.
            A stricter setup (a second approver for every decision) is a configuration choice.
          </p>
          <p className="m-0 text-sm leading-relaxed text-navy-2">
            An AI client never answers an approval. A person answers on the decision page, signed in as themselves.
          </p>
        </Panel>
      </div>
    </div>
  );
}

function TypeRules(props: {
  label: string;
  accountable: ConfigRow[];
  rules: ConfigRow[];
  separation: ConfigRow[];
  labels: RuleLabels & { decisionType(key: string): string };
  rightsFailed: boolean;
}) {
  const approvals = props.rules.filter(r => r.fields.isConsultation !== true);
  const consultations = props.rules.filter(r => r.fields.isConsultation === true);
  return (
    <li className="flex flex-col gap-2 border-t border-line py-3 first:border-t-0 first:pt-0.5">
      <h3 className="m-0 flex items-center gap-2 text-sm font-semibold text-navy">
        <Scale className="size-4 shrink-0 text-brand-dark" aria-hidden />
        {props.label}
      </h3>
      <Lines title="Accountable">
        {props.accountable.length ? props.accountable.map(r => {
          const role = str(r.fields.roleKey);
          const owner = str(r.fields.ownerRoleKey);
          const who = role ? props.labels.role(role) : owner ? `${props.labels.ownerRole(owner)} of the application` : 'Not set';
          const scope = scopeText(r.fields, props.labels);
          return <li key={r.key}><span className="font-medium text-navy">{who}</span>{scope ? <span className="text-navy-3">{` ${scope}`}</span> : null}</li>;
        }) : <li className="text-navy-3">{props.rightsFailed ? 'The decision rights could not be read.' : 'No accountable holder is configured.'}</li>}
      </Lines>
      <Lines title="Approves">
        {approvals.length ? approvals.map(r => <RuleLine key={r.key} row={r} labels={props.labels} />) : (
          <li className="text-navy-3">No approval rule: the accountable holder answers the one request.</li>
        )}
      </Lines>
      {consultations.length > 0 && (
        <Lines title="Consulted">
          {consultations.map(r => <RuleLine key={r.key} row={r} labels={props.labels} />)}
        </Lines>
      )}
      {props.separation.length > 0 && (
        <Lines title="Separation">
          {props.separation.map(r => <li key={r.key} className="text-navy-2">{r.label}</li>)}
        </Lines>
      )}
    </li>
  );
}

function Lines({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="grid grid-cols-[110px_minmax(0,1fr)] gap-x-3 narrow:grid-cols-1 narrow:gap-y-0.5">
      <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{title}</span>
      <ul className="m-0 flex list-none flex-col gap-1 p-0 text-sm">{children}</ul>
    </div>
  );
}

function RuleLine({ row, labels }: { row: ConfigRow; labels: RuleLabels }) {
  const t = approvalRuleText(row.fields, labels);
  return (
    <li className="flex flex-col">
      <span>
        <span className="font-medium text-navy">{t.who}</span>
        {t.scope && <span className="text-navy-2">{` ${t.scope}`}</span>}
      </span>
      {t.how && <span className="text-xs text-navy-3">{`${t.how.charAt(0).toUpperCase()}${t.how.slice(1)}. Rule: ${row.label}.`}</span>}
    </li>
  );
}
