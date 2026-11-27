// The web app pages the accessibility check opens (scripts/a11y.mjs --app): every page of src/app/routes.ts, each
// record page on a record of the demo estate, and the personas it signs in as. No packages. src/app/a11yPages.test.ts
// checks that this list names every page of the routes, so a new page kind fails the tests until it is listed here.
//
// Each page is { name, path, persona?, sheet? }:
//   path     the address under the web app's base path (/app/), with any query string;
//   persona  false for a page checked signed out (the development sign-in), once per width;
//   sheet    false for a page with no action sheet to open, or a pattern for the label of the trigger to try first;
//   sheetOnly  true to open only a trigger whose label matches sheet. A persona with no such trigger gets a note, not
//            another sheet. The wave 4 sheets that are not a page's main action use it.
// The check opens the main action sheet of a page as the first sheet trigger of the page area that the persona sees
// (the page header's actions come first), unless sheet names a better one.

// Records of seed/demo-estate.json (src/adapters/azure/seed). Every ID is fictional.
export const DEMO_IDS = {
  application: '00000000-0000-4000-8000-000000000511', // Inspection Scheduler, owned by Nadia Ferreira
  retiringApplication: '00000000-0000-4000-8000-000000001323', // Legacy Timesheet App, its removal change in progress
  decision: '00000000-0000-4000-8000-000000004000', // DEC-0101, the go-live of Inspection Scheduler
  intakeRequest: '00000000-0000-4000-8000-000000005604', // Supplier rebate tracking, in progress
  draftIntakeRequest: '00000000-0000-4000-8000-000000005600', // Vehicle damage photos for insurance claims, a draft
  review: '00000000-0000-4000-8000-000000005300', // the owner check of Inspection Scheduler, due
  changeRequest: '00000000-0000-4000-8000-000000004206', // CHG-0207, Release 3.5: inspector workload view
  deploymentRecord: '00000000-0000-4000-8000-000000004522', // an Inspection Scheduler deployment with a flag
  documentLink: '00000000-0000-4000-8000-000000005001', // the stale runbook of Inspection Scheduler
  deploymentTarget: '00000000-0000-4000-8000-000000000522', // Field Ops Prod
  portfolio: '00000000-0000-4000-8000-000000000501', // Field operations
  dataDomain: 'inspections', // Field operations / Inspections, a key: a domain is a configuration row
  dataLocation: '00000000-0000-4000-8000-000000006000', // Field Ops Prod, Dataverse
  dataMapping: '00000000-0000-4000-8000-000000006100', // client accounts, system of record
  dataUse: '00000000-0000-4000-8000-000000006200', // DU-0001, Inspection Scheduler's use of inspections
  person: '00000000-0000-4000-8000-000000000212', // Nadia Ferreira
  component: '00000000-0000-4000-8000-000000000531', // a technical component: an entity with no page of its own
  team: '00000000-0000-4000-8000-000000000111', // the Change board group, a committee (wave 4)
  supportTeam: '00000000-0000-4000-8000-000000001106', // Field Apps Support, a support team (wave 4)
  roleGroup: '00000000-0000-4000-8000-000000000101', // Product requesters, a role group (wave 4)
};

// Records the demo director makes (seed/demo-director.mjs), so their IDs are new in every store. The check reads the
// first one through the REST API (POST /api/<tool>) as the lookup persona and takes the named field.
export const DIRECTOR_RECORDS = {
  claim: { tool: 'list_claims', input: {}, field: 'claimId' },
  gapCheckRun: { tool: 'list_gap_check_runs', input: {}, field: 'gapCheckRunId' },
  configurationDraft: { tool: 'list_configuration_changes', input: { view: 'drafts' }, field: 'configurationDraftId' },
};

// The three personas of the role-by-role pass (development personas of appsettings.Development.json): a requester,
// an application owner and the platform admin. The platform admin also reads the director's records.
export const DEFAULT_PERSONAS = ['demo-lena', 'nadia-web', 'owen-web'];
export const LOOKUP_PERSONA = 'owen-web';

// Every page, in the order of the routes file. ids holds DEMO_IDS and the director's records (claim, gapCheckRun,
// configurationDraft); a record that could not be read is null, and its page is reported as not checked.
export function appPages(ids) {
  const record = (entity, id) => (id ? `records/${entity}/${id}` : null);
  return [
    { name: 'work', path: '' },
    { name: 'overview', path: 'overview' },
    { name: 'timeline', path: 'timeline' },
    { name: 'search', path: 'search?q=inspection' },
    { name: 'applications', path: 'applications' },
    { name: 'approvals', path: 'approvals' },
    { name: 'approvals-rules', path: 'approvals?view=rules' },
    { name: 'intake', path: 'intake' },
    { name: 'intake-new', path: 'intake/new' },
    { name: 'demand', path: 'demand' },
    { name: 'owners', path: 'owners' },
    { name: 'owners-gaps', path: 'owners?view=gaps' },
    { name: 'owners-leavers', path: 'owners?view=leavers' },
    { name: 'person', path: `people/${ids.person}` },
    { name: 'reviews', path: 'reviews' },
    { name: 'changes', path: 'changes' },
    { name: 'changes-list', path: 'changes?view=list' },
    { name: 'change-new', path: `changes/new?app=${ids.application}` },
    { name: 'deployments', path: 'deployments' },
    { name: 'deployment-new', path: `deployments/new?app=${ids.application}` },
    { name: 'places', path: 'places' },
    { name: 'places-pipelines', path: 'places?view=pipelines' },
    { name: 'place', path: `places/${ids.deploymentTarget}` },
    { name: 'gap-check', path: 'gap-check' },
    { name: 'gap-check-runs', path: 'gap-check?view=runs' },
    { name: 'ai-activity', path: 'ai-activity' },
    { name: 'agents', path: 'agents' },
    { name: 'audit', path: `audit?app=${ids.application}` },
    { name: 'evidence-application', path: `evidence/business-application/${ids.application}` },
    { name: 'evidence-decision', path: `evidence/decision/${ids.decision}` },
    { name: 'decisions', path: 'decisions' },
    { name: 'decisions-exceptions', path: 'decisions?view=exceptions' },
    { name: 'documents', path: 'documents' },
    { name: 'portfolios', path: 'portfolios' },
    { name: 'portfolio', path: `portfolios/${ids.portfolio}` },
    { name: 'retirement', path: 'retirement' },
    { name: 'retirement-tracker', path: `retirement/${ids.retiringApplication}` },
    { name: 'demo-guide', path: 'demo-guide' },
    { name: 'data', path: 'data' },
    { name: 'data-locations', path: 'data/locations' },
    { name: 'data-mapping', path: 'data/mapping' },
    { name: 'data-uses', path: 'data/uses' },
    { name: 'data-gaps', path: 'data/gaps' },
    { name: 'configuration', path: 'configuration' },
    { name: 'configuration-presets', path: 'configuration?view=presets' },
    { name: 'configuration-versions', path: 'configuration?view=versions' },
    { name: 'configuration-drafts', path: 'configuration?view=drafts' },
    { name: 'roles', path: 'roles' },
    { name: 'guidelines', path: 'guidelines' },
    { name: 'measures', path: 'measures' },
    { name: 'admin', path: 'admin' },
    // ---- wave 4 ----
    { name: 'teams', path: 'teams' },
    { name: 'teams-committees', path: 'teams?kind=committee' },
    { name: 'teams-support', path: 'teams?kind=support-team' },
    { name: 'teams-search', path: 'teams?q=support' },
    { name: 'team', path: `teams/${ids.team}` },
    { name: 'team-support', path: `teams/${ids.supportTeam}` },
    { name: 'team-role-group', path: `teams/${ids.roleGroup}` },
    { name: 'applications-filtered', path: 'applications?ai=yes&q=inspection' },
    { name: 'audit-client', path: `audit?app=${ids.application}&client=Claude%20Code` },
    // The wave 4 sheets that are not a page's main action, each opened by its own trigger only.
    { name: 'app-what-depends', path: record('business-application', ids.application), sheet: '^What depends on', sheetOnly: true },
    { name: 'place-what-depends', path: `places/${ids.deploymentTarget}`, sheet: '^What depends on', sheetOnly: true },
    { name: 'data-location-what-depends', path: record('data-location', ids.dataLocation), sheet: '^What depends on', sheetOnly: true },
    { name: 'measures-record-decision', path: 'measures', sheet: '^Record a decision', sheetOnly: true },
    { name: 'configuration-import', path: 'configuration', sheet: '^Import data domains', sheetOnly: true },
    { name: 'work-ask-again', path: '', sheet: '^Ask again', sheetOnly: true },
    { name: 'decision-ask-again', path: record('decision', ids.decision), sheet: '^Ask again', sheetOnly: true },
    { name: 'admin-outbox', path: 'admin', sheet: '^(Send again|Close):', sheetOnly: true },
    // The record pages, one record each.
    { name: 'app', path: record('business-application', ids.application) },
    { name: 'map', path: `${record('business-application', ids.application)}/map` },
    { name: 'decision', path: record('decision', ids.decision) },
    { name: 'intake-request', path: record('intake-request', ids.intakeRequest) },
    { name: 'intake-edit', path: `${record('intake-request', ids.draftIntakeRequest)}/edit` },
    { name: 'review', path: record('review', ids.review) },
    { name: 'change-request', path: record('change-request', ids.changeRequest) },
    { name: 'deployment-record', path: record('deployment-record', ids.deploymentRecord) },
    { name: 'document-link', path: record('document-link', ids.documentLink) },
    { name: 'claim', path: record('claim', ids.claim) },
    { name: 'gap-check-run', path: record('gap-check-run', ids.gapCheckRun) },
    { name: 'configuration-draft', path: record('configuration-draft', ids.configurationDraft) },
    { name: 'data-domain', path: record('data-domain', ids.dataDomain) },
    { name: 'data-location', path: record('data-location', ids.dataLocation) },
    { name: 'data-mapping-record', path: record('data-mapping', ids.dataMapping) },
    { name: 'data-use', path: record('data-use', ids.dataUse) },
    { name: 'record', path: record('technical-component', ids.component), sheet: false },
    { name: 'missing', path: 'no-such-page', sheet: false },
    { name: 'sign-in', path: '', persona: false, sheet: false },
  ];
}
