// The applications register (/app/applications; screen "Applications register (existing, extended)", priority 1):
// search, filters, sort, and a table that leads to Application 360 and the map, with the two ways in.
// The route carries the shared filters (routes.ts): stage, tier, kind, portfolio, text, owner=<objectId>
// (find_applications ownerObjectId; the Owners page links here) and registration=<meaning> (registrationMeanings).
// Uses AI and build path: ai=yes|no and build=<list value key> are route filters (ApplicationFilter usesAi and
// buildPathKeys) sent to find_applications; ai=unknown and build=not-stated are the page's own (useQueryParam), because
// the tool cannot filter on "no answer yet".
// Reads: find_applications (the rows; one count per registration meaning; the uses AI and build path counts, top 1 each,
// once their menu opens or their filter is set), get_portfolio_summary (the counts per stage, tier, kind and portfolio),
// list_owner_assignments (the owner's name for a link that carries only the ID), get_application_card (uses AI and build
// path, only while "Not stated" is chosen for one of them).
// Writes, each in the shared side sheet: save_application_draft ("Register an app that is already built"), then
// submit_record on its ownership decision (the confirmation). "New request" opens the intake wizard (/app/intake/new).
//
// Design choices (D-41), recorded here:
// - Every filter count is the number of rows the filter gives, so a count never promises rows the list does not show:
//   registration counts are find_applications totals under the other filters; stage, tier, kind and portfolio keep the
//   summary's register-wide counts as before.
// - Wave 4: find_applications filters on uses AI and build path (B24-21), so "Uses AI", "Does not use AI" and each build
//   path are the tool's own filters and their counts are its totals (top 1) under the other filters. "Not stated" is the
//   rest: the total under the other filters less the stated answers, since an application with no answer matches neither
//   true nor false. Choosing "Not stated" still reads the card of each row (its smallest section, four at a time) over
//   the first 100 rows that match the other filters, and says so; while it is chosen, the other menu counts over the same
//   rows, so its counts are the rows it gives.
// - The owner filter finds a person by name (find_people); an ID in the address shows the person's name from their
//   owner assignments. "Applications I own" is one click.
// - A list that mixes registration states marks each draft and archived row, so a draft is never read as registered.
import { ChevronDown, FilePlus2, FolderInput, ListFilter, Search, TriangleAlert, UserRound, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import type { ApplicationCard, ApplicationSummary, PrincipalRef } from '../../data/contracts';
import { useLabels, useListValues, type Labels } from '../../data/labels';
import { humanizeKey, plural } from '../../data/format';
import { useSession } from '../../data/session';
import { dataOf, useTool, type ToolState } from '../../data/toolClient';
import { useHost, type ApplicationFilter } from '../../harness';
import { Row, RowList } from '../../components/rows';
import { DataStamp, Empty, Muted, PageHeader, Panel, PanelFailure, TextLink, ToneBadge, useNarrow } from '../../components/blocks';
import { PersonSearchField } from '../../components/people';
import { AiMark } from '../../components/stamps';
import { Badge } from '../../components/ui/badge';
import { Button } from '../../components/ui/button';
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '../../components/ui/dropdown-menu';
import { Input } from '../../components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import { Skeleton } from '../../components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '../../components/ui/table';
import { cn } from '../../lib/utils';
import { hrefTo, useQueryParam, withParams } from '../../app/links';
import { navigate } from '../../app/navigation';
import { appHref, normalizeBase, REGISTRATION_MEANINGS } from '../../app/routes';
import { useEachRead } from '../ai-audit/hooks';
import {
  BUILD_NOT_STATED,
  keepRow,
  REGISTRATION_TEXT,
  SCAN_LIMIT,
  scanCounts,
  shownRegistrations,
  USES_AI_FILTERS,
  USES_AI_TEXT,
  usesAiOf,
  type CardFacts,
  type RegistrationMeaning,
  type UsesAiFilter,
} from './logic';
import { RegisterBuiltAppSheet, SavedDraftNotice, type SavedDraft } from './RegisterSheet';

const PAGE_SIZE = 50;

type Facet = 'stage' | 'tier' | 'kind' | 'portfolio';
type SortKey = 'name' | 'stage' | 'tier' | 'kind';

const FACETS: { facet: Facet; label: string; groupBy: 'stage' | 'tier' | 'kind' | 'portfolio'; field: keyof ApplicationFilter }[] = [
  { facet: 'stage', label: 'Stage', groupBy: 'stage', field: 'lifecycleStageKeys' },
  { facet: 'tier', label: 'Tier', groupBy: 'tier', field: 'tierKeys' },
  { facet: 'kind', label: 'Kind', groupBy: 'kind', field: 'applicationKindKeys' },
  { facet: 'portfolio', label: 'Portfolio', groupBy: 'portfolio', field: 'portfolioIds' },
];

const SORTS: { key: SortKey; label: string }[] = [
  { key: 'name', label: 'Name, A to Z' },
  { key: 'stage', label: 'Lifecycle stage' },
  { key: 'tier', label: 'Tier' },
  { key: 'kind', label: 'Kind' },
];

// The uses AI and build path filters as the person chose them: yes and no and a build path key come from the route
// (the tool filters on them); unknown and not-stated are the page's own (the card scan filters on them).
interface PageFilters {
  ai?: UsesAiFilter;
  build?: string;
}

const UNKNOWN_PARAM = /^unknown$/;
const NOT_STATED_PARAM = /^not-stated$/;

// The find_applications input for a filter: only the parts that are set.
function toolInput(filter: ApplicationFilter): Record<string, unknown> {
  const input: Record<string, unknown> = { top: PAGE_SIZE };
  if (filter.text) input.text = filter.text;
  if (filter.lifecycleStageKeys?.length) input.lifecycleStageKeys = filter.lifecycleStageKeys;
  if (filter.tierKeys?.length) input.tierKeys = filter.tierKeys;
  if (filter.applicationKindKeys?.length) input.applicationKindKeys = filter.applicationKindKeys;
  // The tool takes one portfolio; the list asks once per portfolio when more are chosen (see PortfolioPages).
  if (filter.portfolioIds?.length === 1) input.portfolioId = filter.portfolioIds[0];
  if (filter.ownerObjectId) input.ownerObjectId = filter.ownerObjectId;
  if (filter.registrationMeanings?.length) input.registrationMeanings = filter.registrationMeanings;
  if (filter.usesAi !== undefined) input.usesAi = filter.usesAi;
  if (filter.buildPathKeys?.length) input.buildPathKeys = filter.buildPathKeys;
  return input;
}

// The uses AI and build path counts from find_applications totals (top 1) under the other filters, read once a menu opens
// or its filter is set. "Not stated" is the total less the stated answers.
interface FilterCounts {
  loading: boolean;
  usesAi: Record<UsesAiFilter, number> | null;
  buildPaths: { key: string; label: string; count: number }[] | null;
}

function useToolCounts(filter: ApplicationFilter, buildKeys: { key: string; label: string }[], aiWanted: boolean, buildWanted: boolean): FilterCounts {
  const otherThanAi = { ...toolInput({ ...filter, usesAi: undefined }), top: 1 };
  const aiAll = useTool('find_applications', aiWanted ? otherThanAi : null);
  const aiYes = useTool('find_applications', aiWanted ? { ...otherThanAi, usesAi: true } : null);
  const aiNo = useTool('find_applications', aiWanted ? { ...otherThanAi, usesAi: false } : null);
  const otherThanBuild = { ...toolInput({ ...filter, buildPathKeys: undefined }), top: 1 };
  const buildAll = useTool('find_applications', buildWanted ? otherThanBuild : null);
  const perPath = useEachRead('find_applications', buildWanted && buildKeys.length ? buildKeys.map(k => ({ ...otherThanBuild, buildPathKeys: [k.key] })) : null);
  const total = (state: ToolState<{ totalCount: number }>) => dataOf(state)?.totalCount ?? null;
  const all = total(aiAll);
  const yes = total(aiYes);
  const no = total(aiNo);
  const usesAi = all !== null && yes !== null && no !== null ? { yes, no, unknown: Math.max(0, all - yes - no) } : null;
  const buildTotal = total(buildAll);
  const pathCounts = perPath.map(total);
  const buildPaths = buildTotal !== null && pathCounts.every(c => c !== null)
    ? [
      ...buildKeys.map((k, i) => ({ key: k.key, label: k.label, count: pathCounts[i]! })),
      { key: BUILD_NOT_STATED, label: 'Not stated', count: Math.max(0, buildTotal - pathCounts.reduce<number>((n, c) => n + (c ?? 0), 0)) },
    ]
    : null;
  return {
    loading: (aiWanted && !usesAi) || (buildWanted && !buildPaths),
    usesAi,
    buildPaths,
  };
}

function selected(filter: ApplicationFilter, field: keyof ApplicationFilter): string[] {
  const v = filter[field];
  return Array.isArray(v) ? v : [];
}

function activeCount(filter: ApplicationFilter, page: PageFilters): number {
  return FACETS.reduce((n, f) => n + selected(filter, f.field).length, 0)
    + (filter.text ? 1 : 0)
    + (filter.ownerObjectId ? 1 : 0)
    + (filter.registrationMeanings?.length ?? 0)
    + (page.ai || filter.usesAi !== undefined ? 1 : 0)
    + (page.build ? 1 : 0)
    + (filter.buildPathKeys?.length ?? 0);
}

const sameId = (a: string | null | undefined, b: string | null | undefined) => !!a && !!b && a.toLowerCase() === b.toLowerCase();

// The name of the person an owner filter names: the one picked on this page, the signed-in person, or the holder name on
// their owner assignments (a link from the Owners page carries only the ID).
function useOwnerName(objectId: string | undefined, picked: PrincipalRef | null, me: string | null): string | null {
  const known = !objectId ? null : picked && sameId(picked.objectId, objectId) ? picked.displayLabel : null;
  const lookup = useTool('list_owner_assignments', objectId && !known && !sameId(objectId, me) ? { principalObjectId: objectId, state: 'all', top: 1 } : null);
  if (!objectId) return null;
  if (known) return known;
  if (sameId(objectId, me)) return 'You';
  const found = dataOf(lookup)?.items[0]?.principal.displayLabel;
  if (found) return found;
  return lookup.status === 'loading' ? 'Reading the name' : 'A person who holds no owner role';
}

// The uses AI and build path facts of the rows that match the other filters, read from each row's card. Off until
// enabled; then the first SCAN_LIMIT rows (one read per portfolio when more than one is chosen, up to four).
interface CardScan {
  enabled: boolean;
  loading: boolean;
  failure: ToolState<unknown> | null;
  rows: ApplicationSummary[];
  facts: Map<string, CardFacts>;
  cardsRead: number;
  cardFailures: number;
  // True when more rows match than the page read cards for.
  more: boolean;
}

function useCardScan(filter: ApplicationFilter, enabled: boolean): CardScan {
  const ids = filter.portfolioIds ?? [];
  const multi = ids.length > 1;
  const at = (i: number) => (enabled && multi && ids[i] ? { ...toolInput({ ...filter, portfolioIds: [ids[i]] }), top: SCAN_LIMIT } : null);
  const single = useTool('find_applications', enabled && !multi ? { ...toolInput(filter), top: SCAN_LIMIT } : null);
  const p0 = useTool('find_applications', at(0));
  const p1 = useTool('find_applications', at(1));
  const p2 = useTool('find_applications', at(2));
  const p3 = useTool('find_applications', at(3));
  const reads = multi ? [p0, p1, p2, p3].slice(0, Math.min(ids.length, 4)) : [single];
  const listLoading = enabled && reads.some(r => r.status === 'loading');
  const failure = reads.find(r => r.status === 'done' && !r.envelope.ok) ?? null;
  const seen = new Map<string, ApplicationSummary>();
  for (const r of reads) for (const a of dataOf(r)?.items ?? []) if (!seen.has(a.businessApplicationId.toLowerCase())) seen.set(a.businessApplicationId.toLowerCase(), a);
  const rows = [...seen.values()].slice(0, SCAN_LIMIT);
  // More rows match than were read: a read stopped at its page, or the merged reads hold more than the limit.
  const more = seen.size > rows.length || reads.some(r => !!dataOf(r)?.skipToken);
  const cards = useEachRead('get_application_card', enabled && !listLoading && rows.length ? rows.map(r => ({ businessApplicationId: r.businessApplicationId, sections: ['links'] })) : null);
  const facts = new Map<string, CardFacts>();
  rows.forEach((r, i) => {
    const card = dataOf<ApplicationCard>(cards[i] ?? { status: 'loading' });
    if (card) facts.set(r.businessApplicationId.toLowerCase(), { usesAi: card.application.usesAi, buildPath: card.application.buildPath, registration: card.application.status.meaning });
  });
  return {
    enabled,
    loading: listLoading || cards.some(c => c.status === 'loading'),
    failure,
    rows,
    facts,
    cardsRead: facts.size,
    cardFailures: cards.filter(c => c.status === 'done' && !c.envelope.ok).length,
    more,
  };
}

// The applications register: search by name or purpose, filter, sort. The search and the filters live in the address,
// so a filtered list can be shared and the browser's back button works.
export function ApplicationsList({ filter }: { filter: ApplicationFilter }) {
  const session = useSession();
  const base = normalizeBase(import.meta.env.BASE_URL);
  const pageAi = useQueryParam('ai', UNKNOWN_PARAM) as 'unknown' | undefined;
  const pageBuild = useQueryParam('build', NOT_STATED_PARAM);
  const ai: UsesAiFilter | undefined = filter.usesAi === true ? 'yes' : filter.usesAi === false ? 'no' : pageAi;
  const build = filter.buildPathKeys?.[0] ?? pageBuild;
  const page: PageFilters = { ai: pageAi, build: pageBuild };
  // The route writes ai=yes|no and build=<key>; the page adds ai=unknown and build=not-stated.
  const go = (next: ApplicationFilter, nextPage: PageFilters = page) => {
    const href = appHref({ page: 'applications', filter: next }, base);
    const extra: Record<string, string> = {};
    if (nextPage.ai === 'unknown' && next.usesAi === undefined) extra.ai = 'unknown';
    if (nextPage.build === BUILD_NOT_STATED && !next.buildPathKeys?.length) extra.build = BUILD_NOT_STATED;
    navigate(Object.keys(extra).length ? withParams(href, extra) : href, true);
  };
  const chooseAi = (v: string) => {
    if (v === 'yes' || v === 'no') go({ ...filter, usesAi: v === 'yes' }, { ...page, ai: undefined });
    else go({ ...filter, usesAi: undefined }, { ...page, ai: v === 'unknown' ? 'unknown' : undefined });
  };
  const chooseBuild = (v: string) => {
    if (v === 'any') go({ ...filter, buildPathKeys: undefined }, { ...page, build: undefined });
    else if (v === BUILD_NOT_STATED) go({ ...filter, buildPathKeys: undefined }, { ...page, build: BUILD_NOT_STATED });
    else go({ ...filter, buildPathKeys: [v] }, { ...page, build: undefined });
  };

  // The search box keeps its own text while the person types; the address follows a moment later.
  const [text, setText] = useState(filter.text ?? '');
  useEffect(() => {
    const t = text.trim();
    if (t === (filter.text ?? '')) return;
    const timer = window.setTimeout(() => go({ ...filter, text: t || undefined }), 300);
    return () => window.clearTimeout(timer);
  }, [text]);
  // The box follows the address when it changes from elsewhere (Back, Clear all, a chip).
  useEffect(() => { setText(filter.text ?? ''); }, [filter.text]);

  const [sort, setSort] = useState<SortKey>('name');
  const [ownerOpen, setOwnerOpen] = useState(false);
  const [pickedOwner, setPickedOwner] = useState<PrincipalRef | null>(null);
  const [aiWanted, setAiWanted] = useState(false);
  const [buildWanted, setBuildWanted] = useState(false);
  const [saved, setSaved] = useState<SavedDraft | null>(null);
  const stages = useLabels('lifecycle-stage');
  const tiers = useLabels('tier');
  const buildPaths = useListValues('build-path');
  const facetCounts = {
    stage: useTool('get_portfolio_summary', { groupBy: 'stage' }),
    tier: useTool('get_portfolio_summary', { groupBy: 'tier' }),
    kind: useTool('get_portfolio_summary', { groupBy: 'kind' }),
    portfolio: useTool('get_portfolio_summary', { groupBy: 'portfolio' }),
  };
  const options = (facet: Facet) => {
    const buckets = (dataOf(facetCounts[facet])?.buckets ?? []).filter(b => b.key !== null) as { key: string; label: string; count: number }[];
    if (facet === 'stage') return [...buckets].sort((a, b) => stages.rank(a.key) - stages.rank(b.key));
    if (facet === 'tier') return [...buckets].sort((a, b) => tiers.rank(a.key) - tiers.rank(b.key));
    return [...buckets].sort((a, b) => a.label.localeCompare(b.label));
  };
  const labelOf = (facet: Facet, key: string) => options(facet).find(o => o.key === key)?.label ?? humanizeKey(key);

  // One count per registration meaning, under the other filters: the rows each choice gives.
  const regBase = { ...toolInput({ ...filter, registrationMeanings: undefined }), top: 1 };
  const regCounts: Record<RegistrationMeaning, ToolState<{ totalCount: number }>> = {
    draft: useTool('find_applications', { ...regBase, registrationMeanings: ['draft'] }),
    registered: useTool('find_applications', { ...regBase, registrationMeanings: ['registered'] }),
    archived: useTool('find_applications', { ...regBase, registrationMeanings: ['archived'] }),
  };
  const regChosen = filter.registrationMeanings ?? [];
  const shownReg = shownRegistrations(regChosen);
  // In a list that mixes registration states, the drafts and the archived rows are marked: one read of each.
  const markRead = (m: RegistrationMeaning) => (shownReg.length > 1 && shownReg.includes(m) ? { ...toolInput({ ...filter, registrationMeanings: [m] }), top: 100 } : null);
  const draftMarks = useTool('find_applications', markRead('draft'));
  const archivedMarks = useTool('find_applications', markRead('archived'));
  // The card scan runs only for "Not stated": the tool filters on every stated answer.
  const pageFiltered = !!pageAi || !!pageBuild;
  const scan = useCardScan(filter, pageFiltered);
  const toolCounts = useToolCounts(filter, buildPaths.options, (aiWanted || !!ai) && !pageBuild, (buildWanted || !!build) && !pageAi);
  const markOf = (id: string): RegistrationMeaning | null => {
    const fromCard = scan.facts.get(id.toLowerCase())?.registration;
    if (fromCard === 'draft' || fromCard === 'archived') return fromCard;
    if (fromCard === 'registered') return null;
    if (shownReg.length === 1) return shownReg[0] === 'registered' ? null : shownReg[0];
    if (dataOf(draftMarks)?.items.some(a => sameId(a.businessApplicationId, id))) return 'draft';
    if (dataOf(archivedMarks)?.items.some(a => sameId(a.businessApplicationId, id))) return 'archived';
    return null;
  };

  const ownerName = useOwnerName(filter.ownerObjectId, pickedOwner, session.objectId);
  const toggle = (field: keyof ApplicationFilter, key: string) => {
    const now = selected(filter, field);
    const next = now.includes(key) ? now.filter(k => k !== key) : [...now, key];
    go({ ...filter, [field]: next.length ? next : undefined });
  };
  const pickOwner = (person: PrincipalRef | null) => {
    if (!person) return;
    setPickedOwner(person);
    setOwnerOpen(false);
    go({ ...filter, ownerObjectId: person.objectId.toLowerCase() });
  };
  const clearAll = () => { setText(''); setOwnerOpen(false); go({}, {}); };

  const multiPortfolio = (filter.portfolioIds?.length ?? 0) > 1;
  const first = useTool('find_applications', multiPortfolio || pageFiltered ? null : toolInput(filter));
  const firstData = dataOf(first);
  const [skipTokens, setSkipTokens] = useState<string[]>([]);
  const inputKey = JSON.stringify(toolInput(filter));
  useEffect(() => { setSkipTokens([]); }, [inputKey]);

  // The uses AI counts follow the build path filter, and the build path counts follow the uses AI filter: the tool's
  // totals, or, while "Not stated" is chosen in the other menu, the cards the scan read.
  const allFacts = [...scan.facts.values()];
  const scanAi = scanCounts(allFacts.filter(f => keepRow(f, undefined, build) === true));
  const scanBuild = scanCounts(allFacts.filter(f => keepRow(f, ai, undefined) === true));
  const aiCounts = pageBuild ? (scan.loading ? null : scanAi.usesAi) : toolCounts.usesAi;
  const buildCounts = pageAi ? (scan.loading ? null : scanBuild.buildPaths) : toolCounts.buildPaths;
  const scanNote = !scan.enabled ? null
    : scan.loading ? `Reading ${scan.cardsRead} of ${scan.rows.length || 'the'} applications to count.`
      : `Counted over ${plural(scan.cardsRead, 'application', 'applications')} that match the other filters${scan.more ? `, the first ${SCAN_LIMIT}` : ''}.`;

  const count = activeCount(filter, page);
  const canRegister = session.can('save_application_draft');
  const canRequest = session.can('save_intake_draft');
  const buildLabel = (key: string) => (key === BUILD_NOT_STATED ? 'Not stated' : buildPaths.label(key));

  return (
    <>
      <PageHeader
        eyebrow="Register"
        title="Applications"
        subtitle="Every registered application you can see. Search by name or purpose, filter, and open an application for its 360 and its dependency map."
        meta={<DataStamp envelope={first.status === 'done' ? first.envelope : undefined} />}
        actions={(
          <>
            {canRegister && (
              <RegisterBuiltAppSheet
                trigger={<Button variant="outline"><FolderInput aria-hidden />Register an app already built</Button>}
                onSaved={setSaved}
              />
            )}
            {canRequest && (
              <Button asChild>
                <a href={hrefTo({ page: 'intake-new' })}><FilePlus2 aria-hidden />New request</a>
              </Button>
            )}
          </>
        )}
      />

      {saved && <SavedDraftNotice key={saved.applicationId} saved={saved} onDismiss={() => setSaved(null)} />}

      <section aria-label="Search and filters" className="mb-4 flex flex-col gap-3 rounded-xl border bg-card p-4 shadow-xs narrow:p-3">
        <div className="flex flex-wrap items-center gap-2">
          <div className="relative min-w-[260px] flex-1 narrow:min-w-0 narrow:basis-full">
            <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-navy-3" aria-hidden />
            <Input
              type="search"
              value={text}
              onChange={e => setText(e.target.value)}
              placeholder="Search by name or purpose"
              aria-label="Search applications by name or purpose"
              maxLength={200}
              className="pl-8"
            />
          </div>
          {FACETS.map(f => {
            const chosen = selected(filter, f.field);
            const opts = options(f.facet);
            return (
              <DropdownMenu key={f.facet}>
                <FilterTrigger label={f.label} chosen={chosen.length} />
                <DropdownMenuContent align="start" className="max-h-[360px] min-w-[240px] overflow-y-auto">
                  <DropdownMenuLabel>{f.label}</DropdownMenuLabel>
                  <DropdownMenuSeparator />
                  {opts.length === 0 && <div className="px-2 py-1.5 text-sm text-navy-3">Loading the choices.</div>}
                  {opts.map(o => (
                    <DropdownMenuCheckboxItem
                      key={o.key}
                      checked={chosen.includes(o.key)}
                      onCheckedChange={() => toggle(f.field, o.key)}
                      onSelect={e => e.preventDefault()}
                    >
                      <span className="flex-1">{o.label}</span>
                      <span className="ml-3 text-xs text-navy-3 tabular-nums">{o.count}</span>
                    </DropdownMenuCheckboxItem>
                  ))}
                </DropdownMenuContent>
              </DropdownMenu>
            );
          })}
          <DropdownMenu>
            <FilterTrigger label="Registration" chosen={regChosen.length} />
            <DropdownMenuContent align="start" className="min-w-[260px]">
              <DropdownMenuLabel>Registration</DropdownMenuLabel>
              <DropdownMenuSeparator />
              {REGISTRATION_MEANINGS.map(m => {
                const c = dataOf(regCounts[m])?.totalCount;
                return (
                  <DropdownMenuCheckboxItem
                    key={m}
                    checked={regChosen.includes(m)}
                    onCheckedChange={() => toggle('registrationMeanings', m)}
                    onSelect={e => e.preventDefault()}
                  >
                    <span className="flex flex-1 flex-col">
                      <span>{REGISTRATION_TEXT[m].label}</span>
                      <span className="text-xs text-navy-3">{REGISTRATION_TEXT[m].help}</span>
                    </span>
                    <span className="ml-3 self-start text-xs text-navy-3 tabular-nums">{c ?? '...'}</span>
                  </DropdownMenuCheckboxItem>
                );
              })}
              <div className="px-2 pt-1 pb-1.5 text-xs text-navy-3">With none chosen, the list shows registered applications.</div>
            </DropdownMenuContent>
          </DropdownMenu>
          <Button
            variant="outline"
            className={cn('gap-1.5', filter.ownerObjectId && 'border-primary text-primary')}
            aria-expanded={ownerOpen}
            aria-controls={ownerOpen ? 'register-owner-filter' : undefined}
            onClick={() => setOwnerOpen(o => !o)}
          >
            <UserRound aria-hidden />
            Owner
            {filter.ownerObjectId && <span className="rounded-full bg-primary px-1.5 text-[11px] leading-4 text-primary-foreground">1</span>}
            <ChevronDown className={cn('size-3.5 opacity-70 transition-transform', ownerOpen && 'rotate-180')} aria-hidden />
          </Button>
          <DropdownMenu onOpenChange={o => { if (o) setAiWanted(true); }}>
            <FilterTrigger label="Uses AI" chosen={ai ? 1 : 0} />
            <DropdownMenuContent align="start" className="min-w-[260px]">
              <DropdownMenuLabel>Uses AI</DropdownMenuLabel>
              <DropdownMenuSeparator />
              <DropdownMenuRadioGroup value={ai ?? 'any'} onValueChange={chooseAi}>
                <DropdownMenuRadioItem value="any">Any</DropdownMenuRadioItem>
                {USES_AI_FILTERS.map(v => (
                  <DropdownMenuRadioItem key={v} value={v}>
                    <span className="flex-1">{USES_AI_TEXT[v]}</span>
                    <span className="ml-3 text-xs text-navy-3 tabular-nums">{aiCounts ? aiCounts[v] : '...'}</span>
                  </DropdownMenuRadioItem>
                ))}
              </DropdownMenuRadioGroup>
              {pageBuild && scanNote && <div className="max-w-[260px] px-2 pt-1 pb-1.5 text-xs text-navy-3">{scanNote}</div>}
            </DropdownMenuContent>
          </DropdownMenu>
          <DropdownMenu onOpenChange={o => { if (o) setBuildWanted(true); }}>
            <FilterTrigger label="Build path" chosen={build ? 1 : 0} />
            <DropdownMenuContent align="start" className="max-h-[360px] min-w-[260px] overflow-y-auto">
              <DropdownMenuLabel>Build path</DropdownMenuLabel>
              <DropdownMenuSeparator />
              <DropdownMenuRadioGroup value={build ?? 'any'} onValueChange={chooseBuild}>
                <DropdownMenuRadioItem value="any">Any</DropdownMenuRadioItem>
                {buildChoices(buildPaths.options, buildCounts, build).map(o => (
                  <DropdownMenuRadioItem key={o.key} value={o.key}>
                    <span className="flex-1">{o.label}</span>
                    <span className="ml-3 text-xs text-navy-3 tabular-nums">{o.count ?? '...'}</span>
                  </DropdownMenuRadioItem>
                ))}
              </DropdownMenuRadioGroup>
              {pageAi && scanNote && <div className="max-w-[260px] px-2 pt-1 pb-1.5 text-xs text-navy-3">{scanNote}</div>}
            </DropdownMenuContent>
          </DropdownMenu>
          <div className="ml-auto flex items-center gap-2 narrow:ml-0">
            <span className="text-xs text-navy-3" id="sort-label">Sort</span>
            <Select value={sort} onValueChange={v => setSort(v as SortKey)}>
              <SelectTrigger className="w-[170px]" aria-labelledby="sort-label">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {SORTS.map(s => <SelectItem key={s.key} value={s.key}>{s.label}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
        </div>
        {ownerOpen && (
          <div id="register-owner-filter" className="flex flex-col gap-2 rounded-md border border-line bg-background p-3">
            <div className="max-w-[480px]">
              <PersonSearchField
                label="Owner"
                name="ownerObjectId"
                value={null}
                onChange={pickOwner}
                hint="Applications where this person holds an owner role, as holder or deputy. Type two or more letters of the name."
              />
            </div>
            <div className="flex flex-wrap items-center gap-2">
              {session.objectId && !sameId(filter.ownerObjectId, session.objectId) && (
                <Button size="sm" variant="outline" onClick={() => { setOwnerOpen(false); go({ ...filter, ownerObjectId: session.objectId! }); }}>Applications I own</Button>
              )}
              <Button size="sm" variant="ghost" onClick={() => setOwnerOpen(false)}>Close</Button>
            </div>
          </div>
        )}
        {count > 0 && (
          <div className="flex flex-wrap items-center gap-1.5" aria-label="Active filters">
            {filter.text && (
              <Chip label={`Text: ${filter.text}`} onRemove={() => { setText(''); go({ ...filter, text: undefined }); }} />
            )}
            {FACETS.flatMap(f => selected(filter, f.field).map(k => (
              <Chip key={`${f.facet}-${k}`} label={`${f.label}: ${labelOf(f.facet, k)}`} onRemove={() => toggle(f.field, k)} />
            )))}
            {regChosen.map(m => (
              <Chip key={`registration-${m}`} label={`Registration: ${REGISTRATION_TEXT[m].label}`} onRemove={() => toggle('registrationMeanings', m)} />
            ))}
            {filter.ownerObjectId && (
              <Chip label={`Owner: ${ownerName ?? 'A person'}`} onRemove={() => go({ ...filter, ownerObjectId: undefined })} />
            )}
            {ai && <Chip label={ai === 'unknown' ? 'Uses AI: not stated' : USES_AI_TEXT[ai]} onRemove={() => chooseAi('any')} />}
            {(filter.buildPathKeys ?? []).map(k => (
              <Chip key={`build-${k}`} label={`Build path: ${buildLabel(k)}`} onRemove={() => go({ ...filter, buildPathKeys: filter.buildPathKeys!.filter(x => x !== k).length ? filter.buildPathKeys!.filter(x => x !== k) : undefined })} />
            ))}
            {pageBuild && <Chip label="Build path: not stated" onRemove={() => chooseBuild('any')} />}
            <Button variant="link" size="sm" className="h-7 px-1" onClick={clearAll}>Clear all</Button>
          </div>
        )}
      </section>

      {pageFiltered ? (
        <ScanPanel scan={scan} ai={pageAi} build={pageBuild} sort={sort} stages={stages} tiers={tiers} markOf={markOf} buildLabel={buildLabel} onClear={clearAll} />
      ) : multiPortfolio ? (
        <PortfolioPages filter={filter} sort={sort} stages={stages} tiers={tiers} markOf={markOf} onClear={clearAll} />
      ) : (
        <Panel title="Applications" count={firstData?.totalCount} state={first} skeletonRows={8}>
          {data => (
            <>
              {data.items.length
                ? <ApplicationTable items={sortItems(data.items, sort, stages, tiers)} markOf={markOf} />
                : <NoMatch onClear={count ? clearAll : undefined} registrations={shownReg} />}
              {skipTokens.map((token, i) => (
                <MorePage key={token} input={toolInput(filter)} token={token} sort={sort} stages={stages} tiers={tiers} markOf={markOf} last={i === skipTokens.length - 1} onNext={next => setSkipTokens(t => (t.includes(next) ? t : [...t, next]))} />
              ))}
              {!skipTokens.length && data.skipToken && (
                <Button className="self-start" variant="outline" onClick={() => setSkipTokens([data.skipToken!])}>
                  Show more
                </Button>
              )}
              <span className="text-xs text-navy-3">
                {plural(data.totalCount, 'application matches', 'applications match')}
                {count ? ' these filters.' : ' in all.'}
                {data.skipToken ? ' Sorting applies to the rows shown.' : ''}
              </span>
            </>
          )}
        </Panel>
      )}
    </>
  );
}

// The button that opens a filter menu, with the number of choices made.
function FilterTrigger({ label, chosen }: { label: string; chosen: number }) {
  return (
    <DropdownMenuTrigger asChild>
      <Button variant="outline" className={cn('gap-1.5', chosen && 'border-primary text-primary')}>
        <ListFilter aria-hidden />
        {label}
        {chosen > 0 && <span className="rounded-full bg-primary px-1.5 text-[11px] leading-4 text-primary-foreground" aria-label={`${chosen} chosen`}>{chosen}</span>}
        <ChevronDown className="size-3.5 opacity-70" aria-hidden />
      </Button>
    </DropdownMenuTrigger>
  );
}

// The build path choices: the configured list in its order, with the counts seen, then any path seen that the list no
// longer holds, then "Not stated".
function buildChoices(configured: { key: string; label: string }[], seen: { key: string; label: string; count: number }[] | null, chosen: string | undefined): { key: string; label: string; count: number | null }[] {
  const countOf = (key: string) => (seen ? seen.find(s => s.key === key)?.count ?? 0 : null);
  const list: { key: string; label: string; count: number | null }[] = configured.map(o => ({ key: o.key, label: o.label, count: countOf(o.key) }));
  for (const s of seen ?? []) if (s.key !== BUILD_NOT_STATED && !list.some(o => o.key === s.key)) list.push(s);
  list.push({ key: BUILD_NOT_STATED, label: 'Not stated', count: countOf(BUILD_NOT_STATED) });
  if (chosen && !list.some(o => o.key === chosen)) list.push({ key: chosen, label: humanizeKey(chosen), count: 0 });
  return list;
}

function Chip({ label, onRemove }: { label: string; onRemove: () => void }) {
  return (
    <span className="inline-flex max-w-full items-center gap-1 rounded-md border border-info-line bg-info-soft py-0.5 pr-1 pl-2 text-xs font-semibold text-brand-dark">
      <span className="truncate">{label}</span>
      <button
        type="button"
        onClick={onRemove}
        aria-label={`Remove the filter ${label}`}
        className="inline-flex size-4 shrink-0 cursor-pointer items-center justify-center rounded-sm border-0 bg-transparent p-0 text-brand-dark hover:bg-card focus-visible:outline-2 focus-visible:outline-ring"
      >
        <X className="size-3" aria-hidden />
      </button>
    </span>
  );
}

function NoMatch({ onClear, registrations }: { onClear?: () => void; registrations?: RegistrationMeaning[] }) {
  const what = registrations && registrations.length === 1 && registrations[0] !== 'registered'
    ? `No ${REGISTRATION_TEXT[registrations[0]].label.toLowerCase()} application matches.`
    : 'No application matches.';
  return (
    <div className="flex flex-col items-start gap-2 py-2">
      <Empty>{what}</Empty>
      {onClear && <Button variant="outline" size="sm" onClick={onClear}>Clear the search and filters</Button>}
    </div>
  );
}

function sortItems(items: ApplicationSummary[], sort: SortKey, stages: Labels, tiers: Labels): ApplicationSummary[] {
  const byName = (a: ApplicationSummary, b: ApplicationSummary) => a.name.localeCompare(b.name);
  const list = [...items];
  switch (sort) {
    case 'stage':
      return list.sort((a, b) => stages.rank(a.stage?.key) - stages.rank(b.stage?.key) || byName(a, b));
    case 'tier':
      return list.sort((a, b) => tiers.rank(a.tier?.key) - tiers.rank(b.tier?.key) || byName(a, b));
    case 'kind':
      return list.sort((a, b) => a.kind.label.localeCompare(b.kind.label) || byName(a, b));
    default:
      return list.sort(byName);
  }
}

type MarkOf = (id: string) => RegistrationMeaning | null;

// The rows filtered by uses AI and build path, from the cards read.
function ScanPanel({ scan, ai, build, sort, stages, tiers, markOf, buildLabel, onClear }: {
  scan: CardScan;
  ai: UsesAiFilter | undefined;
  build: string | undefined;
  sort: SortKey;
  stages: Labels;
  tiers: Labels;
  markOf: MarkOf;
  buildLabel: (key: string) => string;
  onClear: () => void;
}) {
  const listFailure = scan.failure && scan.failure.status === 'done' && !scan.failure.envelope.ok ? scan.failure.envelope : null;
  const kept = scan.rows.filter(r => keepRow(scan.facts.get(r.businessApplicationId.toLowerCase()), ai, build) === true);
  const waiting = scan.rows.length > 0 && scan.cardsRead + scan.cardFailures < scan.rows.length;
  const factsOf = (id: string) => scan.facts.get(id.toLowerCase());
  return (
    <Panel title="Applications" count={scan.loading ? undefined : kept.length}>
      {listFailure ? <PanelFailure failure={listFailure} />
        : scan.rows.length === 0 && scan.loading ? (
          <div role="status" aria-label="Loading Applications" className="flex flex-col gap-2.5 pt-1">
            {Array.from({ length: 6 }, (_, i) => <Skeleton key={i} className="h-4 w-full" />)}
          </div>
        ) : (
          <>
            {waiting && (
              <p className="m-0 text-sm text-navy-2" role="status" aria-live="polite">
                {`Reading each application's facts: ${scan.cardsRead} of ${scan.rows.length}.`}
              </p>
            )}
            {kept.length
              ? <ApplicationTable items={sortItems(kept, sort, stages, tiers)} markOf={markOf} factsOf={factsOf} buildLabel={buildLabel} />
              : !waiting && <NoMatch onClear={onClear} />}
            {scan.cardFailures > 0 && <Muted>{`${plural(scan.cardFailures, 'application', 'applications')} could not be read, so ${scan.cardFailures === 1 ? 'it is' : 'they are'} left out.`}</Muted>}
            <span className="text-xs text-navy-3">
              {`Uses AI and build path are read from each application, over the ${plural(scan.rows.length, 'application', 'applications')} that match the other filters.`}
              {scan.more ? ` Only the first ${SCAN_LIMIT} were read: narrow the other filters to check the rest.` : ''}
            </span>
          </>
        )}
    </Panel>
  );
}

// More than one portfolio: one read per portfolio (the tool takes one), merged into one table.
function PortfolioPages({ filter, sort, stages, tiers, markOf, onClear }: { filter: ApplicationFilter; sort: SortKey; stages: Labels; tiers: Labels; markOf: MarkOf; onClear: () => void }) {
  const ids = filter.portfolioIds ?? [];
  const reads = [
    useTool('find_applications', ids[0] ? { ...toolInput({ ...filter, portfolioIds: [ids[0]] }), top: 100 } : null),
    useTool('find_applications', ids[1] ? { ...toolInput({ ...filter, portfolioIds: [ids[1]] }), top: 100 } : null),
    useTool('find_applications', ids[2] ? { ...toolInput({ ...filter, portfolioIds: [ids[2]] }), top: 100 } : null),
    useTool('find_applications', ids[3] ? { ...toolInput({ ...filter, portfolioIds: [ids[3]] }), top: 100 } : null),
  ].slice(0, Math.min(ids.length, 4));
  const loading = reads.some(r => r.status === 'loading');
  const seen = new Set<string>();
  const items = reads.flatMap(r => dataOf(r)?.items ?? []).filter(a => {
    const k = a.businessApplicationId.toLowerCase();
    if (seen.has(k)) return false;
    seen.add(k);
    return true;
  });
  const failed = reads.find(r => r.status === 'done' && !r.envelope.ok);
  return (
    <Panel title="Applications" count={loading ? undefined : items.length}>
      {loading ? (
        <div role="status" aria-label="Loading Applications" className="flex flex-col gap-2.5 pt-1">
          {Array.from({ length: 6 }, (_, i) => <Skeleton key={i} className="h-4 w-full" />)}
        </div>
      )
        : failed && failed.status === 'done' && !failed.envelope.ok ? <PanelFailure failure={failed.envelope} />
          : items.length ? <ApplicationTable items={sortItems(items, sort, stages, tiers)} markOf={markOf} /> : <NoMatch onClear={onClear} />}
      {ids.length > 4 && <span className="text-xs text-navy-3">The first four chosen portfolios are shown.</span>}
    </Panel>
  );
}

function MorePage({ input, token, sort, stages, tiers, markOf, last, onNext }: { input: Record<string, unknown>; token: string; sort: SortKey; stages: Labels; tiers: Labels; markOf: MarkOf; last: boolean; onNext: (token: string) => void }) {
  const state = useTool('find_applications', { ...input, skipToken: token });
  const data = dataOf(state);
  if (state.status === 'loading') return <Empty>Loading more.</Empty>;
  if (!state.envelope.ok) return <PanelFailure failure={state.envelope} />;
  return (
    <>
      {data && <ApplicationTable items={sortItems(data.items, sort, stages, tiers)} markOf={markOf} />}
      {last && data?.skipToken && (
        <Button className="self-start" variant="outline" onClick={() => onNext(data.skipToken!)}>
          Show more
        </Button>
      )}
    </>
  );
}

function OwnerCell({ owner, missing }: { owner: ApplicationSummary['businessOwner']; missing: string }) {
  if (owner) return <span className="text-sm text-navy">{owner.displayLabel}</span>;
  return (
    <span className="inline-flex items-center gap-1 text-xs font-semibold text-gold-strong">
      <TriangleAlert className="size-3.5 shrink-0" aria-hidden />
      {missing}
    </span>
  );
}

// The marks of one row: its registration state when it is not registered, the AI mark, and (from a card read) whether
// it uses AI and its build path.
function RowMarks({ app, mark, facts, buildLabel }: { app: ApplicationSummary; mark: RegistrationMeaning | null; facts?: CardFacts; buildLabel?: (key: string) => string }) {
  const ai = facts ? usesAiOf(facts.usesAi) : null;
  if (!mark && !facts && !app.isAiCreated && app.performedBy?.actorKind !== 'ai-client-for-person') return null;
  return (
    <span className="mt-1 flex flex-wrap items-center gap-1">
      {mark === 'draft' && <ToneBadge tone="warning" icon={null}>Draft, not registered yet</ToneBadge>}
      {mark === 'archived' && <ToneBadge tone="subtle" icon={null}>Archived</ToneBadge>}
      {ai && <ToneBadge tone={ai === 'yes' ? 'brand' : 'subtle'} icon={null}>{USES_AI_TEXT[ai]}</ToneBadge>}
      {facts && buildLabel && <ToneBadge tone="subtle" icon={null}>{`Build path: ${facts.buildPath ? facts.buildPath.label : buildLabel(BUILD_NOT_STATED)}`}</ToneBadge>}
      <AiMark isAiCreated={app.isAiCreated} performedBy={app.performedBy} />
    </span>
  );
}

function ApplicationTable({ items, markOf, factsOf, buildLabel }: { items: ApplicationSummary[]; markOf: MarkOf; factsOf?: (id: string) => CardFacts | undefined; buildLabel?: (key: string) => string }) {
  const host = useHost();
  const narrow = useNarrow();
  if (narrow) {
    return (
      <RowList label="Applications">
        {items.map(a => (
          <Row
            key={a.businessApplicationId}
            primary={<TextLink href={host.href({ page: 'app', id: a.businessApplicationId })}>{a.name}</TextLink>}
            secondary={[a.kind.label, a.tier?.label ?? 'No tier yet', a.stage?.label ?? 'No stage yet'].join('. ')}
            tertiary={a.businessOwner ? `Business owner ${a.businessOwner.displayLabel}` : <OwnerCell owner={null} missing="No business owner" />}
            badges={<RowMarks app={a} mark={markOf(a.businessApplicationId)} facts={factsOf?.(a.businessApplicationId)} buildLabel={buildLabel} />}
          />
        ))}
      </RowList>
    );
  }
  return (
    <Table aria-label="Applications">
      <TableHeader>
        <TableRow>
          <TableHead className="w-[34%]">Application</TableHead>
          <TableHead>Kind</TableHead>
          <TableHead>Tier</TableHead>
          <TableHead>Stage</TableHead>
          <TableHead>Business owner</TableHead>
          <TableHead>Technical owner</TableHead>
          <TableHead className="text-right"><span className="sr-only">Links</span></TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {items.map(a => (
          <TableRow key={a.businessApplicationId} className="align-top">
            <TableCell className="whitespace-normal">
              <TextLink href={host.href({ page: 'app', id: a.businessApplicationId })} className="text-sm">{a.name}</TextLink>
              <div className="mt-0.5 line-clamp-2 text-xs text-navy-3">{a.purpose}</div>
              <RowMarks app={a} mark={markOf(a.businessApplicationId)} facts={factsOf?.(a.businessApplicationId)} buildLabel={buildLabel} />
            </TableCell>
            <TableCell className="text-sm text-navy">{a.kind.label}</TableCell>
            <TableCell>{a.tier ? <Badge variant="subtle">{a.tier.label}</Badge> : <span className="text-xs text-navy-3">No tier yet</span>}</TableCell>
            <TableCell className="text-sm text-navy">{a.stage?.label ?? <span className="text-xs text-navy-3">No stage yet</span>}</TableCell>
            <TableCell><OwnerCell owner={a.businessOwner} missing="No holder" /></TableCell>
            <TableCell>{a.technicalOwner ? <span className="text-sm text-navy">{a.technicalOwner.displayLabel}</span> : <span className="text-xs text-navy-3">None</span>}</TableCell>
            <TableCell className="text-right">
              <TextLink className="text-xs" href={host.href({ page: 'map', id: a.businessApplicationId })} aria-label={`Dependency map of ${a.name}`}>Map</TextLink>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
