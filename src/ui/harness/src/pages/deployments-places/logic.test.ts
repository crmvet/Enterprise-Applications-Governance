// The Deployments, Places and Gap check pages' own logic: the list query, the record-by-hand and correction inputs, the
// target and pipeline inputs, the pasted inventory list, and the claim counts and views.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { ClaimItem, DeploymentItem, DeploymentTargetItem, PipelineMapEntry } from '../../data/contracts';
import { localInputToUtc } from '../changes-records/logic';
import {
  appsFromTargets,
  buildCorrectionInput,
  buildDeploymentInput,
  buildPipelineInput,
  buildRetireInput,
  buildTargetInput,
  claimCounts,
  claimsInView,
  claimsOfRun,
  correctedIds,
  correctionFormOf,
  dateRangeOf,
  daysSince,
  deploymentsQuery,
  emptyDeploymentForm,
  emptyPipelineForm,
  emptyTargetForm,
  entriesByApp,
  flagExplanation,
  groupByStage,
  guessComponent,
  isProblem,
  isRetired,
  kindBuckets,
  lastSeenText,
  parseInventoryList,
  pipelinesFromTargets,
  runSourceText,
  targetFormOf,
  targetsInState,
} from './logic';

const APP = '00000000-0000-4000-8000-000000000513';
const OTHER = '00000000-0000-4000-8000-000000000514';
const TARGET = '00000000-0000-4000-8000-000000000521';
const PROD = '00000000-0000-4000-8000-000000000522';
const CHANGE = '00000000-0000-4000-8000-000000004201';
const RECORD = '00000000-0000-4000-8000-000000004500';
const NOW = Date.parse('2026-11-24T12:00:00Z');

const target = (over: Partial<DeploymentTargetItem> = {}): DeploymentTargetItem => ({
  deploymentTargetId: TARGET,
  label: 'Field Ops Test',
  targetKind: { key: 'power-platform-environment', label: 'Power Platform environment' },
  targetStage: { key: 'test', label: 'Test', rank: 2 },
  changePath: { key: 'pipeline-only', label: 'Pipeline only', allowedRoutes: ['pipeline-step'] },
  linkKind: { key: 'power-platform-resource', label: 'Power Platform resource' },
  externalId: 'field-ops-test',
  url: 'https://make.example.org/environments/field-ops-test',
  lastSeenOn: '2026-11-20T06:00:00Z',
  applications: [],
  pipelines: [],
  deploymentCount: 0,
  flaggedCount: 0,
  isAiCreated: false,
  performedBy: null,
  recordUrl: 'https://example.org/app/records/deployment-target/x',
  ...over,
});

const record = (over: Partial<DeploymentItem> = {}): DeploymentItem => ({
  deploymentRecordId: RECORD,
  businessApplicationId: APP,
  applicationName: 'Crew Roster',
  deploymentTargetId: TARGET,
  targetLabel: 'Field Ops Test',
  targetStageKey: 'test',
  artifactName: 'CrewRoster',
  artifactVersion: '1.4.0',
  result: { label: 'Succeeded', meaning: 'succeeded' },
  finishedOn: '2026-09-14T10:20:00Z',
  route: { key: 'pipeline-step', meaning: 'pipeline-step' },
  flags: { outsideWindow: false, otherPath: false, missingApproval: false, aiOnlyCodeApproval: false },
  gateLevelApplied: 'flag',
  runUrl: 'https://pipelines.example.org/runs/2140',
  correctsRecordId: null,
  changeRequestId: null,
  pipelineReferenceId: null,
  versionBefore: null,
  versionAfter: null,
  startedByAiClient: null,
  isAiCreated: false,
  performedBy: null,
  configurationSetVersion: 1,
  recordUrl: 'https://example.org/app/records/deployment-record/x',
  ...over,
});

const claim = (over: Partial<ClaimItem> = {}): ClaimItem => ({
  claimId: '00000000-0000-4000-8000-000000009001',
  findingKind: 'unregistered',
  status: { label: 'Open', meaning: 'open' },
  componentKind: { key: 'cloud-flow', label: 'Cloud flow' },
  labelAsSeen: 'Expense reminder flow',
  targetAsSeen: 'Finance Production',
  url: null,
  suggestedClaimant: null,
  foundOn: '2026-11-23T09:00:00Z',
  lastSeenOn: '2026-11-23T09:00:00Z',
  ...over,
});

// ---------- the list ----------

test('the list query carries the route filters and the page parameters, and asks the history by default', () => {
  const q = deploymentsQuery({ page: 'deployments', view: 'flagged', flagKeys: ['ai-only-code-approval'], applicationId: APP, targetId: PROD, changeRequestId: CHANGE }, { within: '30', result: 'failed' }, '2026-11-24');
  assert.deepEqual(q, {
    latestOnly: false,
    flaggedOnly: true,
    flagKeys: ['ai-only-code-approval'],
    businessApplicationId: APP,
    deploymentTargetId: PROD,
    changeRequestId: CHANGE,
    resultMeanings: ['failed'],
    from: '2026-10-25',
    top: 50,
  });
  assert.deepEqual(deploymentsQuery({ page: 'deployments' }, { latest: true }, '2026-11-24'), { latestOnly: true, top: 50 });
  // Wave 4: the portfolio goes to the tool.
  assert.equal(deploymentsQuery({ page: 'deployments' }, { portfolio: '00000000-0000-4000-8000-0000000000aa' }, '2026-11-24').portfolioId, '00000000-0000-4000-8000-0000000000aa');
});

test('a date range is a preset counted back from today, or the dates given; anything else is left open', () => {
  assert.deepEqual(dateRangeOf({ within: '7' }, '2026-11-24'), { from: '2026-11-17', to: null });
  assert.deepEqual(dateRangeOf({ from: '2026-11-01', to: '2026-11-15' }, '2026-11-24'), { from: '2026-11-01', to: '2026-11-15' });
  assert.deepEqual(dateRangeOf({ within: '12', from: 'yesterday' }, '2026-11-24'), { from: null, to: null });
});

test('a row is corrected when another loaded row names it', () => {
  const ids = correctedIds([record(), record({ deploymentRecordId: 'b', correctsRecordId: RECORD.toUpperCase() })]);
  assert.ok(ids.has(RECORD));
  assert.equal(ids.size, 1);
});

// ---------- recording by hand ----------

test('a deployment recorded by hand sends what the person gave, in UTC, and never a route or a flag', () => {
  const form = { ...emptyDeploymentForm(new Date(NOW)), targetId: PROD, artifactName: ' InvoiceMatching ', artifactVersion: ' 2.3.1 ', finishedOn: '2026-11-22T10:00', changeRequestId: CHANGE, runUrl: 'https://pipelines.example.org/runs/9', codeApprovalKind: 'ai-only' as const };
  const input = buildDeploymentInput(form, APP, NOW);
  if (isProblem(input)) throw new Error(input.problem);
  assert.equal(input.businessApplicationId, APP);
  assert.equal(input.deploymentTargetId, PROD);
  assert.equal(input.artifactName, 'InvoiceMatching');
  assert.equal(input.artifactVersion, '2.3.1');
  assert.equal(input.finishedOn, localInputToUtc('2026-11-22T10:00'));
  assert.equal(input.resultMeaning, 'succeeded');
  assert.equal(input.codeApprovalKind, 'ai-only');
  assert.equal(input.changeRequestId, CHANGE);
  assert.ok(!('route' in input) && !('flags' in input) && !('startedOn' in input));
});

test('the record-by-hand form names the field at fault', () => {
  const base = { ...emptyDeploymentForm(new Date(NOW)), targetId: PROD, artifactName: 'A', artifactVersion: '1' };
  const field = (over: Partial<typeof base>) => {
    const r = buildDeploymentInput({ ...base, ...over }, APP, NOW);
    return isProblem(r) ? r.field : null;
  };
  assert.equal(field({ targetId: '' }), 'deploymentTargetId');
  assert.equal(field({ artifactName: ' ' }), 'artifactName');
  assert.equal(field({ artifactVersion: '' }), 'artifactVersion');
  assert.equal(field({ resultMeaning: '' }), 'resultMeaning');
  assert.equal(field({ finishedOn: '' }), 'finishedOn');
  assert.equal(field({ finishedOn: '2027-01-01T10:00' }), 'finishedOn');
  assert.equal(field({ startedOn: '2026-11-24T23:59', finishedOn: '2026-11-22T10:00' }), 'startedOn');
  assert.equal(field({ runUrl: 'http://example.org/run' }), 'runUrl');
  assert.equal(field({}), null);
});

// ---------- corrections ----------

test('a correction sends only the facts that differ, with the reason', () => {
  const r = record({ changeRequestId: null });
  const form = { ...correctionFormOf(r), reason: ' It belongs to the November release. ', changeRequestId: CHANGE };
  const input = buildCorrectionInput(form, r);
  assert.deepEqual(input, { correctsDeploymentRecordId: RECORD, reason: 'It belongs to the November release.', changeRequestId: CHANGE });
  const failed = buildCorrectionInput({ ...correctionFormOf(r), reason: 'The run failed.', resultMeaning: 'failed' }, r);
  assert.deepEqual(failed, { correctsDeploymentRecordId: RECORD, reason: 'The run failed.', resultMeaning: 'failed' });
});

test('a correction that changes nothing, or gives no reason, is stopped before it is sent', () => {
  const r = record();
  const same = buildCorrectionInput({ ...correctionFormOf(r), reason: 'Checked.' }, r);
  assert.ok(isProblem(same) && same.field === 'changeRequestId');
  const noReason = buildCorrectionInput({ ...correctionFormOf(r), resultMeaning: 'failed' }, r);
  assert.ok(isProblem(noReason) && noReason.field === 'reason');
});

// ---------- targets ----------

test('a new target needs its keys, its name and its ID; a change sends only what differs', () => {
  const form = { ...emptyTargetForm(), label: 'Finance Production', targetKindKey: 'power-platform-environment', targetStageKey: 'production', changePathKey: 'pipeline-only', linkKindKey: 'power-platform-resource', externalId: 'finance-prod' };
  assert.deepEqual(buildTargetInput(form), {
    labelAsSeen: 'Finance Production',
    targetKindKey: 'power-platform-environment',
    targetStageKey: 'production',
    changePathKey: 'pipeline-only',
    linkKindKey: 'power-platform-resource',
    externalId: 'finance-prod',
  });
  const missing = buildTargetInput({ ...form, externalId: '' });
  assert.ok(isProblem(missing) && missing.field === 'externalId');
  const t = target();
  assert.deepEqual(buildTargetInput({ ...targetFormOf(t), changePathKey: 'any-route' }, t), { deploymentTargetId: TARGET, changePathKey: 'any-route' });
  const nothing = buildTargetInput(targetFormOf(t), t);
  assert.ok(isProblem(nothing));
  // A reader sees no ID; a change that leaves it empty never clears it.
  const hidden = target({ externalId: null, url: null });
  assert.deepEqual(buildTargetInput({ ...targetFormOf(hidden), label: 'Field Ops QA' }, hidden), { deploymentTargetId: TARGET, labelAsSeen: 'Field Ops QA' });
});

test('targets group by stage in rank order, and the apps and pipelines on them are listed once', () => {
  const prod = target({ deploymentTargetId: PROD, label: 'Field Ops Production', targetStage: { key: 'production', label: 'Production', rank: 3 } });
  const dev = target({ deploymentTargetId: 'd', label: 'Field Ops Dev', targetStage: { key: 'development', label: 'Development', rank: 1 } });
  const groups = groupByStage([prod, target(), dev]);
  assert.deepEqual(groups.map(g => g.stage.key), ['development', 'test', 'production']);

  const app = (id: string, name: string) => ({ businessApplicationId: id, name, artifactName: 'a', artifactVersion: '1', result: null, finishedOn: null, deploymentRecordId: 'r' });
  const pipe = { pipelineReferenceId: 'P1', label: 'Crew Roster release', url: null };
  const withApps = [target({ applications: [app(APP, 'Crew Roster'), app(OTHER, 'Asset Register')], pipelines: [pipe] }), target({ deploymentTargetId: PROD, label: 'Prod', applications: [app(APP.toUpperCase(), 'Crew Roster')], pipelines: [pipe] })];
  assert.deepEqual(appsFromTargets(withApps).map(a => a.name), ['Asset Register', 'Crew Roster']);
  const pipelines = pipelinesFromTargets(withApps);
  assert.equal(pipelines.length, 1);
  assert.deepEqual(pipelines[0].targets.map(t => t.label), ['Field Ops Test', 'Prod']);
});

test('a pipeline map lists each application with its targets in stage order', () => {
  const entry = (app: string, name: string, t: string, order: number): PipelineMapEntry => ({ businessApplicationId: app, applicationName: name, deploymentTargetId: t, targetLabel: t, targetStageKey: null, stageOrder: order });
  const grouped = entriesByApp([entry(APP, 'Crew Roster', 'prod', 2), entry(APP, 'Crew Roster', 'test', 1), entry(OTHER, 'Asset Register', 'test', 1)]);
  assert.deepEqual(grouped.map(g => g.applicationName), ['Asset Register', 'Crew Roster']);
  assert.deepEqual(grouped[1].steps.map(s => s.targetLabel), ['test', 'prod']);
});

test('the pipeline map input checks its link, its steps and their order', () => {
  const form = { ...emptyPipelineForm(), linkKindKey: 'azure-pipelines-pipeline', externalId: 'crew-roster-release', url: 'https://pipelines.example.org/field-ops/crew-roster', entries: [{ businessApplicationId: APP, applicationName: 'Crew Roster', deploymentTargetId: TARGET, stageOrder: '1' }] };
  assert.deepEqual(buildPipelineInput(form, 'P1'), {
    pipelineReferenceId: 'P1',
    linkKindKey: 'azure-pipelines-pipeline',
    externalId: 'crew-roster-release',
    url: 'https://pipelines.example.org/field-ops/crew-roster',
    entries: [{ businessApplicationId: APP, deploymentTargetId: TARGET, stageOrder: 1 }],
  });
  const twice = buildPipelineInput({ ...form, entries: [...form.entries, { ...form.entries[0], stageOrder: '2' }] });
  assert.ok(isProblem(twice) && /twice/.test(twice.problem));
  const order = buildPipelineInput({ ...form, entries: [{ ...form.entries[0], stageOrder: '0' }] });
  assert.ok(isProblem(order) && order.field === 'entries.stageOrder');
  const none = buildPipelineInput({ ...form, entries: [] });
  assert.ok(isProblem(none) && none.field === 'entries');
});

test('days since a moment count whole days from today', () => {
  assert.equal(daysSince('2026-11-20T06:00:00Z', '2026-11-24'), 4);
  assert.equal(daysSince(null, '2026-11-24'), null);
});

// ---------- the gap check ----------

test('a pasted inventory list reads each line, skips a header, and names the lines it cannot read', () => {
  const kinds = [{ key: 'cloud-flow', label: 'Cloud flow' }, { key: 'canvas-app', label: 'Canvas app' }];
  const text = [
    'ID | Name | Kind | Where | Link',
    'flow-1 | Expense reminder | Cloud flow | Finance Production | https://make.example.org/flows/1',
    '# a note',
    '',
    'app-2\tShift Swap\tcanvas-app\tField Ops Production',
    'flow-1, Duplicate',
    'x-3 | No kind | widget',
    'x-4 |',
    'x-5 | Bad link | | | http://example.org',
  ].join('\n');
  const parsed = parseInventoryList(text, kinds);
  assert.deepEqual(parsed.items, [
    { externalId: 'flow-1', labelAsSeen: 'Expense reminder', componentKindKey: 'cloud-flow', targetAsSeen: 'Finance Production', url: 'https://make.example.org/flows/1' },
    { externalId: 'app-2', labelAsSeen: 'Shift Swap', componentKindKey: 'canvas-app', targetAsSeen: 'Field Ops Production' },
  ]);
  assert.deepEqual(parsed.problems.map(p => p.line), [6, 7, 8, 9]);
  // A line with no kind is named, not sent: the engine would leave it out (fix round 2).
  assert.match(parseInventoryList('a-1 | Depot keys reminder', kinds).problems[0].problem, /^No kind\./);
});

test('claims count and list by view: open, vanished, claimed and dismissed', () => {
  const items = [
    claim(),
    claim({ claimId: 'v', findingKind: 'vanished', componentKind: { key: 'canvas-app', label: 'Canvas app' } }),
    claim({ claimId: 'c', status: { label: 'Claimed', meaning: 'claimed' } }),
    claim({ claimId: 'd', status: { label: 'Dismissed', meaning: 'dismissed' }, componentKind: null }),
  ];
  assert.deepEqual(claimCounts(items), { open: 1, vanished: 1, claimed: 1, dismissed: 1 });
  assert.deepEqual(claimsInView(items, 'vanished').map(c => c.claimId), ['v']);
  assert.deepEqual(claimsInView(items, 'runs'), []);
  assert.deepEqual(kindBuckets(items).map(b => [b.key, b.count]), [['cloud-flow', 2], ['canvas-app', 1], [null, 1]]);
});

test('a run\'s claims are the ones found at its start', () => {
  const items = [claim(), claim({ claimId: 'later', foundOn: '2026-11-24T09:00:00Z' })];
  assert.deepEqual(claimsOfRun(items, '2026-11-23T09:00:00.000Z').map(c => c.claimId), [claim().claimId]);
  assert.deepEqual(claimsOfRun(items, null), []);
});

test('the component a vanished claim names is guessed only when one name matches', () => {
  const components = [{ technicalComponentId: 'a', labelAsSeen: 'Timesheet canvas app' }, { technicalComponentId: 'b', labelAsSeen: 'Timesheet flow' }];
  assert.equal(guessComponent(components, ' timesheet canvas app '), 'a');
  assert.equal(guessComponent([...components, { technicalComponentId: 'c', labelAsSeen: 'Timesheet flow' }], 'Timesheet flow'), null);
});

test('a run\'s source reads as live or as reported', () => {
  assert.deepEqual(runSourceText('as reported: CoE kit export, 23 Nov'), { live: false, text: 'As reported, from a list: CoE kit export, 23 Nov' });
  assert.equal(runSourceText('as reported: An export of the default environment.').text, 'As reported, from a list: An export of the default environment');
  assert.equal(runSourceText('Power Platform administrator').live, true);
});

test("a demo install's run reads the demo's fictional inventory, never live as an admin role", () => {
  assert.deepEqual(runSourceText("demo: the demo's fictional inventory"), { live: false, text: "Read from the demo's fictional inventory, not Microsoft's" });
  assert.equal(runSourceText('demo:').live, false);
  assert.ok(!runSourceText("demo: the demo's fictional inventory").text.includes('Read live'));
  // A role that only contains the word stays a live read.
  assert.equal(runSourceText('Demo tenant administrator').live, true);
});

test("a demo's owner check reads the demo's fictional directory (wave 4b QA)", () => {
  assert.deepEqual(runSourceText("demo: the demo's fictional directory"), { live: false, text: "Read from the demo's fictional directory, not Microsoft's" });
});

test('what a run left open counts the claims not registered and the vanished items apart', () => {
  assert.equal(lastSeenText({ openClaimsLastSeen: 2, openVanishedLastSeen: 1 }), '2 open claims not registered and 1 vanished item last seen in it.');
  assert.equal(lastSeenText({ openClaimsLastSeen: 1, openVanishedLastSeen: 0 }), '1 open claim not registered and 0 vanished items last seen in it.');
  // A server before wave 4b sends one count for both.
  assert.equal(lastSeenText({ openClaimsLastSeen: 3 }), '3 open claims last seen in it.');
});

test('a flag explanation does not say the same thing twice', () => {
  assert.equal(
    flagExplanation('The only code approval came from an AI. It never counts as authorization.', 'A person reviews the code change. An AI approval never counts as authorization.'),
    'The only code approval came from an AI. It never counts as authorization. A person reviews the code change.',
  );
  assert.equal(flagExplanation('Finished outside the window.', 'Check why.'), 'Finished outside the window. Check why.');
});

test('a target retires with a reason and returns to service with nothing else', () => {
  assert.deepEqual(buildRetireInput(target(), '  Replaced by Field Ops Test 2. '), { deploymentTargetId: TARGET, retire: true, retiredReason: 'Replaced by Field Ops Test 2.' });
  const missing = buildRetireInput(target(), ' ');
  assert.equal(isProblem(missing) ? missing.field : null, 'retiredReason');
  assert.deepEqual(buildRetireInput(target({ retiredOn: '2026-11-01', retiredReason: 'Replaced.' }), ''), { deploymentTargetId: TARGET, retire: false });
});

test('the targets in one state: in service, retired, or all', () => {
  const live = target();
  const gone = target({ deploymentTargetId: PROD, retiredOn: '2026-11-01', retiredReason: 'Replaced.' });
  assert.equal(isRetired(live), false);
  assert.equal(isRetired(gone), true);
  assert.deepEqual(targetsInState([live, gone], 'active'), [live]);
  assert.deepEqual(targetsInState([live, gone], 'retired'), [gone]);
  assert.equal(targetsInState([live, gone], 'all').length, 2);
});
