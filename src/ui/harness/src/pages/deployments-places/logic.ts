// Pure helpers for the Deployments, Places and Gap check pages and the deployment and claim record pages. No React here,
// so the unit tests run them in Node (logic.test.ts).
import type { DeploymentFlagRouteKey, GapCheckView, RouteOf } from '../../app/routes';
import type {
  ClaimItem,
  CodeApprovalKind,
  CompareInventoryListInput,
  DeploymentFlags,
  DeploymentItem,
  DeploymentResultMeaning,
  DeploymentTargetItem,
  IsoDate,
  IsoDateTime,
  Key,
  PipelineMapEntry,
} from '../../data/contracts';
import { addDays } from '../../data/format';
import type { WriteInput } from '../../data/writes';
import { isHttpsUrl, localInputToUtc, utcToLocalInput, type Problem } from '../changes-records/logic';

export type { Problem } from '../changes-records/logic';
export { isProblem } from '../changes-records/logic';

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
// The exception decision that already covers a deployment (QA 2026-11-24): one proposed or accepted about the record
// itself, or an accepted one about its application. Null when none does, and "Propose an exception" is then offered.
export interface ExceptionLike {
  decisionId: string;
  subjectKind: string;
  subjectId: string;
  status: { meaning: string };
  recordUrl: string;
}

export function exceptionCovering<T extends ExceptionLike>(exceptions: readonly T[], deploymentRecordId: string, applicationId: string | null): T | null {
  return exceptions.find(e => e.subjectKind === 'deployment-record' && sameId(e.subjectId, deploymentRecordId) && (e.status.meaning === 'proposed' || e.status.meaning === 'accepted'))
    ?? exceptions.find(e => !!applicationId && e.subjectKind === 'business-application' && sameId(e.subjectId, applicationId) && e.status.meaning === 'accepted')
    ?? null;
}

export const sameId = (a: string | null | undefined, b: string | null | undefined) => !!a && !!b && a.toLowerCase() === b.toLowerCase();

// ---------- deployment flags ----------

// The flag keys of the address and get_deployments (flagKeys), and the field each one sets on a record.
export const FLAG_FIELD: Record<DeploymentFlagRouteKey, keyof DeploymentFlags> = {
  'missing-approval': 'missingApproval',
  'outside-window': 'outsideWindow',
  'other-path': 'otherPath',
  'ai-only-code-approval': 'aiOnlyCodeApproval',
};

export const FLAG_KEY_OF: Record<keyof DeploymentFlags, DeploymentFlagRouteKey> = {
  missingApproval: 'missing-approval',
  outsideWindow: 'outside-window',
  otherPath: 'other-path',
  aiOnlyCodeApproval: 'ai-only-code-approval',
};

// The order the pages list the flags in.
export const FLAG_ORDER: (keyof DeploymentFlags)[] = ['missingApproval', 'outsideWindow', 'otherPath', 'aiOnlyCodeApproval'];

export const isFlagged = (flags: DeploymentFlags) => FLAG_ORDER.some(f => flags[f]);

// A flag's help and its follow-up as one text, leaving out a follow-up sentence that says again what the help said
// ("It never counts as authorization" and "An AI approval never counts as authorization").
export function flagExplanation(help: string, followUp: string): string {
  const said = help.toLowerCase();
  const sentences = followUp.split(/(?<=\.)\s+/).filter(Boolean);
  const kept = sentences.filter(sentence => {
    const core = sentence.toLowerCase().replace(/\.$/, '').split(/\s+/).slice(-4).join(' ');
    return !said.includes(core);
  });
  return [help, ...kept].join(' ');
}

// ---------- the deployments list ----------

// The page's own address parameters, beside the route's shared filters: a date range (a preset number of days, or from
// and to), a result, and latest=1 for the latest record per application and target only.
export const DATE_PARAM = /^\d{4}-\d{2}-\d{2}$/;
export const WITHIN_PARAM = /^(7|30|90|365)$/;
export const RESULT_PARAM = /^(succeeded|partly-succeeded|failed|not-deployed)$/;
export const LATEST_PARAM = /^1$/;

export const RESULT_MEANINGS: DeploymentResultMeaning[] = ['succeeded', 'partly-succeeded', 'failed', 'not-deployed'];

// The fixed engine result meanings in words, for the filter and the forms. The label a record shows comes from its own
// status value (configuration).
export const RESULT_TEXT: Record<DeploymentResultMeaning, string> = {
  succeeded: 'Succeeded',
  'partly-succeeded': 'Partly succeeded',
  failed: 'Failed',
  'not-deployed': 'Not deployed',
};

export interface DeploymentListParams {
  within?: string;
  from?: string;
  to?: string;
  result?: string;
  latest?: boolean;
  // Wave 4: the portfolio (get_deployments portfolioId, the portfolios below it included).
  portfolio?: string;
}

// The finished-date range the list asks for: a preset counts back from today; else the dates given. Null ends are open.
export function dateRangeOf(params: DeploymentListParams, today: IsoDate): { from: IsoDate | null; to: IsoDate | null } {
  if (params.within && WITHIN_PARAM.test(params.within)) return { from: addDays(today, -Number(params.within)), to: null };
  const from = params.from && DATE_PARAM.test(params.from) ? params.from : null;
  const to = params.to && DATE_PARAM.test(params.to) ? params.to : null;
  return { from, to };
}

// The get_deployments input for the list: the route's filters and the page's own. The counts the tool returns are for
// the same filters before the flag filters, so the figures above the list stay put while a flag is chosen.
export function deploymentsQuery(route: RouteOf<'deployments'>, params: DeploymentListParams, today: IsoDate, top = 50): Record<string, unknown> {
  const range = dateRangeOf(params, today);
  const input: Record<string, unknown> = { latestOnly: params.latest === true };
  if (route.view === 'flagged') input.flaggedOnly = true;
  if (route.flagKeys?.length) input.flagKeys = route.flagKeys;
  if (route.applicationId) input.businessApplicationId = route.applicationId;
  if (route.targetId) input.deploymentTargetId = route.targetId;
  if (route.changeRequestId) input.changeRequestId = route.changeRequestId;
  if (params.portfolio) input.portfolioId = params.portfolio;
  if (params.result && RESULT_PARAM.test(params.result)) input.resultMeanings = [params.result];
  if (range.from) input.from = range.from;
  if (range.to) input.to = range.to;
  input.top = top;
  return input;
}

// The words a deployment holds for the list's text search: the application, the artifact and its versions, the target
// and the AI client that started it.
export function deploymentText(d: Pick<DeploymentItem, 'applicationName' | 'artifactName' | 'artifactVersion' | 'versionAfter' | 'targetLabel' | 'startedByAiClient'>): (string | null)[] {
  return [d.applicationName, d.artifactName, d.artifactVersion, d.versionAfter, d.targetLabel, d.startedByAiClient];
}

// The records the loaded rows show as corrected: another loaded row names them in Corrects Record.
export function correctedIds(items: Pick<DeploymentItem, 'correctsRecordId'>[]): Set<string> {
  return new Set(items.map(i => i.correctsRecordId?.toLowerCase()).filter((id): id is string => !!id));
}

// ---------- recording a deployment by hand (path C, D-19) ----------

export interface DeploymentForm {
  targetId: string;
  artifactName: string;
  artifactVersion: string;
  artifactKindKey: string;
  changeRequestId: string;
  pipelineReferenceId: string;
  // datetime-local values in the viewer's zone.
  startedOn: string;
  finishedOn: string;
  resultMeaning: DeploymentResultMeaning | '';
  versionBefore: string;
  versionAfter: string;
  runUrl: string;
  sourceCommit: string;
  codeApprovalKind: CodeApprovalKind;
}

export const emptyDeploymentForm = (now: Date = new Date()): DeploymentForm => ({
  targetId: '',
  artifactName: '',
  artifactVersion: '',
  artifactKindKey: '',
  changeRequestId: '',
  pipelineReferenceId: '',
  startedOn: '',
  finishedOn: utcToLocalInput(now.toISOString()),
  resultMeaning: 'succeeded',
  versionBefore: '',
  versionAfter: '',
  runUrl: '',
  sourceCommit: '',
  codeApprovalKind: 'unknown',
});

// A little leeway for a clock that runs ahead.
const FUTURE_LEEWAY_MS = 5 * 60 * 1000;

// The record_deployment input for the form, or the first problem the page can see itself. The engine sets the route
// (person in the app), the flags and the gate level; the person cannot.
export function buildDeploymentInput(form: DeploymentForm, businessApplicationId: string, nowMs: number = Date.now()): WriteInput<'record_deployment'> | Problem {
  if (!form.targetId) return { problem: 'Choose the target it reached.', field: 'deploymentTargetId' };
  const name = form.artifactName.trim();
  if (!name) return { problem: 'Give the artifact\'s name, such as the solution or image name.', field: 'artifactName' };
  if (name.length > 200) return { problem: 'The artifact name is longer than 200 characters.', field: 'artifactName' };
  const version = form.artifactVersion.trim();
  if (!version) return { problem: 'Give the version that was deployed.', field: 'artifactVersion' };
  if (version.length > 100) return { problem: 'The version is longer than 100 characters.', field: 'artifactVersion' };
  if (!form.resultMeaning) return { problem: 'Choose how it ended.', field: 'resultMeaning' };
  const finished = localInputToUtc(form.finishedOn);
  if (!finished) return { problem: 'Give the date and time it finished.', field: 'finishedOn' };
  if (Date.parse(finished) > nowMs + FUTURE_LEEWAY_MS) return { problem: 'It finished in the future. Record a deployment once it has run.', field: 'finishedOn' };
  const started = form.startedOn ? localInputToUtc(form.startedOn) : null;
  if (form.startedOn && !started) return { problem: 'The start is not a date and time.', field: 'startedOn' };
  if (started && Date.parse(started) > Date.parse(finished)) return { problem: 'It starts after it finishes.', field: 'startedOn' };
  const runUrl = form.runUrl.trim();
  if (runUrl && !isHttpsUrl(runUrl)) return { problem: 'The run link must be a full https address.', field: 'runUrl' };
  const before = form.versionBefore.trim();
  const after = form.versionAfter.trim();
  if (before.length > 100 || after.length > 100) return { problem: 'A version is longer than 100 characters.', field: before.length > 100 ? 'versionBefore' : 'versionAfter' };
  const commit = form.sourceCommit.trim();
  if (commit.length > 64) return { problem: 'The commit is longer than 64 characters.', field: 'sourceCommit' };

  const input: WriteInput<'record_deployment'> = {
    businessApplicationId,
    deploymentTargetId: form.targetId,
    artifactName: name,
    artifactVersion: version,
    finishedOn: finished,
    resultMeaning: form.resultMeaning,
    codeApprovalKind: form.codeApprovalKind,
  };
  if (form.artifactKindKey) input.artifactKindKey = form.artifactKindKey;
  if (form.changeRequestId) input.changeRequestId = form.changeRequestId;
  if (form.pipelineReferenceId) input.pipelineReferenceId = form.pipelineReferenceId;
  if (started) input.startedOn = started;
  if (before) input.versionBefore = before;
  if (after) input.versionAfter = after;
  if (runUrl) input.runUrl = runUrl;
  if (commit) input.sourceCommit = commit;
  return input;
}

// ---------- a correction ----------

export interface CorrectionForm {
  reason: string;
  // '' keeps the record's own change; NO_CHANGE says it belongs to no change.
  changeRequestId: string;
  resultMeaning: DeploymentResultMeaning | '';
  artifactVersion: string;
  finishedOn: string;
  runUrl: string;
}

export const correctionFormOf = (record: Pick<DeploymentItem, 'changeRequestId' | 'result' | 'artifactVersion' | 'finishedOn' | 'runUrl'>): CorrectionForm => ({
  reason: '',
  changeRequestId: record.changeRequestId?.toLowerCase() ?? '',
  resultMeaning: RESULT_MEANINGS.includes(record.result.meaning as DeploymentResultMeaning) ? (record.result.meaning as DeploymentResultMeaning) : '',
  artifactVersion: record.artifactVersion,
  finishedOn: utcToLocalInput(record.finishedOn),
  runUrl: record.runUrl ?? '',
});

// The record_deployment_correction input: only the facts that differ from the record, with the reason. A correction is
// a new record that names the old one; the old one never changes.
export function buildCorrectionInput(form: CorrectionForm, record: Pick<DeploymentItem, 'deploymentRecordId' | 'changeRequestId' | 'result' | 'artifactVersion' | 'finishedOn' | 'runUrl'>): WriteInput<'record_deployment_correction'> | Problem {
  const reason = form.reason.trim();
  if (!reason) return { problem: 'Say what was wrong, or why the record belongs to another change.', field: 'reason' };
  const input: WriteInput<'record_deployment_correction'> = { correctsDeploymentRecordId: record.deploymentRecordId, reason };
  if (form.changeRequestId && !sameId(form.changeRequestId, record.changeRequestId)) input.changeRequestId = form.changeRequestId;
  if (form.resultMeaning && form.resultMeaning !== record.result.meaning) input.resultMeaning = form.resultMeaning;
  const version = form.artifactVersion.trim();
  if (version.length > 100) return { problem: 'The version is longer than 100 characters.', field: 'artifactVersion' };
  if (version && version !== record.artifactVersion) input.artifactVersion = version;
  if (form.finishedOn) {
    const finished = localInputToUtc(form.finishedOn);
    if (!finished) return { problem: 'The finish is not a date and time.', field: 'finishedOn' };
    if (form.finishedOn !== utcToLocalInput(record.finishedOn)) input.finishedOn = finished;
  }
  const runUrl = form.runUrl.trim();
  if (runUrl && !isHttpsUrl(runUrl)) return { problem: 'The run link must be a full https address.', field: 'runUrl' };
  if (runUrl && runUrl !== (record.runUrl ?? '')) input.runUrl = runUrl;
  if (Object.keys(input).length === 2) {
    return { problem: 'Change at least one fact: the change it belongs to, the result, the version, the finish time or the run link.', field: 'changeRequestId' };
  }
  return input;
}

// ---------- deployment targets ----------

export interface TargetForm {
  label: string;
  targetKindKey: string;
  targetStageKey: string;
  changePathKey: string;
  linkKindKey: string;
  externalId: string;
  url: string;
  description: string;
}

export const emptyTargetForm = (): TargetForm => ({ label: '', targetKindKey: '', targetStageKey: '', changePathKey: '', linkKindKey: '', externalId: '', url: '', description: '' });

export const targetFormOf = (t: DeploymentTargetItem): TargetForm => ({
  label: t.label,
  targetKindKey: t.targetKind?.key ?? '',
  targetStageKey: t.targetStage.key,
  changePathKey: t.changePath.key,
  linkKindKey: t.linkKind.key,
  externalId: t.externalId ?? '',
  url: t.url ?? '',
  description: '',
});

// The save_deployment_target input. A new target needs every key, its name and its ID in its tool; a change sends only
// the fields that differ, so a field the reader cannot see (the ID) is never cleared by mistake.
export function buildTargetInput(form: TargetForm, existing?: DeploymentTargetItem | null): WriteInput<'save_deployment_target'> | Problem {
  const label = form.label.trim();
  const externalId = form.externalId.trim();
  const url = form.url.trim();
  const description = form.description.trim();
  if (!label) return { problem: 'Give the target its name.', field: 'labelAsSeen' };
  if (label.length > 200) return { problem: 'The name is longer than 200 characters.', field: 'labelAsSeen' };
  if (!form.targetKindKey) return { problem: 'Choose what kind of place it is.', field: 'targetKindKey' };
  if (!form.targetStageKey) return { problem: 'Choose the stage: it sets the gate level a deployment there meets.', field: 'targetStageKey' };
  if (!form.changePathKey) return { problem: 'Choose the change path: it sets which routes are flagged.', field: 'changePathKey' };
  if (!form.linkKindKey) return { problem: 'Choose the kind of link.', field: 'linkKindKey' };
  if (!existing && !externalId) return { problem: 'Give its ID in its own tool, such as the environment ID.', field: 'externalId' };
  if (externalId.length > 400) return { problem: 'The ID is longer than 400 characters.', field: 'externalId' };
  if (url && !isHttpsUrl(url)) return { problem: 'The link must be a full https address.', field: 'url' };
  if (description.length > 2000) return { problem: 'The description is longer than 2,000 characters.', field: 'description' };

  if (!existing) {
    return {
      labelAsSeen: label,
      targetKindKey: form.targetKindKey,
      targetStageKey: form.targetStageKey,
      changePathKey: form.changePathKey,
      linkKindKey: form.linkKindKey,
      externalId,
      ...(url ? { url } : {}),
      ...(description ? { description } : {}),
    };
  }
  const input: WriteInput<'save_deployment_target'> = { deploymentTargetId: existing.deploymentTargetId };
  if (label !== existing.label) input.labelAsSeen = label;
  if (form.targetKindKey !== (existing.targetKind?.key ?? '')) input.targetKindKey = form.targetKindKey;
  if (form.targetStageKey !== existing.targetStage.key) input.targetStageKey = form.targetStageKey;
  if (form.changePathKey !== existing.changePath.key) input.changePathKey = form.changePathKey;
  if (form.linkKindKey !== existing.linkKind.key) input.linkKindKey = form.linkKindKey;
  if (externalId && externalId !== (existing.externalId ?? '')) input.externalId = externalId;
  if (url && url !== (existing.url ?? '')) input.url = url;
  if (description) input.description = description;
  if (Object.keys(input).length === 1) return { problem: 'Nothing is changed yet.', field: 'labelAsSeen' };
  return input;
}

export interface StageGroup<T> {
  stage: { key: Key; label: string; rank: number };
  items: T[];
}

// Targets grouped by stage, in the configured stage order (rank), names in order inside a stage.
export function groupByStage<T extends Pick<DeploymentTargetItem, 'targetStage' | 'label'>>(targets: T[]): StageGroup<T>[] {
  const groups = new Map<string, StageGroup<T>>();
  for (const t of targets) {
    const g = groups.get(t.targetStage.key) ?? { stage: t.targetStage, items: [] };
    g.items.push(t);
    groups.set(t.targetStage.key, g);
  }
  return [...groups.values()]
    .sort((a, b) => a.stage.rank - b.stage.rank || a.stage.label.localeCompare(b.stage.label))
    .map(g => ({ ...g, items: [...g.items].sort((a, b) => a.label.localeCompare(b.label)) }));
}

// The applications that have a deployment on any target, from the targets' latest versions: the choices of the list's
// application filter.
export function appsFromTargets(targets: Pick<DeploymentTargetItem, 'applications'>[]): { id: string; name: string }[] {
  const seen = new Map<string, string>();
  for (const t of targets) {
    for (const a of t.applications) {
      if (a.businessApplicationId && !seen.has(a.businessApplicationId.toLowerCase())) seen.set(a.businessApplicationId.toLowerCase(), a.name || 'Unnamed application');
    }
  }
  return [...seen.entries()].map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name));
}

export interface PipelineSeen {
  pipelineReferenceId: string;
  label: string;
  url: string | null;
  targets: { deploymentTargetId: string; label: string }[];
}

// The pipelines mapped to the targets, each once, with the targets it reaches. No read lists every pipeline reference, so
// a pipeline mapped to no target does not show.
export function pipelinesFromTargets(targets: Pick<DeploymentTargetItem, 'deploymentTargetId' | 'label' | 'pipelines'>[]): PipelineSeen[] {
  const seen = new Map<string, PipelineSeen>();
  for (const t of targets) {
    for (const p of t.pipelines) {
      const key = p.pipelineReferenceId.toLowerCase();
      const entry = seen.get(key) ?? { pipelineReferenceId: p.pipelineReferenceId, label: p.label, url: p.url, targets: [] };
      if (!entry.targets.some(x => sameId(x.deploymentTargetId, t.deploymentTargetId))) entry.targets.push({ deploymentTargetId: t.deploymentTargetId, label: t.label });
      seen.set(key, entry);
    }
  }
  return [...seen.values()].sort((a, b) => a.label.localeCompare(b.label));
}

// A pipeline's map entries per application, each application's targets in stage order.
export function entriesByApp(entries: PipelineMapEntry[]): { businessApplicationId: string; applicationName: string; steps: PipelineMapEntry[] }[] {
  const apps = new Map<string, { businessApplicationId: string; applicationName: string; steps: PipelineMapEntry[] }>();
  for (const e of entries) {
    const key = e.businessApplicationId.toLowerCase();
    const a = apps.get(key) ?? { businessApplicationId: e.businessApplicationId, applicationName: e.applicationName || 'Unnamed application', steps: [] };
    a.steps.push(e);
    apps.set(key, a);
  }
  return [...apps.values()]
    .map(a => ({ ...a, steps: [...a.steps].sort((x, y) => x.stageOrder - y.stageOrder) }))
    .sort((a, b) => a.applicationName.localeCompare(b.applicationName));
}

// Whole days since a moment, from today's date; null without a moment.
export function daysSince(moment: IsoDateTime | null | undefined, today: IsoDate): number | null {
  if (!moment) return null;
  const ms = Date.parse(`${today}T00:00:00Z`) - Date.parse(`${moment.slice(0, 10)}T00:00:00Z`);
  return Number.isFinite(ms) ? Math.max(0, Math.round(ms / 86400000)) : null;
}

// ---------- the pipeline map ----------

export interface PipelineEntryForm {
  businessApplicationId: string;
  applicationName: string;
  deploymentTargetId: string;
  stageOrder: string;
}

export interface PipelineForm {
  linkKindKey: string;
  externalId: string;
  url: string;
  label: string;
  entries: PipelineEntryForm[];
}

export const emptyPipelineForm = (): PipelineForm => ({ linkKindKey: '', externalId: '', url: '', label: '', entries: [] });

// The map_pipeline input. Each entry is one application reaching one target at its place in the pipeline's order.
export function buildPipelineInput(form: PipelineForm, pipelineReferenceId?: string | null): WriteInput<'map_pipeline'> | Problem {
  if (!form.linkKindKey) return { problem: 'Choose the tool the pipeline runs in.', field: 'linkKindKey' };
  const externalId = form.externalId.trim();
  if (!externalId) return { problem: 'Give the pipeline\'s ID in its own tool.', field: 'externalId' };
  if (externalId.length > 400) return { problem: 'The ID is longer than 400 characters.', field: 'externalId' };
  const url = form.url.trim();
  if (!isHttpsUrl(url)) return { problem: 'Give the full https address that opens the pipeline.', field: 'url' };
  const label = form.label.trim();
  if (label.length > 200) return { problem: 'The name is longer than 200 characters.', field: 'labelAsSeen' };
  if (!form.entries.length) return { problem: 'Add at least one application and the target the pipeline deploys it to.', field: 'entries' };
  const seen = new Set<string>();
  const entries: { businessApplicationId: string; deploymentTargetId: string; stageOrder: number }[] = [];
  for (const [i, e] of form.entries.entries()) {
    if (!e.businessApplicationId) return { problem: `Step ${i + 1}: choose the application.`, field: 'entries.businessApplicationId' };
    if (!e.deploymentTargetId) return { problem: `Step ${i + 1}: choose the target.`, field: 'entries.deploymentTargetId' };
    const order = Number(e.stageOrder);
    if (!Number.isInteger(order) || order < 1 || order > 99) return { problem: `Step ${i + 1}: the order is a whole number from 1 to 99.`, field: 'entries.stageOrder' };
    const key = `${e.businessApplicationId.toLowerCase()}|${e.deploymentTargetId.toLowerCase()}`;
    if (seen.has(key)) return { problem: `Step ${i + 1}: this application and target are listed twice.`, field: 'entries' };
    seen.add(key);
    entries.push({ businessApplicationId: e.businessApplicationId, deploymentTargetId: e.deploymentTargetId, stageOrder: order });
  }
  return {
    ...(pipelineReferenceId ? { pipelineReferenceId } : {}),
    linkKindKey: form.linkKindKey,
    externalId,
    url,
    ...(label ? { labelAsSeen: label } : {}),
    entries,
  };
}

// ---------- the gap check ----------

type InventoryItem = CompareInventoryListInput['items'][number];

export interface ParsedInventory {
  items: InventoryItem[];
  problems: { line: number; problem: string }[];
}

export const MAX_INVENTORY_ITEMS = 1000;

// One cell per column: tab, then a vertical bar, then a comma, whichever the line uses first.
function cellsOf(line: string): string[] {
  const sep = line.includes('\t') ? '\t' : line.includes('|') ? '|' : ',';
  return line.split(sep).map(c => c.trim());
}

// A list pasted from the admin's own tool, one item per line: ID, name, kind, where it runs, link, suggested claimant's
// object ID. Only the ID and the name are required. The kind is a component kind key or its label. A header line, blank
// lines and lines that start with # are skipped. Each problem names its line, and the items still read are kept.
export function parseInventoryList(text: string, kinds: { key: string; label: string }[]): ParsedInventory {
  const items: InventoryItem[] = [];
  const problems: { line: number; problem: string }[] = [];
  const ids = new Set<string>();
  const lines = text.split(/\r?\n/);
  for (const [i, raw] of lines.entries()) {
    const line = raw.trim();
    const n = i + 1;
    if (!line || line.startsWith('#')) continue;
    const [externalId = '', name = '', kind = '', where = '', link = '', claimant = ''] = cellsOf(line);
    if (items.length === 0 && problems.length === 0 && /^(id|external ?id)$/i.test(externalId)) continue;
    if (!externalId) { problems.push({ line: n, problem: 'No ID.' }); continue; }
    if (externalId.length > 400) { problems.push({ line: n, problem: 'The ID is longer than 400 characters.' }); continue; }
    if (!name) { problems.push({ line: n, problem: 'No name.' }); continue; }
    if (ids.has(externalId.toLowerCase())) { problems.push({ line: n, problem: `The ID ${externalId} is listed twice.` }); continue; }
    // The engine leaves out an item whose kind it does not know (OP-13, rule 5), so a line with no kind is named here
    // instead of being sent and silently left out (fix round 2: a two-line list answered "0 items seen").
    if (!kind) { problems.push({ line: n, problem: 'No kind. The gap check leaves out an item whose kind it does not know.' }); continue; }
    let componentKindKey: string | undefined;
    if (kind) {
      const match = kinds.find(k => k.key === kind || k.label.toLowerCase() === kind.toLowerCase());
      if (!match) { problems.push({ line: n, problem: `"${kind}" is not a component kind.` }); continue; }
      componentKindKey = match.key;
    }
    if (link && !isHttpsUrl(link)) { problems.push({ line: n, problem: 'The link is not a full https address.' }); continue; }
    if (claimant && !GUID.test(claimant)) { problems.push({ line: n, problem: 'The suggested claimant is not an object ID.' }); continue; }
    if (items.length >= MAX_INVENTORY_ITEMS) { problems.push({ line: n, problem: `Only ${MAX_INVENTORY_ITEMS} items go in one list.` }); break; }
    ids.add(externalId.toLowerCase());
    items.push({
      externalId,
      labelAsSeen: name.slice(0, 400),
      ...(componentKindKey ? { componentKindKey } : {}),
      ...(where ? { targetAsSeen: where.slice(0, 400) } : {}),
      ...(link ? { url: link } : {}),
      ...(claimant ? { suggestedClaimantObjectId: claimant.toLowerCase() } : {}),
    });
  }
  return { items, problems };
}

export interface ClaimCounts {
  // Open and not registered: to claim or dismiss.
  open: number;
  // Open and vanished: to resolve.
  vanished: number;
  claimed: number;
  dismissed: number;
}

export function claimCounts(items: Pick<ClaimItem, 'findingKind' | 'status'>[]): ClaimCounts {
  const counts: ClaimCounts = { open: 0, vanished: 0, claimed: 0, dismissed: 0 };
  for (const c of items) {
    const m = c.status.meaning;
    if (m === 'open') {
      if (c.findingKind === 'vanished') counts.vanished += 1;
      else counts.open += 1;
    } else if (m === 'claimed') counts.claimed += 1;
    else if (m === 'dismissed') counts.dismissed += 1;
  }
  return counts;
}

// The claims a view of the page lists. The runs view lists runs, not claims.
export function claimsInView<T extends Pick<ClaimItem, 'findingKind' | 'status'>>(items: T[], view: GapCheckView): T[] {
  switch (view) {
    case 'open':
      return items.filter(c => c.status.meaning === 'open' && c.findingKind === 'unregistered');
    case 'vanished':
      return items.filter(c => c.status.meaning === 'open' && c.findingKind === 'vanished');
    case 'claimed':
      return items.filter(c => c.status.meaning === 'claimed');
    case 'dismissed':
      return items.filter(c => c.status.meaning === 'dismissed');
    default:
      return [];
  }
}

// Claims by component kind, most first, for the bar list. A claim with no kind counts under null.
export function kindBuckets(items: Pick<ClaimItem, 'componentKind'>[]): { key: string | null; label: string; count: number }[] {
  const buckets = new Map<string, { key: string | null; label: string; count: number }>();
  for (const c of items) {
    const key = c.componentKind?.key ?? null;
    const b = buckets.get(key ?? '') ?? { key, label: c.componentKind?.label ?? 'Kind not known', count: 0 };
    b.count += 1;
    buckets.set(key ?? '', b);
  }
  return [...buckets.values()].sort((a, b) => b.count - a.count || a.label.localeCompare(b.label));
}

// The claims a run raised. A claim names no run in the list read, but its found date is the start of the run that raised
// it, so a claim found at the run's start belongs to it.
export function claimsOfRun<T extends Pick<ClaimItem, 'foundOn'>>(items: T[], startedOn: IsoDateTime | null): T[] {
  if (!startedOn) return [];
  const at = Date.parse(startedOn);
  return items.filter(c => Date.parse(c.foundOn) === at);
}

// The registered component a vanished claim most likely names: the one whose name the claim shows. Null when none or
// more than one match, so the person chooses.
export function guessComponent<T extends { technicalComponentId: string; labelAsSeen: string | null }>(components: T[], labelAsSeen: string): string | null {
  const wanted = labelAsSeen.trim().toLowerCase();
  const matches = components.filter(c => (c.labelAsSeen ?? '').trim().toLowerCase() === wanted);
  return matches.length === 1 ? matches[0].technicalComponentId : null;
}

// The estates of a run in words: "Power Platform inventory", or "as reported" for a list the admin pasted. A demo
// install's run reads the demo's fictional inventory, which the server marks "demo: ..." (wave 4b review): it is neither
// live nor read with anyone's admin role, so it says where it read from instead.
export function runSourceText(adminRoleUsed: string | null): { live: boolean; text: string } {
  if (!adminRoleUsed || adminRoleUsed === 'unknown') return { live: true, text: 'Read live. The admin role used was not reported.' };
  // A demo's fictional inventory (the gap check) or directory (the owner check, wave 4b): the marker names which.
  if (adminRoleUsed.startsWith('demo:')) {
    const what = /directory/.test(adminRoleUsed) ? 'directory' : 'inventory';
    return { live: false, text: `Read from the demo's fictional ${what}, not Microsoft's` };
  }
  if (adminRoleUsed.startsWith('as reported')) {
    const note = adminRoleUsed.replace(/^as reported:\s*/, '').trim().replace(/\.+$/, '');
    return { live: false, text: `As reported, from a list: ${note}` };
  }
  return { live: true, text: `Read live as ${adminRoleUsed}` };
}

// What a run left open, in words (wave 4b review): the open claims of items not registered and the vanished items still
// open, each counted apart (openVanishedLastSeen). A server before wave 4b sends one count for both.
export function lastSeenText(run: { openClaimsLastSeen: number; openVanishedLastSeen?: number }): string {
  const claims = `${run.openClaimsLastSeen} open ${run.openClaimsLastSeen === 1 ? 'claim' : 'claims'}`;
  if (typeof run.openVanishedLastSeen !== 'number') return `${claims} last seen in it.`;
  const vanished = `${run.openVanishedLastSeen} vanished ${run.openVanishedLastSeen === 1 ? 'item' : 'items'}`;
  return `${claims} not registered and ${vanished} last seen in it.`;
}

// ---------- retiring a target (save_deployment_target retire, document 24 B24-18) ----------

export type TargetState = 'all' | 'active' | 'retired';
export const TARGET_STATE_PARAM = /^(active|retired)$/;

export const isRetired = (t: Pick<DeploymentTargetItem, 'retiredOn'>): boolean => !!t.retiredOn;

// The targets in one state: in service (active), retired, or all.
export function targetsInState<T extends Pick<DeploymentTargetItem, 'retiredOn'>>(targets: T[], state: TargetState): T[] {
  if (state === 'active') return targets.filter(t => !isRetired(t));
  if (state === 'retired') return targets.filter(t => isRetired(t));
  return targets;
}

// The save_deployment_target input that retires a target (with the reason) or returns a retired one to service. Nothing
// else goes in the same call: the tool changes nothing else then.
export function buildRetireInput(target: Pick<DeploymentTargetItem, 'deploymentTargetId' | 'retiredOn'>, reason: string): WriteInput<'save_deployment_target'> | Problem {
  if (isRetired(target)) return { deploymentTargetId: target.deploymentTargetId, retire: false };
  const text = reason.trim();
  if (!text) return { problem: 'Say why the target is retired, such as the environment that replaced it.', field: 'retiredReason' };
  if (text.length > 2000) return { problem: 'The reason is longer than 2,000 characters.', field: 'retiredReason' };
  return { deploymentTargetId: target.deploymentTargetId, retire: true, retiredReason: text };
}
