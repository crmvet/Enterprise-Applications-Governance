#!/usr/bin/env node
// demo-director.mjs
//
// What it is: the demo director. It makes the open work of the demo estate live through the product's own tools, as
// the fictional demo personas, so that My work and Approvals show real pending items for every role and every page of
// the web app has rows to show. The seed (seed/demo-estate.json) writes rows as given and stamps nothing, so it holds
// no open work: a row the engine never stamped cannot be submitted or evaluated. This script does that part through the
// same core and engine as a person's work in the web app (D-36), so every stamp, approval record and audit entry is the
// engine's own:
//   - intake requests filed by requesters, with tier answers, and submitted for triage (the platform admin is asked, and
//     the architect, the PMO and the security reviewer are consulted);
//   - demand: the PMO scores four requests and proposes the priority of one, so the Demand page has scored rows and a
//     priority waiting (the maker's own request stays unscored for the live demo);
//   - change requests drafted and submitted for the coming windows (business owners and the change manager are asked);
//   - decision proposals submitted so that approval requests wait for other personas: go-live, ownership, risk
//     acceptance, autonomy level, portfolio (one keep, one replace), exception, design and a flagged agent, and an
//     exception about a normal change, which waits on the Change board (the Teams page);
//   - the data taxonomy (D-37; docs/architecture/data-taxonomy.md): data-use decisions about declared uses, each asking the
//     domain's data owner by name (Visit Report Agent's use of safety findings with AI waits for Kai Ostrowski and the
//     security reviewer, the 9:30 step), the copy decision on Finance Test's masked copy (Clara Voss) and the retention
//     decision on Legacy Timesheet App's timesheets (Hana Lindqvist);
//   - a configuration change the configuration admin drafts and submits (the AI use rule of Workplace / Visitor log
//     becomes never), for the second configuration admin to answer;
//   - the daily due-state refresh, run now by the platform admin as the timer runs it: it opens the next access check
//     of Identity Directory Sync;
//   - a security review the AI lead requests for Customer Feedback Bot;
//   - an owner leave with a proposed successor, and a proposed business owner for the application that has none (the
//     successor accepts by name, and the sponsor answers for the business owner role that changes hands);
//   - work finished before the demo: the architect completes Quote Builder's architecture review with its accepted
//     design decision (the review shows the Design gate met), and the change manager closes the Legacy Timesheet App
//     removal change with its evidence (a completed change, and steps 4 and 5 of the Retirement tracker);
//   - documents recorded as drafts, for their owners to confirm;
//   - a deployment target the platform admin registers (Places);
//   - a gap check from a recorded Power Platform inventory, by the platform admin: the whole inventory, as one final
//     page, so the run is finished. Four items no application holds open claims: one the maker claims into her
//     application, one the platform admin dismisses, and two left open for the live demo (one suggested to the maker, one
//     test flow to dismiss). The Timesheets canvas app is not in the list, so its vanished claim waits on the Retirement
//     tracker;
//   - the AI client's work for the AI activity page (local runs only, through the MCP endpoint as Claude Code acting for
//     Priya Castell): a draft she takes forward as drafted, a draft she changes before she submits it, a draft she
//     withdraws, a draft left waiting, a deployment made from the AI tool with only an AI code approval (flagged), and
//     three attempts the server refuses and records: an approve tool that does not exist, an approval answer (served
//     only to a person) and a risk acceptance outside her decision rights. Then its data work: a draft data use on
//     Mileage Claims, a draft mapping for Nadia Ferreira (data owner of Customers), and an AI use of bank details on
//     Expense Assistant that the never rule refuses when it is saved (G-25). In a demo install Claude Code works live.
// It never answers an approval and never accepts a decision: the people in the demo do that on the decision page.
// It needs no packages: Node.js 22 or later.
//
// How it acts as a persona (--mode):
//   dev   The local server with the development identity (README.md, "Run it locally"): each request names the
//         development persona demo-<name> in X-Product-Dev-Identity (Product.Azure.Server/appsettings.Development.json
//         has one for each persona of seed/demo-personas.json). The AI draft goes to the MCP endpoint as the persona
//         priya, which is Claude Code acting for Priya Castell. Loopback URLs only. The default for a loopback URL.
//   demo  A demo install (D-39): each request carries the demo operator's own bearer token for the web app and names
//         the persona in X-Product-Demo-Persona. The token is read from an environment variable (PRODUCT_DEMO_TOKEN
//         unless --token-env names another) and is never printed. Claude Code's part goes to the MCP endpoint with a
//         second token, the operator's Claude Code token for the MCP endpoint, read from PRODUCT_DEMO_MCP_TOKEN (or the
//         variable --ai-token-env names), with X-Product-Demo-Persona naming the persona it acts for: the host lets an AI
//         client act as a demo persona there and stamps "Claude Code, demo operator" (product review). Without that
//         token the AI steps are skipped, and the demo shows Claude Code drafting live instead.
//
// It checks before it creates: every step first reads whether its work is already there (by title or statement, on
// the same application, as the same persona) and then does only what is missing, so running it again creates nothing
// twice and finishes what an earlier run left half done. Every write also carries a fixed clientRequestId, which the
// server answers with the first result if the same write comes again. Window, leave and deployment dates count from the
// day it runs, so the changes always fall in the coming weeks and the AI deployment in the last hours. Approval requests
// expire as their rules say (five days for a change authorization, ten for a risk acceptance or a data use), so run it
// on a freshly seeded store shortly before a demo. A store that has the work already is left as it is.
//
// The summary names personas, applications and record titles only: no IDs, no URL of the install, no token.
//
// Usage, from src/adapters/azure, with the server running:
//   node seed/demo-director.mjs --base http://127.0.0.1:5080
//   PRODUCT_DEMO_TOKEN=<token> node seed/demo-director.mjs --mode demo --base https://<the install's host>
// Options:
//   --base <url>          the server (default http://127.0.0.1:5080)
//   --mode dev|demo       how to act as a persona (default: dev for a loopback URL, demo otherwise)
//   --token-env <name>    demo mode: the environment variable that holds the operator's token (default PRODUCT_DEMO_TOKEN)
//   --ai-token-env <name> demo mode: the variable that holds the operator's Claude Code token for the MCP endpoint
//                         (default PRODUCT_DEMO_MCP_TOKEN); without it Claude Code's steps are skipped
//   --personas <file>     the persona file (default seed/demo-personas.json next to this script)
//   --dev-prefix <text>   dev mode: what goes before a persona's name to name its development persona (default demo-)
//   --ai-persona <name>   dev mode: the development persona of the AI client, for Claude Code's work (default priya,
//                         Claude Code acting for Priya Castell; the steps take forward or withdraw its drafts as priya)
//   --ai-owner-persona <name>  dev mode: the development persona of the AI client that drafts a data mapping for the data
//                         owner of Customers (default nadia, Claude Code acting for Nadia Ferreira)
//   --timeout <seconds>   how long to wait for one answer (default 180). A local server answers in well under a second;
//                         the default leaves room for a slow connection between a server and a demo install's database
//   --check               write nothing: check the personas and print the pending work of each
//   --json                print the summary as JSON
// Exit code: 0 when every step is done (now or by an earlier run), 1 when a step failed, 2 for bad usage or a server
// that does not know the personas.

import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const flag = name => args.includes(name);
const opt = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : fallback;
};
function usage(message) {
  console.error('demo-director: ' + message);
  console.error('Usage: node seed/demo-director.mjs [--base <url>] [--mode dev|demo] [--token-env <name>] [--personas <file>] [--dev-prefix <text>] [--ai-persona <name>] [--ai-owner-persona <name>] [--timeout <seconds>] [--check] [--json]');
  process.exit(2);
}

let base;
try {
  base = new URL(opt('--base', 'http://127.0.0.1:5080'));
} catch {
  usage('--base is not a URL.');
}
const loopback = ['127.0.0.1', 'localhost', '[::1]'].includes(base.hostname);
const mode = opt('--mode', loopback ? 'dev' : 'demo');
if (mode !== 'dev' && mode !== 'demo') usage('--mode is dev or demo.');
if (mode === 'dev' && (!loopback || base.protocol !== 'http:')) {
  usage('--mode dev works only against a local server on http://127.0.0.1, http://localhost or http://[::1]: the development identity takes loopback requests only.');
}
if (mode === 'demo' && base.protocol !== 'https:' && !loopback) usage('A demo install is reached over https.');
const tokenVariable = opt('--token-env', 'PRODUCT_DEMO_TOKEN');
const token = mode === 'demo' ? (process.env[tokenVariable] ?? '').trim() : '';
if (mode === 'demo' && !token) {
  usage('Set ' + tokenVariable + ' to the demo operator\'s bearer token for the web app (the token the page sends to /api after sign-in).');
}
const aiTokenVariable = opt('--ai-token-env', 'PRODUCT_DEMO_MCP_TOKEN');
const aiToken = mode === 'demo' ? (process.env[aiTokenVariable] ?? '').trim() : '';
// Claude Code's part runs in dev mode, and in demo mode when the operator's MCP token is given.
const aiWorks = mode === 'dev' || !!aiToken;
const devPrefix = opt('--dev-prefix', 'demo-');
const aiPersona = opt('--ai-persona', 'priya');
const aiOwnerPersona = opt('--ai-owner-persona', 'nadia');
const timeoutSeconds = Number(opt('--timeout', '180'));
if (!Number.isInteger(timeoutSeconds) || timeoutSeconds < 5 || timeoutSeconds > 3600) usage('--timeout takes a whole number of seconds from 5 to 3600.');
const checkOnly = flag('--check');
const asJson = flag('--json');

let personaFile;
try {
  personaFile = JSON.parse(readFileSync(opt('--personas', join(here, 'demo-personas.json')), 'utf8'));
} catch (e) {
  usage('Cannot read the persona file: ' + e.message);
}
const PERSONAS = new Map((personaFile.personas ?? []).map(p => [p.name, { ...p, person: String(p.label ?? p.name).split(',')[0] }]));
const persona = name => {
  const p = PERSONAS.get(name);
  if (!p) usage('The persona file has no persona named ' + name + '.');
  return p;
};

// ---------------------------------------------------------------------------------------------------------------
// Calls
// ---------------------------------------------------------------------------------------------------------------

// A refusal or failure from the server, with the envelope's code and message.
class Refused extends Error {
  constructor(tool, status, code, message) {
    super(tool + ': ' + code + ' (' + status + '). ' + message);
    this.code = code;
    this.status = status;
  }
}

// Text from the server, made safe to print: IDs become <id> and long text is cut.
function plain(text) {
  const s = String(text ?? '').replace(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi, '<id>').replace(/\s+/g, ' ').trim();
  return s.length > 300 ? s.slice(0, 297) + '...' : s;
}

async function send(path, init) {
  for (let attempt = 0; ; attempt++) {
    const response = await fetch(new URL(path, base), { ...init, redirect: 'manual', signal: AbortSignal.timeout(timeoutSeconds * 1000) });
    const text = await response.text();
    // The request limits per caller answer 429 with Retry-After; wait and try again, a few times.
    if (response.status === 429 && attempt < 3) {
      const wait = Math.min(60, Number(response.headers.get('retry-after')) || 5);
      await new Promise(resolve => setTimeout(resolve, wait * 1000));
      continue;
    }
    let body = null;
    try {
      body = text ? JSON.parse(text) : null;
    } catch {
      body = null;
    }
    return { status: response.status, body };
  }
}

// The operator's own calendar day, as the web app sends it with every call (X-Product-Local-Day, B24-21): a review the
// director completes this evening in New York is done today, not tomorrow (wave 4b QA walk).
function localDay(now = new Date()) {
  const two = n => String(n).padStart(2, '0');
  return now.getFullYear() + '-' + two(now.getMonth() + 1) + '-' + two(now.getDate());
}

function personHeaders(p) {
  return mode === 'dev'
    ? { 'X-Product-Dev-Identity': devPrefix + p.name, 'X-Product-Local-Day': localDay() }
    : { Authorization: 'Bearer ' + token, 'X-Product-Demo-Persona': p.name, 'X-Product-Local-Day': localDay() };
}

function envelopeOf(name, r) {
  const env = r.body;
  if (env && env.ok === true) return env;
  const error = env?.error ?? {};
  throw new Refused(name, r.status, error.code ?? 'no-answer', plain(error.message ?? 'The server gave no envelope.'));
}

// One tool call on the REST API as the person (the person entry).
async function call(p, name, input = {}) {
  return envelopeOf(name, await send('/api/' + name, {
    method: 'POST',
    headers: { ...personHeaders(p), 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  }));
}

// One tool call on the MCP endpoint as an AI client acting for a person: the development identity in dev mode, the
// operator's Claude Code token and the persona header in demo mode. The token is never printed.
let rpcId = 0;
async function callAsAi(devName, name, input = {}) {
  const who = mode === 'dev' ? { 'X-Product-Dev-Identity': devName } : { Authorization: 'Bearer ' + aiToken, 'X-Product-Demo-Persona': devName };
  const r = await send('/mcp', {
    method: 'POST',
    headers: { ...who, 'Content-Type': 'application/json', Accept: 'application/json, text/event-stream' },
    body: JSON.stringify({ jsonrpc: '2.0', id: ++rpcId, method: 'tools/call', params: { name, arguments: input } }),
  });
  return envelopeOf(name, { status: r.status, body: r.body?.result?.structuredContent ?? r.body?.error?.data ?? null });
}

// Every item of a paged list.
async function all(p, name, input = {}, limit = 2000) {
  const items = [];
  let skipToken;
  do {
    const env = await call(p, name, { ...input, top: 100, ...(skipToken ? { skipToken } : {}) });
    items.push(...(env.data?.items ?? []));
    skipToken = env.data?.skipToken ?? undefined;
  } while (skipToken && items.length < limit);
  return items;
}

// The fixed idempotency key of a write: a retry of the same write returns the first result.
const requestId = key => 'demo-director-1:' + key;

// ---------------------------------------------------------------------------------------------------------------
// Dates: counted from the day the director runs (UTC), so windows and leaves are always ahead.
// ---------------------------------------------------------------------------------------------------------------

const today = new Date(Date.UTC(new Date().getUTCFullYear(), new Date().getUTCMonth(), new Date().getUTCDate()));
const day = offset => new Date(today.getTime() + offset * 86400000).toISOString().slice(0, 10);
const at = (offset, hour, minute = 0) => day(offset) + 'T' + String(hour).padStart(2, '0') + ':' + String(minute).padStart(2, '0') + ':00Z';
// A moment some hours before now, to the minute: a deployment that finished a little while ago.
const ago = (hours, minutes = 0) => new Date(Math.floor((Date.now() - (hours * 60 + minutes) * 60000) / 60000) * 60000).toISOString().replace('.000Z', 'Z');

// ---------------------------------------------------------------------------------------------------------------
// The work to make live. Every name and URL is fictional (example.org hosts).
// ---------------------------------------------------------------------------------------------------------------

// Intake requests. tier: answer the tier questions so they point at this tier (a tier 1 request is accepted by the
// engine at submit, so the triage cards come from tier 2 requests). ai: drafted by Claude Code for the person. owner:
// the business owner the requester names (document 25, B25-10); the Accept gate counts them, so the platform admin's
// triage answer registers the application and makes them its business owner.
const INTAKES = [
  { key: 'intake-parts', who: 'lena', owner: 'nadia', title: 'Spare parts returns from field vans', need: 'Technicians return unused parts by email, and the stores team cannot see what is coming back or when.', kind: 'power-platform-app', horizon: 'next', benefit: 'Fewer lost parts, and a stores team that can plan the returns.', tier: 'tier-2', submit: true },
  { key: 'intake-weather', who: 'priya', owner: 'nadia', title: 'Weather alerts for outdoor jobs', need: 'Planners check the forecast by hand before they send crews to roof and mast jobs, and some checks are missed.', kind: 'power-platform-app', horizon: 'now', benefit: 'No crew is sent up a mast in high wind.', tier: 'tier-2', submit: true },
  { key: 'intake-first-aid', who: 'lena', title: 'Depot first-aid kit checks', need: 'Monthly first-aid kit checks are on paper, and nobody sees which kits are short until someone needs one.', kind: 'power-platform-app', horizon: 'later', submit: false },
  { key: 'intake-toolbox-ai', who: 'priya', ai: true, title: 'Toolbox talk sign-off', need: 'Crew leaders read the morning safety briefing aloud and collect signatures on paper, and the sheets get lost.', kind: 'power-platform-app', horizon: 'next', submit: false },
];

// Demand (A-DEMAND, the Demand page): the PMO's score, size band and horizon on four requests, stored as reported.
// Priya Castell's "Weather alerts for outdoor jobs" stays unscored: the PMO scores it in the live demo.
const SCORES = [
  { key: 'score-mileage', who: 'mateo', title: 'Mileage claims from the phone', score: 72, size: 'small', horizon: 'now', benefit: 'Finance stops checking every route by hand, about two days a month.' },
  { key: 'score-rebates', who: 'mateo', title: 'Supplier rebate tracking', score: 86, size: 'medium', horizon: 'next', benefit: 'Rebates worth about 40,000 dollars a year are claimed instead of missed.' },
  { key: 'score-translate', who: 'mateo', title: 'Translate customer messages', score: 64, size: 'medium', horizon: 'now' },
  { key: 'score-parts', who: 'mateo', title: 'Spare parts returns from field vans', score: 58, size: 'small', horizon: 'next' },
];

// Claude Code's work for Priya Castell (dev mode only, through the MCP endpoint). Each item shows one thing a person does
// with an AI draft: take it forward as drafted, change it first, or withdraw it (the AI activity page, D-28).
const AI_TAKEN_FORWARD = {
  key: 'ai-chg-rp-21', who: 'priya', app: 'Route Planner', title: 'Route Planner 2.1: depot closures', start: [12, 18], end: [12, 19], target: 'Field Ops Prod',
  description: 'Plans routes around depots that close for the holidays, read from the depot calendar.',
};
const AI_EDITED = {
  key: 'ai-tier-rp', who: 'priya', app: 'Route Planner', type: 'tier', outcome: 'raise', tierKey: 'tier-2',
  statement: 'Raise Route Planner to tier 2: version 2.0 writes crew assignments into Crew Roster.',
  drafted: {
    context: 'Route Planner 2.0 writes the crew of each job into Crew Roster.',
    reason: 'It changes data in another tier 2 application.',
  },
  edited: {
    context: 'Route Planner 2.0 writes the crew of each job into Crew Roster every night, and supervisors staff the field jobs from those assignments the next morning.',
    reason: 'A wrong assignment now reaches the crews, so Route Planner needs the same change and access controls as Crew Roster.',
  },
};
// A change Claude Code drafted that Priya withdraws, and a decision it drafted that waits for her. Both are records the
// whole estate can read, so the AI lead sees them too (an intake draft, such as "Toolbox talk sign-off", is private to
// its requester until it is submitted).
const AI_WITHDRAWN = {
  key: 'ai-chg-rp-skip', who: 'priya', app: 'Route Planner', title: 'Route Planner 2.0: skip closed depots', start: [10, 18], end: [10, 19], target: 'Field Ops Prod',
  description: 'Leaves depots that are closed for the day out of the morning routes.',
  reason: 'Route Planner 2.1 plans around closed depots from the depot calendar, so this change is not needed.',
};
const AI_WAITING = {
  key: 'ai-tier-ssr', who: 'priya', app: 'Shift Swap Requests', type: 'tier', outcome: 'confirm',
  statement: 'Confirm tier 1 for Shift Swap Requests: it only swaps shifts inside one crew, and a supervisor approves each swap.',
  context: 'Shift Swap Requests is in build. It reads and writes shifts in Crew Roster for one crew at a time.',
  reason: 'A supervisor approves every swap, and the app holds no data of its own.',
};
// A deployment Claude Code made from the AI tool straight to Field Ops Test, whose change path is pipeline only, after
// only an AI review of the code. The engine flags it: another path, an AI-only code approval, and no change covers it
// unless the Route Planner 2.0 test window happens to.
const AI_DEPLOYMENT = {
  key: 'ai-dep-rp-rc1', who: 'priya', app: 'Route Planner', target: 'Field Ops Test', artifactName: 'route-planner', artifactKind: 'code-package', version: '2.0.0-rc.1',
  hoursAgo: 3, minutes: 6, pullRequest: 'https://git.example.org/field-ops/route-planner/pull/212',
};
// A deployment a person records by hand in the web app (Record a deployment, route person-in-app): the change manager
// notes a solution import to Field Ops Dev that no pipeline recorded. The QA run of 2026-11-24 found that the answer
// of this write failed its output schema; the fix lets the person-in-app route through, and this step keeps it covered.
// Tool Policy serves record_deployment to the maker, the application owner and the change manager, not the platform admin.
const HAND_DEPLOYMENT = {
  key: 'hand-dep-is-351', who: 'hugo', app: 'Inspection Scheduler', target: 'Field Ops Dev', artifactName: 'inspection-scheduler', artifactKind: 'managed-solution', version: '3.5.1.0',
  hoursAgo: 5, minutes: 12,
};
// Attempts the server refuses and records (document 25, B25-13), each shown on the AI activity page with its rule: no
// tool approves (AN-01); answer_approval is served only to a person (D-38); and a risk acceptance needs a decision right
// that Priya Castell holds on none of Asset Register's owner roles.
const AI_REFUSALS = [
  { key: 'approve', who: 'priya', tool: 'approve_decision', label: 'asks for a tool that would approve', input: async () => ({ statement: 'Toolbox talk sign-off' }) },
  {
    key: 'answer', who: 'priya', tool: 'answer_approval', label: 'tries to answer the approval of her change "Retire the old export flow"',
    input: async p => {
      const request = (await all(p, 'list_approvals', { view: 'asked-by-me' })).find(a => a.subject?.label === 'Retire the old export flow' || String(a.decision?.statement ?? '').includes('Retire the old export flow'));
      if (!request) throw new Error('No approval request on "Retire the old export flow" to try.');
      return { approvalRecordId: request.approvalRecordId, answer: 'approve', reason: 'The change is small and tested.' };
    },
  },
  {
    key: 'risk', who: 'priya', tool: 'save_decision_proposal', label: 'proposes a risk acceptance on Asset Register, which she does not own',
    input: async p => ({
      decisionTypeKey: 'risk-acceptance', subjectKind: 'business-application', subjectId: (await application(p, 'Asset Register')).businessApplicationId,
      statement: 'Accept that warranty dates are typed in by hand until the supplier feed is ready.',
      context: 'The warranty dates of Asset Register 7.2 come from supplier invoices until the supplier feed goes live.',
      proposedOutcomeKey: 'accept', reason: 'Only the stores team types the dates, and the feed is planned for March.',
    }),
  },
];

// Change requests, drafted by a maker or the change manager. Days count from today; hours are UTC.
const CHANGES = [
  { key: 'chg-is-export', who: 'priya', app: 'Inspection Scheduler', title: 'Retire the old export flow', start: [9, 18], end: [9, 19], target: 'Field Ops Prod', description: 'The inspector workload view replaces the spreadsheet export, so the old export flow is turned off and removed.', submit: true },
  { key: 'chg-cp-sign-in', who: 'priya', app: 'Contractor Portal', title: 'Contractor Portal sign-in page update', start: [10, 22], end: [11, 1], target: 'Customer Prod', description: 'Brings the in-portal sign-in fix of November 19 into the release pipeline.', submit: true },
  { key: 'chg-ar-72', who: 'priya', app: 'Asset Register', title: 'Asset Register 7.2: warranty dates', start: [22, 18], end: [22, 21], target: 'Field Ops Prod', description: 'Shows warranty end dates and warns 90 days before.', submit: true },
  { key: 'chg-cr-53', who: 'priya', app: 'Crew Roster', title: 'Crew Roster 5.3: skills matrix', start: [16, 18], end: [16, 20], target: 'Field Ops Prod', description: 'Adds a skills matrix so planners can see who can do which job.', submit: true },
  { key: 'chg-fml-tires', who: 'priya', app: 'Fleet Maintenance Log', title: 'Fleet Maintenance Log: tire check reminders', start: [14, 18], end: [14, 19], target: 'Field Ops Prod', description: 'Reminds drivers to log a tire check every two weeks, and shows overdue checks to the depot lead.', submit: true },
  { key: 'chg-so-go-live', who: 'hugo', app: 'Supplier Onboarding', title: 'Supplier Onboarding go-live', start: [8, 17], end: [8, 20], target: 'Finance Prod', description: 'Go live for all new suppliers. Waits for the runbook and the owner acceptance.', submit: true },
  { key: 'chg-pca-25', who: 'hugo', app: 'Parts Catalog API', title: 'Parts Catalog API 2.5: supplier stock levels', start: [16, 20], end: [16, 22], target: 'Integration Prod', description: 'Adds supplier stock levels to the part lookup.', submit: true },
  { key: 'chg-db-calendar', who: 'hugo', app: 'Desk Booking', title: 'Desk Booking calendar connector update', start: [17, 6], end: [17, 8], target: 'Desk Booking tenant', description: 'Moves the calendar connector to the vendor\'s new API.', submit: true },
  { key: 'chg-im-113', who: 'hugo', app: 'Invoice Matching', title: 'Invoice Matching 1.13', start: [51, 15], end: [51, 17], target: 'Finance Prod', description: 'Matches invoices in other currencies.', submit: true },
  { key: 'chg-ea-pilot', who: 'hugo', app: 'Expense Assistant', title: 'Expense Assistant pilot in test', start: [15, 9], end: [15, 12], target: 'Finance Test', description: 'Pilot with the finance team in test.', submit: false },
  { key: 'chg-cfb-pilot', who: 'priya', app: 'Customer Feedback Bot', title: 'Customer Feedback Bot pilot', start: [22, 9], end: [23, 17], target: null, description: 'Two-day pilot with one region. The target is not chosen yet.', submit: false },
];

// Decision proposals, each by a persona who holds the decision right (the engine checks it) and submitted, so the
// approval requests wait for the people the rules ask.
const DECISIONS = [
  {
    key: 'dec-is-risk', who: 'nadia', type: 'risk-acceptance', app: 'Inspection Scheduler', outcome: 'accept',
    statement: 'Accept that inspection photos stay on devices for up to 7 days while offline.',
    context: 'Inspectors work where there is no signal. The app keeps photos on the device until it can upload them, for up to 7 days, so a lost device could expose them.',
    reason: 'The devices are managed and encrypted, and losing a week of offline inspections would cost more than the small exposure.',
  },
  {
    key: 'dec-sdp-risk', who: 'noor', type: 'risk-acceptance', app: 'Service Desk Portal', outcome: 'accept',
    statement: 'Accept that call recordings are kept for 90 days until the vendor fixes early deletion.',
    context: 'The portal should delete call recordings after 30 days, but the vendor\'s deletion job fails. The fix ships in the vendor\'s next release.',
    reason: 'Only the service desk can play the recordings, access is logged, and the vendor has a release date.',
  },
  {
    key: 'dec-ids-risk', who: 'hugo', type: 'risk-acceptance', app: 'Identity Directory Sync', outcome: 'accept',
    statement: 'Accept a shared service account for the directory sync until its managed identity is ready.',
    context: 'The sync still signs in with a shared service account. Moving it to a managed identity waits for the platform upgrade in January.',
    reason: 'The account is excluded from interactive sign-in, its secret rotates monthly, and the upgrade has a date.',
  },
  // The data-use decisions about whole applications that wave 2 proposed here (Visit Report Agent and Field Knowledge
  // MCP Server) are now decisions about declared data uses, in DATA_DECISIONS below (data taxonomy slice 5).
  {
    key: 'dec-fkms-go-live', who: 'hugo', type: 'go-live', app: 'Field Knowledge MCP Server', outcome: 'go',
    statement: 'Go live with the read-only tools for the field service desk.',
    context: 'The server passed its test release, and the service desk wants AI clients to look up the safety rules.',
    reason: 'The tools only read, every call is logged with the client and the person, and support is ready.',
  },
  {
    key: 'dec-cfb-autonomy', who: 'dmitri', type: 'autonomy-level', app: 'Customer Feedback Bot', outcome: 'agree',
    statement: 'Agree the assist level: the bot drafts answers and a service agent sends them.',
    context: 'The bot is about to start its pilot. It reads customer feedback and drafts an answer for each message.',
    reason: 'A person reads and sends every answer, so the bot never speaks to a customer on its own.',
  },
  {
    key: 'dec-sdp-portfolio', who: 'mateo', type: 'portfolio', app: 'Service Desk Portal', outcome: 'keep',
    statement: 'Keep Service Desk Portal and renew the vendor contract for two years.',
    context: 'The vendor contract ends in March. Two other portals were compared on cost and on the work to move.',
    reason: 'Moving would cost more than two years of the contract, and the portal meets the service desk\'s needs.',
  },
  {
    key: 'dec-im-exception', who: 'elif', type: 'exception', app: 'Invoice Matching', subject: { kind: 'deployment-record', artifactVersion: '1.12.1' }, outcome: 'grant',
    statement: 'Grant an exception for the 1.12.1 hotfix, whose code was approved only by an AI reviewer.',
    context: 'The hotfix stopped duplicate payments on a Saturday. No person was on call to review the code, so only the AI reviewer approved it before release.',
    options: [
      { option: 'Roll the hotfix back until a person reviews it.', whyNot: 'Duplicate payments would start again.' },
      { option: 'Grant the exception, with a person reviewing the code this week.', whyNot: 'Chosen.' },
    ],
    reason: 'The fix stops a real loss, and a person reviews the code within the week.',
    exception: { standard: 'A person reviews and approves the code of every release before it goes live.', reviewOn: '2026-12-04' },
  },
  {
    // Work for the Change board on the Teams page (wave 4): an exception about a normal change. Under Balanced only the
    // change board holds the decision right and the approval of an exception about a change, so Tomas Rendel proposes it
    // and the Change board's card waits (first to respond, with a typed reason).
    key: 'dec-db-exception', who: 'tomas', type: 'exception', app: 'Desk Booking', subject: { kind: 'change-request', title: 'Desk Booking calendar connector update' }, outcome: 'grant',
    statement: 'Grant an exception: the Desk Booking calendar connector update goes ahead without a rollback rehearsal in test.',
    context: 'The vendor closes its old calendar API on the day of the window, and its test tenant will not be ready before then, so the rollback cannot be rehearsed first.',
    options: [
      { option: 'Move the window until the vendor\'s test tenant is ready.', whyNot: 'Desk bookings would stop syncing when the old API closes.' },
      { option: 'Grant the exception, with the vendor on call during the window.', whyNot: 'Chosen.' },
    ],
    reason: 'The connector only reads calendars, the old version can be put back from the vendor\'s console in minutes, and the vendor\'s support is on call during the window.',
    exception: { standard: 'Every normal change rehearses its rollback in a test environment before its window.', reviewIn: 30 },
  },
  {
    key: 'dec-cta-design', who: 'elif', type: 'design', app: 'Claims Triage Agent', subject: { kind: 'review', reviewType: 'architecture' }, outcome: 'approve',
    statement: 'Route claims with a Copilot Studio agent and keep claim photos in the claims system.',
    context: 'The agent sorts incoming claims by type and urgency. Claim photos can show people and homes.',
    options: [
      { option: 'Copy the photos into the agent\'s own store.', whyNot: 'A second copy of personal photos to protect and delete.' },
      { option: 'A custom model in a code app.', whyNot: 'More to build and run than the approved agent platform.' },
    ],
    evidence: [{ linkKindKey: 'evidence-url', url: 'https://git.example.org/customer-services/claims-triage-agent/docs/adr/0002-routing.md', label: 'ADR 2: claim routing' }],
    reason: 'Copilot Studio is the approved agent platform, and the photos never leave the claims system.',
  },
  {
    // A way out proposed (the Retirement page's first column): the business owner answers.
    key: 'dec-csm-portfolio', who: 'mateo', type: 'portfolio', app: 'Crew Shift Mobile', outcome: 'replace',
    statement: 'Replace Crew Shift Mobile with the phone view of Crew Roster 5.3.',
    context: 'Crew Roster 5.3 adds a phone view of each crew member\'s shifts, which is what Crew Shift Mobile shows today.',
    reason: 'One app for shifts is cheaper to run, and crews already sign in to Crew Roster.',
  },
  {
    // A decision on a flagged agent (the Agents page): the AI lead decides, and the business owner is consulted.
    key: 'dec-oba-flagged', who: 'owen', type: 'flagged-agent', app: 'Onboarding Buddy Agent', outcome: 'keep',
    statement: 'Keep Onboarding Buddy Agent, shared with the pilot group only until its go-live gate is met.',
    context: 'The agent was shared with all staff while it is still in build. Its owner has since limited it to the 40 new starters of the pilot.',
    reason: 'New starters use it every day, and the pilot group is small enough to watch until the agent goes live.',
  },
  {
    // A priority waiting (the Demand page): the PMO proposes it, and the sponsor answers, or the business owner when
    // there is no sponsor.
    key: 'dec-rebates-priority', who: 'mateo', type: 'priority', subject: { kind: 'intake-request', title: 'Supplier rebate tracking' }, outcome: 'fund',
    statement: 'Fund supplier rebate tracking this quarter.',
    context: 'The request scored highest on the demand board. Rebates under volume agreements are worked out in spreadsheets, and some are never claimed.',
    reason: 'The unclaimed rebates alone pay for the build within a year.',
  },
];

const ARCHITECTURE_REVIEW = {
  key: 'review-cta', who: 'priya', app: 'Claims Triage Agent',
  reason: 'Please review the routing design and where claim photos are kept before the build goes further.',
  evidence: [{ linkKindKey: 'evidence-url', url: 'https://git.example.org/customer-services/claims-triage-agent/docs/adr/0002-routing.md', label: 'ADR 2: claim routing' }],
};

// Documents recorded as drafts by an owner of the application, for its owners to confirm.
const DOCUMENTS = [
  { key: 'doc-cp-support', who: 'priya', app: 'Contractor Portal', type: 'support-guide', url: 'https://wiki.example.org/customer-services/contractor-portal/support-guide' },
  { key: 'doc-ids-support', who: 'noor', app: 'Identity Directory Sync', type: 'support-guide', url: 'https://wiki.example.org/platforms/identity-directory-sync/support-guide' },
];

// Owners: a business owner for the application that has none, and a leave with its successor.
const OWNERSHIP = [
  { key: 'own-sft', who: 'owen', app: 'Safety Findings Tracker', successor: 'lena', reason: 'Safety Findings Tracker has had no business owner since the end of September. Lena Okoro covers it as deputy and agreed to take it on.' },
];
// The gap check (UC-10, demo 3:30): the Power Platform inventory, as the platform admin's own tool exported it. The list
// is the whole inventory in one final page: every registered component the gap check covers, read from the register,
// and four items no application holds, each of which opens a claim. One registered component is missing from it: the
// Timesheets canvas app that the Legacy Timesheet App removal change took away, so the finished run raises its vanished
// claim, which the Retirement tracker shows. Each new item carries the link the admin's list gave, so the claim, and the
// component a claim creates, open it where it lives (document 23, B23-10). Afterwards the maker claims one item into
// her application and the platform admin dismisses one; two stay open for the live demo.
const GAP_CHECK = {
  key: 'gap-check-2', who: 'owen', estateKey: 'power-platform-inventory',
  sourceNote: 'Exported from the Power Platform admin center for the demo: every environment, in one list.',
  items: [
    { externalId: 'demo-flow-visit-photos', componentKindKey: 'cloud-flow', labelAsSeen: 'Copy visit photos to SharePoint', targetAsSeen: 'Default environment', suggestedTo: 'priya', url: 'https://flows.example.org/default/demo-flow-visit-photos' },
    { externalId: 'demo-flow-test-reminder', componentKindKey: 'cloud-flow', labelAsSeen: 'Test reminder flow (personal)', targetAsSeen: 'Default environment', url: 'https://flows.example.org/default/demo-flow-test-reminder' },
    { externalId: 'demo-flow-photo-resize', componentKindKey: 'cloud-flow', labelAsSeen: 'Resize inspection photos', targetAsSeen: 'Field Ops Prod', suggestedTo: 'priya', url: 'https://flows.example.org/field-ops-prod/demo-flow-photo-resize' },
    { externalId: 'demo-app-canteen-copy', componentKindKey: 'canvas-app', labelAsSeen: 'Canteen menu (old copy)', targetAsSeen: 'Default environment', url: 'https://apps.example.org/default/demo-app-canteen-copy' },
  ],
  // The registered component the list leaves out: it vanished with the removal change.
  vanishes: { app: 'Legacy Timesheet App', labelAsSeen: 'Timesheets' },
  claim: { key: 'claim-photo-resize', who: 'priya', labelAsSeen: 'Resize inspection photos', app: 'Inspection Scheduler' },
  dismiss: {
    key: 'dismiss-canteen-copy', who: 'owen', labelAsSeen: 'Canteen menu (old copy)',
    reason: 'A copy of the canteen menu app its maker kept for testing. It holds no data and no one uses it, so it is not a business application.',
  },
};
// The component kinds the gap check covers when the configuration cannot be read (Balanced, component-kind.json).
const COVERED_KINDS = ['canvas-app', 'model-driven-app', 'code-app', 'cloud-flow', 'copilot-studio-agent', 'copilot-managed-runtime-app', 'power-pages-site'];

// A deployment target the platform admin registers in the web app (Places). record_deployment takes it at once.
const NEW_TARGET = {
  key: 'target-people-test', who: 'owen', labelAsSeen: 'People Test', targetKindKey: 'power-platform-environment', targetStageKey: 'test', changePathKey: 'pipeline-only',
  linkKindKey: 'power-platform-resource', externalId: 'people-test', url: 'https://make.example.org/environments/people-test',
  description: 'The test environment for the people apps, before People Prod.',
};

// Work finished before the demo. The architect completes the Quote Builder architecture review with the design decision
// she accepted (the review then shows the Design gate met), and the change manager closes the Legacy Timesheet App
// removal change that ran in its window, with the check that the app is gone as evidence.
const REVIEW_DONE = {
  key: 'review-qb-done', who: 'elif', app: 'Quote Builder', reviewType: 'architecture', decisionType: 'design',
  statement: 'Build Quote Builder as a code app that reads prices from Parts Catalog API and keeps no copy of them.',
  evidence: [{ linkKindKey: 'evidence-url', url: 'https://git.example.org/customer/quote-builder/docs/adr/0001-prices.md', label: 'ADR 1: prices from Parts Catalog API' }],
};
const CHANGE_DONE = {
  key: 'change-lta-remove-done', who: 'hugo', app: 'Legacy Timesheet App', title: 'Remove the Timesheets canvas app', resultKey: 'succeeded',
  evidence: [{ linkKindKey: 'evidence-url', url: 'https://wiki.example.org/people/legacy-timesheet-app/removal-check', label: 'Removal check: the app no longer opens in People Prod' }],
};

const LEAVE = {
  key: 'leave-wren', who: 'owen', person: 'Wren Abernathy', knownFrom: 'Claims Triage Agent', leavesIn: 24,
  note: 'Moves to the partner team at the end of the year.',
  successor: { key: 'own-cta', app: 'Claims Triage Agent', successor: 'nadia', reason: 'Wren Abernathy leaves at the end of the year. Nadia Ferreira, the deputy, takes over the claims agent.' },
};

// ---- The data taxonomy (D-37; docs/architecture/data-taxonomy.md; slice 5) ----

// Decisions about declared data uses and copies, proposed and submitted by a persona who holds the decision right
// (Balanced: the application's business or technical owner, or the platform admin, proposes a data-use decision about a
// use; the platform admin proposes a copy or retention decision), so the cards wait for the domain's data owner by name.
// subject: the use (application, domain, location, AI) or the mapping (domain, location, origin) the decision is about.
const DATA_DECISIONS = [
  {
    // The 9:30 step: restricted data to AI, so the data owner of Safety findings (Kai Ostrowski) and the security
    // reviewer are both asked: 0 of 2 approvals until Kai and then Noor Safadi approve in the demo.
    key: 'data-vra-safety', who: 'elif', type: 'data-use', outcome: 'allow',
    use: { app: 'Visit Report Agent', domain: 'safety-findings', location: 'Field Ops Prod', ai: true },
    statement: 'Allow Visit Report Agent to send the open safety findings of a site to the approved AI model.',
    context: 'The agent drafts visit reports from inspector notes. Adding the open safety findings makes the drafts complete, but the findings can hold injury details.',
    reason: 'The approved model keeps the data in the region and does not train on it, and the inspector checks every draft before it leaves the team.',
  },
  {
    key: 'data-mcp-assets', who: 'hugo', type: 'data-use', outcome: 'allow',
    use: { app: 'Field Knowledge MCP Server', domain: 'assets', location: 'Field Data Lake', ai: true },
    statement: 'Allow AI clients to read asset data from the Field Data Lake through the MCP server.',
    context: 'The service desk asks AI clients where an asset is and when it was last serviced. The server reads the asset replica in the lake.',
    reason: 'The tools only read, every call is logged with the client and the person, and site security details are left out of the answers.',
  },
  {
    key: 'data-cta-cases', who: 'owen', type: 'data-use', outcome: 'allow',
    use: { app: 'Claims Triage Agent', domain: 'client-cases', location: 'Customer Prod', ai: true },
    statement: 'Allow Claims Triage Agent to send damage claims to the approved AI model to route them.',
    context: 'The agent reads each new damage claim and routes it to the right team with a short summary.',
    reason: 'Claims stay in the region, the model does not train on them, and a person in the team checks each routing.',
  },
  {
    key: 'data-oba-personnel', who: 'owen', type: 'data-use', outcome: 'allow',
    use: { app: 'Onboarding Buddy Agent', domain: 'personnel-records', location: 'People Hub tenant', ai: true },
    statement: 'Allow Onboarding Buddy Agent to read a new starter\'s manager, team and training plan with AI.',
    context: 'The agent answers new starters\' questions about their first weeks. It needs to know who their manager is and which training is booked.',
    reason: 'The agent reads only the new starter\'s own record, and the pilot group is 40 people.',
  },
  {
    // A copy outside production: Finance Test holds a masked copy of supplier contracts, so the copy rule (masked-only)
    // asks for a decision by the data owner of Contracts, Clara Voss.
    key: 'data-copy-fint', who: 'owen', type: 'data-use', outcome: 'allow',
    mapping: { domain: 'supplier-contracts', location: 'Finance Test', origin: 'masked-copy' },
    statement: 'Allow the masked copy of supplier contracts in Finance Test for the invoice matching tests.',
    context: 'The invoice matching tests need contracts with real payment terms. Supplier names and bank details are masked before the copy is made.',
    reason: 'The copy is masked, Finance Test is open only to the finance team, and the copy is refreshed and checked each quarter.',
  },
  {
    // Retention of the data Legacy Timesheet App wrote on its system of record (P09). The data owner of Timesheets
    // (inherited from People: Hana Lindqvist) decides; the business owner is consulted.
    key: 'data-lta-retention', who: 'owen', type: 'data-retention', outcome: 'archive',
    use: { app: 'Legacy Timesheet App', domain: 'timesheets', location: 'Timesheets', ai: false },
    statement: 'Archive the timesheet entries of the old app and keep them for seven years.',
    context: 'People Hub holds every new time entry. The old entries in the Timesheets database are read-only and payroll audits can still ask for them.',
    reason: 'Seven years is what payroll audits need, and the archive lets the Timesheets database be switched off.',
  },
];

// Claude Code's data work (dev mode, through the MCP endpoint). A draft use on Mileage Claims for Priya Castell, who
// builds it (docs/architecture/data-taxonomy.md, In the AI build loop), and a draft mapping for Nadia Ferreira, data owner of Customers: an AI client's row is a
// draft that a person confirms (G-21). And the never rule (D-41, question 7, as the product owner changed it): Claude Code tries to
// declare Expense Assistant's AI use of bank details for Priya, and the save is refused with the rule's name (G-25).
const AI_DATA_USE = {
  key: 'ai-du-mc', who: 'priya', app: 'Mileage Claims', domain: 'paychecks', location: 'PAYLIB', access: 'write',
  purpose: 'Writes approved mileage payments into the next pay run.',
};
const AI_DATA_MAPPING = {
  key: 'ai-map-cases-sfdc', who: 'nadia', domain: 'client-cases', location: 'Sales CRM production org', origin: 'replica', containers: ['Case'],
};
const AI_NEVER = {
  key: 'never', who: 'priya', tool: 'save_data_use', label: 'tries to declare Expense Assistant\'s use of bank details with AI',
  input: async p => ({
    applicationId: (await application(p, 'Expense Assistant')).businessApplicationId, domainKey: 'bank-details',
    locationId: (await dataLocation(p, 'PAYLIB')).dataLocationId, access: 'read', reachesAi: true,
    purpose: 'Checks the bank account on an expense claim against the account pay goes to.', aiServiceAsSeen: 'The approved AI model, in the region',
  }),
};

// A configuration change the configuration admin drafts and submits (P11): the AI use rule of Workplace / Visitor log
// becomes never. It tightens the taxonomy, so the data owner of Workplace is consulted by name. In the demo the second
// configuration admin persona, Jonah Pereira, accepts it; Balanced lets one admin accept, and Regulated and
// strict-separation require a second (docs/architecture/data-taxonomy.md).
const CONFIGURATION_CHANGE = {
  key: 'cfg-visitor-log-ai', who: 'yusuf',
  proposalRationale: 'Visitor names and the people they visit must not go to AI tools. Set the AI use rule of Workplace / Visitor log to never, so a data use that would send them is refused when it is saved.',
  operations: [{ op: 'upsert', entity: 'data-domain', key: 'visitor-log', values: { aiUseRule: 'never' } }],
};

// The daily due-state refresh, run now by the platform admin as the timer runs it (UC-08). The estate's done access
// check of Identity Directory Sync came due again on November 23, so the run opens the next one.
const DUE_STATE_REFRESH = { key: 'refresh', who: 'owen', app: 'Identity Directory Sync', reviewType: 'access-check', dueOn: '2026-11-23' };

// A security review request for an agent (the Agents page, tool 94): a request, never a decision.
const SECURITY_REVIEW = {
  key: 'security-review-cfb', who: 'dmitri', app: 'Customer Feedback Bot', dueIn: 10,
  reason: 'The bot starts its pilot next month and reads customer feedback. Please review what it sends to the model and who can see its answers.',
};

// ---------------------------------------------------------------------------------------------------------------
// Steps
// ---------------------------------------------------------------------------------------------------------------

const results = [];
const roleText = key => String(key ?? '').replace(/-/g, ' ');

// The labels of the roles and owner roles in force, read once at the start (wave 4b QA), so the director's lines name
// them as the web app does ("the PMO", "the business owner of Claims Triage Agent"), not by their keys.
const ROLE_LABELS = new Map();
async function readRoleLabels(p) {
  for (const entity of ['role', 'owner-role']) {
    try {
      const rows = (await call(p, 'get_configuration', { entity, top: 100 })).data?.rows ?? [];
      for (const r of rows) if (r.key && r.fields?.label) ROLE_LABELS.set(entity + ':' + r.key, r.fields.label);
    } catch {
      // The keys stand in for the labels.
    }
  }
}

// A label in running text: "Platform admin" reads "platform admin"; an acronym such as "PMO" or "AI lead" stays.
function labelOf(entity, key) {
  const label = ROLE_LABELS.get(entity + ':' + key) ?? roleText(key);
  return /^[A-Z][a-z]/.test(label) ? label[0].toLowerCase() + label.slice(1) : label;
}

// "a", "a and b", "a, b and c".
const andList = parts => (parts.length <= 1 ? parts.join('') : parts.slice(0, -1).join(', ') + ' and ' + parts[parts.length - 1]);

async function step(title, who, fn) {
  let result;
  try {
    result = await fn();
  } catch (e) {
    result = { outcome: 'failed', note: e instanceof Refused ? e.message : plain(e.message) };
  }
  results.push({ title, persona: who, ...result });
  if (!asJson) {
    const label = { created: 'done', present: 'already there', skipped: 'skipped', failed: 'FAILED' }[result.outcome] ?? result.outcome;
    console.log('  ' + label.padEnd(14) + who.padEnd(7) + title + (result.note ? ': ' + result.note : ''));
  }
}

// Who a submit asked, in words.
function asked(env, app) {
  const records = env.data?.approvalsRequested ?? [];
  if (env.data?.engineAccepted) return 'the engine accepted it with no card';
  if (!records.length) return 'nobody asked yet';
  // A record that asks one person by name (a proposed owner, or a portfolio's accountable person) names them, with no
  // article; an owner role names its application; a consultation says so (wave 4b QA).
  const who = records.map(r => r.askedPrincipal ? r.askedPrincipal.displayLabel + ' by name'
    : r.requiredOwnerRoleKey ? 'the ' + labelOf('owner-role', r.requiredOwnerRoleKey) + (app ? ' of ' + app : '') + (r.isConsultation ? ' (consulted)' : '')
      : 'the ' + labelOf('role', r.requiredRoleKey) + (r.isConsultation ? ' (consulted)' : ''));
  return 'asks ' + andList([...new Set(who)]);
}

// The application rows the work is about, by name.
const apps = new Map();
async function application(p, name) {
  if (apps.has(name)) return apps.get(name);
  const found = (await all(p, 'find_applications', { text: name })).find(a => a.name === name);
  if (!found) throw new Error('No application named ' + name + ' in the store. Seed it with seed/demo-estate.json.');
  apps.set(name, found);
  return found;
}

// Deployment targets by label, read from the change schedule and the deployments.
let targetIds;
async function target(p, label) {
  if (!label) return undefined;
  if (!targetIds) {
    targetIds = new Map();
    for (const c of await all(p, 'get_change_schedule', { from: day(-120), to: day(180) })) {
      if (c.target?.label && c.target?.deploymentTargetId) targetIds.set(c.target.label, c.target.deploymentTargetId);
    }
    for (const d of await all(p, 'get_deployments', {})) {
      if (d.targetLabel && d.deploymentTargetId) targetIds.set(d.targetLabel, d.deploymentTargetId);
    }
  }
  return targetIds.get(label);
}

// Tier answers that point at the tier: for each question, the answer that implies it.
async function tierAnswers(p, kind, tierKey) {
  const questions = (await call(p, 'get_tier_questions', { applicationKindKey: kind })).data?.questions ?? [];
  return questions.map(q => ({ questionKey: q.questionKey, answerKey: (q.answers.find(a => a.impliedTier?.key === tierKey) ?? q.answers[0]).answerKey }));
}

// True when the decision is on approval cards (submitted): it has approval records.
async function submitted(p, decisionId) {
  const d = (await call(p, 'get_decision', { decisionId })).data;
  return (d?.approvals?.records ?? []).length > 0 || d?.decision?.status?.meaning !== 'proposed';
}

async function intakeStep(spec) {
  const p = persona(spec.who);
  const title = 'intake "' + spec.title + '"' + (spec.ai ? ', drafted by Claude Code' : '');
  if (spec.ai && !aiWorks) {
    return step(title, p.name, async () => ({ outcome: 'skipped', note: 'an AI draft needs the AI client entry; in a demo install Claude Code drafts it live' }));
  }
  return step(title, p.name, async () => {
    let intake = (await all(p, 'list_intake_requests', { view: 'mine', text: spec.title })).find(i => i.title === spec.title);
    const done = [];
    let id = intake?.intakeRequestId;
    let status = intake?.status?.meaning ?? 'draft';
    if (!intake) {
      const input = {
        title: spec.title, need: spec.need, proposedKindKey: spec.kind, horizonKey: spec.horizon, ...(spec.benefit ? { expectedBenefit: spec.benefit } : {}), usesAi: false,
        ...(spec.owner ? { businessOwnerObjectId: persona(spec.owner).objectId } : {}), clientRequestId: requestId(spec.key),
      };
      const env = spec.ai ? await callAsAi(aiPersona, 'save_intake_draft', input) : await call(p, 'save_intake_draft', input);
      id = env.data.recordId;
      done.push('drafted');
    }
    if (spec.tier && status === 'draft') {
      const detail = (await call(p, 'get_intake_request', { intakeRequestId: id })).data;
      if (!(detail?.tierAssessments ?? []).length) {
        const env = await call(p, 'answer_tier_questions', { intakeRequestId: id, answers: await tierAnswers(p, spec.kind, spec.tier), clientRequestId: requestId(spec.key + ':tier') });
        done.push('tier answers (' + (env.data?.computedTier?.label ?? spec.tier) + ')');
      }
    }
    if (spec.submit && status === 'draft') {
      const env = await call(p, 'submit_record', { subjectKind: 'intake-request', subjectId: id, clientRequestId: requestId(spec.key + ':submit') });
      status = 'submitted';
      done.push('submitted, ' + asked(env));
    }
    return done.length ? { outcome: 'created', note: done.join('; ') } : { outcome: 'present', note: status };
  });
}

// ---- The AI client's work (dev mode only) ----

const AI_SKIP = "needs the AI client entry: set PRODUCT_DEMO_MCP_TOKEN to the operator's Claude Code token, or Claude Code works live in the demo";

// Claude Code, acting for a persona, makes a call the server refuses: the server records the refused call as an audit
// entry (document 25, B25-13), which get_ai_activity lists as a refusal with its rule. Run once: a refusal of the same
// tool already listed for the persona is left as it is.
async function aiRefusalStep(spec) {
  const p = persona(spec.who);
  const title = 'Claude Code ' + spec.label + ', for ' + p.person;
  if (!aiWorks) return step(title, p.name, async () => ({ outcome: 'skipped', note: AI_SKIP }));
  return step(title, p.name, async () => {
    const refused = await all(p, 'get_ai_activity', { view: 'refused' });
    if (refused.some(i => i.refusal?.tool === spec.tool)) return { outcome: 'present', note: 'the refused attempt is listed' };
    try {
      await callAsAi(aiPersona, spec.tool, await spec.input(p));
    } catch (e) {
      if (e instanceof Refused) return { outcome: 'created', note: 'refused with ' + e.code + ', and recorded' };
      throw e;
    }
    return { outcome: 'failed', note: 'the server took the call' };
  });
}

// Claude Code drafts a change for the persona, who submits it as drafted: the AI activity page shows it taken forward
// by a person, with no edit before.
async function aiTakenForwardStep(spec) {
  const p = persona(spec.who);
  const title = 'change "' + spec.title + '" on ' + spec.app + ', drafted by Claude Code, submitted as drafted';
  if (!aiWorks) return step(title, p.name, async () => ({ outcome: 'skipped', note: AI_SKIP }));
  return step(title, p.name, async () => {
    const app = await application(p, spec.app);
    const existing = (await all(p, 'get_change_schedule', { from: day(-60), to: day(180), businessApplicationId: app.businessApplicationId }))
      .find(c => c.title === spec.title && c.businessApplicationId === app.businessApplicationId);
    const done = [];
    let id = existing?.changeRequestId;
    if (!existing) {
      const targetId = await target(p, spec.target);
      const env = await callAsAi(aiPersona, 'save_change_request_draft', {
        title: spec.title, businessApplicationId: app.businessApplicationId, changeTypeKey: 'normal', description: spec.description,
        windowStart: at(...spec.start), windowEnd: at(...spec.end), ...(targetId ? { plannedTargetId: targetId } : {}), clientRequestId: requestId(spec.key),
      });
      id = env.data.recordId;
      done.push('drafted by Claude Code for ' + day(spec.start[0]));
    }
    if ((existing?.status?.meaning ?? 'draft') === 'draft') {
      const env = await call(p, 'submit_record', { subjectKind: 'change-request', subjectId: id, clientRequestId: requestId(spec.key + ':submit') });
      done.push('submitted by ' + p.person + ' as drafted, ' + asked(env, spec.app));
    }
    return done.length ? { outcome: 'created', note: done.join('; ') } : { outcome: 'present', note: existing.status?.meaning };
  });
}

// Claude Code drafts a decision for the persona, who changes its context and reason, then submits it: the AI activity
// page shows that a person edited it first.
async function aiEditedStep(spec) {
  const p = persona(spec.who);
  const title = spec.type + ' decision on ' + spec.app + ', drafted by Claude Code, changed by ' + p.person + ' before she submits it';
  if (!aiWorks) return step(title, p.name, async () => ({ outcome: 'skipped', note: AI_SKIP }));
  return step(title, p.name, async () => {
    const app = await application(p, spec.app);
    const existing = (await all(p, 'get_decision_log', { businessApplicationId: app.businessApplicationId, decisionTypeKeys: [spec.type] }))
      .find(d => d.statement === spec.statement);
    if (existing && existing.status?.meaning !== 'proposed') return { outcome: 'present', note: existing.status?.meaning };
    const fields = { decisionTypeKey: spec.type, subjectKind: 'business-application', subjectId: app.businessApplicationId, statement: spec.statement, proposedOutcomeKey: spec.outcome, tierKey: spec.tierKey };
    const done = [];
    let id = existing?.decisionId;
    if (!id) {
      const env = await callAsAi(aiPersona, 'save_decision_proposal', { ...fields, ...spec.drafted, clientRequestId: requestId(spec.key) });
      id = env.data.recordId;
      done.push('drafted by Claude Code');
    }
    if (!(await submitted(p, id))) {
      await call(p, 'save_decision_proposal', { ...fields, ...spec.edited, decisionId: id, clientRequestId: requestId(spec.key + ':edit') });
      const env = await call(p, 'submit_record', { subjectKind: 'decision', subjectId: id, clientRequestId: requestId(spec.key + ':submit') });
      done.push('context and reason changed by ' + p.person + ', then submitted, ' + asked(env, spec.app));
    }
    return done.length ? { outcome: 'created', note: done.join('; ') } : { outcome: 'present', note: 'waiting for answers' };
  });
}

// Claude Code drafts a change for the persona, who withdraws it: the AI activity page shows it withdrawn, and by whom.
async function aiWithdrawnStep(spec) {
  const p = persona(spec.who);
  const title = 'change "' + spec.title + '" on ' + spec.app + ', drafted by Claude Code, withdrawn by ' + p.person;
  if (!aiWorks) return step(title, p.name, async () => ({ outcome: 'skipped', note: AI_SKIP }));
  return step(title, p.name, async () => {
    const app = await application(p, spec.app);
    const existing = (await all(p, 'get_change_schedule', { from: day(-60), to: day(180), businessApplicationId: app.businessApplicationId, statusMeanings: ['draft', 'withdrawn'] }))
      .find(c => c.title === spec.title && c.businessApplicationId === app.businessApplicationId);
    if (existing && existing.status?.meaning !== 'draft') return { outcome: 'present', note: existing.status?.meaning };
    const done = [];
    let id = existing?.changeRequestId;
    if (!id) {
      const targetId = await target(p, spec.target);
      const env = await callAsAi(aiPersona, 'save_change_request_draft', {
        title: spec.title, businessApplicationId: app.businessApplicationId, changeTypeKey: 'normal', description: spec.description,
        windowStart: at(...spec.start), windowEnd: at(...spec.end), ...(targetId ? { plannedTargetId: targetId } : {}), clientRequestId: requestId(spec.key),
      });
      id = env.data.recordId;
      done.push('drafted by Claude Code');
    }
    await call(p, 'withdraw_record', { subjectKind: 'change-request', subjectId: id, reason: spec.reason, clientRequestId: requestId(spec.key + ':withdraw') });
    done.push('withdrawn by ' + p.person);
    return { outcome: 'created', note: done.join('; ') };
  });
}

// Claude Code drafts a decision for the persona and leaves it with her: it waits on the AI activity page and in her My work.
async function aiWaitingStep(spec) {
  const p = persona(spec.who);
  const title = spec.type + ' decision on ' + spec.app + ', drafted by Claude Code, waiting for ' + p.person;
  if (!aiWorks) return step(title, p.name, async () => ({ outcome: 'skipped', note: AI_SKIP }));
  return step(title, p.name, async () => {
    const app = await application(p, spec.app);
    const existing = (await all(p, 'get_decision_log', { businessApplicationId: app.businessApplicationId, decisionTypeKeys: [spec.type] }))
      .find(d => d.statement === spec.statement);
    if (existing) return { outcome: 'present', note: existing.status?.meaning };
    await callAsAi(aiPersona, 'save_decision_proposal', {
      decisionTypeKey: spec.type, subjectKind: 'business-application', subjectId: app.businessApplicationId, statement: spec.statement, context: spec.context,
      proposedOutcomeKey: spec.outcome, reason: spec.reason, clientRequestId: requestId(spec.key),
    });
    return { outcome: 'created', note: 'drafted by Claude Code, not submitted' };
  });
}

// Claude Code records a deployment it made from the AI tool. The engine sets the route and the flags; a second run sends
// the same idempotency key, which returns the record already there and writes nothing new (I-189).
async function aiDeploymentStep(spec) {
  const p = persona(spec.who);
  const title = 'deployment of ' + spec.app + ' ' + spec.version + ' to ' + spec.target + ', made from Claude Code';
  if (!aiWorks) return step(title, p.name, async () => ({ outcome: 'skipped', note: AI_SKIP }));
  return step(title, p.name, async () => {
    const app = await application(p, spec.app);
    const existing = (await all(p, 'get_deployments', { businessApplicationId: app.businessApplicationId, latestOnly: false }))
      .find(d => d.artifactVersion === spec.version && d.targetLabel === spec.target);
    if (existing) return { outcome: 'present', note: flagWords(existing.flags) };
    const targetId = await target(p, spec.target);
    if (!targetId) throw new Error('No deployment target named ' + spec.target + '.');
    const finishedOn = ago(spec.hoursAgo);
    const env = await callAsAi(aiPersona, 'record_deployment', {
      idempotencyKey: requestId(spec.key), businessApplicationId: app.businessApplicationId, artifactName: spec.artifactName, artifactVersion: spec.version,
      artifactKindKey: spec.artifactKind, deploymentTargetId: targetId, startedOn: ago(spec.hoursAgo, spec.minutes), finishedOn, resultMeaning: 'succeeded',
      codeApprovalKind: 'ai-only', reportedApprovals: [{ approverKind: 'ai', approvedOn: ago(spec.hoursAgo + 1), sourceUrl: spec.pullRequest }],
    });
    return { outcome: 'created', note: 'route ' + roleText(env.data?.route?.meaning) + ', ' + flagWords(env.data?.flags) };
  });
}

// A person records a deployment by hand in the web app (record_deployment on the person entry; route person-in-app).
async function handDeploymentStep(spec) {
  const p = persona(spec.who);
  return step('deployment of ' + spec.app + ' ' + spec.version + ' to ' + spec.target + ', recorded by hand', p.name, async () => {
    const app = await application(p, spec.app);
    const existing = (await all(p, 'get_deployments', { businessApplicationId: app.businessApplicationId, latestOnly: false }))
      .find(d => d.artifactVersion === spec.version && d.targetLabel === spec.target);
    if (existing) return { outcome: 'present', note: flagWords(existing.flags) };
    const targetId = await target(p, spec.target);
    if (!targetId) return { outcome: 'skipped', note: 'no deployment target named ' + spec.target };
    const env = await call(p, 'record_deployment', {
      idempotencyKey: requestId(spec.key), businessApplicationId: app.businessApplicationId, artifactName: spec.artifactName, artifactVersion: spec.version,
      artifactKindKey: spec.artifactKind, deploymentTargetId: targetId, startedOn: ago(spec.hoursAgo, spec.minutes), finishedOn: ago(spec.hoursAgo), resultMeaning: 'succeeded',
      codeApprovalKind: 'person',
    });
    return { outcome: 'created', note: 'route ' + roleText(env.data?.route?.meaning) + ', ' + flagWords(env.data?.flags) };
  });
}

// The flags of a deployment, in words: record_deployment answers { otherPath: true, ... }, get_deployments a list.
function flagWords(flags) {
  const keys = Array.isArray(flags) ? flags.map(f => f.key ?? f) : Object.entries(flags ?? {}).filter(([, on]) => on === true).map(([k]) => k.replace(/[A-Z]/g, c => '-' + c.toLowerCase()));
  return keys.length ? 'flagged: ' + keys.map(roleText).join(', ') : 'not flagged';
}

// ---- Demand ----

// The PMO scores a request (score_intake_request, stored as reported). A request with a score is left as it is.
async function scoreStep(spec) {
  const p = persona(spec.who);
  return step('score of "' + spec.title + '"', p.name, async () => {
    const intake = await intakeByTitle(p, spec.title);
    if (intake.score !== null && intake.score !== undefined) return { outcome: 'present', note: 'scored ' + intake.score };
    await call(p, 'score_intake_request', {
      intakeRequestId: intake.intakeRequestId, score: spec.score, sizeBandKey: spec.size, horizonKey: spec.horizon,
      ...(spec.benefit ? { expectedBenefit: spec.benefit } : {}), clientRequestId: requestId(spec.key),
    });
    return { outcome: 'created', note: 'scored ' + spec.score + ', ' + spec.size + ', ' + spec.horizon };
  });
}

// ---- Work finished before the demo ----

// The architect completes a due review with the accepted decision it records (complete_review, a person's tool).
async function reviewDoneStep(spec) {
  const p = persona(spec.who);
  return step(roleText(spec.reviewType) + ' review of ' + spec.app + ', completed', p.name, async () => {
    const app = await application(p, spec.app);
    const reviews = await all(p, 'list_reviews', { state: 'all', reviewTypeKeys: [spec.reviewType], businessApplicationId: app.businessApplicationId });
    const done = reviews.find(r => r.status?.meaning === 'done');
    if (done) return { outcome: 'present', note: 'done on ' + done.doneOn };
    const due = reviews.find(r => r.status?.meaning === 'due');
    if (!due) throw new Error('No due ' + spec.reviewType + ' review of ' + spec.app + ' to complete.');
    const decision = (await all(p, 'get_decision_log', { businessApplicationId: app.businessApplicationId, decisionTypeKeys: [spec.decisionType], statusMeanings: ['accepted'] }))
      .find(d => d.statement === spec.statement);
    if (!decision) throw new Error('No accepted ' + spec.decisionType + ' decision "' + spec.statement + '" on ' + spec.app + '.');
    await call(p, 'complete_review', { reviewId: due.reviewId, outcomeDecisionId: decision.decisionId, evidence: spec.evidence, clientRequestId: requestId(spec.key) });
    const gates = (await call(p, 'get_review', { reviewId: due.reviewId })).data?.gates ?? [];
    return { outcome: 'created', note: 'done today' + (gates.length ? '; ' + gates.map(g => g.gateLabel + ' gate ' + (g.met ? 'met' : 'not met')).join(', ') : '') };
  });
}

// The change manager closes a change whose window has passed (complete_change, a person's tool), with its evidence.
async function changeDoneStep(spec) {
  const p = persona(spec.who);
  return step('change "' + spec.title + '" on ' + spec.app + ', closed', p.name, async () => {
    const app = await application(p, spec.app);
    const change = (await all(p, 'get_change_schedule', { from: day(-180), to: day(180), businessApplicationId: app.businessApplicationId, statusMeanings: ['in-progress', 'completed'] }))
      .find(c => c.title === spec.title);
    if (!change) throw new Error('No change "' + spec.title + '" on ' + spec.app + ' to close.');
    if (change.status?.meaning === 'completed') return { outcome: 'present', note: 'completed' };
    await call(p, 'complete_change', { changeRequestId: change.changeRequestId, resultKey: spec.resultKey, evidence: spec.evidence, clientRequestId: requestId(spec.key) });
    return { outcome: 'created', note: 'closed as ' + spec.resultKey + ', with ' + spec.evidence.length + ' evidence link' };
  });
}

// The platform admin registers a deployment target (save_deployment_target, a person's tool).
async function targetStep(spec) {
  const p = persona(spec.who);
  return step('deployment target "' + spec.labelAsSeen + '"', p.name, async () => {
    const known = (await all(p, 'list_deployment_targets', {})).find(t => t.label === spec.labelAsSeen);
    if (known) return { outcome: 'present', note: roleText(known.targetStage?.key) + ', ' + roleText(known.changePath?.key) };
    const { key, who, ...input } = spec;
    await call(p, 'save_deployment_target', { ...input, clientRequestId: requestId(key) });
    return { outcome: 'created', note: roleText(spec.targetStageKey) + ', ' + roleText(spec.changePathKey) };
  });
}

async function changeStep(spec) {
  const p = persona(spec.who);
  return step('change "' + spec.title + '" on ' + spec.app, p.name, async () => {
    const app = await application(p, spec.app);
    const existing = (await all(p, 'get_change_schedule', { from: day(-60), to: day(180), businessApplicationId: app.businessApplicationId }))
      .find(c => c.title === spec.title && c.businessApplicationId === app.businessApplicationId);
    const done = [];
    let id = existing?.changeRequestId;
    let status = existing?.status?.meaning ?? 'draft';
    if (!existing) {
      const targetId = await target(p, spec.target);
      const env = await call(p, 'save_change_request_draft', {
        title: spec.title, businessApplicationId: app.businessApplicationId, changeTypeKey: 'normal', description: spec.description,
        windowStart: at(...spec.start), windowEnd: at(...spec.end), ...(targetId ? { plannedTargetId: targetId } : {}),
        clientRequestId: requestId(spec.key),
      });
      id = env.data.recordId;
      done.push('drafted for ' + day(spec.start[0]));
    }
    if (spec.submit && status === 'draft') {
      const env = await call(p, 'submit_record', { subjectKind: 'change-request', subjectId: id, clientRequestId: requestId(spec.key + ':submit') });
      status = 'submitted';
      done.push('submitted, ' + asked(env, spec.app));
    }
    return done.length ? { outcome: 'created', note: done.join('; ') } : { outcome: 'present', note: status };
  });
}

// The intake request with this title, as the persona sees it.
async function intakeByTitle(p, title) {
  const found = (await all(p, 'list_intake_requests', { view: 'all', text: title })).find(i => i.title === title);
  if (!found) throw new Error('No intake request "' + title + '" in the store.');
  return found;
}

// The subject of a decision: the application, one of its deployments or reviews, or an intake request.
async function subjectOf(p, spec, app) {
  if (!spec.subject) return { subjectKind: 'business-application', subjectId: app.businessApplicationId };
  if (spec.subject.kind === 'intake-request') return { subjectKind: 'intake-request', subjectId: (await intakeByTitle(p, spec.subject.title)).intakeRequestId };
  if (spec.subject.kind === 'change-request') {
    const found = (await all(p, 'get_change_schedule', { from: day(-60), to: day(180), businessApplicationId: app.businessApplicationId }))
      .find(c => c.title === spec.subject.title && c.businessApplicationId === app.businessApplicationId);
    if (!found) throw new Error('No change "' + spec.subject.title + '" on ' + spec.app + ' to decide on. The change step makes it first.');
    return { subjectKind: 'change-request', subjectId: found.changeRequestId };
  }
  if (spec.subject.kind === 'deployment-record') {
    const found = (await all(p, 'get_deployments', { businessApplicationId: app.businessApplicationId, latestOnly: false })).find(d => d.artifactVersion === spec.subject.artifactVersion);
    if (!found) throw new Error('No deployment ' + spec.subject.artifactVersion + ' of ' + spec.app + ' in the store.');
    return { subjectKind: 'deployment-record', subjectId: found.deploymentRecordId };
  }
  const review = (await all(p, 'list_reviews', { state: 'all', reviewTypeKeys: [spec.subject.reviewType], businessApplicationId: app.businessApplicationId }))
    .find(r => r.status?.meaning === 'due');
  if (!review) throw new Error('No open ' + spec.subject.reviewType + ' review of ' + spec.app + ' to decide on.');
  return { subjectKind: 'review', subjectId: review.reviewId };
}

// An exception's standard and review date: reviewIn counts days from the day the director runs, so the review date is
// always ahead; reviewOn is a fixed date.
function exceptionOf({ reviewIn, ...rest }) {
  return reviewIn === undefined ? rest : { ...rest, reviewOn: day(reviewIn) };
}

async function decisionStep(spec) {
  const p = persona(spec.who);
  return step(spec.type + ' decision on ' + (spec.app ?? spec.subject.title) + ': "' + spec.statement + '"', p.name, async () => {
    const app = spec.app ? await application(p, spec.app) : null;
    const existing = (await all(p, 'get_decision_log', { ...(app ? { businessApplicationId: app.businessApplicationId } : {}), decisionTypeKeys: [spec.type] }))
      .find(d => d.statement === spec.statement);
    const done = [];
    let id = existing?.decisionId;
    if (!existing) {
      const subject = await subjectOf(p, spec, app);
      const env = await call(p, 'save_decision_proposal', {
        decisionTypeKey: spec.type, ...subject, statement: spec.statement, context: spec.context, proposedOutcomeKey: spec.outcome, reason: spec.reason,
        ...(spec.options ? { optionsConsidered: spec.options } : {}), ...(spec.evidence ? { evidence: spec.evidence } : {}),
        ...(spec.exception ? { exception: exceptionOf(spec.exception) } : {}),
        clientRequestId: requestId(spec.key),
      });
      id = env.data.recordId;
      done.push('proposed');
    } else if (existing.status?.meaning !== 'proposed') {
      return { outcome: 'present', note: existing.status?.meaning };
    }
    if (!(await submitted(p, id))) {
      const env = await call(p, 'submit_record', { subjectKind: 'decision', subjectId: id, clientRequestId: requestId(spec.key + ':submit') });
      done.push('submitted, ' + asked(env, spec.app));
    }
    return done.length ? { outcome: 'created', note: done.join('; ') } : { outcome: 'present', note: 'waiting for answers' };
  });
}

async function architectureReviewStep(spec) {
  const p = persona(spec.who);
  return step('architecture review request for ' + spec.app, p.name, async () => {
    const app = await application(p, spec.app);
    const open = (await all(p, 'list_reviews', { state: 'all', reviewTypeKeys: ['architecture'], businessApplicationId: app.businessApplicationId })).find(r => r.status?.meaning === 'due');
    if (open) return { outcome: 'present', note: 'due ' + open.dueOn };
    const env = await call(p, 'request_architecture_review', { businessApplicationId: app.businessApplicationId, reason: spec.reason, dueOn: day(7), evidence: spec.evidence, clientRequestId: requestId(spec.key) });
    return { outcome: 'created', note: 'due ' + day(7) + ', for the ' + roleText(env.data?.assignedRoleKey ?? 'architect') };
  });
}

async function documentStep(spec) {
  const p = persona(spec.who);
  return step(roleText(spec.type) + ' of ' + spec.app + ', as a draft to confirm', p.name, async () => {
    const app = await application(p, spec.app);
    const status = (await call(p, 'get_documentation_status', { businessApplicationId: app.businessApplicationId })).data;
    const row = (status?.applications?.[0]?.documents ?? []).find(d => d.documentTypeKey === spec.type);
    if (row && row.status?.meaning !== 'missing') return { outcome: 'present', note: row.status?.meaning };
    await call(p, 'record_document_link', {
      businessApplicationId: app.businessApplicationId, documentTypeKey: spec.type, url: spec.url, linkKindKey: 'ado-wiki-page',
      statusMeaning: 'draft', lastUpdatedOn: day(-2), clientRequestId: requestId(spec.key),
    });
    return { outcome: 'created', note: 'recorded as a draft' };
  });
}

// Every claim the persona can see, unregistered and vanished, in every state.
const allClaims = p => all(p, 'list_claims', { statusMeanings: ['open', 'claimed', 'dismissed'] });

// The component kinds the gap check covers, from the configuration in force (Component Kind, Is Gap Check Covered).
async function coveredKinds(p) {
  try {
    const rows = (await call(p, 'get_configuration', { entity: 'component-kind', top: 100 })).data?.rows ?? [];
    const kinds = rows.filter(r => r.fields?.isGapCheckCovered === true).map(r => r.key);
    if (kinds.length) return new Set(kinds);
  } catch {
    // Fall back to Balanced's list below.
  }
  return new Set(COVERED_KINDS);
}

// The inventory as the admin's tool would list it: every registered component of a covered kind that is not retired,
// except the one that vanished. Wave 4b review: read from the demo estate file the server was seeded with, not one
// application card at a time (2,000 card reads at about half a second each on a 2,000-application estate). The
// director's own writes add no component before the gap check, so the file is the register's components then.
async function inventoryOf(p, spec) {
  const covered = await coveredKinds(p);
  const estate = JSON.parse(readFileSync(new URL('./demo-estate.json', import.meta.url), 'utf8'));
  const apps = new Map(estate.rows.filter(r => r.entity === 'business-application').map(r => [String(r.id).toLowerCase(), r.columns]));
  const items = [];
  for (const row of estate.rows.filter(r => r.entity === 'technical-component')) {
    const c = row.columns;
    if (!covered.has(c.componentKind) || !c.externalId || c.retiredOn) continue;
    const app = apps.get(String(c.businessApplication ?? '').toLowerCase());
    if (!app || !['registered', 'draft'].includes(app.statusMeaning)) continue;
    if (app.name === spec.vanishes.app && c.labelAsSeen === spec.vanishes.labelAsSeen) continue;
    items.push({ externalId: c.externalId, componentKindKey: c.componentKind, labelAsSeen: c.labelAsSeen ?? c.externalId, ...(c.url ? { url: c.url } : {}) });
  }
  return items;
}

// A gap check from the recorded inventory: claims for the items no application holds, and the vanished claim for the
// component the list leaves out. It reads the claims first, by the items' names, so a second run adds nothing; a run
// that finds a claim missing sends the whole list again, under a request ID of its own.
async function gapCheckStep(spec) {
  const p = persona(spec.who);
  await step('gap check of the whole Power Platform inventory', p.name, async () => {
    const claims = await allClaims(p);
    const known = new Set(claims.filter(c => c.findingKind !== 'vanished').map(c => c.labelAsSeen));
    const missing = spec.items.filter(i => !known.has(i.labelAsSeen));
    const vanished = claims.some(c => c.findingKind === 'vanished' && c.labelAsSeen === spec.vanishes.labelAsSeen);
    if (!missing.length && vanished) return { outcome: 'present', note: spec.items.length + ' claims and the vanished ' + spec.vanishes.labelAsSeen };
    const registered = await inventoryOf(p, spec);
    const items = [...registered, ...spec.items.map(({ suggestedTo, ...item }) => ({
      ...item,
      ...(suggestedTo ? { suggestedClaimantObjectId: persona(suggestedTo).objectId } : {}),
    }))];
    const attempt = createHash('sha256').update(missing.map(i => i.labelAsSeen).join('|') + '|' + vanished).digest('hex').slice(0, 12);
    // Pages of up to 1,000 items, the tool's limit, in one run (QA speed, fix round 1: a 2,000-application estate sent
    // the whole inventory as one page and was refused). The first page opens the run; the last one is final.
    const PAGE = 1000;
    let runId = null;
    let newClaims = 0;
    let vanishedNow = 0;
    const pages = Math.max(1, Math.ceil(items.length / PAGE));
    for (let n = 0; n < pages; n++) {
      const env = await call(p, 'compare_inventory_list', {
        estateKey: spec.estateKey, ...(runId ? { gapCheckRunId: runId } : {}), items: items.slice(n * PAGE, (n + 1) * PAGE), sourceNote: spec.sourceNote,
        isFinalPage: n === pages - 1, clientRequestId: requestId(spec.key + ':' + attempt + (pages > 1 ? ':' + n : '')),
      });
      runId = env.data?.gapCheckRunId ?? runId;
      newClaims += env.data?.newClaims?.length ?? 0;
      vanishedNow += env.data?.vanished?.length ?? 0;
    }
    return {
      outcome: 'created',
      note: items.length + ' items in ' + (pages === 1 ? 'one final page' : pages + ' pages') + ': ' + newClaims + ' new claims, ' + vanishedNow + ' vanished',
    };
  });

  // The maker claims one item into the application she owns; the platform admin dismisses one.
  const claimer = persona(spec.claim.who);
  await step('claim of "' + spec.claim.labelAsSeen + '" into ' + spec.claim.app, claimer.name, async () => {
    const claim = (await allClaims(claimer)).find(c => c.labelAsSeen === spec.claim.labelAsSeen && c.findingKind !== 'vanished');
    if (!claim) throw new Error('No claim "' + spec.claim.labelAsSeen + '" for ' + claimer.person + ' to claim.');
    if (claim.status?.meaning !== 'open') return { outcome: 'present', note: claim.status?.meaning };
    const app = await application(claimer, spec.claim.app);
    await call(claimer, 'claim_resource', { claimId: claim.claimId, businessApplicationId: app.businessApplicationId, clientRequestId: requestId(spec.claim.key) });
    return { outcome: 'created', note: 'claimed' };
  });
  const dismisser = persona(spec.dismiss.who);
  await step('dismissal of "' + spec.dismiss.labelAsSeen + '"', dismisser.name, async () => {
    const claim = (await allClaims(dismisser)).find(c => c.labelAsSeen === spec.dismiss.labelAsSeen && c.findingKind !== 'vanished');
    if (!claim) throw new Error('No claim "' + spec.dismiss.labelAsSeen + '" to dismiss.');
    if (claim.status?.meaning !== 'open') return { outcome: 'present', note: claim.status?.meaning };
    await call(dismisser, 'dismiss_claim', { claimId: claim.claimId, reason: spec.dismiss.reason, clientRequestId: requestId(spec.dismiss.key) });
    return { outcome: 'created', note: 'dismissed with a reason' };
  });
}

// An owner change, proposed by the platform admin and submitted: the successor becomes the business owner. changes
// builds the list from the current owners; it returns null when the successor already holds the role.
async function ownershipStep(spec, startOn, changesFor) {
  const p = persona(spec.who);
  const successor = persona(spec.successor);
  return step('owner change on ' + spec.app + ': ' + successor.person + ' as business owner', p.name, async () => {
    const app = await application(p, spec.app);
    // An open owner change that already names the successor as business owner (its proposed changes, as get_decision
    // reads them; the statement and context are the engine's words and name people, not IDs).
    const proposed = [];
    for (const d of await all(p, 'get_decision_log', { businessApplicationId: app.businessApplicationId, decisionTypeKeys: ['ownership'], statusMeanings: ['proposed'] })) {
      const full = (await call(p, 'get_decision', { decisionId: d.decisionId })).data;
      const owners = full?.proposedChanges?.owners ?? full?.decision?.proposedChanges?.owners ?? [];
      const names = owners.some(o => o.ownerRole?.key === 'business-owner' && String(o.add?.objectId ?? '').toLowerCase() === successor.objectId.toLowerCase());
      if (names || String(full?.decision?.context ?? '').includes(successor.objectId)) proposed.push(d);
    }
    const done = [];
    let id = proposed[0]?.decisionId;
    if (!id) {
      const owners = (await call(p, 'get_application_owners', { businessApplicationId: app.businessApplicationId })).data?.owners ?? [];
      const changes = changesFor(owners, successor);
      if (!changes) return { outcome: 'present', note: successor.person + ' already holds the role' };
      const env = await call(p, 'propose_owner_change', { businessApplicationId: app.businessApplicationId, changes, reason: spec.reason, clientRequestId: requestId(spec.key) });
      id = env.data.decisionId ?? env.data.recordId;
      done.push('proposed from ' + startOn);
    }
    if (!(await submitted(p, id))) {
      const env = await call(p, 'submit_record', { subjectKind: 'decision', subjectId: id, clientRequestId: requestId(spec.key + ':submit') });
      done.push('submitted, ' + asked(env, spec.app));
    }
    return done.length ? { outcome: 'created', note: done.join('; ') } : { outcome: 'present', note: 'waiting for answers' };
  });
}

// The successor takes the business owner role: the holder's assignment ends (when there is one), the successor's
// deputy assignment ends, and the successor holds the role from startOn.
const successorChanges = startOn => (owners, successor) => {
  const current = owners.filter(o => o.ownerRoleKey === 'business-owner' && !o.endOn);
  if (current.some(o => !o.isDeputy && o.principal?.objectId === successor.objectId)) return null;
  const changes = [];
  // Every end item names the successor's start date, so the outgoing owner (and the successor's own deputy row) stay
  // until the successor starts, and the role is never empty (product review, blocker; the engine does the same for an
  // end with no date of its own, document 25, B25-20).
  for (const o of current.filter(o => !o.isDeputy)) changes.push({ ownerRoleKey: 'business-owner', endAssignmentId: o.ownerAssignmentId, startOn });
  changes.push({ ownerRoleKey: 'business-owner', addObjectId: successor.objectId, startOn });
  for (const o of current.filter(o => o.isDeputy && o.principal?.objectId === successor.objectId)) changes.push({ ownerRoleKey: 'business-owner', endAssignmentId: o.ownerAssignmentId, startOn });
  return changes;
};

async function leaveStep(spec) {
  const p = persona(spec.who);
  let leaver;
  await step('leave of ' + spec.person + ' on ' + day(spec.leavesIn), p.name, async () => {
    const app = await application(p, spec.knownFrom);
    const owners = (await call(p, 'get_application_owners', { businessApplicationId: app.businessApplicationId })).data?.owners ?? [];
    leaver = owners.find(o => o.principal?.displayLabel === spec.person)?.principal;
    if (!leaver) throw new Error(spec.person + ' owns nothing on ' + spec.knownFrom + '.');
    const recorded = (await all(p, 'list_owner_assignments', { principalObjectId: leaver.objectId, leaversOnly: true, state: 'all' }))
      .find(a => a.holder?.leavesOn);
    if (recorded) return { outcome: 'present', note: 'leaves on ' + recorded.holder.leavesOn };
    const env = await call(p, 'record_owner_leave', { principalObjectId: leaver.objectId, leavesOn: day(spec.leavesIn), kind: 'leave', note: spec.note, clientRequestId: requestId(spec.key) });
    return { outcome: 'created', note: (env.data?.affected ?? []).length + ' owner roles affected, ' + (env.data?.ownerCheckReviewIds ?? []).length + ' owner checks due' };
  });
  if (leaver) await ownershipStep({ ...spec.successor, who: spec.who }, day(spec.leavesIn), successorChanges(day(spec.leavesIn)));
}

// ---- The data taxonomy ----

// A data location by its label (find_data_locations), cached.
const dataLocations = new Map();
async function dataLocation(p, label) {
  if (dataLocations.has(label)) return dataLocations.get(label);
  const found = (await all(p, 'find_data_locations', { text: label })).find(l => l.label === label);
  if (!found) throw new Error('No data location named ' + label + ' in the store. Seed it with seed/demo-estate.json.');
  dataLocations.set(label, found);
  return found;
}

// One section of a data domain (get_data_domain): its uses or its mappings.
async function domainSection(p, domainKey, section) {
  return (await call(p, 'get_data_domain', { domainKey, sections: [section] })).data?.[section] ?? [];
}

const openRow = r => !r.endedOn && r.status?.meaning !== 'archived';

// The open data use of an application on a domain, at a location, with or without AI.
async function dataUseOf(p, spec) {
  return (await domainSection(p, spec.domain, 'uses'))
    .find(u => openRow(u) && u.applicationName === spec.app && (u.location?.label ?? null) === (spec.location ?? null) && !!u.reachesAi === !!spec.ai) ?? null;
}

// A decision about a declared use or a copy mapping: proposed with save_decision_proposal, then submitted, so the cards
// go to the domain's data owner by name. A decision with the same statement on the same subject is left as it is.
async function dataDecisionStep(spec) {
  const p = persona(spec.who);
  const what = spec.use ? spec.use.app + ' on ' + spec.use.domain : 'the ' + roleText(spec.mapping.origin) + ' of ' + spec.mapping.domain + ' in ' + spec.mapping.location;
  return step(spec.type + ' decision about ' + what + ': "' + spec.statement + '"', p.name, async () => {
    let subject;
    let app = null;
    if (spec.use) {
      const use = await dataUseOf(p, spec.use);
      if (!use) throw new Error('No data use of ' + spec.use.app + ' on ' + spec.use.domain + ' at ' + spec.use.location + '. Seed the store with seed/demo-estate.json.');
      subject = { subjectKind: 'data-use', subjectId: use.dataUseId };
      app = await application(p, spec.use.app);
    } else {
      const mapping = (await domainSection(p, spec.mapping.domain, 'mappings'))
        .find(m => openRow(m) && m.location?.label === spec.mapping.location && m.origin === spec.mapping.origin);
      if (!mapping) throw new Error('No ' + spec.mapping.origin + ' of ' + spec.mapping.domain + ' in ' + spec.mapping.location + '. Seed the store with seed/demo-estate.json.');
      subject = { subjectKind: 'data-mapping', subjectId: mapping.dataMappingId };
    }
    const existing = (await all(p, 'get_decision_log', { ...(app ? { businessApplicationId: app.businessApplicationId } : {}), decisionTypeKeys: [spec.type] }))
      .find(d => d.statement === spec.statement);
    const done = [];
    let id = existing?.decisionId;
    if (!existing) {
      const env = await call(p, 'save_decision_proposal', {
        decisionTypeKey: spec.type, ...subject, statement: spec.statement, context: spec.context, proposedOutcomeKey: spec.outcome, reason: spec.reason,
        clientRequestId: requestId(spec.key),
      });
      id = env.data.recordId;
      done.push('proposed');
    } else if (existing.status?.meaning !== 'proposed') {
      return { outcome: 'present', note: existing.status?.meaning };
    }
    if (!(await submitted(p, id))) {
      const env = await call(p, 'submit_record', { subjectKind: 'decision', subjectId: id, clientRequestId: requestId(spec.key + ':submit') });
      done.push('submitted, ' + asked(env));
    }
    return done.length ? { outcome: 'created', note: done.join('; ') } : { outcome: 'present', note: 'waiting for answers' };
  });
}

// Claude Code drafts a data use for the persona (save_data_use on the MCP endpoint): a draft a person confirms.
async function aiDataUseStep(spec) {
  const p = persona(spec.who);
  const title = 'data use of ' + spec.app + ' on ' + spec.domain + ', drafted by Claude Code for ' + p.person;
  if (!aiWorks) return step(title, p.name, async () => ({ outcome: 'skipped', note: AI_SKIP }));
  return step(title, p.name, async () => {
    const existing = await dataUseOf(p, { app: spec.app, domain: spec.domain, location: spec.location, ai: false });
    if (existing) return { outcome: 'present', note: existing.status?.meaning };
    const env = await callAsAi(aiPersona, 'save_data_use', {
      applicationId: (await application(p, spec.app)).businessApplicationId, domainKey: spec.domain, locationId: (await dataLocation(p, spec.location)).dataLocationId,
      access: spec.access, purpose: spec.purpose, reachesAi: false, proposeDecision: false, clientRequestId: requestId(spec.key),
    });
    return { outcome: 'created', note: 'a ' + (env.data?.use?.status?.meaning ?? 'draft') + ' for ' + p.person + ' to confirm, ' + roleText(env.data?.use?.state) };
  });
}

// Claude Code drafts a data mapping for the persona (save_data_mapping on the MCP endpoint, as the persona's AI client).
async function aiDataMappingStep(spec) {
  const p = persona(spec.who);
  const title = roleText(spec.origin) + ' of ' + spec.domain + ' in ' + spec.location + ', drafted by Claude Code for ' + p.person;
  if (!aiWorks) return step(title, p.name, async () => ({ outcome: 'skipped', note: AI_SKIP }));
  return step(title, p.name, async () => {
    const existing = (await domainSection(p, spec.domain, 'mappings')).find(m => openRow(m) && m.location?.label === spec.location && m.origin === spec.origin);
    if (existing) return { outcome: 'present', note: existing.status?.meaning };
    const env = await callAsAi(aiOwnerPersona, 'save_data_mapping', {
      items: [{ domainKey: spec.domain, locationId: (await dataLocation(p, spec.location)).dataLocationId, origin: spec.origin, containers: spec.containers }],
      clientRequestId: requestId(spec.key),
    });
    return { outcome: 'created', note: 'a ' + (env.data?.items?.[0]?.status?.meaning ?? 'draft') + ' for a person to confirm' };
  });
}

// The configuration admin drafts a configuration change and submits it; another configuration admin answers it.
async function configurationChangeStep(spec) {
  const p = persona(spec.who);
  return step('configuration change: "' + spec.proposalRationale.split('. ')[0] + '"', p.name, async () => {
    const drafts = (await call(p, 'list_configuration_changes', { view: 'drafts', top: 50 })).data?.drafts ?? [];
    let draft = null;
    for (const d of drafts.filter(d => String(d.instructedBy?.objectId ?? '').toLowerCase() === p.objectId.toLowerCase())) {
      const full = (await call(p, 'get_configuration_draft', { configurationDraftId: d.configurationDraftId })).data;
      if (full?.proposalRationale === spec.proposalRationale) {
        draft = d;
        break;
      }
    }
    const done = [];
    if (draft && draft.state?.key !== 'draft') return { outcome: 'present', note: draft.state?.label ?? draft.state?.key };
    let id = draft?.configurationDraftId;
    if (!id) {
      const base = (await call(p, 'list_configuration_changes', { view: 'versions', top: 1 })).data?.configurationSet?.activeVersion;
      const env = await call(p, 'propose_configuration_change', { baseVersionNumber: base, proposalRationale: spec.proposalRationale, operations: spec.operations, clientRequestId: requestId(spec.key) });
      id = env.data.configurationDraftId;
      done.push('drafted on version ' + base + (env.data?.valid === false ? ', with rule errors' : ''));
    }
    const env = await call(p, 'submit_record', { subjectKind: 'configuration-draft', subjectId: id, clientRequestId: requestId(spec.key + ':submit') });
    done.push('submitted, ' + asked(env));
    return { outcome: 'created', note: done.join('; ') };
  });
}

// The platform admin runs the daily due-state refresh now (tool 86), as the timer does. It runs once: when the review it
// opens is there already, the step is left as it is.
async function dueStateRefreshStep(spec) {
  const p = persona(spec.who);
  return step('daily due-state refresh, run now (it opens the ' + roleText(spec.reviewType) + ' of ' + spec.app + ')', p.name, async () => {
    const app = await application(p, spec.app);
    const opened = (await all(p, 'list_reviews', { state: 'all', reviewTypeKeys: [spec.reviewType], businessApplicationId: app.businessApplicationId }))
      .find(r => r.dueOn === spec.dueOn);
    if (opened) return { outcome: 'present', note: roleText(spec.reviewType) + ' due ' + opened.dueOn };
    const report = (await call(p, 'run_due_state_refresh', {})).data ?? {};
    const reviews = report.reviewsOpened ?? [];
    return {
      outcome: reviews.length ? 'created' : 'failed',
      note: reviews.length + ' reviews opened (' + reviews.map(r => r.applicationName + ', ' + roleText(r.reviewType?.key ?? r.reviewType)).join('; ') + '), '
        + (report.documentsStale ?? []).length + ' documents turned stale, ' + (report.notices ?? []).length + ' notices, ' + (report.failed ?? []).length + ' failed',
    };
  });
}

// A security review request for an agent (request_security_review, tool 94).
async function securityReviewStep(spec) {
  const p = persona(spec.who);
  return step('security review request for ' + spec.app, p.name, async () => {
    const app = await application(p, spec.app);
    const open = (await all(p, 'list_reviews', { state: 'all', reviewTypeKeys: ['security-review'], businessApplicationId: app.businessApplicationId })).find(r => r.status?.meaning === 'due');
    if (open) return { outcome: 'present', note: 'due ' + open.dueOn };
    const env = await call(p, 'request_security_review', { businessApplicationId: app.businessApplicationId, reason: spec.reason, dueOn: day(spec.dueIn), clientRequestId: requestId(spec.key) });
    return { outcome: 'created', note: 'due ' + day(spec.dueIn) + ', for the ' + roleText(env.data?.assignedRoleKey ?? 'security reviewer') };
  });
}

// ---------------------------------------------------------------------------------------------------------------
// The check of the personas, and the pending work of each
// ---------------------------------------------------------------------------------------------------------------

async function checkPersonas() {
  const problems = [];
  for (const p of PERSONAS.values()) {
    const r = await send('/api/me', { method: 'GET', headers: personHeaders(p) });
    const me = r.body;
    if (r.status !== 200 || !me) {
      problems.push(p.name + ': the server answered ' + r.status + (mode === 'dev'
        ? '. Start the local server from this version of Product.Azure.Server/appsettings.Development.json, which has the development persona ' + devPrefix + p.name + ', with --Product:DevIdentity:Enabled=true.'
        : '. Check that the token is the web app\'s own and has not expired.'));
      continue;
    }
    if (me.actorKind !== 'person') problems.push(p.name + ': the server takes this caller as ' + me.actorKind + ', not as the person in the web app.');
    if (mode === 'demo' && me.demoPersona?.name !== p.name) problems.push(p.name + ': the server ignored the persona header. The persona switch works only in a Demo install, with the web app\'s own token.');
    if (String(me.objectId).toLowerCase() !== String(p.objectId).toLowerCase()) problems.push(p.name + ': the server signs this persona in as someone else than the persona file says.');
    if (!me.configurationSetVersion) problems.push(p.name + ': no configuration version is in force. Seed the store first.');
  }
  return problems;
}

async function pendingWork() {
  const rows = [];
  for (const p of PERSONAS.values()) {
    const queue = await all(p, 'get_my_queue', {}, 1000);
    const kinds = {};
    for (const item of queue) kinds[item.kind] = (kinds[item.kind] ?? 0) + 1;
    const askedOfMe = await all(p, 'list_approvals', { view: 'mine' });
    const waiting = (await call(p, 'list_approvals', { view: 'asked-by-me', top: 1 })).data?.totalCount ?? 0;
    const open = (await call(p, 'list_approvals', { view: 'open', top: 1 })).data?.totalCount ?? 0;
    const aiDrafts = queue.filter(i => i.kind === 'draft' && i.isAiCreated).length;
    rows.push({
      persona: p.name, label: p.label,
      approvalsAskedOfThem: askedOfMe.length, ofWhichTheirOwnProposal: askedOfMe.filter(a => a.isOwnProposal).length,
      waitingOnOthers: waiting, drafts: kinds.draft ?? 0, aiDrafts, reviews: kinds.review ?? 0, documents: kinds.document ?? 0,
      ownerGaps: kinds['owner-gap'] ?? 0, claims: kinds.claim ?? 0, openApprovalsInTheEstate: open,
    });
  }
  return rows;
}

function printPending(rows) {
  console.log('\nPending work by persona (My work and Approvals):');
  for (const r of rows) {
    const parts = [];
    if (r.approvalsAskedOfThem) parts.push(r.approvalsAskedOfThem + ' asked of them' + (r.ofWhichTheirOwnProposal ? ' (' + r.ofWhichTheirOwnProposal + ' their own proposal)' : ''));
    if (r.waitingOnOthers) parts.push(r.waitingOnOthers + ' waiting on others');
    if (r.drafts) parts.push(r.drafts + ' drafts' + (r.aiDrafts ? ' (' + r.aiDrafts + ' by AI)' : ''));
    if (r.reviews) parts.push(r.reviews + ' reviews');
    if (r.documents) parts.push(r.documents + ' documents');
    if (r.ownerGaps) parts.push(r.ownerGaps + ' owner gaps');
    if (r.claims) parts.push(r.claims + ' claims');
    const own = parts.length ? parts.join(', ') : 'nothing of their own';
    console.log('  ' + r.label.padEnd(38) + own + '; ' + r.openApprovalsInTheEstate + ' open approvals in the estate');
  }
}

// ---------------------------------------------------------------------------------------------------------------
// Run
// ---------------------------------------------------------------------------------------------------------------

async function main() {
  if (!asJson) console.log('Demo director: ' + (mode === 'dev' ? 'local server, development identity' : 'demo install, demo persona switch') + ', ' + PERSONAS.size + ' personas.');
  const problems = await checkPersonas();
  if (problems.length) {
    console.error('demo-director: the server does not take the personas:\n  ' + problems.join('\n  '));
    process.exit(2);
  }

  if (!checkOnly) {
    await readRoleLabels(persona('owen'));
    const section = title => { if (!asJson) console.log('\n' + title); };
    section('Intake requests');
    for (const spec of INTAKES) await intakeStep(spec);
    section('Demand');
    for (const spec of SCORES) await scoreStep(spec);
    section('Change requests');
    for (const spec of CHANGES) await changeStep(spec);
    section('Reviews and decisions');
    await architectureReviewStep(ARCHITECTURE_REVIEW);
    await securityReviewStep(SECURITY_REVIEW);
    for (const spec of DECISIONS) await decisionStep(spec);
    section('Data uses, copies and retention');
    for (const spec of DATA_DECISIONS) await dataDecisionStep(spec);
    section('Configuration');
    await configurationChangeStep(CONFIGURATION_CHANGE);
    section('Owners');
    for (const spec of OWNERSHIP) await ownershipStep(spec, day(0), successorChanges(day(0)));
    await leaveStep(LEAVE);
    section('Documents');
    for (const spec of DOCUMENTS) await documentStep(spec);
    // Before the gap check: the removal change takes the Timesheets canvas app away, and the run then misses it.
    section('Work finished before the demo');
    await reviewDoneStep(REVIEW_DONE);
    await changeDoneStep(CHANGE_DONE);
    section('Places and the gap check');
    await targetStep(NEW_TARGET);
    await handDeploymentStep(HAND_DEPLOYMENT);
    await gapCheckStep(GAP_CHECK);
    // Last, so the approval request on Priya's change exists for Claude Code to try to answer.
    section('Claude Code for ' + persona(AI_TAKEN_FORWARD.who).person);
    await aiTakenForwardStep(AI_TAKEN_FORWARD);
    await aiEditedStep(AI_EDITED);
    await aiWithdrawnStep(AI_WITHDRAWN);
    await aiWaitingStep(AI_WAITING);
    await aiDeploymentStep(AI_DEPLOYMENT);
    for (const spec of AI_REFUSALS) await aiRefusalStep(spec);
    await aiDataUseStep(AI_DATA_USE);
    await aiDataMappingStep(AI_DATA_MAPPING);
    await aiRefusalStep(AI_NEVER);
    // The daily refresh runs after everything else, Claude Code's deployment included, so the day's flag notices are
    // sent and a run in the demo finds nothing to do (e2e step 14).
    section('The daily due-state refresh');
    await dueStateRefreshStep(DUE_STATE_REFRESH);
  }

  const pending = await pendingWork();
  const failed = results.filter(r => r.outcome === 'failed');
  if (asJson) {
    console.log(JSON.stringify({ mode, steps: results, pending }, null, 2));
  } else {
    printPending(pending);
    const count = outcome => results.filter(r => r.outcome === outcome).length;
    if (!checkOnly) console.log('\nSteps: ' + count('created') + ' done now, ' + count('present') + ' already there, ' + count('skipped') + ' skipped, ' + failed.length + ' failed.');
  }
  process.exit(failed.length ? 1 : 0);
}

main().catch(e => {
  console.error('demo-director: ' + plain(e.message));
  process.exit(1);
});
