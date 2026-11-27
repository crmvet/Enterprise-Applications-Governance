// The Presets view of the Configuration page: the full presets and overlays this product release ships (list_presets),
// how close each full preset is to the rules in force, and their gate levels side by side.
// Choices (D-41):
//   - No read says which preset and overlays an install started from (backend gap), so the page compares the gate
//     levels and tiers in force with each full preset and names the ones that match, and says that is a comparison.
//   - Moving to another preset is not offered: propose_configuration_change refuses a whole preset ("not built yet").
//     The page says so and points to the draft builder, which changes rows.
import { Layers } from 'lucide-react';
import { Empty, Muted, Panel, ToneBadge, useNarrow } from '../../components/blocks';
import type { GateLevel, PresetList } from '../../data/contracts';
import { plural } from '../../data/format';
import { useRead } from '../../data/reads';
import { choiceText, GATE_LEVEL_TONE, gateLevelIn, inSentence, presetMatch, sortRows } from './logic';
import { useConfigRowsInForce, type ConfigRowsInForce } from './rows';
import type { OpenBuilder } from './rules';

type Preset = PresetList['presets'][number];

export function PresetsView({ canPropose, openBuilder }: { canPropose: boolean; openBuilder: OpenBuilder }) {
  const config = useConfigRowsInForce();
  const state = useRead('list_presets', {});
  const presets = state.status === 'done' && state.envelope.ok ? state.envelope.data.presets : [];
  const full = presets.filter(p => p.presetKind === 'full');
  const overlays = presets.filter(p => p.presetKind === 'overlay');
  const label = (key: string) => presets.find(p => p.presetKey === key)?.label ?? choiceText(key);
  const matches = full.map(p => ({ p, m: presetMatch(p, config.rows) }));
  const exact = matches.filter(x => x.m.total > 0 && x.m.matched === x.m.total).map(x => x.p.label);
  return (
    <div className="flex flex-col gap-4 narrow:gap-3">
      <Panel title="Full presets" state={state} count={state.status === 'done' && state.envelope.ok ? full.length : undefined} subtitle="A ready-made set of rules. An install starts from one full preset and any overlays that fit it.">
        {() => (
          <>
            {!config.loading && (
              <p className="m-0 text-sm text-navy-2">
                {exact.length
                  ? `The tiers and gate levels in force match ${exact.join(' and ')}. The product does not record which preset this install started from, so this is a comparison.`
                  : 'The tiers and gate levels in force match no full preset exactly: the install has changed them. The product does not record which preset it started from.'}
              </p>
            )}
            {!full.length ? <Empty>This release ships no full preset.</Empty> : (
              <ul className="m-0 grid list-none grid-cols-2 gap-3 p-0 medium:grid-cols-1">
                {matches.map(({ p, m }) => (
                  <li key={p.presetKey} className="flex flex-col gap-1.5 rounded-lg border border-line px-3 py-2.5">
                    <span className="flex flex-wrap items-center gap-2">
                      <Layers className="size-4 text-navy-3" aria-hidden />
                      <span className="text-sm font-semibold text-navy">{p.label}</span>
                      {!config.loading && m.total > 0 && (
                        m.matched === m.total
                          ? <ToneBadge tone="success">Matches the rules in force</ToneBadge>
                          : <ToneBadge tone="subtle" icon={null}>{`${m.matched} of ${m.total} tiers and gate levels match`}</ToneBadge>
                      )}
                    </span>
                    <span className="text-sm text-navy-2">{p.summary.approvalsSummary}</span>
                    <span className="flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-navy-3">
                      <span>{`Tiers: ${p.summary.tiers.map(t => t.label).join(', ') || 'none'}`}</span>
                      {p.summary.changeManagementMode && <span>{`Change management: ${choiceText(p.summary.changeManagementMode).toLowerCase()}`}</span>}
                      {p.extends && <span>{`Builds on ${label(p.extends)}`}</span>}
                      <span>{`Release ${p.productRelease}`}</span>
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </>
        )}
      </Panel>

      <Panel title="Overlays" state={state} count={state.status === 'done' && state.envelope.ok ? overlays.length : undefined} subtitle="An overlay adds rows on top of a full preset, such as a starter domain tree.">
        {() => !overlays.length ? <Empty>This release ships no overlay.</Empty> : (
          <ul className="m-0 list-none p-0">
            {overlays.map(o => (
              <li key={o.presetKey} className="flex flex-col gap-0.5 border-t border-line py-2 first:border-t-0 first:pt-0.5">
                <span className="text-sm font-semibold text-navy">{o.label}</span>
                <span className="text-sm text-navy-2">{o.summary.approvalsSummary}</span>
                <span className="flex flex-wrap gap-x-4 text-xs text-navy-3">
                  {o.extends ? <span>{`For ${label(o.extends)}`}</span> : <span>For any full preset</span>}
                  {o.conflictsWith.length > 0 && <span>{`Does not go with ${o.conflictsWith.map(label).join(', ')}`}</span>}
                </span>
              </li>
            ))}
          </ul>
        )}
      </Panel>

      <CompareTable config={config} full={full} />

      <Muted>
        Moving this install to another preset is a set of row changes: compare the rows above, then change the ones
        you need.{canPropose ? ' A draft holds those changes.' : ''}
        {canPropose && (
          <>
            {' '}
            <button type="button" onClick={() => openBuilder({ title: 'Propose a change to the rules' })} className="cursor-pointer border-0 bg-transparent p-0 text-xs font-medium text-primary underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-ring">
              Start a draft
            </button>
          </>
        )}
      </Muted>
    </div>
  );
}

// The gate level of every tier and target stage, for a normal change: in force, and in each full preset.
function CompareTable({ config, full }: { config: ConfigRowsInForce; full: Preset[] }) {
  const narrow = useNarrow();
  if (!full.length) return null;
  const tiers = sortRows(config.of('tier'));
  const stages = sortRows(config.of('target-stage'));
  const pairs = new Map<string, { tierKey: string; stageKey: string }>();
  for (const t of tiers) for (const s of stages) pairs.set(`${t.key}|${s.key}`, { tierKey: t.key, stageKey: s.key });
  for (const p of full) for (const g of p.summary.gateLevelsByTierAndStage) pairs.set(`${g.tierKey}|${g.targetStageKey}`, { tierKey: g.tierKey, stageKey: g.targetStageKey });
  const label = (entity: string, key: string) => config.lookup(entity, key) ?? full.flatMap(p => p.summary.tiers).find(t => t.key === key)?.label ?? choiceText(key);
  const badge = (level: GateLevel | null | undefined) => level
    ? <ToneBadge tone={GATE_LEVEL_TONE[level]} icon={null}>{choiceText(level)}</ToneBadge>
    : <Muted>None</Muted>;
  return (
    <Panel title="Gate levels compared" subtitle={`For a normal change, by tier and target stage: the rules in force beside each full preset. ${plural(pairs.size, 'pair', 'pairs')}.`}>
      {config.loading ? <Muted>Reading the rules in force.</Muted> : narrow ? (
        <ul className="m-0 flex list-none flex-col gap-2 p-0">
          {[...pairs.values()].map(({ tierKey, stageKey }) => (
            <li key={`${tierKey}|${stageKey}`} className="flex flex-col gap-1 border-t border-line pt-2 first:border-t-0 first:pt-0">
              <span className="text-sm font-semibold text-navy">{`${label('tier', tierKey)}, ${inSentence(label('target-stage', stageKey))}`}</span>
              <span className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-navy-2">
                <span className="flex items-center gap-1 font-semibold text-navy">In force: {badge(gateLevelIn(config.rows, tierKey, stageKey))}</span>
                {full.map(p => (
                  <span key={p.presetKey} className="flex items-center gap-1">
                    {`${p.label}:`}
                    {badge(p.summary.gateLevelsByTierAndStage.find(g => g.tierKey === tierKey && g.targetStageKey === stageKey)?.gateLevel)}
                  </span>
                ))}
              </span>
            </li>
          ))}
        </ul>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[560px] border-collapse text-sm">
            <caption className="sr-only">Gate level by tier and target stage, in force and in each full preset</caption>
            <thead>
              <tr>
                <th scope="col" className="border-b border-line py-1.5 pr-3 text-left text-xs font-semibold text-navy-3">Tier and stage</th>
                <th scope="col" className="border-b border-line px-2 py-1.5 text-left text-xs font-semibold text-navy">In force</th>
                {full.map(p => <th key={p.presetKey} scope="col" className="border-b border-line px-2 py-1.5 text-left text-xs font-semibold text-navy-3">{p.label}</th>)}
              </tr>
            </thead>
            <tbody>
              {[...pairs.values()].map(({ tierKey, stageKey }) => (
                <tr key={`${tierKey}|${stageKey}`}>
                  <th scope="row" className="border-b border-line py-1.5 pr-3 text-left font-medium text-navy">{`${label('tier', tierKey)}, ${inSentence(label('target-stage', stageKey))}`}</th>
                  <td className="border-b border-line px-2 py-1.5">{badge(gateLevelIn(config.rows, tierKey, stageKey))}</td>
                  {full.map(p => (
                    <td key={p.presetKey} className="border-b border-line px-2 py-1.5">
                      {badge(p.summary.gateLevelsByTierAndStage.find(g => g.tierKey === tierKey && g.targetStageKey === stageKey)?.gateLevel)}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Panel>
  );
}
