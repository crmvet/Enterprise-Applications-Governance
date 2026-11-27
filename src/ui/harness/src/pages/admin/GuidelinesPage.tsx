// Guidelines and playbook (screen "Guidelines and playbook", priority 3; D-17): why each rule exists. The guidelines of
// the configuration in force, each with the organization's own copy and whether it is adopted; the playbook chapters by
// track (a title and a link, never the text); the build paths; and the sanctioned AI clients.
//   /app/guidelines?g=G17          opens the page at one guideline, as every guideline chip links (links.ts guidelineHref)
//   ?q=<text>                      searches the number, title, rule and the organization's own text
//   ?show=not-adopted|retired|all  narrows the list (the default is the guidelines in force)
// The open guideline shows where the rules of the configuration apply it: the approval rules, gate requirements and
// protected fields that name it (get_configuration), so its "used by" counts open to their rows.
// Reads: list_guidelines; get_configuration (approval-rule, gate-requirement, column-protection-rule) for the open one.
// Read only: the page has no write.
import { BookOpen, Bot, ChevronDown, ChevronUp, MonitorSmartphone } from 'lucide-react';
import { useEffect, useRef } from 'react';
import { hrefTo, useQueryParam } from '../../app/links';
import type { RouteOf } from '../../app/routes';
import { DataStamp, Empty, KpiTile, LinkButton, Muted, PageHeader, Panel, SwitchTabs, TextLink, ToneBadge } from '../../components/blocks';
import { ExternalLink, Row, RowList } from '../../components/rows';
import type { GuidelineList } from '../../data/contracts';
import { plural } from '../../data/format';
import { useRead } from '../../data/reads';
import { dataOf, useTool, type ToolState } from '../../data/toolClient';
import { cn } from '../../lib/utils';
import { kpiRowClass4, twoColumnClass } from '../owners-reviews/people';
import { LeadIcon } from '../ai-audit/parts';
import {
  GUIDELINE_SHOW_TEXT,
  GUIDELINE_SHOWS,
  guidelineCounts,
  guidelineMatches,
  guidelineNumber,
  guidelineShown,
  chapterTitleText,
  supersededNumber,
  usedByText,
  type GuidelineItem,
  type GuidelineShow,
} from './logic';
import { Note, PageFailure, SearchField, SubHeading, useParamNav } from './parts';

const SHOW = new RegExp(`^(${GUIDELINE_SHOWS.join('|')})$`);
const TEXT = /^[^\u0000-\u001f]{1,100}$/;

export function GuidelinesPage({ route }: { route: RouteOf<'guidelines'> }) {
  const list = useRead('list_guidelines', { includeRetired: true });
  const data = dataOf(list);
  const open = guidelineNumber(route.guideline);
  const show = (useQueryParam('show', SHOW) ?? 'in-force') as GuidelineShow;
  const text = useQueryParam('q', TEXT) ?? '';
  const go = useParamNav();
  const envelope = list.status === 'done' ? list.envelope : undefined;
  const counts = data ? guidelineCounts(data.guidelines) : null;
  // The open guideline always shows, whatever the filter, so a chip that links here never lands on an empty list.
  const shown = data ? data.guidelines.filter(g => g.number === open || (guidelineShown(g, show) && guidelineMatches(g, text))) : [];
  const toggle = (number: string) => go({ g: open === number ? undefined : number }, { push: true });

  const header = (
    <PageHeader
      eyebrow="Govern"
      title="Guidelines and playbook"
      subtitle="Why each rule exists. Every refusal, requirement and gate names the guideline it applies, and opens it here, with your organization's own copy when it keeps one."
      meta={(
        <span className="flex flex-col gap-0.5">
          {data && !data.hubBaseUrl && <span>No guideline hub link is set, so the playbook chapters carry no link.</span>}
          <span><DataStamp envelope={envelope} /></span>
        </span>
      )}
    />
  );
  if (envelope && !envelope.ok) return <>{header}<PageFailure failure={envelope} /></>;

  return (
    <>
      {header}

      <div className={kpiRowClass4} role="group" aria-label="Key figures">
        <KpiTile label="In force" value={counts?.['in-force'] ?? null} caption="Guidelines the rules apply" onClick={() => go({ show: undefined, q: undefined })} />
        <KpiTile label="Not adopted" value={counts?.['not-adopted'] ?? null} caption="In force, but your organization does not adopt them" attention={!!counts?.['not-adopted']} onClick={() => go({ show: 'not-adopted', q: undefined })} />
        <KpiTile label="Retired" value={counts?.retired ?? null} caption="Kept so their numbers are never reused" onClick={() => go({ show: 'retired', q: undefined })} />
        <KpiTile label="Playbook chapters" value={data?.chapters.length ?? null} caption="By track, each with the guidelines it applies" onClick={() => document.getElementById('chapters')?.scrollIntoView({ behavior: 'smooth', block: 'start' })} />
      </div>

      <div className={twoColumnClass}>
        <Panel title="Guidelines" id="guidelines" count={data ? shown.length : undefined} state={list} skeletonRows={6}>
          {d => (
            <>
              <div className="flex flex-wrap items-end gap-3">
                <SearchField className="w-[280px] narrow:w-full" label="Search the guidelines" placeholder="Owners, AI, G17" value={text} onChange={v => go({ q: v.trim() ? v : undefined })} />
                <SwitchTabs
                  label="Show"
                  ariaLabel="Which guidelines"
                  value={show}
                  onChange={v => go({ show: v === 'in-force' ? undefined : v })}
                  options={GUIDELINE_SHOWS.map(s => ({ value: s, label: GUIDELINE_SHOW_TEXT[s] }))}
                />
              </div>
              {shown.length === 0 ? (
                <Empty>
                  No guideline matches.{' '}
                  <LinkButton onClick={() => go({ q: undefined, show: undefined })}>Clear the search</LinkButton>
                </Empty>
              ) : (
                <ul className="m-0 flex list-none flex-col p-0" aria-label="Guidelines">
                  {shown.map(g => (
                    <GuidelineEntry key={g.key} guideline={g} all={d.guidelines} open={g.number === open} onToggle={() => toggle(g.number)} chapters={d.chapters} />
                  ))}
                </ul>
              )}
            </>
          )}
        </Panel>

        <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
          <ChaptersPanel data={data} state={list} open={open} />
          <Panel title="Build paths" state={list} notes={[]} subtitle="The ways an application may be built here. Intake asks which one.">
            {d => d.buildPaths.length === 0 ? <Empty>The configuration lists no build path.</Empty> : (
              <RowList label="Build paths">
                {d.buildPaths.map(p => <Row key={p.key} primary={p.label} secondary={p.description ?? undefined} />)}
              </RowList>
            )}
          </Panel>
          <Panel title="Sanctioned clients" state={list} notes={[]} subtitle="The clients on the Known Client list. AI work through any other client is a slide-back signal.">
            {d => d.knownClients.length === 0 ? <Empty>The Known Client list is empty.</Empty> : (
              <RowList label="Known clients">
                {d.knownClients.map(c => (
                  <Row
                    key={c.key}
                    lead={<LeadIcon tone={c.isAllowed ? 'brand' : 'danger'}>{c.isAiClient ? <Bot /> : <MonitorSmartphone />}</LeadIcon>}
                    primary={c.label}
                    secondary={c.isAiClient ? 'AI client' : 'Not an AI client'}
                    end={c.isAllowed ? <ToneBadge tone="success">Sanctioned</ToneBadge> : <ToneBadge tone="danger">Blocked</ToneBadge>}
                  />
                ))}
              </RowList>
            )}
          </Panel>
        </div>
      </div>
    </>
  );
}

function GuidelineEntry({ guideline: g, all, open, onToggle, chapters }: {
  guideline: GuidelineItem;
  all: GuidelineItem[];
  open: boolean;
  onToggle: () => void;
  chapters: GuidelineList['chapters'];
}) {
  const ref = useRef<HTMLLIElement>(null);
  // Opened from a chip or a link: bring it into view once.
  useEffect(() => {
    if (open) ref.current?.scrollIntoView({ behavior: 'smooth', block: 'center' });
  }, [open]);
  const replaces = supersededNumber(g.supersedes, all);
  const bodyId = `guideline-${g.key}`;
  const chapterTitles = g.chapters.map(n => chapters.find(c => c.number === n)).filter((c): c is GuidelineList['chapters'][number] => !!c);
  return (
    <li ref={ref} className={cn('flex flex-col gap-1.5 border-t border-line py-3 first:border-t-0 first:pt-1', open && 'rounded-lg border border-primary bg-info-soft/40 px-3 first:pt-3')}>
      <div className="flex items-start gap-3">
        <span className="grid h-7 min-w-10 shrink-0 place-items-center rounded-md bg-navy px-1.5 text-xs font-bold text-white tabular-nums" aria-hidden>{g.number}</span>
        <div className="flex min-w-0 flex-1 flex-col gap-1">
          <h3 className="m-0 text-sm leading-5 font-semibold text-navy">
            <button
              type="button"
              onClick={onToggle}
              aria-expanded={open}
              aria-controls={open ? bodyId : undefined}
              className="inline cursor-pointer border-0 bg-transparent p-0 text-left text-[length:inherit] leading-[inherit] font-semibold text-navy hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            >
              <span className="sr-only">{g.number} </span>
              {g.title}
              {open ? <ChevronUp className="ml-1 inline size-4 align-[-3px] text-navy-3" aria-hidden /> : <ChevronDown className="ml-1 inline size-4 align-[-3px] text-navy-3" aria-hidden />}
            </button>
          </h3>
          <p className="m-0 text-sm leading-relaxed text-navy-2">{g.rule}</p>
          <div className="flex flex-wrap gap-1">
            {!g.isActive && <ToneBadge tone="subtle">Retired</ToneBadge>}
            {g.isActive && (g.isAdopted ? <ToneBadge tone="success">Adopted</ToneBadge> : <ToneBadge tone="warning">Not adopted</ToneBadge>)}
            {g.localText && <ToneBadge tone="brand" icon={<BookOpen aria-hidden />}>Your organization's text</ToneBadge>}
            <Muted className="self-center">Version {g.version}</Muted>
          </div>
        </div>
      </div>
      {open && (
        <div id={bodyId} className="flex flex-col gap-2.5 pl-[52px] narrow:pl-0">
          {!g.isAdopted && g.isActive && (
            <p className="m-0 text-sm text-navy"><span className="font-semibold">Why it is not adopted: </span>{g.notAdoptedReason ?? 'No reason is recorded.'}</p>
          )}
          {g.localText && (
            <blockquote className="m-0 border-l-2 border-primary pl-3 text-sm leading-relaxed whitespace-pre-wrap text-navy [overflow-wrap:anywhere]">{g.localText}</blockquote>
          )}
          <div className="flex flex-wrap gap-x-4 gap-y-1 text-sm">
            {g.localUrl && <ExternalLink href={g.localUrl}>Open your organization's copy</ExternalLink>}
            {g.hubUrl && <ExternalLink href={g.hubUrl}>Open the public guideline</ExternalLink>}
            {!g.localUrl && !g.hubUrl && <Muted>No copy is linked: the configuration names no link for it.</Muted>}
          </div>
          {replaces && <p className="m-0 text-xs text-navy-3">It replaces {replaces}.</p>}
          {chapterTitles.length > 0 && (
            <p className="m-0 text-xs text-navy-3">
              Playbook: {chapterTitles.map((c, i) => (
                <span key={c.number}>
                  {i > 0 && ', '}
                  {c.url ? <ExternalLink href={c.url}>{`chapter ${c.number}, ${chapterTitleText(c)}`}</ExternalLink> : `chapter ${c.number}, ${chapterTitleText(c)}`}
                </span>
              ))}
            </p>
          )}
          <p className="m-0 text-xs text-navy-2">{usedByText(g.usedBy)}</p>
          <AppliedBy guideline={g} />
        </div>
      )}
    </li>
  );
}

// The rows of the configuration in force that name a guideline: approval rules, gate requirements and protected fields.
// Read only for the open guideline, three reads of up to 100 rows each.
function AppliedBy({ guideline: g }: { guideline: GuidelineItem }) {
  const want = (n: number) => n > 0;
  const approvals = useTool('get_configuration', want(g.usedBy.approvalRules) ? { entity: 'approval-rule', top: 100 } : null);
  const gates = useTool('get_configuration', want(g.usedBy.gateRequirements) ? { entity: 'gate-requirement', top: 100 } : null);
  const fields = useTool('get_configuration', want(g.usedBy.columnProtections) ? { entity: 'column-protection-rule', top: 100 } : null);
  const groups = [
    { key: 'approvals', title: 'Approval rules', state: approvals, count: g.usedBy.approvalRules },
    { key: 'gates', title: 'Gate requirements', state: gates, count: g.usedBy.gateRequirements },
    { key: 'fields', title: 'Protected fields', state: fields, count: g.usedBy.columnProtections },
  ].filter(x => x.count > 0);
  if (!groups.length) return null;
  return (
    <div className="flex flex-col gap-2">
      {groups.map(x => {
        const data = dataOf(x.state);
        const rows = data?.rows.filter(r => r.isActive && r.fields.guidelineKey === g.key) ?? [];
        return (
          <div key={x.key} className="flex flex-col gap-1">
            <SubHeading>{x.title}</SubHeading>
            {x.state.status === 'loading' ? <Muted>Reading the rules in force.</Muted>
              : !data ? <Muted>The rules could not be read just now.</Muted>
                : rows.length === 0 ? <Muted>No rule in force names it.</Muted>
                  : (
                    <ul className="m-0 flex list-disc flex-col gap-0.5 pl-5 text-sm text-navy">
                      {rows.map(r => <li key={r.key}>{r.label}</li>)}
                    </ul>
                  )}
            {data?.skipToken && <Note>The configuration holds more of these rows than this list read.</Note>}
          </div>
        );
      })}
      <TextLink className="self-start text-xs" href={hrefTo({ page: 'configuration' })}>Open the rules in force</TextLink>
    </div>
  );
}

function ChaptersPanel({ data, state, open }: { data: GuidelineList | null; state: ToolState<GuidelineList>; open: string | undefined }) {
  return (
    <Panel title="Playbook chapters" id="chapters" state={state} notes={[]} count={data?.chapters.length} subtitle="By track. Each chapter names the guidelines it applies; open one to read it here.">
      {d => d.tracks.length === 0 ? <Empty>The playbook index is empty.</Empty> : (
        <div className="flex flex-col gap-3">
          {d.tracks.map(t => (
            <div key={t.track} className="flex flex-col gap-1">
              <SubHeading>{t.track}</SubHeading>
              <ul className="m-0 flex list-none flex-col gap-1.5 p-0" aria-label={`${t.track} chapters`}>
                {t.chapters.map(n => d.chapters.find(c => c.number === n)).filter((c): c is GuidelineList['chapters'][number] => !!c).map(c => (
                  <li key={c.number} className="flex flex-col gap-0.5">
                    <span className="text-sm text-navy">
                      <span className="mr-1.5 text-navy-3 tabular-nums">{c.number}.</span>
                      {c.url ? <ExternalLink href={c.url}>{chapterTitleText(c)}</ExternalLink> : chapterTitleText(c)}
                    </span>
                    {c.guidelineNumbers.length > 0 && (
                      <span className="flex flex-wrap gap-1 pl-5">
                        {c.guidelineNumbers.map(n => (
                          <a
                            key={n}
                            href={hrefTo({ page: 'guidelines', guideline: n })}
                            aria-current={n === open ? 'true' : undefined}
                            aria-label={`Guideline ${n}`}
                            className={cn(
                              'rounded-md border px-1.5 text-xs leading-[18px] font-semibold no-underline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring',
                              n === open ? 'border-primary bg-primary text-primary-foreground' : 'border-info-line bg-info-soft text-brand-dark hover:underline',
                            )}
                          >
                            {n}
                          </a>
                        ))}
                      </span>
                    )}
                  </li>
                ))}
              </ul>
            </div>
          ))}
          <p className="m-0 text-xs text-navy-3">{plural(d.chapters.length, 'chapter', 'chapters')} in {plural(d.tracks.length, 'track', 'tracks')}. The product shows their titles and links, never their text.</p>
        </div>
      )}
    </Panel>
  );
}
