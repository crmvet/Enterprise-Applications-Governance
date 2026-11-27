// Configuration (process P11; screen "Configuration", priority 2): the rules in force in plain words, and the
// configuration change flow. A configuration admin proposes, a second person accepts on the decision page (D-38);
// nothing changes until then.
//   /app/configuration                    Rules in force (rules.tsx): every configuration entity with its rows in plain
//                                         words, searchable (?q=), by area (?area=) or kind (?entity=), retired rows on
//                                         request (?retired=1); the gate levels, the locked settings, the data taxonomy
//                                         (levels, classifications with their sensitivity label links, platforms,
//                                         retention classes), and the rules explained for any application (?app=)
//   /app/configuration?view=presets       Presets and overlays (presets.tsx): the ones this release ships, how close
//                                         each full preset is to the rules in force, gate levels compared
//   /app/configuration?view=versions      Versions (history.tsx): who instructed and accepted each and when, the
//                                         decision, what changed row by row; the configuration's health
//   /app/configuration?view=drafts        Drafts (history.tsx): state (?state=), author, AI drafted or not, rule check,
//                                         decision; submit and withdraw
//   /app/records/configuration-draft/<id> ConfigurationDraftRecord (record.tsx): the rationale, the diff, the rule
//                                         check, what it touches, the decision and its approvals; submit, change,
//                                         withdraw
// Reads: get_configuration (every row of the version in force, read once: rows.tsx), list_presets,
// get_configuration_health, explain_rules, list_configuration_changes, get_configuration_draft, find_data_domains (the
// data uses a change touches). Writes: propose_configuration_change through the draft builder (sheets.tsx, shared with
// the Data page and Roles and access), submit_record and withdraw_record (subjectKind configuration-draft), and
// import_data_taxonomy ("Import data domains", importSheet.tsx; wave 4) for a person whose Tool Policy gives it.
//
// Choices (D-41), each with its reason:
//   - Every configuration row of the version in force is read once (about 650 rows, 7 pages) and shared by the page
//     and the draft builder. Why: the rules view lists them, the search runs across every kind, every key a page shows
//     turns into a label, and the builder fills its choices, all from one read.
//   - The rules are grouped in eight areas by the process they serve (logic.ts AREAS). The design names the parts of
//     "the rules in force" but no grouping.
//   - The draft builder's typed fields come from the configuration bundle schema, the engine's own contract for a
//     draft patch (entity-fields.json, checked against the schema by fieldSpecs.test.ts). Why: the field kinds, the
//     required fields and the entity each key points to stay right when the schema changes.
//   - A draft is saved, then submitted from its own page, never saved and submitted in one step. Why: the draft's page
//     shows what accepting it changes (computed again against the version in force), which is what the person submits.
//   - The Health panel sits on the Versions view: its findings are about the version in force.
import { useState } from 'react';
import { goTo, hrefTo, recordHrefOf } from '../../app/links';
import { navigate } from '../../app/navigation';
import { CONFIGURATION_VIEWS, CONFIGURATION_VIEW_TITLES, type ConfigurationView, type RouteOf } from '../../app/routes';
import { DataStamp, KpiTile, PageHeader, PanelFailure } from '../../components/blocks';
import { PageTabs } from '../../components/pageTabs';
import { Button } from '../../components/ui/button';
import { fmtDate, plural } from '../../data/format';
import { useRead } from '../../data/reads';
import { useSession } from '../../data/session';
import { dataOf } from '../../data/toolClient';
import { kpiRowClass4, scrollToPanel } from '../owners-reviews/people';
import { DraftsView, VersionsView } from './history';
import { PresetsView } from './presets';
import { ConfigRowsProvider, useConfigRowsInForce } from './rows';
import { RulesView, type OpenBuilder } from './rules';
import { ImportDomainsSheet } from './importSheet';
import { DraftBuilderSheet, type DraftBuilderProps } from './sheets';

export { ConfigurationDraftRecord } from './record';

export function configurationViewRoute(view: ConfigurationView): RouteOf<'configuration'> {
  return view === 'rules' ? { page: 'configuration' } : { page: 'configuration', view };
}

export function ConfigurationPage({ route }: { route: RouteOf<'configuration'> }) {
  return (
    <ConfigRowsProvider>
      <ConfigurationBody route={route} />
    </ConfigRowsProvider>
  );
}

type BuilderState = Omit<DraftBuilderProps, 'open' | 'onOpenChange'> & { n: number };

function ConfigurationBody({ route }: { route: RouteOf<'configuration'> }) {
  const view = route.view ?? 'rules';
  const session = useSession();
  const config = useConfigRowsInForce();
  const canPropose = session.can('propose_configuration_change');
  const canImport = session.isPerson && session.can('import_data_taxonomy');
  const changes = useRead('list_configuration_changes', { view: 'all', top: 1 });
  const health = useRead('get_configuration_health', {});
  const counts = dataOf(changes)?.counts ?? null;
  const healthData = dataOf(health);
  const activeVersion = dataOf(changes)?.configurationSet.activeVersion ?? config.version;
  const activeRows = config.rows.filter(r => r.isActive);
  const kinds = new Set(activeRows.map(r => r.entity)).size;
  const changesFailed = changes.status === 'done' && !changes.envelope.ok;
  const healthFailed = health.status === 'done' && !health.envelope.ok;
  const rowsFailed = !!config.failure && !config.rows.length;

  // The page's one draft builder: every Propose, Add, Change and Retire opens it with its own start.
  const [builder, setBuilder] = useState<BuilderState | null>(null);
  const openBuilder: OpenBuilder = props => setBuilder({ ...props, n: Date.now() });
  const onSaved = (id: string) => {
    setBuilder(null);
    const href = recordHrefOf('configuration-draft', id);
    if (href) navigate(href);
  };

  const viewHref = (v: ConfigurationView) => hrefTo(configurationViewRoute(v));
  const tabCount: Partial<Record<ConfigurationView, number | null>> = {
    rules: config.loading || rowsFailed ? null : activeRows.length,
    versions: counts?.versions ?? null,
    drafts: counts?.openDrafts ?? null,
  };

  return (
    <>
      <PageHeader
        eyebrow="Govern"
        title="Configuration"
        subtitle="The rules in force, and how they change: a configuration admin proposes, another person accepts the exact change on the decision page, and nothing changes until then."
        meta={(
          <span className="flex flex-col gap-0.5">
            <span>
              {activeVersion ? `Version ${activeVersion} is in force` : 'Reading the version in force'}
              {healthData?.active ? ` since ${fmtDate(healthData.active.activatedOn)}` : ''}
              {healthData?.reviewDueOn ? `. The rules are next reviewed on ${fmtDate(healthData.reviewDueOn)}` : ''}.
            </span>
            <span><DataStamp envelope={changes.status === 'done' ? changes.envelope : undefined} /></span>
          </span>
        )}
        actions={(
          <>
            {canPropose && <Button onClick={() => openBuilder({ title: 'Propose a change to the rules' })}>Propose a change</Button>}
            {canImport && <ImportDomainsSheet />}
            <Button asChild variant="outline" size="sm"><a href={hrefTo({ page: 'roles' })}>Roles and access</a></Button>
            <Button asChild variant="outline" size="sm"><a href={hrefTo({ page: 'guidelines' })}>Guidelines</a></Button>
          </>
        )}
      />

      {changes.status === 'done' && !changes.envelope.ok && <div className="mb-4"><PanelFailure failure={changes.envelope} /></div>}

      {/* Every figure opens the rows it counts. A figure whose read failed is left out, never shown as 0: the failure
          shows where its rows are (above for the versions and drafts, the Rules view, the Health panel). */}
      <div className={kpiRowClass4}>
        {!changesFailed && (
          <KpiTile
            label="Version in force"
            value={activeVersion ?? null}
            caption={counts ? `${plural(counts.versions, 'version', 'versions')} in all` : 'Reading the versions'}
            onClick={() => goTo(configurationViewRoute('versions'))}
          />
        )}
        {!changesFailed && (
          <KpiTile
            label="Open drafts"
            value={counts?.openDrafts ?? null}
            caption="Waiting to be submitted or accepted"
            attention={!!counts?.openDrafts}
            onClick={() => goTo(configurationViewRoute('drafts'))}
          />
        )}
        {!rowsFailed && (
          <KpiTile
            label="Rules in force"
            value={config.loading ? null : activeRows.length}
            caption={config.loading ? 'Reading the rules' : `Configuration rows of ${plural(kinds, 'kind', 'kinds')}`}
            onClick={() => goTo(configurationViewRoute('rules'))}
          />
        )}
        {!healthFailed && (
          <KpiTile
            label="Health findings"
            value={healthData ? healthData.findings.length : null}
            caption={healthData ? (healthData.engine.loaded ? 'The engine loaded the rules' : 'The engine could not load the rules') : 'Reading the health'}
            attention={!!healthData?.findings.length || healthData?.engine.loaded === false}
            onClick={() => {
              if (view === 'versions') scrollToPanel('configuration-health');
              else goTo(configurationViewRoute('versions'));
            }}
          />
        )}
      </div>

      <PageTabs
        label="Configuration views"
        current={view}
        tabs={CONFIGURATION_VIEWS.map(v => ({
          key: v,
          label: CONFIGURATION_VIEW_TITLES[v],
          href: viewHref(v),
          count: tabCount[v] ?? null,
          attention: v === 'drafts',
        }))}
      />

      {view === 'rules' && <RulesView canPropose={canPropose} openBuilder={openBuilder} />}
      {view === 'presets' && <PresetsView canPropose={canPropose} openBuilder={openBuilder} />}
      {view === 'versions' && <VersionsView health={health} />}
      {view === 'drafts' && <DraftsView canPropose={canPropose} openBuilder={openBuilder} />}

      {canPropose && builder && (
        <DraftBuilderSheet
          key={builder.n}
          {...builder}
          open
          onOpenChange={open => { if (!open) setBuilder(null); }}
          onSaved={onSaved}
        />
      )}
    </>
  );
}
