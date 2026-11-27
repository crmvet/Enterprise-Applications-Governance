// Measures and slide-back signals (screen "Measures and slide-back signals", priority 3; playbook chapters 17 and 19;
// G24): is AI app development working? The nine measures over six equal windows that end today, and the ten slide-back
// signals with their thresholds, all counted from the records the product keeps. Every figure opens the rows behind it.
//   /app/measures                          six windows of 30 days
//   ?window=7|90                           six windows of 7 or 90 days instead
//   ?trend=better|worse|same|none          only the measures that moved that way since the window before (the figures)
//   ?measure=<key> or ?signal=<1..10>      the rows behind one measure or one signal, page by page (Back closes them)
//   ?target=met|missed|unset               only the measures that meet their target, fall short of it, or have none yet
// Reads: get_measures (the figures; once more with measureKey or signalNumber for the rows), get_configuration (the
// decision types, for the proposal sheet). Write: "Record a decision" on a signal opens the decision proposal sheet
// (save_decision_proposal, guide-log-docs/DecisionSheets.tsx) with the signal set (slideBackSignalKey).
// Wave 4 (B24-22): each measure shows its target from the
// measure-targets setting and whether the newest value meets it; each signal says where its threshold comes from
// (the signal-thresholds setting or the playbook's default) and lists the decisions recorded about it, newest first,
// each linking to its decision page. Choices (D-41): the target filter sits beside the trend filter, so each target
// count opens its measures; a signal's decisions show on its row and, in full, in its drill (?signal=), which is where
// the decision page's "Acts on" link lands.
// Chart form (dataviz method): one series per measure, so each trend is six thin columns with no legend, the newest
// window in the accent and the earlier ones in the muted bar color, the value printed on the newest column only, a
// tooltip on every column, and the same numbers in a table for screen readers.
import { ArrowDownRight, ArrowRight, ArrowUpRight, FilePlus2, Gavel, Siren, X } from 'lucide-react';
import { useEffect, type ReactElement } from 'react';
import { hrefTo, recordHref, useQueryParam } from '../../app/links';
import { DataStamp, Empty, EnvelopeNotes, KpiTile, LinkButton, Muted, PageHeader, Panel, PanelFailure, StatusBadge, SwitchTabs, TextLink, ToneBadge } from '../../components/blocks';
import { Row, RowList } from '../../components/rows';
import { Button } from '../../components/ui/button';
import { Tooltip, TooltipContent, TooltipTrigger } from '../../components/ui/tooltip';
import type { Measures } from '../../data/contracts';
import { fmtDate, fmtDayMonth, humanizeDates, plural } from '../../data/format';
import { useLabels } from '../../data/labels';
import { usePagedRead, useRead } from '../../data/reads';
import { useSession } from '../../data/session';
import { dataOf } from '../../data/toolClient';
import { cn } from '../../lib/utils';
import { MoreButton } from '../ai-audit/parts';
import { ProposeDecisionSheet } from '../guide-log-docs/DecisionSheets';
import { EXCEPTION_TYPE } from '../guide-log-docs/decisionLog';
import { kpiRowClass, scrollToPanel } from '../owners-reviews/people';
import {
  drillOf,
  HEALTHY_TEXT,
  lowerFirst,
  MEASURE_WINDOW_DAYS,
  MEASURE_WINDOWS,
  measureValueText,
  ratioText,
  SIGNAL_STATE_TEXT,
  signalState,
  slideBackKeyOf,
  TARGET_FILTER_TEXT,
  TARGET_FILTERS,
  TARGET_TEXT,
  targetCounts,
  targetFilterOf,
  targetsAndThresholdsText,
  targetState,
  thresholdSourceText,
  trendCall,
  windowDaysOf,
  withoutToolNames,
  type MeasureItem,
  type SignalItem,
  type TargetFilter,
  type TrendCall,
} from './logic';
import { GuidelineLink, Note, PageFailure, useParamNav } from './parts';

const KEY = /^[a-z][a-z0-9-]{0,63}$/;
const TREND = /^(better|worse|same|none)$/;
type TrendFilter = TrendCall | 'none';

const TREND_TEXT: Record<TrendFilter, string> = {
  better: 'Better than the window before',
  worse: 'Worse than the window before',
  same: 'Same as the window before',
  none: 'Nothing to compare yet',
};

function trendOf(m: MeasureItem): TrendFilter {
  return trendCall(m)?.call ?? 'none';
}

export function MeasuresPage() {
  const days = windowDaysOf(useQueryParam('window', /^(7|30|90)$/));
  const drill = drillOf(useQueryParam('measure', KEY), useQueryParam('signal', /^([1-9]|10)$/));
  const trend = useQueryParam('trend', TREND) as TrendFilter | undefined;
  const target = targetFilterOf(useQueryParam('target', /^(met|missed|unset)$/));
  const go = useParamNav();
  const propose = useProposeSignal();
  const state = useRead('get_measures', { windowDays: days, windows: MEASURE_WINDOWS });
  const data = dataOf(state);
  const envelope = state.status === 'done' ? state.envelope : undefined;
  const measures = data?.measures ?? [];
  const byTrend = (t: TrendFilter) => measures.filter(m => trendOf(m) === t).length;
  // The target counts follow the trend filter but never the target filter, so each count opens its measures.
  const byTarget = targetCounts(trend ? measures.filter(m => trendOf(m) === trend) : measures);
  const shown = measures.filter(m => (!trend || trendOf(m) === trend) && (!target || targetState(m) === target));
  const met = data ? targetCounts(measures).met : null;
  const withTarget = measures.filter(m => m.target !== null).length;
  const newest = data?.windows[data.windows.length - 1];
  const openMeasure = (key: string) => go({ measure: key, signal: undefined }, { push: true });
  const openSignal = (n: number) => go({ signal: String(n), measure: undefined }, { push: true });
  const drilledMeasure = drill && 'measureKey' in drill ? drill.measureKey : null;

  const header = (
    <PageHeader
      eyebrow="Govern"
      title="Measures"
      subtitle="Is AI app development working? Read the measures in pairs: lead time with deployments on record, standard changes with failed changes, AI work accepted with decisions on time."
      meta={(
        <span className="flex flex-col gap-0.5">
          {data && newest && <span>{`Six windows of ${data.windowDays} days, the newest ending ${fmtDate(newest.to)}. ${targetsAndThresholdsText(data.measures, data.signals)}`}</span>}
          <span><DataStamp envelope={envelope} /></span>
        </span>
      )}
      actions={(
        <SwitchTabs
          label="Window"
          ariaLabel="Window length"
          value={String(days)}
          onChange={v => go({ window: v === '30' ? undefined : v })}
          options={MEASURE_WINDOW_DAYS.map(d => ({ value: String(d), label: `${d} days` }))}
        />
      )}
    />
  );
  if (envelope && !envelope.ok) return <>{header}<PageFailure failure={envelope} /></>;

  return (
    <>
      {header}

      <>
        <div className={kpiRowClass} role="group" aria-label="Key figures">
          <KpiTile label="Signals firing" value={data?.firingCount ?? null} caption="Slide-back signals over their threshold now" attention={!!data?.firingCount} onClick={() => openMeasure('slide-back-signals')} />
          <KpiTile label="Measures improving" value={data ? byTrend('better') : null} caption="Better than the window before" onClick={() => go({ trend: trend === 'better' ? undefined : 'better' })} className={cn(trend === 'better' && 'ring-2 ring-primary')} />
          <KpiTile label="Measures slipping" value={data ? byTrend('worse') : null} caption="Worse than the window before" attention={!!(data && byTrend('worse'))} onClick={() => go({ trend: trend === 'worse' ? undefined : 'worse' })} className={cn(trend === 'worse' && 'ring-2 ring-primary')} />
          <KpiTile label="Nothing to compare" value={data ? byTrend('none') : null} caption="Read as they stand now, or no value yet" onClick={() => go({ trend: trend === 'none' ? undefined : 'none' })} className={cn(trend === 'none' && 'ring-2 ring-primary')} />
          <KpiTile
            label="Targets met"
            value={met}
            caption={data ? (withTarget ? `Of ${plural(withTarget, 'measure', 'measures')} with a target` : 'No target is set yet') : 'Measures with a target'}
            onClick={() => go({ target: target === 'met' ? undefined : 'met' })}
            className={cn(target === 'met' && 'ring-2 ring-primary')}
          />
        </div>
        {envelope && (envelope.couldNotRead.length > 0 || envelope.warnings.length > 0) && (
          <div className="mb-4"><EnvelopeNotes couldNotRead={envelope.couldNotRead} warnings={envelope.warnings} /></div>
        )}
      </>

      {drill && data && <DrillPanel key={JSON.stringify(drill)} drill={drill} data={data} days={days} onClose={() => go({ measure: undefined, signal: undefined })} onSignal={openSignal} propose={propose} />}

      {state.status === 'loading' && <Panel title="The measures" className="mb-4" state={state} skeletonRows={6}>{() => null}</Panel>}

      {data && (
        <section aria-labelledby="measures-title" className="mb-4 flex flex-col gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <h2 id="measures-title" className="m-0 text-base font-semibold text-navy">
              {trend && target ? `Measures: ${TREND_TEXT[trend].toLowerCase()}, ${TARGET_FILTER_TEXT[target].toLowerCase()}` : trend ? `Measures: ${TREND_TEXT[trend].toLowerCase()}` : target ? `Measures: ${TARGET_FILTER_TEXT[target].toLowerCase()}` : 'The measures'}
              <span className="ml-2 inline-block rounded-full bg-muted px-2 py-px align-[1px] text-xs leading-[18px] font-semibold text-navy-3">{shown.length}</span>
            </h2>
            {(trend || target) && <LinkButton className="text-xs" onClick={() => go({ trend: undefined, target: undefined })}>Show every measure</LinkButton>}
          </div>
          <SwitchTabs
            label="Target"
            ariaLabel="Measures by their target"
            value={target ?? 'all'}
            onChange={v => go({ target: v === 'all' ? undefined : v })}
            options={[
              { value: 'all', label: 'Every measure' },
              ...TARGET_FILTERS.map((f: TargetFilter) => ({ value: f, label: `${TARGET_FILTER_TEXT[f]} (${byTarget[f]})` })),
            ]}
          />
          {shown.length === 0 ? <Empty>{target && !trend ? 'No measure is in this state.' : 'No measure moved that way.'}</Empty> : (
            <div className="grid grid-cols-3 gap-4 medium:grid-cols-2 narrow:grid-cols-1 narrow:gap-3">
              {shown.map(m => <MeasureCard key={m.key} measure={m} windows={data.windows} onOpen={() => openMeasure(m.key)} selected={drilledMeasure === m.key} />)}
            </div>
          )}
        </section>
      )}

      <Panel title="Slide-back signals" id="signals" state={state} notes={[]} count={data?.signals.length} subtitle="The ten signs that governance is slipping back to tickets and meetings (playbook chapter 17), each with its threshold and who watches it.">
        {d => (
          <RowList label="Slide-back signals">
            {d.signals.map(s => <SignalRow key={s.number} signal={s} onOpen={() => openSignal(s.number)} propose={propose} />)}
          </RowList>
        )}
      </Panel>
    </>
  );
}

// ---------- one measure ----------

const CALL_BADGE: Record<TrendCall, { tone: 'success' | 'warning' | 'subtle'; icon: (up: boolean) => ReactElement }> = {
  better: { tone: 'success', icon: up => (up ? <ArrowUpRight aria-hidden /> : <ArrowDownRight aria-hidden />) },
  worse: { tone: 'warning', icon: up => (up ? <ArrowUpRight aria-hidden /> : <ArrowDownRight aria-hidden />) },
  same: { tone: 'subtle', icon: () => <ArrowRight aria-hidden /> },
};

function MeasureCard({ measure: m, windows, onOpen, selected }: { measure: MeasureItem; windows: Measures['windows']; onOpen: () => void; selected: boolean }) {
  const call = trendCall(m);
  const ratio = ratioText(m.numerator, m.denominator);
  const baselineWindow = m.baseline ? windows.find(w => w.index === m.baseline!.index) : null;
  const headingId = `measure-${m.key}`;
  return (
    <section
      aria-labelledby={headingId}
      className={cn('flex min-w-0 flex-col gap-2.5 rounded-xl border bg-card px-5 pt-4 pb-4 shadow-xs narrow:rounded-lg narrow:px-3.5 narrow:pt-3.5 narrow:pb-3', selected && 'ring-2 ring-primary')}
    >
      <div className="flex flex-col gap-0.5">
        <h3 id={headingId} className="m-0 text-base leading-6 font-semibold text-navy">{m.label}</h3>
        <span className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-navy-3">
          {m.watcher && <span>Watched by the {lowerFirst(m.watcher.label)}</span>}
          <GuidelineLink number={m.guideline} />
        </span>
      </div>
      <div className="flex flex-wrap items-baseline gap-x-2.5 gap-y-1">
        <span className="text-[28px] leading-9 font-semibold text-navy">{measureValueText(m.value, m.unit)}</span>
        {ratio && <span className="text-sm text-navy-2">{ratio}</span>}
      </div>
      <div className="flex flex-wrap gap-1.5">
        {call && (
          <ToneBadge tone={CALL_BADGE[call.call].tone} icon={CALL_BADGE[call.call].icon(call.to > call.from)}>
            {`${TREND_TEXT[call.call]} (${measureValueText(call.from, m.unit)})`}
          </ToneBadge>
        )}
        <ToneBadge tone="subtle" icon={null}>{HEALTHY_TEXT[m.healthyDirection]}</ToneBadge>
        {m.target !== null && <ToneBadge tone={TARGET_TEXT[targetState(m)].tone}>{TARGET_TEXT[targetState(m)].text}</ToneBadge>}
      </div>
      <TrendColumns measure={m} windows={windows} />
      <dl className="m-0 flex flex-col gap-1 text-xs text-navy-2">
        <div><dt className="inline font-semibold text-navy">Baseline: </dt><dd className="inline">{m.baseline && baselineWindow ? `${measureValueText(m.baseline.value, m.unit)}, in the window ending ${fmtDayMonth(baselineWindow.to)}` : 'none yet'}</dd></div>
        <div><dt className="inline font-semibold text-navy">Target: </dt><dd className="inline">{m.target !== null ? `${measureValueText(m.target, m.unit)}, set in your configuration` : 'not set. Measure first, then agree a target (playbook chapter 19).'}</dd></div>
        {m.secondary && (
          <div>
            <dt className="inline font-semibold text-navy">{m.secondary.label}: </dt>
            <dd className="inline">{measureValueText(m.secondary.value, m.secondary.unit)}{ratioText(m.secondary.numerator, m.secondary.denominator) ? ` (${ratioText(m.secondary.numerator, m.secondary.denominator)})` : ''}</dd>
          </div>
        )}
      </dl>
      <p className="m-0 text-xs leading-relaxed text-navy-3">
        {m.definition}
        {m.kind === 'coverage' && ' Read as the records stand today, so only the newest window holds a value.'}
      </p>
      <div className="mt-auto pt-1">
        {m.rowCount > 0
          ? <Button size="sm" variant="outline" onClick={onOpen} aria-label={`Open the ${m.rowCount} rows behind ${m.label}`}>{`Open the ${plural(m.rowCount, 'row', 'rows')}`}</Button>
          : <Muted>No rows behind it yet.</Muted>}
      </div>
    </section>
  );
}

// Six thin columns, one per window, oldest first. Percent measures share a 0 to 100 scale, so two windows compare
// honestly; days and counts scale to their largest window.
function TrendColumns({ measure: m, windows }: { measure: MeasureItem; windows: Measures['windows'] }) {
  const points = [...m.trend].sort((a, b) => a.index - b.index);
  const values = points.map(p => p.value).filter((v): v is number => v !== null);
  const max = m.unit === 'percent' ? 100 : Math.max(1, ...values);
  const last = points.length ? points[points.length - 1].index : -1;
  const label = (i: number) => windows.find(w => w.index === i);
  const tip = (p: (typeof points)[number]) => {
    const w = label(p.index);
    const span = w ? `${fmtDayMonth(w.from)} to ${fmtDayMonth(w.to)}` : `Window ${p.index + 1}`;
    const ratio = ratioText(p.numerator, p.denominator);
    return `${span}: ${p.value === null ? 'no value' : measureValueText(p.value, m.unit)}${ratio ? ` (${ratio})` : ''}`;
  };
  return (
    <figure className="m-0 flex flex-col gap-1">
      <ul className="m-0 grid h-[76px] list-none grid-cols-6 items-end gap-0.5 border-b border-line-strong p-0" aria-hidden>
        {points.map(p => {
          const pct = p.value === null ? 0 : Math.max(0, Math.min(1, p.value / max));
          const isLast = p.index === last;
          return (
            <li key={p.index} className="flex h-full flex-col items-center justify-end">
              <Tooltip>
                <TooltipTrigger asChild>
                  <span className="flex h-full w-full cursor-default flex-col items-center justify-end">
                    {isLast && p.value !== null && <span className="mb-0.5 text-[11px] leading-none font-semibold text-navy">{measureValueText(p.value, m.unit)}</span>}
                    {p.value !== null && (
                      <span
                        className={cn('w-full max-w-6 min-h-[2px] rounded-t-[4px]', isLast ? 'bg-primary' : 'bg-bar-muted')}
                        style={{ height: `calc((100% - 16px) * ${pct.toFixed(4)})` }}
                      />
                    )}
                  </span>
                </TooltipTrigger>
                <TooltipContent side="top">{tip(p)}</TooltipContent>
              </Tooltip>
            </li>
          );
        })}
      </ul>
      <div className="grid grid-cols-6 gap-0.5 text-center text-[11px] leading-4 text-navy-3 tabular-nums" aria-hidden>
        {points.map(p => {
          const w = label(p.index);
          return <span key={p.index} className="truncate">{w ? fmtDayMonth(w.to) : ''}</span>;
        })}
      </div>
      <figcaption className="sr-only">
        <table>
          <caption>{`${m.label}, by window`}</caption>
          <thead><tr><th scope="col">Window</th><th scope="col">Value</th></tr></thead>
          <tbody>
            {points.map(p => {
              const w = label(p.index);
              return (
                <tr key={p.index}>
                  <th scope="row">{w ? `${fmtDayMonth(w.from)} to ${fmtDayMonth(w.to)}` : `Window ${p.index + 1}`}</th>
                  <td>{p.value === null ? 'No value' : measureValueText(p.value, m.unit)}{ratioText(p.numerator, p.denominator) ? `, ${ratioText(p.numerator, p.denominator)}` : ''}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </figcaption>
    </figure>
  );
}

// ---------- signals ----------

// "Record a decision" about a signal: the decision proposal sheet with the signal set, for a person whose Tool Policy
// gives save_decision_proposal. The decision types offered are those about an application, exceptions left out, as
// the decision log offers them. Null when the person may not propose.
type ProposeSignal = ((signal: SignalItem, size?: 'sm' | 'xs') => ReactElement | null) | null;

function useProposeSignal(): ProposeSignal {
  const session = useSession();
  const types = useLabels('decision-type');
  if (!session.can('save_decision_proposal')) return null;
  const choices = types.keys()
    .filter(k => k !== EXCEPTION_TYPE && types.field<string>(k, 'defaultSubjectKind') === 'business-application')
    .map(k => ({ value: k, label: types.label(k) }));
  return (signal, size = 'xs') => {
    const key = slideBackKeyOf(signal.key);
    if (!key) return null;
    return (
      <ProposeDecisionSheet
        typeChoices={choices}
        slideBackSignalKey={key}
        trigger={(
          <Button size={size} variant="outline" aria-label={`Record a decision about signal ${signal.number}, ${signal.label}`}>
            <FilePlus2 aria-hidden />
            Record a decision
          </Button>
        )}
      />
    );
  };
}

type SignalDecision = NonNullable<SignalItem['decisions']>[number];

function DecisionLinks({ decisions }: { decisions: SignalDecision[] }) {
  return (
    <span className="flex flex-wrap gap-x-3 gap-y-0.5">
      {decisions.map(d => {
        const href = recordHref(d.recordUrl) ?? hrefTo({ page: 'decision', id: d.decisionId });
        return (
          <TextLink key={d.decisionId} href={href} className="text-xs" aria-label={`${d.recordNumber ?? 'Decision'}: ${d.statement}${d.status ? `, ${d.status.label}` : ''}`}>
            {`${d.recordNumber ?? 'Decision'}${d.status ? ` (${d.status.label.toLowerCase()})` : ''}`}
          </TextLink>
        );
      })}
    </span>
  );
}

function SignalRow({ signal: s, onOpen, propose }: { signal: SignalItem; onOpen: () => void; propose: ProposeSignal }) {
  const st = SIGNAL_STATE_TEXT[signalState(s)];
  const decisions = s.decisions ?? [];
  const count = s.decisionCount ?? decisions.length;
  return (
    <Row
      lead={(
        <span className={cn('grid size-8 place-items-center rounded-full text-xs font-bold tabular-nums', s.firing ? 'bg-danger-soft text-danger' : 'bg-secondary text-navy-2')} aria-hidden>
          {s.firing ? <Siren className="size-4" /> : s.number}
        </span>
      )}
      primary={<><span className="sr-only">Signal {s.number}: </span>{s.label}</>}
      secondary={`Threshold: ${withoutToolNames(s.threshold)} (${thresholdSourceText(s.thresholdSource)})`}
      tertiary={(
        <span className="flex flex-col gap-0.5">
          <span>{[s.watcher ? `Watched by the ${lowerFirst(s.watcher.label)}` : null, `Playbook chapter ${s.chapter}`, s.note ? humanizeDates(withoutToolNames(s.note)) : null].filter(Boolean).join('. ')}</span>
          {count > 0 ? (
            <span className="flex flex-wrap items-center gap-x-2 gap-y-0.5">
              <Gavel className="size-3.5 text-navy-3" aria-hidden />
              <LinkButton className="text-xs" onClick={onOpen} aria-label={`${plural(count, 'decision', 'decisions')} recorded about signal ${s.number}. Opens them.`}>{`${plural(count, 'decision', 'decisions')} recorded`}</LinkButton>
              <DecisionLinks decisions={decisions.slice(0, 3)} />
            </span>
          ) : <span>No decision recorded about it yet.</span>}
        </span>
      )}
      wideEnd
      end={(
        <>
          <ToneBadge tone={st.tone}>{st.text}</ToneBadge>
          {s.value !== null && <ToneBadge tone="subtle" icon={null}>{`Now ${measureValueText(s.value, s.unit)}`}</ToneBadge>}
        </>
      )}
      endSub={(s.rowCount > 0 || propose) ? (
        <span className="mt-1 flex flex-wrap items-center justify-end gap-2 narrow:justify-start">
          {s.rowCount > 0 && <LinkButton className="text-xs" onClick={onOpen} aria-label={`Open the ${s.rowCount} rows behind signal ${s.number}`}>{`Open the ${plural(s.rowCount, 'row', 'rows')}`}</LinkButton>}
          {propose?.(s)}
        </span>
      ) : undefined}
    />
  );
}

// The decisions recorded about one signal, in its drill: the latest five the read gives, each with its status and day.
function SignalDecisions({ signal: s, propose }: { signal: SignalItem; propose: ProposeSignal }) {
  const decisions = s.decisions ?? [];
  const count = s.decisionCount ?? decisions.length;
  return (
    <section aria-labelledby="signal-decisions-title" className="flex flex-col gap-2 rounded-lg border border-line bg-background px-3 py-2.5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 id="signal-decisions-title" className="m-0 text-sm font-semibold text-navy">{`Decisions about this signal (${count})`}</h3>
        {propose?.(s, 'sm')}
      </div>
      {decisions.length === 0 ? (
        <Muted>No decision names this signal yet. Record one when the signal fires, so the response is on record.</Muted>
      ) : (
        <RowList label="Decisions about this signal">
          {decisions.map(d => {
            const href = recordHref(d.recordUrl) ?? hrefTo({ page: 'decision', id: d.decisionId });
            return (
              <Row
                key={d.decisionId}
                primary={<TextLink href={href}>{`${d.recordNumber ? `${d.recordNumber}: ` : ''}${d.statement || 'A decision without a statement'}`}</TextLink>}
                secondary={`${d.decisionType.label} decision${d.decidedOn ? `, decided ${fmtDate(d.decidedOn)}` : ''}`}
                end={d.status ? <StatusBadge status={d.status} /> : undefined}
              />
            );
          })}
        </RowList>
      )}
      {count > decisions.length && (
        <Note>
          {`The latest ${decisions.length} of ${count} show here. The `}
          <TextLink href={hrefTo({ page: 'decisions' })}>decision log</TextLink>
          {' holds every one.'}
        </Note>
      )}
    </section>
  );
}

// ---------- the rows behind one figure ----------

type Drill = { measureKey: string } | { signalNumber: number };
type DrillRowItem = NonNullable<Measures['rows']>['items'][number];

function DrillPanel({ drill, data, days, onClose, onSignal, propose }: { drill: Drill; data: Measures; days: number; onClose: () => void; onSignal: (n: number) => void; propose: ProposeSignal }) {
  const measure = 'measureKey' in drill ? data.measures.find(m => m.key === drill.measureKey) ?? null : null;
  const signal = 'signalNumber' in drill ? data.signals.find(s => s.number === drill.signalNumber) ?? null : null;
  const rows = usePagedRead('get_measures', { windowDays: days, windows: MEASURE_WINDOWS, ...drill, top: 25 }, (d: Measures) => d.rows?.items ?? []);
  useEffect(() => { scrollToPanel('measure-rows'); }, []);
  const title = measure ? `Rows behind: ${measure.label}` : signal ? `Rows behind signal ${signal.number}` : 'Rows behind the figure';
  const unit = measure?.unit ?? signal?.unit ?? null;
  const total = rows.status === 'done' ? rows.first.data.rows?.totalCount ?? rows.items.length : undefined;
  return (
    <Panel
      title={title}
      id="measure-rows"
      count={total}
      className="mb-4"
      subtitle={measure ? `${measure.definition} Every window of the six.` : signal ? `${signal.label}. Threshold: ${withoutToolNames(signal.threshold)} (${thresholdSourceText(signal.thresholdSource)}).` : undefined}
      actions={<Button size="sm" variant="ghost" onClick={onClose}><X aria-hidden />Close</Button>}
    >
      {signal && <SignalDecisions signal={signal} propose={propose} />}
      {rows.status === 'loading' && <Muted>Reading the rows.</Muted>}
      {rows.status === 'failed' && <PanelFailure failure={rows.failure} />}
      {rows.status === 'done' && (
        rows.items.length === 0 ? <Empty>No row stands behind this figure now.</Empty> : (
          <>
            <RowList label={title}>
              {rows.items.map((r: DrillRowItem, i) => (
                <DrillRow
                  key={`${r.entity}-${r.recordId}-${i}`}
                  row={r}
                  unit={r.entity === 'signal' ? data.signals.find(s => String(s.number) === r.recordId)?.unit ?? null : unit}
                  onSignal={onSignal}
                />
              ))}
            </RowList>
            <MoreButton skipToken={rows.skipToken} loading={rows.loadingMore} onMore={rows.more} what="rows" laterFailure={rows.laterFailure} />
          </>
        )
      )}
      {!measure && !signal && <Note>This figure is not in the measures of this window.</Note>}
    </Panel>
  );
}

function DrillRow({ row, unit, onSignal }: { row: DrillRowItem; unit: 'days' | 'percent' | 'count' | null; onSignal: (n: number) => void }) {
  const href = recordHref(row.recordUrl);
  const signal = row.entity === 'signal' ? Number(row.recordId) : NaN;
  const primary = href
    ? <TextLink href={href}>{row.label}</TextLink>
    : Number.isInteger(signal)
      ? <LinkButton onClick={() => onSignal(signal)}>{row.label}</LinkButton>
      : row.label;
  return (
    <Row
      primary={primary}
      secondary={row.note ? humanizeDates(withoutToolNames(row.note)) : undefined}
      end={row.value !== null ? <span className="text-sm font-semibold text-navy tabular-nums">{measureValueText(row.value, unit)}</span> : undefined}
    />
  );
}
