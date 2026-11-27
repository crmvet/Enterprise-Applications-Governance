// The writes of the Deployments, Places and Gap check pages, each in the shared side sheet (components/actions.tsx). The
// engine checks every one as it checks an AI client's; the sheet shows its answer, and "Check first" runs every rule
// with nothing written.
//   RecordDeploymentSheet   record_deployment: path C, a person records a deployment by hand (D-19)
//   CorrectionSheet         record_deployment_correction: a new record that names the one it corrects
//   TargetSheet             save_deployment_target: register a target, or change one (platform admin)
//   RetireTargetSheet       save_deployment_target retire: retire a target with a reason, or return it to service
//   MapPipelineSheet        map_pipeline: this pipeline deploys this application to these targets
//   RunGapCheckSheet        run_gap_check: read the estate as the signed-in admin (and continue a run with more pages)
//   CompareListSheet        compare_inventory_list: the same check over a list from the admin's own tool
//   OwnerCheckSheet         run_owner_check: which owners the directory shows as disabled, deleted or leaving
//   ResolveVanishedSheet    link_component: a vanished item is gone (retired) or moved
import { Plus, Trash2 } from 'lucide-react';
import { useEffect, useMemo, useState, type ReactElement } from 'react';
import type { ClaimItem, CodeApprovalKind, DeploymentItem, DeploymentResultMeaning, DeploymentTargetItem, GapRunResult, PipelineMap, RecordDeploymentResult } from '../../data/contracts';
import { addDays, fmtDate, fmtWindow, plural, utcDayRange } from '../../data/format';
import { useLabels, useListValues } from '../../data/labels';
import { dataOf, useTool } from '../../data/toolClient';
import { useHost } from '../../harness';
import { useInstall } from '../../app/install';
import { cn } from '../../lib/utils';
import { ActionSheet, Field } from '../../components/actions';
import { Muted, ToneBadge } from '../../components/blocks';
import { Button } from '../../components/ui/button';
import { Checkbox } from '../../components/ui/checkbox';
import { Input } from '../../components/ui/input';
import { Label } from '../../components/ui/label';
import { Textarea } from '../../components/ui/textarea';
import { choicesOf, SelectField, useSheet, type Choice } from '../changes-records/parts';
import {
  buildCorrectionInput,
  buildDeploymentInput,
  buildPipelineInput,
  buildRetireInput,
  buildTargetInput,
  correctionFormOf,
  emptyDeploymentForm,
  emptyPipelineForm,
  emptyTargetForm,
  guessComponent,
  isRetired,
  parseInventoryList,
  RESULT_MEANINGS,
  RESULT_TEXT,
  sameId,
  targetFormOf,
  type CorrectionForm,
  type DeploymentForm,
  type PipelineEntryForm,
  type PipelineForm,
  type TargetForm,
} from './logic';
import { AppField, radioClass, subhead, TargetSelectField, useAllowedRoutes, useRouteWords, type AppChoice } from './parts';

const RESULT_CHOICES: Choice[] = RESULT_MEANINGS.map(m => ({ value: m, label: RESULT_TEXT[m] }));

const CODE_APPROVAL_CHOICES: { value: CodeApprovalKind; label: string }[] = [
  { value: 'unknown', label: 'Not known' },
  { value: 'person', label: 'A person approved the code' },
  { value: 'ai-only', label: 'Only an AI approved the code' },
  { value: 'none', label: 'Nobody approved the code' },
];

// The changes of an application with a window near a day, as choices: "Title (27 Nov, 18:00 to 21:00 UTC)".
function useChangeChoices(applicationId: string | null, aroundDay: string, enabled: boolean): { choices: Choice[]; loading: boolean } {
  const schedule = useTool('get_change_schedule', enabled && applicationId ? { ...utcDayRange(addDays(aroundDay, -45), addDays(aroundDay, 14)), businessApplicationId: applicationId, top: 100 } : null);
  const items = dataOf(schedule)?.items ?? [];
  return {
    choices: items.map(c => ({ value: c.changeRequestId.toLowerCase(), label: `${c.title} (${fmtWindow(c.windowStart, c.windowEnd)})` })),
    loading: enabled && !!applicationId && schedule.status === 'loading',
  };
}

// The value of a datetime-local field in a two-column row.
function DateTimeField({ label, name, value, onChange, required, hint }: { label: string; name: string; value: string; onChange: (v: string) => void; required?: boolean; hint?: string }) {
  return (
    <Field label={label} name={name} required={required} hint={hint}>
      {p => <Input {...p} type="datetime-local" value={value} onChange={e => onChange(e.target.value)} />}
    </Field>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// record_deployment
// ---------------------------------------------------------------------------------------------------------------------

// Path C (D-19): a person records that a version reached a target. The engine sets the route (a person in the app), the
// flags and the gate level from the target's stage and change path; "Check first" shows them before anything is written.
export function RecordDeploymentSheet({ app, targets, changeRequestId, trigger, onRecorded }: {
  app: AppChoice;
  targets: DeploymentTargetItem[];
  changeRequestId?: string;
  trigger: ReactElement;
  onRecorded?: (data: RecordDeploymentResult) => void;
}) {
  const host = useHost();
  const kinds = useListValues('artifact-kind');
  const routeWords = useRouteWords();
  const allowedRoutes = useAllowedRoutes();
  const [form, setForm] = useState<DeploymentForm>(() => ({ ...emptyDeploymentForm(), changeRequestId: changeRequestId ?? '' }));
  const sheet = useSheet(() => setForm({ ...emptyDeploymentForm(), changeRequestId: changeRequestId ?? '' }));
  const changes = useChangeChoices(app.id, host.today, sheet.open);
  const pipelines = useTool('get_pipeline_map', sheet.open ? { businessApplicationId: app.id } : null);
  const last = useTool('get_deployments', sheet.open ? { businessApplicationId: app.id, latestOnly: false, top: 1 } : null);
  const lastName = dataOf(last)?.items[0]?.artifactName ?? null;
  const set = <K extends keyof DeploymentForm>(key: K, value: DeploymentForm[K]) => setForm(f => ({ ...f, [key]: value }));

  // The name the application's last deployment used, while the field is empty, each time the sheet opens.
  useEffect(() => {
    if (sheet.open && lastName) setForm(f => (f.artifactName ? f : { ...f, artifactName: lastName }));
  }, [lastName, sheet.open]);

  const target = targets.find(t => sameId(t.deploymentTargetId, form.targetId)) ?? null;
  const personAllowed = target ? allowedRoutes(target.changePath).includes('person-in-app') : true;
  const pipelineChoices = (dataOf(pipelines)?.pipelines ?? []).map(p => ({ value: p.pipelineReferenceId.toLowerCase(), label: p.label ?? p.externalId }));
  const changeChoices = changes.choices.some(c => sameId(c.value, form.changeRequestId)) || !form.changeRequestId
    ? changes.choices
    : [{ value: form.changeRequestId, label: 'The change in the address' }, ...changes.choices];

  return (
    <ActionSheet
      tool="record_deployment" site="pages/deployments-places/sheets#RecordDeploymentSheet"
      title={`Record a deployment of ${app.name}`}
      description="Say what reached which target and how it ended. The engine records the route as a person in the app, works out the flags and applies the gate level of the target's stage."
      trigger={trigger}
      open={sheet.open}
      onOpenChange={sheet.onOpenChange}
      size="wide"
      input={() => buildDeploymentInput(form, app.id)}
      submitLabel="Record it"
      preview
      onDone={data => onRecorded?.(data)}
    >
      <TargetSelectField
        targets={targets}
        value={form.targetId}
        onChange={v => set('targetId', v)}
        required
        hint={target ? (
          <span className="flex flex-col gap-1">
            <span>{`Change path: ${target.changePath.label}. Allowed routes: ${routeWords(target.changePath)}.`}</span>
            {!personAllowed && <span className="font-medium text-warning">This target expects its pipeline. A deployment a person records here is flagged as having come another way.</span>}
          </span>
        ) : 'Only a registered target can be chosen. A platform admin registers one on the Places page.'}
      />
      <div className="grid grid-cols-2 gap-3 narrow:grid-cols-1">
        <Field label="Artifact name" name="artifactName" required hint="Such as the solution's unique name or the image name.">
          {p => <Input {...p} value={form.artifactName} onChange={e => set('artifactName', e.target.value)} maxLength={200} />}
        </Field>
        <Field label="Version" name="artifactVersion" required>
          {p => <Input {...p} value={form.artifactVersion} onChange={e => set('artifactVersion', e.target.value)} maxLength={100} placeholder="1.12.1" />}
        </Field>
      </div>
      <div className="grid grid-cols-2 gap-3 narrow:grid-cols-1">
        <SelectField label="Artifact kind" name="artifactKindKey" value={form.artifactKindKey} onChange={v => set('artifactKindKey', v)} choices={kinds.choices} noneLabel="Not stated" />
        <SelectField label="Result" name="resultMeaning" value={form.resultMeaning} onChange={v => set('resultMeaning', v as DeploymentResultMeaning)} choices={RESULT_CHOICES} required />
      </div>
      <div className="grid grid-cols-2 gap-3 narrow:grid-cols-1">
        <DateTimeField label="Finished" name="finishedOn" value={form.finishedOn} onChange={v => set('finishedOn', v)} required hint="In your own time zone." />
        <DateTimeField label="Started" name="startedOn" value={form.startedOn} onChange={v => set('startedOn', v)} hint="Optional." />
      </div>
      <SelectField
        label="The change it belongs to"
        name="changeRequestId"
        value={form.changeRequestId}
        onChange={v => set('changeRequestId', v)}
        choices={changeChoices}
        noneLabel={changes.loading ? 'Reading the changes' : 'No change'}
        hint="Changes of this application from six weeks back to two weeks ahead. Without a change, the engine may flag a missing approval."
      />
      <SelectField label="Pipeline" name="pipelineReferenceId" value={form.pipelineReferenceId} onChange={v => set('pipelineReferenceId', v)} choices={pipelineChoices} noneLabel="No pipeline ran" hint="The pipelines mapped to this application." />
      <div className="grid grid-cols-2 gap-3 narrow:grid-cols-1">
        <Field label="Version before" name="versionBefore" hint="As read on the target before.">
          {p => <Input {...p} value={form.versionBefore} onChange={e => set('versionBefore', e.target.value)} maxLength={100} />}
        </Field>
        <Field label="Version after" name="versionAfter" hint="As read on the target after.">
          {p => <Input {...p} value={form.versionAfter} onChange={e => set('versionAfter', e.target.value)} maxLength={100} />}
        </Field>
      </div>
      <Field label="Run link" name="runUrl" hint="Optional. The full https address of the run or the release.">
        {p => <Input {...p} type="url" inputMode="url" value={form.runUrl} onChange={e => set('runUrl', e.target.value)} placeholder="https://" maxLength={2000} />}
      </Field>
      <SelectField
        label="Who approved the code"
        name="codeApprovalKind"
        value={form.codeApprovalKind}
        onChange={v => set('codeApprovalKind', v as CodeApprovalKind)}
        choices={CODE_APPROVAL_CHOICES}
        hint="Evidence only, never authorization. When only an AI approved the code, the engine flags it."
      />
      <Field label="Source commit" name="sourceCommit" hint="Optional.">
        {p => <Input {...p} value={form.sourceCommit} onChange={e => set('sourceCommit', e.target.value)} maxLength={64} className="font-mono" />}
      </Field>
    </ActionSheet>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// record_deployment_correction
// ---------------------------------------------------------------------------------------------------------------------

// A deployment record is final. A correction is a new record that names it: the change it belongs to, a wrong result,
// version, finish time or run link.
export function CorrectionSheet({ record, trigger }: { record: DeploymentItem; trigger: ReactElement }) {
  const [form, setForm] = useState<CorrectionForm>(() => correctionFormOf(record));
  const sheet = useSheet(() => setForm(correctionFormOf(record)));
  const changes = useChangeChoices(record.businessApplicationId, record.finishedOn.slice(0, 10), sheet.open);
  const set = <K extends keyof CorrectionForm>(key: K, value: CorrectionForm[K]) => setForm(f => ({ ...f, [key]: value }));
  const own = record.changeRequestId?.toLowerCase() ?? '';
  const choices = own && !changes.choices.some(c => c.value === own) ? [{ value: own, label: 'The change it names now' }, ...changes.choices] : changes.choices;
  return (
    <ActionSheet
      tool="record_deployment_correction" site="pages/deployments-places/sheets#CorrectionSheet"
      title="Record a correction"
      description="The record stays as it is. A new record names it and carries the right facts, and the engine works out the flags again."
      trigger={trigger}
      open={sheet.open}
      onOpenChange={sheet.onOpenChange}
      input={() => buildCorrectionInput(form, record)}
      submitLabel="Record the correction"
      preview
      confirm={{
        title: 'Record this correction?',
        body: <p className="m-0">A correction cannot be taken back. The record it corrects stays in the history, marked as corrected.</p>,
        confirmLabel: 'Record it',
      }}
    >
      <Field label="What was wrong" name="reason" required hint="Stored on the new record.">
        {p => <Textarea {...p} rows={3} maxLength={2000} value={form.reason} onChange={e => set('reason', e.target.value)} placeholder="It belongs to the November release change." />}
      </Field>
      <SelectField
        label="The change it belongs to"
        name="changeRequestId"
        value={form.changeRequestId}
        onChange={v => set('changeRequestId', v)}
        choices={choices}
        noneLabel={changes.loading ? 'Reading the changes' : own ? 'Keep the change it names' : 'No change'}
        hint="Changes of the application from six weeks before it finished to two weeks after."
      />
      <div className="grid grid-cols-2 gap-3 narrow:grid-cols-1">
        <SelectField label="Result" name="resultMeaning" value={form.resultMeaning} onChange={v => set('resultMeaning', v as DeploymentResultMeaning)} choices={RESULT_CHOICES} />
        <Field label="Version" name="artifactVersion">
          {p => <Input {...p} value={form.artifactVersion} onChange={e => set('artifactVersion', e.target.value)} maxLength={100} />}
        </Field>
      </div>
      <DateTimeField label="Finished" name="finishedOn" value={form.finishedOn} onChange={v => set('finishedOn', v)} hint="In your own time zone." />
      <Field label="Run link" name="runUrl">
        {p => <Input {...p} type="url" inputMode="url" value={form.runUrl} onChange={e => set('runUrl', e.target.value)} placeholder="https://" maxLength={2000} />}
      </Field>
      <Muted>Only the facts you change go on the correction.</Muted>
    </ActionSheet>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// save_deployment_target
// ---------------------------------------------------------------------------------------------------------------------

// Register a target, or change one. Its stage sets the gate level a deployment there meets and its change path sets which
// routes are flagged, so a person states them (D-36); the platform admin holds the tool.
export function TargetSheet({ existing, trigger, onSaved }: { existing?: DeploymentTargetItem | null; trigger: ReactElement; onSaved?: (id: string | null) => void }) {
  const kinds = useListValues('target-kind');
  const stages = useLabels('target-stage');
  const paths = useLabels('change-path');
  const linkKinds = useLabels('link-kind');
  const routeWords = useRouteWords();
  const blank = () => (existing ? targetFormOf(existing) : emptyTargetForm());
  const [form, setForm] = useState<TargetForm>(blank);
  const sheet = useSheet(() => setForm(blank()));
  const set = <K extends keyof TargetForm>(key: K, value: TargetForm[K]) => setForm(f => ({ ...f, [key]: value }));
  const linkChoices = choicesOf(linkKinds, k => linkKinds.field<string>(k, 'linkCategory') === 'microsoft-resource');
  const allowed = form.changePathKey ? paths.field<string[]>(form.changePathKey, 'allowedRouteKeys') ?? [] : [];
  const rulesChange = !!existing && (form.targetStageKey !== existing.targetStage.key || form.changePathKey !== existing.changePath.key);
  return (
    <ActionSheet
      tool="save_deployment_target" site="pages/deployments-places/sheets#TargetSheet"
      title={existing ? `Change ${existing.label}` : 'Register a deployment target'}
      description="A place deployments land, such as a Power Platform environment. Once registered, a deployment to it can be recorded."
      trigger={trigger}
      open={sheet.open}
      onOpenChange={sheet.onOpenChange}
      size="wide"
      input={() => buildTargetInput(form, existing)}
      submitLabel={existing ? 'Save the change' : 'Register it'}
      preview
      onDone={data => onSaved?.(data.deploymentTargetId)}
    >
      <Field label="Name" name="labelAsSeen" required hint="As people know it, such as Finance Production.">
        {p => <Input {...p} value={form.label} onChange={e => set('label', e.target.value)} maxLength={200} />}
      </Field>
      <div className="grid grid-cols-2 gap-3 narrow:grid-cols-1">
        <SelectField label="Kind" name="targetKindKey" value={form.targetKindKey} onChange={v => set('targetKindKey', v)} choices={kinds.choices} required />
        <SelectField label="Stage" name="targetStageKey" value={form.targetStageKey} onChange={v => set('targetStageKey', v)} choices={choicesOf(stages)} required hint="Sets the gate level a deployment there meets." />
      </div>
      <SelectField
        label="Change path"
        name="changePathKey"
        value={form.changePathKey}
        onChange={v => set('changePathKey', v)}
        choices={choicesOf(paths)}
        required
        hint={form.changePathKey ? `Allowed routes: ${routeWords(allowed)}. A deployment by any other route is flagged.` : 'The routes this target expects. A deployment by any other route is flagged.'}
      />
      <div className="grid grid-cols-2 gap-3 narrow:grid-cols-1">
        <SelectField label="Link kind" name="linkKindKey" value={form.linkKindKey} onChange={v => set('linkKindKey', v)} choices={linkChoices} required />
        <Field label="ID in its tool" name="externalId" required={!existing} hint={existing && !existing.externalId ? 'Hidden from your role. Leave it empty to keep it.' : 'Such as the environment ID. With the link kind, it must be unique.'}>
          {p => <Input {...p} value={form.externalId} onChange={e => set('externalId', e.target.value)} maxLength={400} className="font-mono" />}
        </Field>
      </div>
      <Field label="Link" name="url" hint="Optional. The full https address that opens the target.">
        {p => <Input {...p} type="url" inputMode="url" value={form.url} onChange={e => set('url', e.target.value)} placeholder="https://" maxLength={2000} />}
      </Field>
      <Field label="What it is for" name="description" hint="Optional.">
        {p => <Textarea {...p} rows={2} maxLength={2000} value={form.description} onChange={e => set('description', e.target.value)} />}
      </Field>
      {rulesChange && (
        <p className="m-0 rounded-md border border-warning-line bg-warning-soft px-3 py-2 text-sm text-navy">
          The stage or change path changes. Deployments recorded before keep the flags and gate level they were recorded with; new ones meet the new rules.
        </p>
      )}
    </ActionSheet>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// save_deployment_target: retire a target, or return it to service
// ---------------------------------------------------------------------------------------------------------------------

// A retired target keeps every record it holds and refuses new deployments with the reason given here. Returning it to
// service takes nothing but the target. The platform admin holds the tool; the engine checks the write.
export function RetireTargetSheet({ target, trigger }: { target: DeploymentTargetItem; trigger: ReactElement }) {
  const [reason, setReason] = useState('');
  // The action the sheet was opened for, so the done answer keeps its title after the write turns the target around
  // (QA: the title read "Return People Test to service" over "The deployment target People Test is retired").
  const [openedRetired, setOpenedRetired] = useState<boolean | null>(null);
  const base = useSheet(() => { setReason(''); setOpenedRetired(null); });
  const sheet = {
    open: base.open,
    onOpenChange: (next: boolean) => {
      if (next) setOpenedRetired(isRetired(target));
      base.onOpenChange(next);
    },
  };
  const retired = openedRetired ?? isRetired(target);
  const apps = target.applications.length;
  const pipes = target.pipelines.length;
  return (
    <ActionSheet
      tool="save_deployment_target" site="pages/deployments-places/sheets#RetireTargetSheet"
      title={retired ? `Return ${target.label} to service` : `Retire ${target.label}`}
      description={retired
        ? 'It takes new deployments again. Its records stay as they are.'
        : 'A retired target keeps its deployment records and refuses new ones with your reason. It can be returned to service later.'}
      trigger={trigger}
      open={sheet.open}
      onOpenChange={sheet.onOpenChange}
      input={() => buildRetireInput(target, reason)}
      submitLabel={retired ? 'Return it to service' : 'Retire it'}
      submitTone={retired ? 'default' : 'destructive'}
      preview
      confirm={retired ? undefined : {
        title: `Retire ${target.label}?`,
        body: (
          <>
            <p className="m-0">New deployments to it are refused from now on, by every route, with your reason. Its records stay.</p>
            {reason.trim() && <p className="m-0 mt-2 border-l-2 border-line-strong pl-3 whitespace-pre-wrap text-navy [overflow-wrap:anywhere]">{reason.trim()}</p>}
          </>
        ),
        confirmLabel: 'Retire it',
        tone: 'destructive',
      }}
    >
      {retired ? (
        <div className="flex flex-col gap-1 rounded-md border border-line bg-background px-3 py-2.5 text-sm text-navy">
          <span>{`Retired ${fmtDate(target.retiredOn)}.`}</span>
          {target.retiredReason && <span className="text-navy-2 [overflow-wrap:anywhere]">{`Why: ${target.retiredReason}`}</span>}
        </div>
      ) : (
        <Field label="Why it is retired" name="retiredReason" required hint="Stored on the target and shown to anyone who tries to record a deployment to it, such as the environment that replaced it.">
          {p => <Textarea {...p} rows={3} maxLength={2000} value={reason} onChange={e => setReason(e.target.value)} />}
        </Field>
      )}
      {!retired && (apps > 0 || pipes > 0) && (
        <p className="m-0 rounded-md border border-warning-line bg-warning-soft px-3 py-2 text-sm text-navy">
          {[
            apps ? `${plural(apps, 'application runs', 'applications run')} here now. ${apps === 1 ? 'It stays' : 'They stay'} in the target's history.` : null,
            pipes ? `${plural(pipes, 'pipeline is', 'pipelines are')} mapped to it. A deployment ${pipes === 1 ? 'it reports' : 'they report'} here is refused.` : null,
          ].filter(Boolean).join(' ')}
        </p>
      )}
    </ActionSheet>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// map_pipeline
// ---------------------------------------------------------------------------------------------------------------------

type PipelineRef = PipelineMap['pipelines'][number];

const pipelineFormOf = (pipeline: PipelineRef | null | undefined, app: AppChoice | null): PipelineForm => ({
  ...emptyPipelineForm(),
  ...(pipeline ? { linkKindKey: pipeline.linkKind.key, externalId: pipeline.externalId, url: pipeline.url, label: pipeline.label ?? '' } : {}),
  entries: pipeline && app
    ? pipeline.entries.filter(e => sameId(e.businessApplicationId, app.id)).map(e => ({ businessApplicationId: app.id, applicationName: app.name, deploymentTargetId: e.deploymentTargetId.toLowerCase(), stageOrder: String(e.stageOrder) }))
    : [],
});

// This pipeline deploys this application to these targets, in this order. One application at a time: the map entries of
// other applications stay as they are. The product never runs the pipeline.
export function MapPipelineSheet({ pipeline, app: givenApp, targets, trigger }: { pipeline?: PipelineRef | null; app?: AppChoice | null; targets: DeploymentTargetItem[]; trigger: ReactElement }) {
  const linkKinds = useLabels('link-kind');
  // A pipeline that deploys one application opens on that application.
  const mapped = [...new Map((pipeline?.entries ?? []).map(e => [e.businessApplicationId.toLowerCase(), e.applicationName])).entries()];
  const presetApp = givenApp ?? (mapped.length === 1 ? { id: mapped[0][0], name: mapped[0][1] || 'The mapped application' } : null);
  const [app, setApp] = useState<AppChoice | null>(presetApp ?? null);
  const [form, setForm] = useState<PipelineForm>(() => pipelineFormOf(pipeline, presetApp ?? null));
  const sheet = useSheet(() => { setApp(presetApp ?? null); setForm(pipelineFormOf(pipeline, presetApp ?? null)); });
  const set = <K extends keyof PipelineForm>(key: K, value: PipelineForm[K]) => setForm(f => ({ ...f, [key]: value }));
  const choose = (next: AppChoice | null) => {
    setApp(next);
    setForm(f => {
      const fromMap = pipelineFormOf(pipeline, next).entries;
      return { ...f, entries: next ? (fromMap.length ? fromMap : [{ businessApplicationId: next.id, applicationName: next.name, deploymentTargetId: '', stageOrder: '1' }]) : [] };
    });
  };
  const setEntry = (i: number, patch: Partial<PipelineEntryForm>) => set('entries', form.entries.map((e, j) => (j === i ? { ...e, ...patch } : e)));
  const others = pipeline ? pipeline.entries.filter(e => !app || !sameId(e.businessApplicationId, app.id)) : [];
  const pipelineKinds = choicesOf(linkKinds, k => linkKinds.field<string>(k, 'linkCategory') === 'pipeline');
  return (
    <ActionSheet
      tool="map_pipeline" site="pages/deployments-places/sheets#MapPipelineSheet"
      title={pipeline ? `Change the map of ${pipeline.label ?? pipeline.externalId}` : 'Map a pipeline'}
      description="Say which targets the pipeline deploys an application to, in order. The product stores the map; it never runs the pipeline."
      trigger={trigger}
      open={sheet.open}
      onOpenChange={sheet.onOpenChange}
      size="wide"
      input={() => buildPipelineInput(form, pipeline?.pipelineReferenceId)}
      submitLabel="Save the map"
      preview
    >
      <div className="grid grid-cols-2 gap-3 narrow:grid-cols-1">
        <SelectField label="Tool" name="linkKindKey" value={form.linkKindKey} onChange={v => set('linkKindKey', v)} choices={pipelineKinds} required />
        <Field label="ID in its tool" name="externalId" required>
          {p => <Input {...p} value={form.externalId} onChange={e => set('externalId', e.target.value)} maxLength={400} className="font-mono" />}
        </Field>
      </div>
      <Field label="Link" name="url" required hint="The full https address that opens the pipeline. The engine checks it against the tool's pattern.">
        {p => <Input {...p} type="url" inputMode="url" value={form.url} onChange={e => set('url', e.target.value)} placeholder="https://" maxLength={2000} />}
      </Field>
      <Field label="Name" name="labelAsSeen" hint="As the tool shows it.">
        {p => <Input {...p} value={form.label} onChange={e => set('label', e.target.value)} maxLength={200} />}
      </Field>
      <AppField label="Application" name="entries.businessApplicationId" value={app} onChange={choose} required hint="You map an application you own, or any as a platform admin." />
      {app && (
        <fieldset className="m-0 flex min-w-0 flex-col gap-2 border-0 p-0">
          <legend className="mb-1 text-sm font-medium text-navy">{`Where it deploys ${app.name}, in order`}</legend>
          {form.entries.map((e, i) => (
            <div key={i} className="grid grid-cols-[minmax(0,1fr)_96px_auto] items-end gap-2 narrow:grid-cols-[minmax(0,1fr)_80px_auto]">
              <TargetSelectField targets={targets} value={e.deploymentTargetId} onChange={v => setEntry(i, { deploymentTargetId: v })} label={`Step ${i + 1}: target`} name="entries.deploymentTargetId" />
              <Field label="Order" name="entries.stageOrder">
                {p => <Input {...p} type="number" inputMode="numeric" min={1} max={99} value={e.stageOrder} onChange={ev => setEntry(i, { stageOrder: ev.target.value })} />}
              </Field>
              <Button type="button" variant="ghost" size="icon" aria-label={`Remove step ${i + 1}`} onClick={() => set('entries', form.entries.filter((_, j) => j !== i))}>
                <Trash2 aria-hidden />
              </Button>
            </div>
          ))}
          {form.entries.length < 10 && (
            <Button
              type="button"
              variant="link"
              size="sm"
              className="h-7 self-start px-0"
              onClick={() => set('entries', [...form.entries, { businessApplicationId: app.id, applicationName: app.name, deploymentTargetId: '', stageOrder: String(form.entries.length + 1) }])}
            >
              <Plus aria-hidden />Add a step
            </Button>
          )}
          <Muted>A step removed here stays on the map: the product has no way to take a map entry away yet.</Muted>
        </fieldset>
      )}
      {others.length > 0 && <Muted>{`It also deploys ${plural(new Set(others.map(o => o.businessApplicationId.toLowerCase())).size, 'other application', 'other applications')}. Those steps stay as they are.`}</Muted>}
    </ActionSheet>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// run_gap_check, compare_inventory_list, run_owner_check
// ---------------------------------------------------------------------------------------------------------------------

export interface RunContinuation {
  gapCheckRunId: string;
  skipToken: string;
  estateKeys?: string[];
  includeVanished: boolean;
  compareOnly: boolean;
}

// Reads the estate (the Power Platform inventory) as the signed-in admin, with their own admin role, and opens a claim for
// each item no application holds. A run with more pages is continued from the same sheet's second form. On a demo
// install the server reads the demo's fictional inventory instead (wave 4b review), and the sheet says so.
export function RunGapCheckSheet({ trigger, continuation, onRan }: { trigger: ReactElement; continuation?: RunContinuation | null; onRan?: (data: GapRunResult, input: { estateKeys?: string[]; includeVanished: boolean; compareOnly: boolean }) => void }) {
  const demo = useInstall().demo;
  const estates = useListValues('estate');
  const [chosen, setChosen] = useState<string[] | null>(null);
  const [environments, setEnvironments] = useState('');
  const [includeVanished, setIncludeVanished] = useState(true);
  const [compareOnly, setCompareOnly] = useState(false);
  const sheet = useSheet(() => { setChosen(null); setEnvironments(''); setIncludeVanished(true); setCompareOnly(false); });
  const all = estates.options.map(o => o.key);
  const picked = chosen ?? all;
  const toggle = (key: string) => setChosen(picked.includes(key) ? picked.filter(k => k !== key) : [...picked, key]);
  const envIds = environments.split(/[\s,;]+/).map(e => e.trim()).filter(Boolean);
  return (
    <ActionSheet
      tool="run_gap_check" site="pages/deployments-places/sheets#RunGapCheckSheet"
      title={continuation ? 'Continue the gap check' : 'Run the gap check'}
      description={continuation
        ? 'The run has more pages. Continue it to read the next page of the estate.'
        : demo
          ? "On this demo install it reads the demo's fictional inventory, not Microsoft's, and compares it with the register. Each item no application holds opens a claim."
          : 'Reads the estate as you, with your own admin role, and compares it with the register. Each item no application holds opens a claim.'}
      trigger={trigger}
      open={sheet.open}
      onOpenChange={sheet.onOpenChange}
      input={() => {
        if (continuation) {
          return {
            gapCheckRunId: continuation.gapCheckRunId,
            skipToken: continuation.skipToken,
            ...(continuation.estateKeys ? { estateKeys: continuation.estateKeys } : {}),
            includeVanished: continuation.includeVanished,
            compareOnly: continuation.compareOnly,
          };
        }
        if (!picked.length) return { problem: 'Choose at least one estate.', field: 'estateKeys' };
        if (envIds.some(e => e.length > 100)) return { problem: 'An environment ID is longer than 100 characters.', field: 'environmentIds' };
        return {
          ...(chosen && picked.length < all.length ? { estateKeys: picked } : {}),
          ...(envIds.length ? { environmentIds: [...new Set(envIds)].slice(0, 100) } : {}),
          includeVanished,
          compareOnly,
        };
      }}
      submitLabel={continuation ? 'Continue' : 'Run it'}
      preview={!continuation}
      onDone={data => onRan?.(data, continuation
        ? { estateKeys: continuation.estateKeys, includeVanished: continuation.includeVanished, compareOnly: continuation.compareOnly }
        : { ...(chosen && picked.length < all.length ? { estateKeys: picked } : {}), includeVanished, compareOnly })}
    >
      {!continuation && (
        <>
          <fieldset className="m-0 flex flex-col gap-2 border-0 p-0">
            <legend className="mb-1 text-sm font-medium text-navy">Estates</legend>
            {estates.options.length === 0 && <Muted>{estates.loading ? 'Reading the estates.' : 'No estate is configured.'}</Muted>}
            {estates.options.map(o => (
              <div key={o.key} className="flex items-center gap-2">
                <Checkbox id={`estate-${o.key}`} checked={picked.includes(o.key)} onCheckedChange={() => toggle(o.key)} />
                <Label htmlFor={`estate-${o.key}`} className="font-normal">{o.label}</Label>
              </div>
            ))}
          </fieldset>
          <Field label="Only these environments" name="environmentIds" hint="Optional. Environment IDs, separated by commas or spaces. Leave empty for the whole estate.">
            {p => <Input {...p} value={environments} onChange={e => setEnvironments(e.target.value)} className="font-mono" />}
          </Field>
          <div className="flex items-start gap-2">
            <Checkbox id="gap-vanished" checked={includeVanished} onCheckedChange={v => setIncludeVanished(v === true)} className="mt-0.5" />
            <Label htmlFor="gap-vanished" className="flex-col items-start gap-0.5 font-normal">
              Also look for registered items that have gone
              <span className="text-xs text-navy-3">Raised only once the last page of every estate is read.</span>
            </Label>
          </div>
          <div className="flex items-start gap-2">
            <Checkbox id="gap-compare" checked={compareOnly} onCheckedChange={v => setCompareOnly(v === true)} className="mt-0.5" />
            <Label htmlFor="gap-compare" className="flex-col items-start gap-0.5 font-normal">
              Compare only
              <span className="text-xs text-navy-3">Shows what it finds and opens no claim.</span>
            </Label>
          </div>
        </>
      )}
      <Muted>The product reads the estate only as you, and only with an admin role you hold. If the directory reads are off, compare a list from your own tool instead.</Muted>
    </ActionSheet>
  );
}

const LIST_EXAMPLE = 'flow-6c1e | Expense reminder | Cloud flow | Finance Production | https://make.powerautomate.com/...';

// The same comparison over a list the admin exported from their own tool (the CoE kit, an admin center export). The run
// is marked as reported, not read.
export function CompareListSheet({ trigger }: { trigger: ReactElement }) {
  const estates = useListValues('estate');
  const kinds = useLabels('component-kind');
  const [estate, setEstate] = useState('');
  const [source, setSource] = useState('');
  const [text, setText] = useState('');
  const [whole, setWhole] = useState(false);
  const sheet = useSheet(() => { setEstate(''); setSource(''); setText(''); setWhole(false); });
  const kindList = useMemo(() => kinds.keys().map(k => ({ key: k, label: kinds.label(k) })), [kinds]);
  const parsed = useMemo(() => parseInventoryList(text, kindList), [text, kindList]);
  const chosenEstate = estate || estates.options[0]?.key || '';
  return (
    <ActionSheet
      tool="compare_inventory_list" site="pages/deployments-places/sheets#CompareListSheet"
      title="Compare a list from your own tool"
      description="Paste the items your own tool lists. The product compares them with the register and opens a claim for each item no application holds. The run is marked as reported."
      trigger={trigger}
      open={sheet.open}
      onOpenChange={sheet.onOpenChange}
      size="wide"
      input={() => {
        if (!chosenEstate) return { problem: 'Choose the estate the list covers.', field: 'estateKey' };
        const note = source.trim();
        if (!note) return { problem: 'Say where the list came from.', field: 'sourceNote' };
        if (!parsed.items.length) return { problem: parsed.problems.length ? `No line could be read. Line ${parsed.problems[0].line}: ${parsed.problems[0].problem}` : 'Paste at least one item.', field: 'items' };
        if (parsed.problems.length) return { problem: `${plural(parsed.problems.length, 'line', 'lines')} could not be read. Fix or remove ${parsed.problems.length === 1 ? 'it' : 'them'} first.`, field: 'items' };
        return { estateKey: chosenEstate, sourceNote: note.slice(0, 500), items: parsed.items, isFinalPage: whole };
      }}
      submitLabel="Compare it"
      preview
    >
      <div className="grid grid-cols-2 gap-3 narrow:grid-cols-1">
        <SelectField label="Estate" name="estateKey" value={chosenEstate} onChange={setEstate} choices={estates.choices} required />
        <Field label="Where the list came from" name="sourceNote" required>
          {p => <Input {...p} value={source} onChange={e => setSource(e.target.value)} maxLength={500} placeholder="CoE kit export, 24 Nov" />}
        </Field>
      </div>
      <Field
        label="The items"
        name="items"
        required
        hint={(
          <span className="flex flex-col gap-0.5">
            <span>One item per line: ID, name, kind, where it runs, link, and the object ID of who should claim it. The ID, the name and the kind are needed. Separate them with a tab, a vertical bar or a comma.</span>
            <span className="font-mono [overflow-wrap:anywhere]">{LIST_EXAMPLE}</span>
          </span>
        )}
      >
        {p => <Textarea {...p} rows={8} value={text} onChange={e => setText(e.target.value)} className="font-mono text-xs" spellCheck={false} />}
      </Field>
      {text.trim() && (
        <div className="flex flex-col gap-1" aria-live="polite">
          <span className="flex flex-wrap items-center gap-2 text-sm text-navy">
            <ToneBadge tone={parsed.problems.length ? 'warning' : 'success'}>{`${plural(parsed.items.length, 'item', 'items')} read`}</ToneBadge>
            {parsed.problems.length > 0 && <span className="text-navy-2">{`${plural(parsed.problems.length, 'line', 'lines')} could not be read`}</span>}
          </span>
          {parsed.problems.length > 0 && (
            <ul className="m-0 flex list-disc flex-col gap-0.5 pl-5 text-xs text-navy-2">
              {parsed.problems.slice(0, 6).map(p => <li key={p.line}>{`Line ${p.line}: ${p.problem}`}</li>)}
              {parsed.problems.length > 6 && <li>{`And ${parsed.problems.length - 6} more.`}</li>}
            </ul>
          )}
        </div>
      )}
      <div className="flex items-start gap-2">
        <Checkbox id="list-whole" checked={whole} onCheckedChange={v => setWhole(v === true)} className="mt-0.5" />
        <Label htmlFor="list-whole" className="flex-col items-start gap-0.5 font-normal">
          This list is the whole estate
          <span className="text-xs text-navy-3">Only then does a registered item the list leaves out show as vanished. Leave it off for a partial list.</span>
        </Label>
      </div>
    </ActionSheet>
  );
}

// Asks the directory about every owner the product holds: who is disabled, deleted or leaving. Each finding opens an
// owner check review on the applications it touches.
export function OwnerCheckSheet({ trigger }: { trigger: ReactElement }) {
  const sheet = useSheet();
  return (
    <ActionSheet
      tool="run_owner_check" site="pages/deployments-places/sheets#OwnerCheckSheet"
      title="Check the owners in the directory"
      description="Asks the directory, as you, about every owner the product holds. An owner who is disabled, deleted or leaving opens an owner check review on each application they own."
      trigger={trigger}
      open={sheet.open}
      onOpenChange={sheet.onOpenChange}
      input={() => ({})}
      submitLabel="Check the owners"
      preview
    >
      <Muted>The Owners page lists the leavers and the gaps the check finds.</Muted>
    </ActionSheet>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// link_component: a vanished item is gone, or it moved
// ---------------------------------------------------------------------------------------------------------------------

type VanishedOutcome = 'retired' | 'moved';

// A vanished claim names a component the register holds that the estate no longer shows. The claims list does not say
// which application holds it, so the person chooses the application and the component (the one with the claim's name is
// chosen for them). Gone: the component is marked retired. Moved: a new component replaces it. Still there is a
// dismissal (dismiss_claim), a separate sheet.
export function ResolveVanishedSheet({ claim, trigger, open, onOpenChange }: { claim: ClaimItem; trigger?: ReactElement; open?: boolean; onOpenChange?: (open: boolean) => void }) {
  const kinds = useLabels('component-kind');
  const [outcome, setOutcome] = useState<VanishedOutcome>('retired');
  const [app, setApp] = useState<AppChoice | null>(null);
  const [componentId, setComponentId] = useState('');
  const [externalId, setExternalId] = useState('');
  const [url, setUrl] = useState('');
  const [kind, setKind] = useState('');
  const [label, setLabel] = useState(claim.labelAsSeen);
  const [note, setNote] = useState('');
  const own = useSheet();
  const isOpen = open ?? own.open;
  const reset = () => { setOutcome('retired'); setApp(null); setComponentId(''); setExternalId(''); setUrl(''); setKind(''); setLabel(claim.labelAsSeen); setNote(''); };
  const card = useTool('get_application_card', isOpen && app ? { businessApplicationId: app.id, sections: ['components'] } : null);
  const components = dataOf(card)?.components ?? [];
  const guessed = guessComponent(components, claim.labelAsSeen);
  const chosen = componentId || guessed || '';
  const old = components.find(c => sameId(c.technicalComponentId, chosen)) ?? null;
  const chosenKind = kind || old?.kind.key || claim.componentKind?.key || '';
  return (
    <ActionSheet
      tool="link_component" site="pages/deployments-places/sheets#ResolveVanishedSheet"
      title={`Resolve: ${claim.labelAsSeen}`}
      description="The gap check no longer finds this item in the estate. Say whether it is gone or has moved."
      trigger={trigger}
      open={isOpen}
      onOpenChange={next => { (onOpenChange ?? own.onOpenChange)(next); if (!next) reset(); }}
      size="wide"
      input={() => {
        if (!app) return { problem: 'Choose the application that holds it.', field: 'businessApplicationId' };
        if (!chosen) return { problem: 'Choose the component the gap check no longer finds.', field: outcome === 'retired' ? 'technicalComponentId' : 'replacesComponentId' };
        const n = note.trim();
        if (outcome === 'retired') {
          return { businessApplicationId: app.id, outcome, technicalComponentId: chosen, claimId: claim.claimId, ...(n ? { note: n } : {}) };
        }
        const id = externalId.trim();
        const link = url.trim();
        if (!chosenKind) return { problem: 'Choose the kind of the new component.', field: 'componentKindKey' };
        if (!id) return { problem: 'Give the new ID in its tool.', field: 'externalId' };
        if (!/^https:\/\//.test(link)) return { problem: 'Give the full https address of where it lives now.', field: 'url' };
        return {
          businessApplicationId: app.id,
          outcome,
          replacesComponentId: chosen,
          componentKindKey: chosenKind,
          externalId: id,
          url: link,
          ...(label.trim() ? { labelAsSeen: label.trim() } : {}),
          claimId: claim.claimId,
          ...(n ? { note: n } : {}),
        };
      }}
      submitLabel={outcome === 'retired' ? 'Mark it gone' : 'Link its new place'}
      preview
    >
      <fieldset className="m-0 flex flex-col gap-2 border-0 p-0">
        <legend className="mb-1 text-sm font-medium text-navy">What happened to it</legend>
        <label className="flex cursor-pointer items-center gap-2 text-sm text-navy">
          <input type="radio" name="vanished-outcome" className={radioClass} checked={outcome === 'retired'} onChange={() => setOutcome('retired')} />
          It is gone: mark the component retired
        </label>
        <label className="flex cursor-pointer items-center gap-2 text-sm text-navy">
          <input type="radio" name="vanished-outcome" className={radioClass} checked={outcome === 'moved'} onChange={() => setOutcome('moved')} />
          It moved: link where it lives now
        </label>
      </fieldset>
      <AppField label="Application that holds it" name="businessApplicationId" value={app} onChange={next => { setApp(next); setComponentId(''); }} required />
      {app && (
        <Field label="The component" name={outcome === 'retired' ? 'technicalComponentId' : 'replacesComponentId'} required hint={guessed ? `Chosen by its name, ${claim.labelAsSeen}.` : undefined}>
          {p => (
            card.status === 'loading' ? <Muted>Reading the components.</Muted>
              : card.status === 'done' && !card.envelope.ok ? <Muted>{`The components could not be read: ${card.envelope.error.message}`}</Muted>
                : components.length === 0 ? <Muted>{`${app.name} has no component in the register.`}</Muted>
                  : (
                    <div role="radiogroup" aria-labelledby={p.id} aria-describedby={p['aria-describedby']} className="flex flex-col gap-1">
                      <span id={p.id} className="sr-only">The component</span>
                      {components.map(c => (
                        <label key={c.technicalComponentId} className={cn('flex cursor-pointer items-center gap-2 rounded-md border px-3 py-2 text-sm text-navy', sameId(chosen, c.technicalComponentId) ? 'border-primary bg-info-soft' : 'border-line')}>
                          <input type="radio" name="vanished-component" className={radioClass} checked={sameId(chosen, c.technicalComponentId)} onChange={() => setComponentId(c.technicalComponentId)} />
                          <span className="flex min-w-0 flex-col">
                            <span className="[overflow-wrap:anywhere]">{c.labelAsSeen ?? c.externalId}</span>
                            <span className="text-xs text-navy-3">{c.kind.label}</span>
                          </span>
                        </label>
                      ))}
                    </div>
                  )
          )}
        </Field>
      )}
      {outcome === 'moved' && (
        <>
          <h3 className={subhead}>Where it lives now</h3>
          <div className="grid grid-cols-2 gap-3 narrow:grid-cols-1">
            <SelectField label="Kind" name="componentKindKey" value={chosenKind} onChange={setKind} choices={choicesOf(kinds)} required />
            <Field label="ID in its tool" name="externalId" required>
              {p => <Input {...p} value={externalId} onChange={e => setExternalId(e.target.value)} maxLength={400} className="font-mono" />}
            </Field>
          </div>
          <Field label="Link" name="url" required>
            {p => <Input {...p} type="url" inputMode="url" value={url} onChange={e => setUrl(e.target.value)} placeholder="https://" maxLength={2000} />}
          </Field>
          <Field label="Name" name="labelAsSeen">
            {p => <Input {...p} value={label} onChange={e => setLabel(e.target.value)} maxLength={400} />}
          </Field>
        </>
      )}
      <Field label="Note" name="note" hint="Optional.">
        {p => <Textarea {...p} rows={2} maxLength={1000} value={note} onChange={e => setNote(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}
