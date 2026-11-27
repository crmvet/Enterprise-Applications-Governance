// The Rules view of the Configuration page: every configuration entity with its rows in plain words, searchable, the
// data taxonomy (levels, classifications with their sensitivity label links, platforms), and the rules explained for
// any application. Filters live in the address (?area=, ?entity=, ?q=, ?retired=1), so Back keeps them.
// A configuration admin changes or retires a row, or adds one, through the draft builder (./sheets.tsx).
import { BookOpen, Lock, Search } from 'lucide-react';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { hrefTo, useQueryParam } from '../../app/links';
import { Empty, LinkButton, Muted, Panel, PanelFailure, TextLink, ToneBadge, useNarrow } from '../../components/blocks';
import { DataRuleChip } from '../../components/dataChips';
import { ExternalLink } from '../../components/rows';
import { Button } from '../../components/ui/button';
import { Checkbox } from '../../components/ui/checkbox';
import { Input } from '../../components/ui/input';
import { Label } from '../../components/ui/label';
import { Select, SelectContent, SelectGroup, SelectItem, SelectLabel, SelectTrigger, SelectValue } from '../../components/ui/select';
import type { ApplicationSummary, DataHandlingRule, ExplainAspect, GateLevel } from '../../data/contracts';
import { fmtDate, plural } from '../../data/format';
import { useRead } from '../../data/reads';
import { dataOf, useTool } from '../../data/toolClient';
import { plainEngineText } from '../../data/writes';
import { useSettled } from '../../components/people';
import { cn } from '../../lib/utils';
import { ApplicationSearchField } from '../app360/sheets';
import { entitySpec, FIELD_SPECS } from './fieldSpecs';
import {
  AREA_KEYS,
  AREAS,
  areaOf,
  childRowsOf,
  choiceText,
  entityName,
  GATE_LEVEL_TONE,
  gateLevelIn,
  rowFacts,
  rowMatches,
  rowSummary,
  sortRows,
  type AreaKey,
  type ConfigRow,
} from './logic';
import { useSetParams } from './parts';
import { useConfigRowsInForce, type ConfigRowsInForce } from './rows';
import type { DraftBuilderProps } from './sheets';
import { ImportDomainsSheet } from './importSheet';
import { useSession } from '../../data/session';

const KEY = /^[a-z][a-z0-9-]{0,63}$/;
const TEXT = /^[^\u0000-\u001f]{1,100}$/;
const ID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

// Opens the page's one draft builder sheet with these props.
export type OpenBuilder = (props: Omit<DraftBuilderProps, 'open' | 'onOpenChange'>) => void;

export function RulesView({ canPropose, openBuilder }: { canPropose: boolean; openBuilder: OpenBuilder }) {
  const config = useConfigRowsInForce();
  const setParams = useSetParams();
  const areaParam = useQueryParam('area', KEY);
  const entityParam = useQueryParam('entity', KEY);
  const q = useQueryParam('q', TEXT) ?? '';
  const retired = useQueryParam('retired', /^1$/) === '1';
  const entity = entityParam && (entitySpec(entityParam) || config.rows.some(r => r.entity === entityParam)) ? entityParam : undefined;
  const area: AreaKey | undefined = entity ? areaOf(entity) : areaParam && AREA_KEYS.includes(areaParam) ? (areaParam as AreaKey) : undefined;

  const content = useRef<HTMLDivElement>(null);
  // A move to another area or kind adds a history entry and brings its panel into view, with the keyboard focus on it;
  // the search and the retired rows toggle replace the address in place and leave the focus where it is.
  const go = (next: { area?: string; entity?: string; q?: string; retired?: boolean }) => {
    const moves = next.q === undefined && (next.area !== area || next.entity !== entity);
    setParams({ area: next.area, entity: next.entity, q: next.q, retired: next.retired ? '1' : undefined, app: undefined }, { push: moves });
    if (!moves) return;
    requestAnimationFrame(() => {
      const el = content.current;
      if (!el) return;
      if (el.getBoundingClientRect().top < 0) el.scrollIntoView({ block: 'start' });
      el.focus({ preventScroll: true });
    });
  };

  if (config.failure && !config.rows.length) return <Panel title="The rules in force"><PanelFailure failure={config.failure} /></Panel>;

  return (
    <div className="grid grid-cols-[264px_minmax(0,1fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3">
      <RulesNav config={config} area={area} entity={entity} q={q} retired={retired} go={go} />
      <div ref={content} tabIndex={-1} role="region" aria-label="The rules shown" className="flex min-w-0 flex-col gap-4 outline-none narrow:gap-3">
        {config.partial && <Muted>The product holds more configuration rows than this page reads (2,000). Search to find a row.</Muted>}
        {config.failure && config.rows.length > 0 && (
          <Muted>{`Some rules could not be read, so this page shows part of them: ${config.failure.error.message} Reference ${config.failure.correlationId}.`}</Muted>
        )}
        {q ? (
          <SearchResults config={config} q={q} retired={retired} go={go} canPropose={canPropose} openBuilder={openBuilder} />
        ) : entity ? (
          <EntityPanel config={config} entity={entity} retired={retired} canPropose={canPropose} openBuilder={openBuilder} onRetired={v => go({ entity, retired: v })} />
        ) : area === 'data' ? (
          <>
            <DataTaxonomy config={config} canPropose={canPropose} openBuilder={openBuilder} />
            <AreaPanel config={config} area="data" go={go} />
          </>
        ) : area ? (
          <AreaPanel config={config} area={area} go={go} />
        ) : (
          <Overview config={config} go={go} canPropose={canPropose} openBuilder={openBuilder} />
        )}
      </div>
    </div>
  );
}

// ---------- navigation: search, areas, entities ----------

function RulesNav(props: {
  config: ConfigRowsInForce;
  area: AreaKey | undefined;
  entity: string | undefined;
  q: string;
  retired: boolean;
  go: (next: { area?: string; entity?: string; q?: string; retired?: boolean }) => void;
}) {
  const { config, go } = props;
  const [text, setText] = useState(props.q);
  const settled = useSettled(text.trim(), 300);
  useEffect(() => { setText(props.q); }, [props.q]);
  useEffect(() => {
    if (settled === props.q) return;
    go(settled ? { q: settled, retired: props.retired } : { area: props.area, entity: props.entity, retired: props.retired });
    // go changes on every render; the address is the source of truth.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [settled]);
  const count = (e: string) => config.of(e, props.retired).length;
  return (
    <nav aria-label="Kinds of rules" className="flex flex-col gap-3 rounded-xl border bg-card px-3 py-3 shadow-xs medium:px-3.5">
      <div className="flex flex-col gap-1">
        <Label htmlFor="rules-search" className="text-xs text-navy-2">Search every rule</Label>
        <div className="relative">
          <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-navy-3" aria-hidden />
          <Input id="rules-search" type="search" value={text} onChange={e => setText(e.target.value)} placeholder="Such as go-live or payroll" maxLength={100} autoComplete="off" className="pl-8" />
        </div>
      </div>
      {/* Wide screens list every kind; below 1024 px a select picks one. */}
      <div className="hidden flex-col gap-1 medium:flex">
        <Label htmlFor="rules-kind" className="text-xs text-navy-2">Kind of rule</Label>
        <Select value={props.entity ?? (props.area ? `area:${props.area}` : 'overview')} onValueChange={v => (v === 'overview' ? go({}) : v.startsWith('area:') ? go({ area: v.slice(5) }) : go({ entity: v }))}>
          <SelectTrigger id="rules-kind" className="w-full"><SelectValue /></SelectTrigger>
          <SelectContent className="max-h-[360px]">
            <SelectItem value="overview">The rules in plain words</SelectItem>
            {AREAS.map(a => (
              <SelectGroup key={a.key}>
                <SelectLabel>{a.label}</SelectLabel>
                <SelectItem value={`area:${a.key}`}>{`All of ${a.label.toLowerCase()}`}</SelectItem>
                {a.entities.map(e => <SelectItem key={e} value={e}>{`${entityName(e)} (${count(e)})`}</SelectItem>)}
              </SelectGroup>
            ))}
          </SelectContent>
        </Select>
      </div>
      <ul className="m-0 flex list-none flex-col gap-2 p-0 medium:hidden">
        <li>
          <NavLink current={!props.area && !props.entity && !props.q} onClick={() => go({})}>The rules in plain words</NavLink>
        </li>
        {AREAS.map(a => (
          <li key={a.key} className="flex flex-col gap-0.5">
            <NavLink current={props.area === a.key && !props.entity && !props.q} onClick={() => go({ area: a.key })} strong>{a.label}</NavLink>
            <ul className="m-0 flex list-none flex-col p-0 pl-2">
              {a.entities.map(e => (
                <li key={e}>
                  <NavLink current={props.entity === e && !props.q} onClick={() => go({ entity: e, retired: props.retired })} count={config.loading ? null : count(e)}>{entityName(e)}</NavLink>
                </li>
              ))}
            </ul>
          </li>
        ))}
      </ul>
    </nav>
  );
}

function NavLink({ current, onClick, children, count, strong }: { current: boolean; onClick: () => void; children: ReactNode; count?: number | null; strong?: boolean }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-current={current ? 'page' : undefined}
      className={cn(
        'flex w-full cursor-pointer items-center justify-between gap-2 rounded-md border-0 bg-transparent px-2 py-1 text-left text-sm text-navy-2 hover:bg-background hover:text-navy focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring',
        strong && 'font-semibold text-navy',
        current && 'bg-secondary text-navy ring-1 ring-primary',
      )}
    >
      <span className="min-w-0 [overflow-wrap:anywhere]">{children}</span>
      {typeof count === 'number' && <span className="text-xs text-navy-3 tabular-nums" aria-label={`${count} rows`}>{count}</span>}
    </button>
  );
}

// ---------- one entity ----------

function EntityPanel(props: { config: ConfigRowsInForce; entity: string; retired: boolean; canPropose: boolean; openBuilder: OpenBuilder; onRetired: (v: boolean) => void }) {
  const { config, entity } = props;
  const spec = entitySpec(entity);
  const rows = sortRows(config.of(entity, props.retired));
  const retiredCount = config.of(entity, true).length - config.of(entity).length;
  const canAdd = props.canPropose && entity !== 'setting' && !!spec;
  const groups = spec?.parent ? groupByParent(rows, spec.parent, config) : [{ key: '', label: '', rows }];
  return (
    <Panel
      title={entityName(entity)}
      count={config.loading ? undefined : rows.length}
      subtitle={spec?.about}
      actions={canAdd ? (
        <Button size="sm" variant="outline" onClick={() => props.openBuilder({ title: `Add a ${entityName(entity).toLowerCase()}`, start: { entity, mode: 'add' } })}>
          Add a row
        </Button>
      ) : undefined}
    >
      {retiredCount > 0 && (
        <label className="flex cursor-pointer items-center gap-2 text-xs text-navy-2">
          <Checkbox checked={props.retired} onCheckedChange={v => props.onRetired(v === true)} />
          {`Show the ${plural(retiredCount, 'retired row', 'retired rows')} too`}
        </label>
      )}
      {config.loading && !rows.length ? <Muted>Reading the rules in force.</Muted> : !rows.length ? <Empty>No row of this kind is in force.</Empty> : (
        groups.map(g => (
          <div key={g.key} className="flex flex-col gap-1">
            {g.label && <h3 className="m-0 mt-1 text-sm font-semibold text-navy">{g.label}</h3>}
            <ul className="m-0 list-none p-0">
              {g.rows.map(r => <RuleRow key={`${r.entity}-${r.key}`} row={r} config={config} canPropose={props.canPropose} openBuilder={props.openBuilder} />)}
            </ul>
          </div>
        ))
      )}
    </Panel>
  );
}

function groupByParent(rows: ConfigRow[], parentEntity: string, config: ConfigRowsInForce): { key: string; label: string; rows: ConfigRow[] }[] {
  const groups = new Map<string, ConfigRow[]>();
  for (const r of rows) {
    const parent = r.key.split('/')[0];
    const list = groups.get(parent) ?? [];
    list.push(r);
    groups.set(parent, list);
  }
  return [...groups.entries()]
    .map(([key, list]) => ({ key, label: config.lookup(parentEntity, key) ?? choiceText(key), rows: list }))
    .sort((a, b) => a.label.localeCompare(b.label));
}

// The child rows a parent row holds, in one line: a list's values, a decision type's outcomes, a question's answers.
function childLine(row: ConfigRow, config: ConfigRowsInForce): string | null {
  const child = FIELD_SPECS.entities.find(e => e.parent === row.entity);
  if (!child) return null;
  const kids = childRowsOf(config.rows, child.entity, row.key);
  if (!kids.length) return null;
  const names = kids.slice(0, 8).map(k => k.label).join(', ');
  return `${child.name === 'List value' ? 'Values' : child.name === 'Decision outcome' ? 'Outcomes' : 'Answers'}: ${names}${kids.length > 8 ? `, and ${kids.length - 8} more` : ''}.`;
}

function RuleRow({ row, config, canPropose, openBuilder }: { row: ConfigRow; config: ConfigRowsInForce; canPropose: boolean; openBuilder: OpenBuilder }) {
  const locked = row.fields.isLocked === true;
  const summary = rowSummary(row.entity, row.fields, config.lookup);
  const kids = childLine(row, config);
  const facts = rowFacts(row.entity, row.fields, config.lookup);
  const name = entityName(row.entity).toLowerCase();
  const editable = canPropose && row.isActive && !locked && !!entitySpec(row.entity);
  return (
    <li className="flex flex-col gap-1 border-t border-line py-2.5 first:border-t-0 first:pt-0.5">
      <div className="flex flex-wrap items-start gap-x-3 gap-y-1.5">
        <div className="flex min-w-0 flex-1 flex-col gap-0.5">
          <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
            <span className="text-sm leading-5 font-semibold text-navy [overflow-wrap:anywhere]">{row.label}</span>
            {!row.isActive && <ToneBadge tone="subtle" icon={null}>Retired</ToneBadge>}
            {locked && <ToneBadge tone="subtle" icon={<Lock aria-hidden />}>Locked</ToneBadge>}
            {row.origin === 'local' && <ToneBadge tone="informative" icon={null}>Changed here</ToneBadge>}
          </span>
          {summary && <span className="text-xs leading-4 text-navy-2 [overflow-wrap:anywhere]">{summary}</span>}
          {kids && <span className="text-xs leading-4 text-navy-3 [overflow-wrap:anywhere]">{kids}</span>}
        </div>
        {editable && (
          <span className="flex flex-wrap gap-1">
            <Button size="xs" variant="outline" onClick={() => openBuilder({ title: `Change the ${name} "${row.label}"`, start: { entity: row.entity, mode: 'change', key: row.key } })} aria-label={`Change ${row.label}`}>
              Change
            </Button>
            {row.entity !== 'setting' && (
              <Button size="xs" variant="ghost" onClick={() => openBuilder({ title: `Retire the ${name} "${row.label}"`, operations: [{ op: 'retire', entity: row.entity, key: row.key }] })} aria-label={`Retire ${row.label}`}>
                Retire
              </Button>
            )}
          </span>
        )}
      </div>
      {facts.length > 0 && (
        <details className="group text-xs">
          <summary className="w-fit cursor-pointer rounded-sm text-xs font-medium text-primary hover:underline focus-visible:outline-2 focus-visible:outline-ring">Every fact of this row</summary>
          <dl className="m-0 mt-1.5 grid grid-cols-[minmax(0,200px)_minmax(0,1fr)] gap-x-3 gap-y-1 narrow:grid-cols-1">
            {facts.map(f => (
              <div key={f.name} className="contents">
                <dt className="font-semibold text-navy-2">{f.label}</dt>
                <dd className="m-0 text-navy [overflow-wrap:anywhere]">{f.text}</dd>
              </div>
            ))}
          </dl>
        </details>
      )}
    </li>
  );
}

// ---------- search ----------

function SearchResults(props: { config: ConfigRowsInForce; q: string; retired: boolean; go: (next: { area?: string; entity?: string; q?: string; retired?: boolean }) => void; canPropose: boolean; openBuilder: OpenBuilder }) {
  const { config, q } = props;
  const found = config.rows.filter(r => (props.retired || r.isActive) && rowMatches(r, q, config.lookup));
  const byEntity = new Map<string, ConfigRow[]>();
  for (const r of found) {
    const list = byEntity.get(r.entity) ?? [];
    list.push(r);
    byEntity.set(r.entity, list);
  }
  const order: string[] = AREAS.flatMap(a => [...a.entities]);
  const groups = [...byEntity.entries()].sort((a, b) => (order.indexOf(a[0]) + 1 || 999) - (order.indexOf(b[0]) + 1 || 999));
  return (
    <Panel title={`Rules that match "${q}"`} count={config.loading ? undefined : found.length} actions={<LinkButton className="text-sm" onClick={() => props.go({})}>Clear the search</LinkButton>}>
      {config.loading && !found.length ? <Muted>Reading the rules in force.</Muted> : !found.length ? <Empty>No rule matches. Try fewer or other words.</Empty> : (
        groups.map(([entity, rows]) => (
          <div key={entity} className="flex flex-col gap-1">
            <h3 className="m-0 mt-1 flex flex-wrap items-baseline gap-2 text-sm font-semibold text-navy">
              {entityName(entity)}
              <LinkButton className="text-xs font-normal" onClick={() => props.go({ entity })}>{`Open all ${plural(config.of(entity).length, 'row', 'rows')}`}</LinkButton>
            </h3>
            <ul className="m-0 list-none p-0">
              {sortRows(rows).slice(0, 20).map(r => <RuleRow key={`${r.entity}-${r.key}`} row={r} config={config} canPropose={props.canPropose} openBuilder={props.openBuilder} />)}
            </ul>
            {rows.length > 20 && <Muted>{`And ${rows.length - 20} more. Open the kind to see them all.`}</Muted>}
          </div>
        ))
      )}
    </Panel>
  );
}

// ---------- an area ----------

function AreaPanel({ config, area, go }: { config: ConfigRowsInForce; area: AreaKey; go: (next: { area?: string; entity?: string }) => void }) {
  const a = AREAS.find(x => x.key === area)!;
  return (
    <Panel title={area === 'data' ? 'Every kind of data taxonomy row' : a.label}>
      <ul className="m-0 grid list-none grid-cols-2 gap-2 p-0 narrow:grid-cols-1">
        {a.entities.map(e => (
          <li key={e}>
            <button
              type="button"
              onClick={() => go({ entity: e })}
              className="flex h-full w-full cursor-pointer flex-col gap-0.5 rounded-lg border border-line bg-card px-3 py-2 text-left hover:border-line-strong hover:bg-background focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring"
            >
              <span className="flex items-baseline justify-between gap-2 text-sm font-semibold text-navy">
                {entityName(e)}
                <span className="text-xs font-normal text-navy-3 tabular-nums">{config.loading ? '' : plural(config.of(e).length, 'row', 'rows')}</span>
              </span>
              <span className="line-clamp-2 text-xs text-navy-3">{entitySpec(e)?.about}</span>
            </button>
          </li>
        ))}
      </ul>
    </Panel>
  );
}

// ---------- the overview: the rules in plain words ----------

function Overview({ config, go, canPropose, openBuilder }: { config: ConfigRowsInForce; go: (next: { area?: string; entity?: string }) => void; canPropose: boolean; openBuilder: OpenBuilder }) {
  const narrow = useNarrow();
  const tiers = sortRows(config.of('tier'));
  const stages = sortRows(config.of('target-stage'));
  const locked = config.of('setting').filter(r => r.fields.isLocked === true);
  const count = (e: string) => config.of(e).length;
  return (
    <>
      <Panel title="The rules in force" subtitle="Each area opens its kinds of rules; each kind lists its rows in plain words.">
        <ul className="m-0 grid list-none grid-cols-2 gap-2 p-0 narrow:grid-cols-1">
          {AREAS.map(a => (
            <li key={a.key}>
              <button
                type="button"
                onClick={() => go({ area: a.key })}
                className="flex h-full w-full cursor-pointer flex-col gap-0.5 rounded-lg border border-line bg-card px-3 py-2 text-left hover:border-line-strong hover:bg-background focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring"
              >
                <span className="text-sm font-semibold text-navy">{a.label}</span>
                <span className="text-xs text-navy-3">
                  {config.loading ? 'Reading' : a.entities.filter(e => count(e) > 0).map(e => `${entityName(e)} (${count(e)})`).slice(0, 4).join(', ') || 'No rows in force'}
                </span>
              </button>
            </li>
          ))}
        </ul>
      </Panel>

      <Panel
        title="Gate levels for a normal change"
        subtitle="What the gate does when a normal change reaches each stage, by tier. A more specific gate policy row wins."
        actions={<LinkButton className="text-sm" onClick={() => go({ entity: 'gate-policy' })}>Every gate policy</LinkButton>}
      >
        {config.loading ? <Muted>Reading the rules in force.</Muted> : !tiers.length || !stages.length ? <Empty>No tiers or target stages are in force.</Empty> : narrow ? (
          <ul className="m-0 flex list-none flex-col gap-2 p-0">
            {tiers.map(t => (
              <li key={t.key} className="flex flex-col gap-1 border-t border-line pt-2 first:border-t-0 first:pt-0">
                <span className="text-sm font-semibold text-navy">{t.label}</span>
                <span className="flex flex-wrap gap-x-3 gap-y-1">
                  {stages.map(s => {
                    const level = gateLevelIn(config.rows, t.key, s.key);
                    return (
                      <span key={s.key} className="flex items-center gap-1 text-xs text-navy-2">
                        {`${s.label}:`}
                        {level ? <ToneBadge tone={GATE_LEVEL_TONE[level as GateLevel]} icon={null}>{choiceText(level)}</ToneBadge> : <Muted>No policy</Muted>}
                      </span>
                    );
                  })}
                </span>
              </li>
            ))}
          </ul>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[420px] border-collapse text-sm">
              <caption className="sr-only">Gate level by tier and target stage, for a normal change</caption>
              <thead>
                <tr>
                  <th scope="col" className="border-b border-line py-1.5 pr-3 text-left text-xs font-semibold text-navy-3">Tier</th>
                  {stages.map(s => <th key={s.key} scope="col" className="border-b border-line px-2 py-1.5 text-left text-xs font-semibold text-navy-3">{s.label}</th>)}
                </tr>
              </thead>
              <tbody>
                {tiers.map(t => (
                  <tr key={t.key}>
                    <th scope="row" className="border-b border-line py-1.5 pr-3 text-left font-semibold text-navy">{t.label}</th>
                    {stages.map(s => {
                      const level = gateLevelIn(config.rows, t.key, s.key);
                      return (
                        <td key={s.key} className="border-b border-line px-2 py-1.5">
                          {level ? <ToneBadge tone={GATE_LEVEL_TONE[level as GateLevel]} icon={null}>{choiceText(level)}</ToneBadge> : <Muted>No policy</Muted>}
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>

      <div className="grid grid-cols-2 items-start gap-4 medium:grid-cols-1 narrow:gap-3">
        <Panel title="Who decides and who approves" subtitle="The decision rights and approval rules in force.">
          <ul className="m-0 flex list-none flex-col gap-1.5 p-0 text-sm">
            {(['decision-type', 'decision-right', 'approval-rule', 'separation-rule'] as const).map(e => (
              <li key={e} className="flex items-baseline justify-between gap-2">
                <LinkButton onClick={() => go({ entity: e })}>{entityName(e)}</LinkButton>
                <span className="text-xs text-navy-3 tabular-nums">{config.loading ? '' : plural(count(e), 'row', 'rows')}</span>
              </li>
            ))}
          </ul>
        </Panel>
        <Panel title="Locked settings" count={config.loading ? undefined : locked.length} subtitle="No configuration change may alter these. Only a product release changes them.">
          {config.loading ? <Muted>Reading the rules in force.</Muted> : !locked.length ? <Empty>No setting is locked.</Empty> : (
            <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
              {locked.map(r => (
                <li key={r.key} className="flex flex-col gap-0.5">
                  <span className="flex items-center gap-1.5 text-sm font-semibold text-navy"><Lock className="size-3.5 text-navy-3" aria-hidden />{r.label}</span>
                  <span className="text-xs text-navy-2">{rowSummary('setting', r.fields, config.lookup)}</span>
                </li>
              ))}
            </ul>
          )}
        </Panel>
      </div>

      <ExplainPanel config={config} />
      {canPropose && (
        <Muted>
          To change a rule, open its kind and choose Change, or{' '}
          <LinkButton className="text-xs" onClick={() => openBuilder({ title: 'Propose a change to the rules' })}>start a draft</LinkButton>.
        </Muted>
      )}
    </>
  );
}

// ---------- the rules explained for one application ----------

const ASPECT_TEXT: Record<ExplainAspect, string> = {
  approvals: 'Who approves its decisions',
  gate: 'The gate for its changes',
  flags: 'What is flagged',
  requirements: 'What a gate needs from it',
  separation: 'Who may not approve',
  data: 'Its data',
};

function ExplainPanel({ config }: { config: ConfigRowsInForce }) {
  const setParams = useSetParams();
  const appId = useQueryParam('app', ID);
  const [chosen, setChosen] = useState<ApplicationSummary | null>(null);
  // A link opened with ?app= names the application by ID: its name comes from its card.
  const card = useTool('get_application_card', appId && (!chosen || chosen.businessApplicationId.toLowerCase() !== appId.toLowerCase()) ? { businessApplicationId: appId, sections: ['owners'] } : null);
  const cardName = dataOf(card)?.application.name;
  const explain = useRead('explain_rules', appId ? { subjectKind: 'business-application', subjectId: appId } : null);
  const shown: ApplicationSummary | null = chosen && appId && chosen.businessApplicationId.toLowerCase() === appId.toLowerCase()
    ? chosen
    : appId ? ({ businessApplicationId: appId, name: cardName ?? 'The application' } as ApplicationSummary) : null;
  const plain = (text: string) => plainEngineText(text, config.labelOfAnyKey);
  return (
    <Panel title="The rules for one application" subtitle="Pick an application to see which rule applies to it, and why: the engine explains each answer.">
      <div className="max-w-[480px]">
        <ApplicationSearchField
          label="Application"
          name="subjectId"
          value={shown}
          onChange={app => { setChosen(app); setParams({ app: app?.businessApplicationId }); }}
          hint="Type two or more letters of its name."
        />
      </div>
      {appId && (explain.status === 'loading' ? <Muted>The engine is explaining the rules.</Muted> : !explain.envelope.ok ? <PanelFailure failure={explain.envelope} /> : (
        <div className="flex flex-col gap-3">
          <p className="m-0 text-sm text-navy">{plain(explain.envelope.data.summary)}</p>
          <Muted>{`Explained under configuration version ${explain.envelope.data.configurationSetVersion}.`}</Muted>
          <ul className="m-0 grid list-none grid-cols-2 gap-3 p-0 medium:grid-cols-1">
            {explain.envelope.data.aspects.map(a => {
              const why = a.trace.filter(t => t.matched).map(t => plain(t.why));
              return (
                <li key={a.aspect} className="flex flex-col gap-1 rounded-lg border border-line px-3 py-2">
                  <span className="text-sm font-semibold text-navy">{ASPECT_TEXT[a.aspect]}</span>
                  <span className="text-sm text-navy-2">
                    {a.winner
                      ? <>{`${entityName(a.winner.entity)}: `}<span className="font-medium text-navy">{config.lookup(a.winner.entity, a.winner.key) ?? choiceText(a.winner.key.split('/').pop() ?? a.winner.key)}</span></>
                      : 'No rule applies.'}
                  </span>
                  {why.length > 0 && (
                    <ul className="m-0 flex list-disc flex-col gap-0.5 pl-4 text-xs text-navy-2">
                      {why.slice(0, 5).map((w, i) => <li key={i}>{w}</li>)}
                    </ul>
                  )}
                  {a.dropped.length > 0 && (
                    <details className="text-xs">
                      <summary className="w-fit cursor-pointer text-primary hover:underline">{`${plural(a.dropped.length, 'broader rule', 'broader rules')} did not apply`}</summary>
                      <ul className="m-0 mt-1 flex list-disc flex-col gap-0.5 pl-4 text-navy-2">
                        {a.dropped.map((d, i) => <li key={i}>{`${config.lookup(d.entity, d.key) ?? entityName(d.entity)}: ${plain(d.why)}`}</li>)}
                      </ul>
                    </details>
                  )}
                </li>
              );
            })}
          </ul>
        </div>
      ))}
    </Panel>
  );
}

// ---------- the data taxonomy ----------

const RULE_FIELDS: { field: string; name: 'use' | 'ai' | 'copy' }[] = [
  { field: 'defaultUseRule', name: 'use' },
  { field: 'defaultAiUseRule', name: 'ai' },
  { field: 'defaultCopyToTestRule', name: 'copy' },
];

function isRule(v: unknown): v is DataHandlingRule {
  return v === 'allowed' || v === 'decision' || v === 'masked-only' || v === 'never';
}

function DataTaxonomy({ config, canPropose, openBuilder }: { config: ConfigRowsInForce; canPropose: boolean; openBuilder: OpenBuilder }) {
  const levels = sortRows(config.of('data-domain-level'));
  const classes = sortRows(config.of('data-classification'));
  const platforms = sortRows(config.of('data-platform'));
  const labelLinks = config.of('vocabulary-mapping').filter(r => r.fields.sourceEntity === 'data-classification');
  const mode = config.of('setting').find(r => r.key === 'data-taxonomy-mode');
  const stale = config.of('setting').find(r => r.key === 'data-location-stale-days');
  const retention = childRowsOf(config.rows, 'list-value', 'retention-class');
  const domains = config.of('data-domain').length;
  const session = useSession();
  const canImport = session.isPerson && session.can('import_data_taxonomy');
  const add = (entity: string) => canPropose ? (
    <Button size="sm" variant="outline" onClick={() => openBuilder({ title: `Add a ${entityName(entity).toLowerCase()}`, start: { entity, mode: 'add' } })}>Add</Button>
  ) : undefined;
  const change = (row: ConfigRow) => canPropose && row.fields.isLocked !== true ? (
    <Button size="xs" variant="outline" onClick={() => openBuilder({ title: `Change the ${entityName(row.entity).toLowerCase()} "${row.label}"`, start: { entity: row.entity, mode: 'change', key: row.key } })} aria-label={`Change ${row.label}`}>Change</Button>
  ) : null;
  const groupLabel = (key: unknown) => (typeof key === 'string' ? config.lookup('list-value', `location-kind/${key}`) ?? choiceText(key) : 'Other');
  const platformGroups = new Map<string, ConfigRow[]>();
  for (const p of platforms) {
    const g = groupLabel(p.fields.locationKindKey);
    platformGroups.set(g, [...(platformGroups.get(g) ?? []), p]);
  }
  return (
    <>
      <Panel
        title="Data taxonomy"
        subtitle="How data is classified and where it may sit, for every install. The domain tree itself is the organization's own and lives on the Data page."
        actions={(
          <>
            <Button asChild size="sm" variant="outline"><a href={hrefTo({ page: 'data' })}>The domain tree</a></Button>
            {canImport && <ImportDomainsSheet />}
          </>
        )}
      >
        <dl className="m-0 grid grid-cols-3 gap-x-6 gap-y-3 medium:grid-cols-2 narrow:grid-cols-1">
          <div className="flex flex-col gap-0.5">
            <dt className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">Data taxonomy mode</dt>
            <dd className="m-0 text-sm text-navy">{mode ? choiceText(mode.fields.value) : 'Not set'}</dd>
          </div>
          <div className="flex flex-col gap-0.5">
            <dt className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">A location is stale after</dt>
            <dd className="m-0 text-sm text-navy">{stale && typeof stale.fields.value === 'number' ? plural(stale.fields.value, 'day', 'days') : 'Not set'}</dd>
          </div>
          <div className="flex flex-col gap-0.5">
            <dt className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">Domains in the tree</dt>
            <dd className="m-0 text-sm"><TextLink href={hrefTo({ page: 'data' })}>{plural(domains, 'domain', 'domains')}</TextLink></dd>
          </div>
        </dl>
      </Panel>

      <Panel title="Classifications" count={classes.length} subtitle="Higher rank means more sensitive. Each one sets the default handling rules a domain can make stricter, never looser." actions={add('data-classification')}>
        {!classes.length ? <Empty>No classification is in force.</Empty> : (
          <ul className="m-0 list-none p-0">
            {classes.map(c => {
              const links = labelLinks.filter(l => l.fields.sourceKey === c.key);
              const implied = typeof c.fields.impliedTierQuestionKey === 'string' && typeof c.fields.impliedAnswerKey === 'string'
                ? config.lookup('tier-answer-option', `${c.fields.impliedTierQuestionKey}/${c.fields.impliedAnswerKey}`)
                : null;
              return (
                <li key={c.key} className="flex flex-col gap-1 border-t border-line py-2.5 first:border-t-0 first:pt-0.5">
                  <span className="flex flex-wrap items-start justify-between gap-2">
                    <span className="flex flex-wrap items-center gap-2">
                      <span className="text-sm font-semibold text-navy">{c.label}</span>
                      {typeof c.fields.rank === 'number' && <span className="text-xs text-navy-3">{`Rank ${c.fields.rank}`}</span>}
                    </span>
                    {change(c)}
                  </span>
                  {typeof c.fields.description === 'string' && <span className="text-xs text-navy-2">{c.fields.description}</span>}
                  <span className="flex flex-wrap gap-1">
                    {RULE_FIELDS.map(({ field, name }) => isRule(c.fields[field])
                      ? <DataRuleChip key={field} name={name} rule={{ value: c.fields[field] as DataHandlingRule, setOn: null, fromClassification: { key: c.key, label: c.label } }} />
                      : null)}
                  </span>
                  <span className="flex flex-col gap-0.5 text-xs text-navy-2">
                    {links.length ? links.map(l => (
                      <span key={l.key} className="flex flex-wrap items-center gap-x-1.5">
                        <BookOpen className="size-3.5 text-navy-3" aria-hidden />
                        <span>{`Sensitivity label: ${typeof l.fields.term === 'string' ? l.fields.term : l.label}.`}</span>
                        {typeof l.fields.sourceUrl === 'string' && <ExternalLink href={l.fields.sourceUrl}>Where the label is defined</ExternalLink>}
                        {typeof l.fields.checkedOn === 'string' && <span className="text-navy-3">{`Checked ${fmtDate(l.fields.checkedOn)}.`}</span>}
                      </span>
                    )) : <span>No sensitivity label is linked.</span>}
                    {implied && <span>{`Implies the tier answer "${implied}".`}</span>}
                  </span>
                </li>
              );
            })}
          </ul>
        )}
        {canPropose && (
          <Muted>
            To link a classification to a sensitivity label, add a vocabulary mapping:{' '}
            <LinkButton className="text-xs" onClick={() => openBuilder({ title: 'Link a classification to a sensitivity label', start: { entity: 'vocabulary-mapping', mode: 'add' } })}>link a label</LinkButton>.
          </Muted>
        )}
      </Panel>

      <div className="grid grid-cols-2 items-start gap-4 medium:grid-cols-1 narrow:gap-3">
        <Panel title="Domain levels" count={levels.length} subtitle="The levels of the domain tree, top first." actions={add('data-domain-level')}>
          {!levels.length ? <Empty>No level is in force.</Empty> : (
            <ul className="m-0 list-none p-0">
              {levels.map(l => (
                <li key={l.key} className="flex flex-col gap-1 border-t border-line py-2 first:border-t-0 first:pt-0.5">
                  <span className="flex flex-wrap items-start justify-between gap-2">
                    <span className="text-sm font-semibold text-navy">{l.label}</span>
                    {change(l)}
                  </span>
                  {typeof l.fields.description === 'string' && <span className="text-xs text-navy-2">{l.fields.description}</span>}
                  <span className="flex flex-wrap gap-1">
                    <ToneBadge tone={l.fields.ownerRequired ? 'brand' : 'subtle'} icon={null}>{l.fields.ownerRequired ? 'Needs its own owner' : 'Inherits its owner'}</ToneBadge>
                    <ToneBadge tone={l.fields.declarable ? 'brand' : 'subtle'} icon={null}>{l.fields.declarable ? 'Data uses can name it' : 'Data uses name a level below'}</ToneBadge>
                  </span>
                </li>
              ))}
            </ul>
          )}
        </Panel>
        <Panel title="Retention classes" count={retention.length} subtitle="How long data is kept. A domain names one, or inherits it.">
          {!retention.length ? <Empty>No retention class is in force.</Empty> : (
            <ul className="m-0 flex list-disc flex-col gap-0.5 pl-5 text-sm text-navy">
              {retention.map(r => <li key={r.key}>{r.label}</li>)}
            </ul>
          )}
        </Panel>
      </div>

      <Panel title="Platforms" count={platforms.length} subtitle="The kinds of data store, from any vendor. A location is one of them; its data sits in containers." actions={add('data-platform')}>
        {!platforms.length ? <Empty>No platform is in force.</Empty> : (
          <div className="grid grid-cols-2 gap-x-6 gap-y-3 medium:grid-cols-1">
            {[...platformGroups.entries()].sort((a, b) => a[0].localeCompare(b[0])).map(([group, list]) => (
              <div key={group} className="flex flex-col gap-1">
                <h3 className="m-0 text-sm font-semibold text-navy">{group}</h3>
                <ul className="m-0 list-none p-0">
                  {list.map(p => (
                    <li key={p.key} className="grid grid-cols-[minmax(0,1fr)_auto] items-start gap-2 border-t border-line py-1.5 first:border-t-0">
                      <span className="flex min-w-0 flex-col">
                        <span className="text-sm text-navy">{p.label}</span>
                        <span className="text-xs text-navy-3">
                          {[
                            typeof p.fields.vendorKey === 'string' ? config.lookup('list-value', `data-vendor/${p.fields.vendorKey}`) ?? choiceText(p.fields.vendorKey) : null,
                            typeof p.fields.instanceLabel === 'string' ? `instance: ${p.fields.instanceLabel}` : null,
                            typeof p.fields.storeLabel === 'string' ? `store: ${p.fields.storeLabel}` : null,
                            typeof p.fields.containerLabel === 'string' ? `container: ${p.fields.containerLabel}` : null,
                          ].filter(Boolean).join('; ')}
                        </span>
                      </span>
                      {change(p)}
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
        )}
      </Panel>
    </>
  );
}

