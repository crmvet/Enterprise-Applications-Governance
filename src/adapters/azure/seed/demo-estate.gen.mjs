// demo-estate.gen.mjs
//
// What it is: the generator of the fictional demo estate for a demo install (D-33). It writes
// seed/demo-estate.json, a rows file for the local server's seed command, in the shape
// { "rows": [ { "entity", "id", "columns" } ] } (Product.Azure.Host/Seeding/ProductSeeding.cs, SeedRows).
// The estate tells one story, seen from about 2026-11-24 for several weeks: 48 business applications in five
// portfolios, their owners (with six deliberate gaps), dependencies, deployment targets, deployments of the last
// 30 days (some flagged), change windows over the next weeks, decisions, reviews, documents, delivery links,
// components, a few intake requests and two past approval answers. It reuses the story and the fictional names of the design harness fixtures
// (src/ui/harness/fixtures), as store rows that the real engine reads.
//
// The file is a superset of seed/walkthrough-world.json: every walkthrough row is read from that file and kept with
// its entity, ID and identifying values (names, record numbers, Entra object IDs, external IDs, owner principals), so
// the demo binding's personas still own the same things. Walkthrough rows gain columns, and a few values change on
// purpose; ENRICH below lists each one.
//
// How to regenerate: from src/adapters/azure, run
//   node seed/demo-estate.gen.mjs
// It needs no packages, reads seed/walkthrough-world.json and writes seed/demo-estate.json, and the data domain tree into
// the draftOperations of seed/demo-binding.patch.json (section 17, the demo configuration patch; every other operation of
// that file is kept). The output is the same on every run: fixed IDs and fixed dates, no randomness and no clock. Seed it into an empty store with
//   dotnet run --project Product.Azure.Server -- seed --environment Development --rows seed/demo-estate.json
//
// Everything in it is fictional: the people, the organization, the applications, the IDs (patterned placeholders in
// the 00000000-0000-4000-8000-xxxxxxxxxxxx form) and the URLs (example.org hosts). No real person, company or tenant.
//
// The product reads with the real clock, so the story moves as real days pass: deployments leave the 14-day timeline
// and the 30-day flagged list, change windows move into the past, and due reviews become overdue on their due dates.
// The two draft intake requests are old drafts already. The summary this script prints shows the key figures on a few
// days of the demo weeks.
//
// Deliberate choices, where the seed differs from what the product's own writes would produce:
// - Standard changes. The four routine standard changes (tier 1 and 2, one application, a window of four hours or less)
//   carry an accepted change-authorization decision (shown as Pre-authorized) and fit Balanced's one shipped model,
//   routine-maintenance (document 21, B21-9). The model counts only once a person approves it in the install through a
//   configuration change (P11, the demo's configuration step); until then check_standard_change names the model and
//   says it has no approving decision. Seeding the approval would need a configuration draft and version 2, not rows.
//   The two tier 3 maintenance changes (Finance Ledger, People Hub) are normal changes the change manager authorized,
//   as Balanced asks at tier 3.
// - Repository components use the link kind repository-file (category document). The write guard accepts only
//   Microsoft resource kinds for a technical component, and the engine's own default for a repository
//   (IntakeTools.cs DefaultComponentLinkKind) is a document kind too. This conflict is the engine's to settle.
// - Open work is not seeded. The seed writes columns as given and stamps nothing, and a row the engine never stamped
//   cannot move: submit_record asks for the record's requester or creator, and OP-03 reads the proposer of a decision
//   from the same stamps. So the estate holds no proposed decision, no submitted change request and no draft change
//   request. seed/demo-director.mjs makes that work live through the tools, as the fictional demo personas: intake
//   requests, tier answers, change requests for the coming windows, decision proposals, an owner leave with its
//   successor, and documents to confirm. Its approval records, stamps and audit entries are then the engine's own.
//   The rows it replaces keep their slots below (they are skipped, not deleted), so every other row keeps its ID and
//   record number.
// - Pipelines are pipeline-reference rows with pipeline map entries (O16, O17), not delivery links.
// - The data taxonomy (section 17; docs/architecture/data-taxonomy.md) is appended after the wave 2 facts:
//   data locations, mappings and uses, the owners of domains and locations with their accepted ownership decisions, the
//   accepted data-use decisions of the allowed uses (DEC-0106 and DEC-0124 re-pointed to their uses), and one tier
//   assessment. Its open work (the proposed data decisions, Claude Code's drafts and refused call) is the director's.
// - Facts the wave 2 screens build on (section 16) are appended after every other row, so no earlier row changes its ID
//   or record number. Legacy Timesheet App is on its way out, no longer retired: its replace decision and retention
//   decision are made, and its removal change is authorized and done in its window, so the Retirement tracker shows the
//   steps left (the director closes the change with its evidence, and the platform admin proposes retirement complete
//   live; the retirement decision's old slot, DEC-0122, is reserved). Quote Builder's design decision is accepted and its
//   architecture review is due, for the director to complete. Two standing exceptions name the standard each sets aside
//   and the review date. Two answers on the Invoice Matching 1.12 authorization show that an agent identity's answer is
//   stored as a recommendation only and a person's counts. A seeded approval record is a past fact: no open request is
//   seeded.

import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const walkthroughPath = join(here, 'walkthrough-world.json');
const outputPath = join(here, 'demo-estate.json');

// ---------------------------------------------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------------------------------------------

const PREFIX = '00000000-0000-4000-8000-';
// A fictional ID: the number's decimal digits, padded to the 12 hex digits of the last group.
const id = n => PREFIX + String(n).padStart(12, '0');

const DAY_MS = 86400000;
const addDays = (date, n) => new Date(Date.parse(date + 'T00:00:00Z') + n * DAY_MS).toISOString().slice(0, 10);
const addMinutes = (moment, n) => new Date(Date.parse(moment) + n * 60000).toISOString().replace('.000Z', 'Z');
const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const monthDay = moment => MONTHS[Number(moment.slice(5, 7)) - 1] + ' ' + Number(moment.slice(8, 10));
const slug = text => text.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');

const CSV = 1; // configurationSetVersion: the seed version is always 1

const rows = [];
function add(entity, rowId, columns) {
  rows.push({ entity, id: rowId, columns: { ...columns, configurationSetVersion: CSV } });
  return rowId;
}

// Drops keys whose value is undefined or null, so optional columns are simply absent.
function clean(columns) {
  const out = {};
  for (const [k, v] of Object.entries(columns)) {
    if (v !== undefined && v !== null) out[k] = v;
  }
  return out;
}

// ---------------------------------------------------------------------------------------------------------------
// 1. The walkthrough rows, kept and enriched
// ---------------------------------------------------------------------------------------------------------------

const walkthrough = JSON.parse(readFileSync(walkthroughPath, 'utf8'));

// Every change to a walkthrough row. "add" columns are new; "replace" columns change a walkthrough value on purpose.
const ENRICH = {
  // Field operations: the walkthrough's level "department" is not a portfolio-level value (value-list.json).
  [id(501)]: { replace: { level: 'portfolio' }, add: { accountablePrincipal: id(215) } },
  // Inspection Scheduler takes the harness fixture's story: fusion build, AI summaries, live since March 2026.
  [id(511)]: {
    replace: {
      purpose: 'Schedules safety inspections of field sites, assigns inspectors and tracks findings until they are closed.',
      buildPath: 'fusion',
      usesAi: true,
      stageSince: '2026-03-16T08:00:00Z',
    },
    // tierDecision and stageDecision are added below, once the decisions exist. nextReviewOn is set from the
    // application's due reviews (section 12), like every other application's.
  },
  // The walkthrough purposes are short fragments; the demo uses full sentences like the other applications.
  [id(512)]: {
    replace: { purpose: 'Plans the daily routes of the field crews around depot opening hours and job times.' },
    add: { nextReviewOn: '2027-06-02' },
  },
  [id(513)]: {
    replace: { purpose: 'Keeps crew shifts, skills and certifications so supervisors can staff every field job.' },
    add: { nextReviewOn: '2027-02-04' },
  },
  [id(514)]: {
    replace: { purpose: 'Shows each crew member their shifts and any changes to them on their phone.' },
    add: { nextReviewOn: '2027-09-01' },
  },
  [id(521)]: { add: { url: 'https://make.example.org/environments/field-ops-test', lastSeenOn: '2026-11-20T06:00:00Z' } },
  [id(522)]: { add: { url: 'https://make.example.org/environments/field-ops-prod', lastSeenOn: '2026-11-20T06:00:00Z' } },
  // The walkthrough components point at a real Microsoft host; the demo keeps every URL on example.org.
  [id(531)]: {
    replace: { url: 'https://make.example.org/environments/field-ops-prod/apps/201' },
    add: { lastSeenOn: '2026-11-20T06:00:00Z', ownerDiffers: false, ownerCheckedOn: '2026-11-17T07:00:00Z' },
  },
  [id(532)]: {
    replace: { url: 'https://make.example.org/environments/field-ops-prod/apps/202' },
    add: { lastSeenOn: '2026-11-20T06:00:00Z', ownerDiffers: false, ownerCheckedOn: '2026-11-17T07:00:00Z' },
  },
  [id(561)]: { add: { criticality: 'high' } },
  [id(562)]: { add: { criticality: 'medium' } },
  [id(563)]: { add: { criticality: 'medium' } },
};

const walkthroughIds = new Set();
for (const row of walkthrough.rows) {
  walkthroughIds.add(row.id);
  const columns = { ...row.columns };
  const change = ENRICH[row.id];
  if (change?.replace) Object.assign(columns, change.replace);
  if (change?.add) Object.assign(columns, change.add);
  if (row.entity === 'principal-reference') {
    columns.directoryState = 'enabled';
    columns.directoryStateOn = '2026-11-20T06:00:00Z';
  }
  add(row.entity, row.id, columns);
}
const rowById = new Map(rows.map(r => [r.id, r]));

// ---------------------------------------------------------------------------------------------------------------
// 2. Principals (principal-reference rows 1000..1029, Entra object IDs 1100..1129)
// ---------------------------------------------------------------------------------------------------------------

// The walkthrough's people and workloads, by Entra object ID.
const P = {
  PRIYA: id(211), // Priya Castell, maker (persona priya-web)
  NADIA: id(212), // Nadia Ferreira, business owner (persona nadia-web)
  OWEN: id(213), // Owen Achterberg, platform admin (persona owen-web)
  MATEO: id(214), // Mateo Brandt, planner (persona mateo-web)
  INES: id(215), // Ines Varga
  RUNNER: id(301), // Approval runner
  FOPIPE: id(302), // Field ops release pipeline
};

const PEOPLE = [
  ['JONAH', 'Jonah Pereira', 'user'],
  ['LENA', 'Lena Okoro', 'user'],
  ['SANA', 'Sana Kowalczyk', 'user'],
  ['RAVI', 'Ravi Holm', 'user'],
  ['ELIF', 'Elif Dunmore', 'user'],
  ['TOMAS', 'Tomas Rendel', 'user'],
  ['FAS', 'Field Apps Support', 'group'],
  ['AMARA', 'Amara Quist', 'user', { directoryState: 'disabled', directoryStateOn: '2026-11-02T06:00:00Z', leavesOn: '2026-10-30' }],
  ['CLARA', 'Clara Voss', 'user'],
  ['DMITRI', 'Dmitri Halloran', 'user'],
  ['GRETA', 'Greta Lund', 'user'],
  ['YUSUF', 'Yusuf Brennan', 'user'],
  ['ROSA', 'Rosa Tamaki', 'user'],
  ['FELIX', 'Felix Arendt', 'user'],
  ['HANA', 'Hana Lindqvist', 'user'],
  ['MARCUS', 'Marcus Oyelaran', 'user'],
  ['IRIS', 'Iris Calloway', 'user'],
  ['HUGO', 'Hugo Brandvold', 'user'],
  ['NOOR', 'Noor Safadi', 'user'],
  ['KAI', 'Kai Ostrowski', 'user'],
  ['FINSUP', 'Finance Apps Support', 'group'],
  ['SERVDESK', 'Service Desk', 'group'],
  ['PLATTEAM', 'Platform Team', 'group'],
  ['FINPIPE', 'Finance release pipeline', 'service-principal'],
  ['CUSTPIPE', 'Customer release pipeline', 'service-principal'],
  ['INTPIPE', 'Integration release pipeline', 'service-principal'],
  ['PEOPLEPIPE', 'People release pipeline', 'service-principal'],
  ['THEO', 'Theo Marsh', 'user'],
  ['BIANCA', 'Bianca Ferro', 'user'],
  ['WREN', 'Wren Abernathy', 'user'],
  // An agent identity that answered an approval card in Teams (section 16): AI never approves, so its answer is a
  // recommendation only. Appended, so every principal above keeps its ID.
  ['FINAGENT', 'Finance Approvals Agent', 'agent-identity'],
];
PEOPLE.forEach(([short, label, kind, extra], i) => {
  P[short] = id(1100 + i);
  add('principal-reference', id(1000 + i), {
    principalKind: kind,
    entraObjectId: P[short],
    displayLabel: label,
    isErased: false,
    directoryState: 'enabled',
    directoryStateOn: '2026-11-20T06:00:00Z',
    lastSeenOn: '2026-11-20T06:00:00Z',
    ...extra,
  });
});

// ---------------------------------------------------------------------------------------------------------------
// 3. Portfolios (1200..1204; Field operations is the walkthrough's 501)
// ---------------------------------------------------------------------------------------------------------------

const PF = {
  FO: { id: id(501), name: 'Field operations', slug: 'field-ops' },
  FIN: { id: id(1200), name: 'Finance', slug: 'finance' },
  AP: { id: id(1201), name: 'Accounts payable', slug: 'finance' },
  PW: { id: id(1202), name: 'People and workplace', slug: 'people' },
  CS: { id: id(1203), name: 'Customer services', slug: 'customer' },
  SP: { id: id(1204), name: 'Shared platforms', slug: 'platforms' },
};
add('portfolio', PF.FIN.id, { name: PF.FIN.name, level: 'portfolio', accountablePrincipal: P.CLARA, isActive: true });
add('portfolio', PF.AP.id, { name: PF.AP.name, parentPortfolio: PF.FIN.id, level: 'application-group', accountablePrincipal: P.GRETA, isActive: true });
add('portfolio', PF.PW.id, { name: PF.PW.name, level: 'portfolio', accountablePrincipal: P.ROSA, isActive: true });
add('portfolio', PF.CS.id, { name: PF.CS.name, level: 'portfolio', accountablePrincipal: P.NADIA, isActive: true });
add('portfolio', PF.SP.id, { name: PF.SP.name, level: 'portfolio', accountablePrincipal: P.HUGO, isActive: true });

// ---------------------------------------------------------------------------------------------------------------
// 4. Business applications (walkthrough 511..514; new 1300..1343, APP-0105..APP-0148)
// ---------------------------------------------------------------------------------------------------------------

// [key, name, portfolio, kind, tier, stage, stageSince (date), buildPath, usesAi, agreedAutonomyLevel, nextReviewOn, purpose]
const NEW_APPS = [
  ['AR', 'Asset Register', 'FO', 'power-platform-app', 'tier-3', 'operate', '2024-05-13', 'fusion', false, null, '2027-02-08', 'Keeps every field asset with its location, warranty and inspection history.'],
  ['PCA', 'Parts Catalog API', 'FO', 'api', 'tier-2', 'operate', '2025-02-17', 'pro', false, null, '2027-02-17', 'Gives other apps one API to look up spare parts, prices and stock.'],
  ['VRA', 'Visit Report Agent', 'FO', 'agent', 'tier-2', 'test', '2026-11-09', 'fusion', true, 'assist', null, 'Drafts site visit reports from inspector notes and photos for the inspector to check and send.'],
  ['SFT', 'Safety Findings Tracker', 'FO', 'power-platform-app', 'tier-1', 'operate', '2025-04-07', 'citizen', false, null, '2027-04-07', 'Lists open safety findings from inspections until each one is fixed.'],
  ['CNS', 'Customer Notice Sender', 'CS', 'power-platform-app', 'tier-1', 'operate', '2025-08-04', 'citizen', false, null, '2027-08-04', 'Sends planned work notices to affected customers by email and text message.'],
  ['CP', 'Contractor Portal', 'CS', 'power-platform-app', 'tier-3', 'operate', '2024-10-21', 'fusion', false, null, '2027-01-21', 'Lets outside contractors sign in, see their assigned jobs and upload completion reports.'],
  ['IM', 'Invoice Matching', 'AP', 'ai-built-app', 'tier-2', 'operate', '2025-11-03', 'ai-built', true, null, '2027-05-03', 'Matches supplier invoices to purchase orders and goods receipts and flags the differences.'],
  ['SO', 'Supplier Onboarding', 'AP', 'power-platform-app', 'tier-2', 'change-and-release', '2026-11-16', 'fusion', false, null, null, 'Collects new supplier details, bank checks and approvals before a supplier can be paid.'],
  ['TRV', 'Travel Requests', 'PW', 'power-platform-app', 'tier-1', 'operate', '2024-09-09', 'citizen', false, null, '2027-03-09', 'Lets staff request business travel and get it approved by their manager.'],
  ['DB', 'Desk Booking', 'PW', 'saas', 'tier-1', 'operate', '2025-06-23', null, false, null, '2027-06-23', 'Books office desks and parking spaces through a hosted desk booking service.'],
  ['IDS', 'Identity Directory Sync', 'SP', 'api', 'tier-3', 'operate', '2024-03-04', 'pro', false, null, '2027-03-04', 'Copies people, teams and manager lines from People Hub to the directory every hour.'],
  ['FDL', 'Field Data Lake', 'SP', 'code-app', 'tier-3', 'operate', '2024-06-03', 'pro', false, null, '2027-01-28', 'Collects asset, telemetry and maintenance data from field systems for reporting and planning.'],
  ['CFB', 'Customer Feedback Bot', 'CS', 'agent', 'tier-2', 'build', '2026-10-26', 'fusion', true, null, null, 'Asks customers about completed work and sorts their answers into themes.'],
  ['EA', 'Expense Assistant', 'FIN', 'agent', 'tier-2', 'build', '2026-10-12', 'fusion', true, 'route', null, 'Helps staff file expense claims from card transactions and routes them for approval.'],
  ['TRN', 'Training Records', 'PW', 'power-platform-app', 'tier-1', 'operate', '2024-02-12', 'citizen', false, null, '2027-02-12', 'Keeps each person\'s completed training and certificates and reminds them before renewals.'],
  ['CSSH', 'Customer Self-Service Hub', 'CS', 'code-app', 'tier-2', 'operate', '2025-01-27', 'pro', false, null, '2027-03-01', 'Lets customers report faults, track jobs and book visits online.'],
  ['FML', 'Fleet Maintenance Log', 'FO', 'power-platform-app', 'tier-1', 'operate', '2025-05-19', 'citizen', false, null, '2027-05-19', 'Records vehicle checks, repairs and service dates for the field fleet.'],
  ['FL', 'Finance Ledger', 'FIN', 'external-system', 'tier-3', 'operate', '2023-07-03', null, false, null, '2027-07-03', 'The general ledger and purchasing system that holds suppliers, orders and payments.'],
  ['PH', 'People Hub', 'PW', 'saas', 'tier-3', 'operate', '2024-01-08', null, false, null, '2027-01-25', 'The hosted HR system for people records, leave and payroll inputs.'],
  ['SDP', 'Service Desk Portal', 'SP', 'saas', 'tier-2', 'operate', '2024-11-11', null, true, null, '2027-05-11', 'The hosted service desk where staff and customers raise and track support tickets.'],
  ['CTF', 'Card Transactions Feed', 'FIN', 'external-system', 'tier-2', 'operate', '2025-03-10', null, false, null, '2027-03-10', 'The bank\'s daily feed of corporate card transactions.'],
  ['SAB', 'Site Access Badges', 'FO', 'power-platform-app', 'tier-1', 'review', '2026-10-05', 'citizen', false, null, '2027-01-20', 'Issues and tracks temporary site access badges for visitors and contractors.'],
  ['PA', 'Purchase Approvals', 'AP', 'power-platform-app', 'tier-2', 'review', '2026-09-21', 'citizen', false, null, '2027-02-10', 'Routes purchase requests to the right approver by amount and cost center.'],
  ['LTA', 'Legacy Timesheet App', 'PW', 'power-platform-app', 'tier-1', 'review', '2026-06-15', 'citizen', false, null, null, 'The old timesheet app, replaced by time entry in People Hub. Read-only until its data is archived.'],
  ['WSC', 'Warehouse Stock Count', 'FO', 'power-platform-app', 'tier-2', 'change-and-release', '2026-11-02', 'citizen', false, null, null, 'Lets store staff count stock with a phone camera and posts the differences to the parts catalog.'],
  ['CCF', 'Contract Clause Finder', 'FIN', 'ai-built-app', 'tier-2', 'test', '2026-11-02', 'ai-built', true, null, null, 'Finds payment, liability and renewal clauses in supplier contracts and summarizes them.'],
  ['MCP', 'Field Knowledge MCP Server', 'SP', 'mcp-server', 'tier-3', 'test', '2026-11-09', 'pro', true, null, null, 'Gives AI clients read access to field procedures, asset data and safety rules through MCP.'],
  ['OBA', 'Onboarding Buddy Agent', 'PW', 'agent', 'tier-2', 'build', '2026-10-19', 'citizen', true, 'assist', null, 'Answers new starters\' questions about their first weeks and books their training.'],
  ['SSR', 'Shift Swap Requests', 'FO', 'power-platform-app', 'tier-1', 'build', '2026-11-09', 'citizen', false, null, null, 'Lets crew members offer and accept shift swaps for a supervisor to approve.'],
  ['COSA', 'Customer Order Status API', 'CS', 'api', 'tier-2', 'build', '2026-10-05', 'pro', false, null, null, 'Gives the self-service hub and partners the live status of customer orders.'],
  ['SPA', 'Supplier Price Agent', 'AP', 'agent', 'tier-3', 'risk-and-tier', '2026-10-28', 'pro', true, null, null, 'Compares supplier price lists and places reorders for common parts under its own identity.'],
  ['VSK', 'Visitor Sign-In Kiosk', 'PW', 'power-platform-app', 'tier-1', 'risk-and-tier', '2026-11-11', 'citizen', false, null, null, 'Signs office visitors in on a tablet and tells their host they have arrived.'],
  ['CTA', 'Claims Triage Agent', 'CS', 'agent', 'tier-2', 'design-and-review', '2026-10-19', 'fusion', true, null, null, 'Reads damage claims from customers and routes each one to the right team with a summary.'],
  ['QB', 'Quote Builder', 'CS', 'code-app', 'tier-2', 'design-and-review', '2026-11-02', 'pro', false, null, null, 'Builds customer quotes for repair work from parts prices and labor rates.'],
  ['SET', 'Site Energy Tracker', 'FO', 'power-platform-app', 'tier-1', 'design-and-review', '2026-11-16', 'citizen', false, null, null, 'Tracks electricity and fuel use at each depot against monthly targets.'],
  ['TCC', 'Tool Crib Checkout', 'FO', 'power-platform-app', 'tier-1', 'prioritize-and-fund', '2026-10-26', 'citizen', false, null, null, 'Records who has borrowed which tools from the depot tool crib and when they are due back.'],
  ['MRS', 'Meeting Room Signage', 'PW', 'saas', 'tier-1', 'prioritize-and-fund', '2026-11-09', null, false, null, null, 'Shows room bookings on screens outside each meeting room.'],
  ['MC', 'Mileage Claims', 'FIN', 'power-platform-app', 'tier-1', 'intake-and-triage', '2026-11-04', 'citizen', false, null, null, 'Lets staff claim business mileage from their phone with the route filled in.'],
  ['VDP', 'Volunteer Day Planner', 'PW', 'saas', 'tier-1', 'intake-and-triage', '2026-11-12', null, false, null, null, 'Books staff volunteering days with local charities.'],
  ['PRC', 'Partner Rebate Calculator', 'AP', 'ai-built-app', 'tier-2', 'intake-and-triage', '2026-11-17', 'ai-built', true, null, null, 'Works out the supplier rebates owed under volume agreements.'],
  ['TH', 'Translation Helper', 'CS', 'code-app', null, 'intake-and-triage', '2026-11-18', 'pro', true, null, null, 'Translates customer messages for agents and drafts replies in the customer\'s language.'],
  ['LS', 'Learning Sandbox', 'SP', 'practice-app', null, 'idea', '2026-11-05', 'citizen', false, null, null, 'A practice environment where new makers build a first app with sample data.'],
  ['PLF', 'Parcel Locker Finder', 'CS', 'power-platform-app', null, 'idea', '2026-11-10', 'citizen', false, null, null, 'Shows customers the nearest parcel locker where they can collect spare parts.'],
  ['FPT', 'Field Photo Tagger', 'FO', 'code-app', null, 'idea', '2026-11-19', 'pro', true, null, null, 'Tags inspection photos with the asset and the defect type so they are easy to find.'],
];

// Every application by key: id, name, portfolio key, kind, tier, stage.
const A = {
  IS: { id: id(511), name: 'Inspection Scheduler', pf: 'FO', kind: 'power-platform-app', tier: 'tier-2', stage: 'operate' },
  RP: { id: id(512), name: 'Route Planner', pf: 'FO', kind: 'power-platform-app', tier: 'tier-1', stage: 'operate' },
  CR: { id: id(513), name: 'Crew Roster', pf: 'FO', kind: 'power-platform-app', tier: 'tier-2', stage: 'operate' },
  CSM: { id: id(514), name: 'Crew Shift Mobile', pf: 'FO', kind: 'power-platform-app', tier: 'tier-1', stage: 'operate' },
};
const appRows = {}; // key -> row, for later columns (decisions)
for (const key of Object.keys(A)) appRows[key] = rowById.get(A[key].id);

NEW_APPS.forEach(([key, name, pf, kind, tier, stage, since, buildPath, usesAi, autonomy, nextReviewOn, purpose], i) => {
  const rowId = id(1300 + i);
  A[key] = { id: rowId, name, pf, kind, tier, stage };
  add('business-application', rowId, clean({
    name,
    recordNumber: 'APP-' + String(105 + i).padStart(4, '0'),
    purpose,
    applicationKind: kind,
    portfolio: PF[pf].id,
    status: 'app-registered',
    statusMeaning: 'registered',
    tier,
    lifecycleStage: stage,
    stageSince: since + 'T08:00:00Z',
    buildPath,
    usesAi,
    agreedAutonomyLevel: autonomy,
    nextReviewOn,
  }));
  appRows[key] = rows[rows.length - 1];
});

// ---------------------------------------------------------------------------------------------------------------
// 5. Deployment targets (walkthrough 521, 522; new 1400..1409)
// ---------------------------------------------------------------------------------------------------------------

const T = { FOT: id(521), FOP: id(522) };
const TARGETS = [
  ['FOD', 'Field Ops Dev', 'power-platform-environment', 'development', 'pipeline-or-ai-tool', 'power-platform-resource', 'field-ops-dev'],
  ['FINT', 'Finance Test', 'power-platform-environment', 'test', 'pipeline-only', 'power-platform-resource', 'finance-test'],
  ['FINP', 'Finance Prod', 'power-platform-environment', 'production', 'pipeline-only', 'power-platform-resource', 'finance-prod'],
  ['CUST', 'Customer Test', 'power-platform-environment', 'test', 'pipeline-only', 'power-platform-resource', 'customer-test'],
  ['CUSP', 'Customer Prod', 'power-platform-environment', 'production', 'pipeline-only', 'power-platform-resource', 'customer-prod'],
  ['PPL', 'People Prod', 'power-platform-environment', 'production', 'pipeline-only', 'power-platform-resource', 'people-prod'],
  // A deployment target takes a Microsoft resource link kind (WriteGuardEntities.cs), so the integration targets are
  // Azure resource groups linked as Entra objects, like the Azure resource components.
  ['INTT', 'Integration Test', 'azure-resource-group', 'test', 'pipeline-only', 'entra-object', 'integration-test'],
  ['INTP', 'Integration Prod', 'azure-resource-group', 'production', 'pipeline-only', 'entra-object', 'integration-prod'],
  ['DBT', 'Desk Booking tenant', 'saas-tenant', 'production', 'any-route', 'entra-object', id(1490)],
  ['PHT', 'People Hub tenant', 'saas-tenant', 'production', 'any-route', 'entra-object', id(1491)],
];
TARGETS.forEach(([key, label, targetKind, stage, changePath, linkKind, externalId], i) => {
  T[key] = id(1400 + i);
  const host = linkKind === 'power-platform-resource' ? 'https://make.example.org/environments/' + externalId
    : targetKind === 'azure-resource-group' ? 'https://portal.example.org/platforms/resource-groups/' + externalId
      : 'https://admin.example.org/tenants/' + slug(label);
  add('deployment-target', T[key], {
    targetKind,
    targetStage: stage,
    changePath,
    linkKind,
    externalId,
    url: host,
    labelAsSeen: label,
    lastSeenOn: '2026-11-20T06:00:00Z',
  });
});

// ---------------------------------------------------------------------------------------------------------------
// 6. Technical components (walkthrough 531, 532; new 3000..)
// ---------------------------------------------------------------------------------------------------------------

// [app, componentKind, externalId, label, extra]
const COMPONENTS = [
  ['IS', 'cloud-flow', 'flow-2b91d0', 'Send inspection reminders', { ownerDiffers: true }],
  ['IS', 'solution', 'sol-inspection-scheduler', 'Inspection Scheduler solution'],
  ['IS', 'repository', 'field-ops/inspection-scheduler', 'inspection-scheduler'],
  ['IS', 'cloud-flow', 'flow-5e07aa', 'Export inspections to spreadsheet'],
  ['RP', 'solution', 'sol-route-planner', 'Route Planner solution'],
  ['CR', 'canvas-app', 'app-3c90e1', 'Crew Roster'],
  ['CR', 'solution', 'sol-crew-roster', 'Crew Roster solution'],
  ['CR', 'canvas-app', 'app-1a77b2', 'Crew Roster (classic)', { retiredOn: '2026-05-04' }],
  ['CSM', 'canvas-app', 'app-8d21f4', 'Crew Shift Mobile'],
  ['AR', 'model-driven-app', 'app-4b18c6', 'Asset Register'],
  ['AR', 'solution', 'sol-asset-register', 'Asset Register solution'],
  ['AR', 'cloud-flow', 'flow-9a3d52', 'Sync warranty dates'],
  ['PCA', 'azure-resource', 'func-parts-catalog-api', 'parts-catalog-api'],
  ['PCA', 'repository', 'field-ops/parts-catalog-api', 'parts-catalog-api'],
  ['VRA', 'copilot-studio-agent', 'agent-visit-report', 'Visit Report Agent'],
  ['VRA', 'entra-agent-identity', 'agent-id-visit-report', 'Visit Report Agent identity'],
  ['SFT', 'canvas-app', 'app-6f42d9', 'Safety Findings Tracker'],
  ['CNS', 'canvas-app', 'app-2e65a0', 'Customer Notice Sender'],
  ['CNS', 'cloud-flow', 'flow-7c14e8', 'Send planned work notices'],
  ['CP', 'power-pages-site', 'site-contractor-portal', 'Contractor Portal'],
  ['CP', 'solution', 'sol-contractor-portal', 'Contractor Portal solution'],
  ['IM', 'code-app', 'codeapp-invoice-matching', 'Invoice Matching'],
  ['IM', 'repository', 'finance/invoice-matching', 'invoice-matching'],
  ['SO', 'model-driven-app', 'app-5d83b7', 'Supplier Onboarding'],
  ['SO', 'solution', 'sol-supplier-onboarding', 'Supplier Onboarding solution'],
  ['TRV', 'canvas-app', 'app-0c59f3', 'Travel Requests'],
  ['TRN', 'canvas-app', 'app-9e30c4', 'Training Records'],
  ['IDS', 'azure-resource', 'func-identity-directory-sync', 'identity-directory-sync'],
  ['IDS', 'repository', 'platforms/identity-directory-sync', 'identity-directory-sync'],
  ['FDL', 'azure-resource', 'lake-field-data', 'field-data-lake'],
  ['FDL', 'repository', 'platforms/field-data-lake', 'field-data-lake'],
  ['CSSH', 'code-app', 'codeapp-self-service-hub', 'Customer Self-Service Hub'],
  ['CSSH', 'repository', 'customer/self-service-hub', 'self-service-hub'],
  ['FML', 'canvas-app', 'app-7b26e5', 'Fleet Maintenance Log'],
  ['CFB', 'copilot-studio-agent', 'agent-customer-feedback', 'Customer Feedback Bot'],
  ['EA', 'copilot-studio-agent', 'agent-expense-assistant', 'Expense Assistant'],
  ['EA', 'entra-agent-identity', 'agent-id-expense-assistant', 'Expense Assistant identity'],
  ['SAB', 'canvas-app', 'app-3f81d0', 'Site Access Badges'],
  ['PA', 'canvas-app', 'app-6a12c7', 'Purchase Approvals'],
  ['PA', 'cloud-flow', 'flow-4d68b1', 'Route purchase approvals'],
  ['LTA', 'canvas-app', 'app-8c47a2', 'Timesheets'],
  ['WSC', 'canvas-app', 'app-1e93f6', 'Warehouse Stock Count'],
  ['CCF', 'code-app', 'codeapp-contract-clause-finder', 'Contract Clause Finder'],
  ['CCF', 'repository', 'finance/contract-clause-finder', 'contract-clause-finder'],
  ['MCP', 'azure-resource', 'aca-field-knowledge-mcp', 'field-knowledge-mcp'],
  ['MCP', 'repository', 'platforms/field-knowledge-mcp', 'field-knowledge-mcp'],
  ['OBA', 'copilot-studio-agent', 'agent-onboarding-buddy', 'Onboarding Buddy'],
  ['SSR', 'canvas-app', 'app-5b70e9', 'Shift Swap Requests'],
  ['COSA', 'azure-resource', 'apim-customer-order-status', 'customer-order-status-api'],
  ['COSA', 'repository', 'customer/order-status-api', 'order-status-api'],
  // Desk Booking is hosted, so its one component of its own is the flow that copies bookings to people's calendars.
  ['DB', 'cloud-flow', 'flow-6d02b8', 'Desk Booking calendar connector'],
];
const POWER_PLATFORM_KINDS = new Set(['canvas-app', 'model-driven-app', 'code-app', 'cloud-flow', 'copilot-studio-agent', 'power-pages-site', 'solution']);
COMPONENTS.forEach(([app, kind, externalId, label, extra], i) => {
  const pf = PF[A[app].pf].slug;
  let linkKind;
  let url;
  if (POWER_PLATFORM_KINDS.has(kind)) {
    linkKind = 'power-platform-resource';
    url = 'https://make.example.org/' + (kind === 'cloud-flow' ? 'flows/' : kind === 'solution' ? 'solutions/' : kind === 'copilot-studio-agent' ? 'agents/' : kind === 'power-pages-site' ? 'sites/' : 'apps/') + externalId;
  } else if (kind === 'repository') {
    linkKind = 'repository-file';
    url = 'https://git.example.org/' + externalId;
  } else {
    // Azure resources and agent identities: the first Microsoft resource link kind, as the engine picks for a
    // component kind with no default (IntakeTools.cs DefaultComponentLinkKind).
    linkKind = 'entra-object';
    url = (kind === 'entra-agent-identity' ? 'https://entra.example.org/agents/' : 'https://portal.example.org/' + pf + '/') + externalId;
  }
  const lastSeenOn = kind === 'repository' ? '2026-11-21T22:00:00Z' : '2026-11-20T06:00:00Z';
  add('technical-component', id(3000 + i), clean({
    businessApplication: A[app].id,
    componentKind: kind,
    linkKind,
    externalId,
    url,
    labelAsSeen: label,
    lastSeenOn: extra?.retiredOn ? '2026-05-01T06:00:00Z' : lastSeenOn,
    ownerDiffers: linkKind === 'power-platform-resource' ? (extra?.ownerDiffers ?? false) : undefined,
    ownerCheckedOn: linkKind === 'power-platform-resource' && !extra?.retiredOn ? '2026-11-17T07:00:00Z' : undefined,
    retiredOn: extra?.retiredOn,
  }));
});

// ---------------------------------------------------------------------------------------------------------------
// 7. Decisions (4000..) and change requests (4200..), linked both ways
// ---------------------------------------------------------------------------------------------------------------

let decisionSeq = 0;
const D = {}; // decision key -> id
function decision(key, d) {
  const rowId = id(4000 + decisionSeq);
  const recordNumber = 'DEC-' + String(101 + decisionSeq).padStart(4, '0');
  decisionSeq++;
  // A proposed decision is open work: seed/demo-director.mjs proposes it through the tools instead (see the header).
  // Its slot stays taken, so the decisions after it keep their IDs and record numbers.
  if (d.status === 'proposed') return undefined;
  D[key] = rowId;
  const accepted = d.status === 'accepted';
  // The model requires a reason to accept a decision (O09 Reason).
  if (accepted && !d.reason) throw new Error('The accepted decision ' + key + ' has no reason');
  add('decision', rowId, clean({
    recordNumber,
    decisionType: d.type,
    // d.subject names a data subject (section 17): { kind: 'data-use', use } or { kind: 'data-domain', domain } or
    // { kind: 'data-location', location }. A data-use decision also names its application, as the tools write it.
    subjectKind: d.subject?.kind ?? (d.change ? 'change-request' : d.intake ? 'intake-request' : d.deployment ? 'deployment-record' : 'business-application'),
    subjectApplication: d.app ? A[d.app].id : undefined,
    subjectChangeRequest: d.change,
    subjectIntakeRequest: d.intake,
    subjectDeploymentRecord: d.deployment,
    subjectDataUse: d.subject?.use,
    subjectDataDomain: d.subject?.domain,
    subjectDataLocation: d.subject?.location,
    statement: d.statement,
    context: d.context,
    // Stored as the tools store it: a JSON array of { option, whyNot } in a text column.
    optionsConsidered: d.options ? JSON.stringify(d.options) : undefined,
    exceptionStandard: d.exception?.standard,
    exceptionReviewOn: d.exception?.reviewOn,
    reason: d.reason,
    outcome: accepted ? d.outcome : undefined,
    status: 'decision-' + d.status,
    statusMeaning: d.status,
    decider: accepted ? d.by : undefined,
    decidedOn: accepted ? d.on : undefined,
    engineCheckedOn: d.on,
  }));
  return rowId;
}

// Decisions about applications. The proposed ones are not written: seed/demo-director.mjs proposes their live
// counterparts as the personas who hold the decision right (the reserved slots keep the other record numbers).
// Deciders follow the Balanced approval rules: the security reviewer agrees autonomy levels, and the business owner of
// the time decides ownership.
const APP_DECISIONS = [
  ['IS-GOLIVE', { app: 'IS', type: 'go-live', outcome: 'go', status: 'accepted', by: P.NADIA, on: '2026-03-16T08:00:00Z', statement: 'Go live for all field regions.', reason: 'The two pilot regions closed every inspection on time for six weeks, and support is ready.' }],
  ['IS-DESIGN', { app: 'IS', type: 'design', outcome: 'approve', status: 'accepted', by: P.HUGO, on: '2026-06-30T13:00:00Z', statement: 'Read asset data from Asset Register instead of keeping a local copy.', reason: 'One source of asset data stops the two copies from drifting apart.' }],
  ['IS-TIER', { app: 'IS', type: 'tier', outcome: 'confirm', status: 'accepted', by: P.OWEN, on: '2026-08-28T10:00:00Z', statement: 'Keep tier 2 after adding AI summaries of inspection findings.', reason: 'The summaries only draft text that an inspector checks, so the risk stays the same.' }],
  ['IS-RISK', { app: 'IS', type: 'risk-acceptance', status: 'proposed', on: '2026-11-21T10:00:00Z', statement: 'Accept that inspection photos stay on devices for up to 7 days while offline.' }],
  ['AR-TIER', { app: 'AR', type: 'tier', outcome: 'confirm', status: 'accepted', by: P.OWEN, on: '2026-01-19T14:00:00Z', statement: 'Keep tier 3: Asset Register holds restricted site security data.', reason: 'Site security data needs the strictest change and access controls.' }],
  ['VRA-DATA', { app: 'VRA', type: 'data-use', outcome: 'allow', status: 'accepted', by: P.MATEO, on: '2026-09-08T10:00:00Z', statement: 'Allow inspector notes and site photos to be sent to the approved AI model.', reason: 'The approved model keeps the data in the region and does not train on it.' }],
  ['VRA-AUTO', { app: 'VRA', type: 'autonomy-level', outcome: 'agree', status: 'accepted', by: P.NOOR, on: '2026-10-14T11:00:00Z', statement: 'Agree the assist level: the agent drafts reports and an inspector sends them.', reason: 'The inspector stays responsible for every report that leaves the team.' }],
  ['SFT-END', { app: 'SFT', type: 'ownership', outcome: 'end', status: 'accepted', by: P.KAI, on: '2026-09-30T12:00:00Z', statement: 'End Kai Ostrowski\'s business ownership after his move to the projects team.', reason: 'Kai Ostrowski no longer works with the safety team and cannot act for the app.' }],
  ['SFT-NAME', { app: 'SFT', type: 'ownership', status: 'proposed', on: '2026-11-18T09:30:00Z', statement: 'Name Lena Okoro business owner of Safety Findings Tracker.' }],
  ['CP-EXC', { app: 'CP', type: 'exception', status: 'proposed', on: '2026-11-20T09:00:00Z', statement: 'Accept the sign-in fix made directly in the live portal on November 19.' }],
  ['IM-TIER', { app: 'IM', type: 'tier', outcome: 'confirm', status: 'accepted', by: P.OWEN, on: '2025-10-20T10:00:00Z', statement: 'Tier 2: Invoice Matching changes shared finance data as the signed-in person.', reason: 'It changes shared finance data, but a person confirms every match before payment.' }],
  ['IM-EXC', { app: 'IM', type: 'exception', status: 'proposed', on: '2026-11-22T13:00:00Z', statement: 'Grant an exception for the 1.12.1 hotfix, whose code was approved only by an AI reviewer.' }],
  ['SO-ACC', { app: 'SO', type: 'acceptance', status: 'proposed', on: '2026-11-21T11:00:00Z', statement: 'Accept Supplier Onboarding once release candidate 2 is fixed and retested.' }],
  ['DB-END', { app: 'DB', type: 'ownership', outcome: 'end', status: 'accepted', by: P.ROSA, on: '2026-10-31T16:00:00Z', statement: 'End Bianca Ferro\'s deputy technical owner role after her move to another team.', reason: 'Bianca Ferro moved to another team on October 31 and no longer covers the app.' }],
  ['EA-AUTO', { app: 'EA', type: 'autonomy-level', outcome: 'agree', status: 'accepted', by: P.NOOR, on: '2026-10-30T15:00:00Z', statement: 'Agree the route level: the assistant routes claims and a person approves them.', reason: 'A person still approves every claim, so the assistant cannot pay anything on its own.' }],
  ['CFB-DESIGN', { app: 'CFB', type: 'design', outcome: 'approve', status: 'accepted', by: P.HUGO, on: '2026-10-20T10:00:00Z', statement: 'Build on Copilot Studio and read job data from Customer Self-Service Hub.', reason: 'Copilot Studio is the approved agent platform, and the hub already holds the job data.' }],
  ['OBA-AUTO', { app: 'OBA', type: 'autonomy-level', outcome: 'agree', status: 'accepted', by: P.NOOR, on: '2026-10-16T09:00:00Z', statement: 'Agree the assist level: the agent answers questions and suggests training.', reason: 'The agent only answers and suggests, and a person confirms each training booking.' }],
  ['SPA-TIER', { app: 'SPA', type: 'tier', outcome: 'raise', status: 'accepted', by: P.OWEN, on: '2026-11-05T14:00:00Z', statement: 'Raise to tier 3 because the agent places orders under its own identity.', reason: 'An agent that spends money under its own identity needs the strictest controls.' }],
  ['SPA-AUTO', { app: 'SPA', type: 'autonomy-level', status: 'proposed', on: '2026-11-19T16:00:00Z', statement: 'Agree that the agent may place reorders under 5,000 dollars without a person.' }],
  ['CTA-DESIGN', { app: 'CTA', type: 'design', status: 'proposed', on: '2026-11-17T10:00:00Z', statement: 'Route claims with a Copilot Studio agent and keep claim photos in the claims system.' }],
  ['LTA-PORT', { app: 'LTA', type: 'portfolio', outcome: 'replace', status: 'accepted', by: P.ROSA, on: '2026-06-15T10:00:00Z', statement: 'Replace the timesheet app with time entry in People Hub.', reason: 'People Hub now has time entry, so a second timesheet app only adds cost.' }],
  // The retirement of Legacy Timesheet App is in progress (section 16): the platform admin proposes it complete in the
  // live demo, so this slot is reserved like the other proposed ones.
  ['LTA-RET', { app: 'LTA', type: 'retirement', status: 'proposed', on: '2026-10-01T09:00:00Z', statement: 'Legacy Timesheet App is retired: nobody can enter time, and its data stays read-only until it is archived at the end of March.' }],
  ['PA-PORT', { app: 'PA', type: 'portfolio', status: 'proposed', on: '2026-11-12T10:00:00Z', statement: 'Keep Purchase Approvals for another year, then compare it with the Finance Ledger approvals.' }],
  ['CCF-DATA', { app: 'CCF', type: 'data-use', outcome: 'allow', status: 'accepted', by: P.CLARA, on: '2026-10-20T11:00:00Z', statement: 'Allow supplier contract text to be sent to the approved AI model.', reason: 'The approved model keeps contract text in the region and does not train on it.' }],
  ['MCP-DESIGN', { app: 'MCP', type: 'design', outcome: 'approve', status: 'accepted', by: P.HUGO, on: '2026-10-06T13:00:00Z', statement: 'Expose read-only tools only, and log every call with the client and the person.', reason: 'Read-only tools keep AI clients from changing field data, and the log shows who asked what.' }],
  ['WSC-ACC', { app: 'WSC', type: 'acceptance', outcome: 'accept', status: 'accepted', by: P.TOMAS, on: '2026-11-16T15:00:00Z', statement: 'Warehouse Stock Count works as agreed in test and is ready for release.', reason: 'Staff counted three test stores with no errors, and the differences posted correctly.' }],
];
for (const [key, d] of APP_DECISIONS) decision(key, d);

// The decisions an application row points at.
Object.assign(appRows.IS.columns, { tierDecision: D['IS-TIER'], stageDecision: D['IS-GOLIVE'] });
Object.assign(appRows.AR.columns, { tierDecision: D['AR-TIER'] });
Object.assign(appRows.IM.columns, { tierDecision: D['IM-TIER'] });
Object.assign(appRows.SPA.columns, { tierDecision: D['SPA-TIER'] });
Object.assign(appRows.VRA.columns, { autonomyDecision: D['VRA-AUTO'] });
Object.assign(appRows.EA.columns, { autonomyDecision: D['EA-AUTO'] });
Object.assign(appRows.OBA.columns, { autonomyDecision: D['OBA-AUTO'] });

// Change requests. auth: an accepted change-authorization decision { on, by, reason? }; proposed: a proposed one made
// when the change was submitted, at that moment; neither: a draft, with no decision yet. Windows are UTC.
// Submitted and draft changes are open work: they are not written, and seed/demo-director.mjs drafts and submits the
// same changes (title, window, target) as the personas, so their authorization requests are real. Their slots stay
// reserved. An accepted authorization names the decider Balanced asks: the business owner for a normal change at tier
// 1 or 2, the change manager at tier 3 and for an emergency. Standard changes stay pre-authorized by the change manager
// (see the header).
const CHANGES = [
  ['CR-EMERG', { app: 'CR', title: 'Fix the shift export after a payroll change', type: 'emergency', status: 'completed', start: '2026-11-10T06:00:00Z', end: '2026-11-10T07:00:00Z', target: 'FOP', created: '2026-11-10T05:20:00Z', auth: { on: '2026-11-10T08:15:00Z', by: P.HUGO, reason: 'The payroll run could not wait; the fix was tested in Field Ops Dev and reviewed the same morning.' }, result: 'succeeded', description: 'The payroll change of November 9 broke the nightly shift export. Fixed in the morning before the payroll run.' }],
  ['IS-342', { app: 'IS', title: 'Release 3.4.2: photo upload fix', type: 'normal', status: 'completed', start: '2026-11-13T18:00:00Z', end: '2026-11-13T19:30:00Z', target: 'FOP', created: '2026-11-09T10:00:00Z', auth: { on: '2026-11-11T16:00:00Z', by: P.NADIA }, result: 'succeeded', description: 'Fixes photo uploads that failed on slow connections.' }],
  ['IM-112', { app: 'IM', title: 'Invoice Matching 1.12', type: 'normal', status: 'completed', start: '2026-11-14T15:00:00Z', end: '2026-11-14T17:00:00Z', target: 'FINP', created: '2026-11-10T09:00:00Z', auth: { on: '2026-11-12T11:30:00Z', by: P.CLARA }, result: 'succeeded', description: 'Matches credit notes to the invoices they correct.' }],
  ['SO-RC2', { app: 'SO', title: 'Supplier Onboarding release candidate 2 to test', type: 'normal', status: 'completed', start: '2026-11-21T09:00:00Z', end: '2026-11-21T10:00:00Z', target: 'FINT', created: '2026-11-18T13:00:00Z', auth: { on: '2026-11-19T09:00:00Z', by: P.CLARA }, result: 'partly-succeeded', description: 'Second release candidate for the test cycle. The bank check flow did not import.' }],
  ['RP-20T', { app: 'RP', title: 'Route Planner 2.0 to test', type: 'normal', status: 'in-progress', start: '2026-11-24T09:00:00Z', end: '2026-11-24T12:00:00Z', target: 'FOT', created: '2026-11-18T10:00:00Z', auth: { on: '2026-11-20T10:00:00Z', by: P.INES }, description: 'First test release of the new route optimizer.' }],
  ['IS-ROT', { app: 'IS', title: 'Rotate the reminder flow connection', type: 'standard', status: 'in-progress', start: '2026-11-25T07:00:00Z', end: '2026-11-25T08:00:00Z', target: 'FOP', created: '2026-11-19T08:00:00Z', auth: { on: '2026-11-20T15:00:00Z', by: P.HUGO }, description: 'Moves the reminder flow to the new shared mail connection.' }],
  ['IS-35', { app: 'IS', title: 'Release 3.5: inspector workload view', type: 'normal', status: 'in-progress', start: '2026-11-27T18:00:00Z', end: '2026-11-27T21:00:00Z', target: 'FOP', created: '2026-11-16T09:00:00Z', auth: { on: '2026-11-19T15:20:00Z', by: P.NADIA }, description: 'Adds a workload view so supervisors can balance inspections across inspectors.' }],
  ['MCP-04', { app: 'MCP', title: 'Field Knowledge MCP Server 0.4 to test', type: 'normal', status: 'in-progress', start: '2026-11-30T10:00:00Z', end: '2026-11-30T11:00:00Z', target: 'INTT', created: '2026-11-17T14:00:00Z', auth: { on: '2026-11-20T13:00:00Z', by: P.HUGO }, description: 'Adds the safety rules tool for AI clients.' }],
  ['VRA-10', { app: 'VRA', title: 'Visit Report Agent 1.0 to test', type: 'normal', status: 'in-progress', start: '2026-12-01T09:00:00Z', end: '2026-12-01T11:00:00Z', target: 'FOT', created: '2026-11-13T11:00:00Z', auth: { on: '2026-11-18T14:00:00Z', by: P.RAVI }, description: 'First full release of the agent for the pilot inspectors.' }],
  ['SO-GL', { app: 'SO', title: 'Supplier Onboarding go-live', type: 'normal', status: 'submitted', start: '2026-12-02T17:00:00Z', end: '2026-12-02T20:00:00Z', target: 'FINP', created: '2026-11-17T10:00:00Z', proposed: '2026-11-22T08:00:00Z', description: 'Go live for all new suppliers. Waits for the runbook and the owner acceptance.' }],
  ['CP-SIGN', { app: 'CP', title: 'Contractor Portal sign-in page update', type: 'normal', status: 'submitted', start: '2026-12-04T22:00:00Z', end: '2026-12-05T01:00:00Z', target: 'CUSP', created: '2026-11-20T10:00:00Z', proposed: '2026-11-20T15:00:00Z', description: 'Brings the in-portal sign-in fix of November 19 into the release pipeline.' }],
  ['WSC-GL', { app: 'WSC', title: 'Warehouse Stock Count go-live', type: 'normal', status: 'in-progress', start: '2026-12-08T18:00:00Z', end: '2026-12-08T20:00:00Z', target: 'FOP', created: '2026-11-12T09:00:00Z', auth: { on: '2026-11-17T11:00:00Z', by: P.TOMAS }, description: 'Go live in the three main stores.' }],
  ['EA-PIL', { app: 'EA', title: 'Expense Assistant pilot in test', type: 'normal', status: 'draft', start: '2026-12-09T09:00:00Z', end: '2026-12-09T12:00:00Z', target: 'FINT', created: '2026-11-19T14:00:00Z', description: 'Pilot with the finance team in test.' }],
  ['PCA-25', { app: 'PCA', title: 'Parts Catalog API 2.5: supplier stock levels', type: 'normal', status: 'submitted', start: '2026-12-10T20:00:00Z', end: '2026-12-10T22:00:00Z', target: 'INTP', created: '2026-11-19T15:00:00Z', proposed: '2026-11-20T09:00:00Z', description: 'Adds supplier stock levels to the part lookup.' }],
  ['DB-CAL', { app: 'DB', title: 'Desk Booking calendar connector update', type: 'normal', status: 'submitted', start: '2026-12-11T06:00:00Z', end: '2026-12-11T08:00:00Z', target: 'DBT', created: '2026-11-18T12:00:00Z', proposed: '2026-11-19T08:30:00Z', description: 'Moves the calendar connector to the vendor\'s new API.' }],
  ['CSSH-HOL', { app: 'CSSH', title: 'Holiday opening hours banner', type: 'standard', status: 'in-progress', start: '2026-12-14T07:00:00Z', end: '2026-12-14T07:30:00Z', target: 'CUSP', created: '2026-11-13T09:00:00Z', auth: { on: '2026-11-16T09:00:00Z', by: P.HUGO }, description: 'Shows the holiday opening hours of the contact center.' }],
  ['TRN-ARC', { app: 'TRN', title: 'Training Records yearly archive', type: 'standard', status: 'in-progress', start: '2026-12-15T19:00:00Z', end: '2026-12-15T22:00:00Z', target: 'PPL', created: '2026-11-11T10:00:00Z', auth: { on: '2026-11-13T10:00:00Z', by: P.HUGO }, description: 'Archives training records older than seven years.' }],
  ['CFB-PIL', { app: 'CFB', title: 'Customer Feedback Bot pilot', type: 'normal', status: 'draft', start: '2026-12-16T09:00:00Z', end: '2026-12-17T17:00:00Z', target: null, created: '2026-11-20T16:00:00Z', description: 'Two-day pilot with one region. The target is not chosen yet.' }],
  ['FL-CERT', { app: 'FL', title: 'Finance Ledger connector certificate renewal', type: 'normal', status: 'in-progress', start: '2026-12-21T07:00:00Z', end: '2026-12-21T08:00:00Z', target: 'INTP', created: '2026-11-16T10:00:00Z', auth: { on: '2026-11-17T10:00:00Z', by: P.HUGO }, description: 'Renews the certificate the integration uses to reach the ledger.' }],
  ['PH-LEAVE', { app: 'PH', title: 'Year-end leave carry-over settings', type: 'normal', status: 'in-progress', start: '2027-01-05T07:00:00Z', end: '2027-01-05T09:00:00Z', target: 'PHT', created: '2026-11-18T09:00:00Z', auth: { on: '2026-11-19T10:00:00Z', by: P.HUGO }, description: 'Sets the leave carry-over rules for the new year.' }],
  ['AR-72', { app: 'AR', title: 'Asset Register 7.2: warranty dates', type: 'normal', status: 'submitted', start: '2026-12-16T18:00:00Z', end: '2026-12-16T21:00:00Z', target: 'FOP', created: '2026-11-20T11:00:00Z', proposed: '2026-11-21T10:00:00Z', description: 'Shows warranty end dates and warns 90 days before.' }],
  ['CR-53', { app: 'CR', title: 'Crew Roster 5.3: skills matrix', type: 'normal', status: 'submitted', start: '2026-12-10T18:00:00Z', end: '2026-12-10T20:00:00Z', target: 'FOP', created: '2026-11-20T14:00:00Z', proposed: '2026-11-21T11:30:00Z', description: 'Adds a skills matrix so planners can see who can do which job.' }],
  ['IM-113', { app: 'IM', title: 'Invoice Matching 1.13', type: 'normal', status: 'submitted', start: '2027-01-14T15:00:00Z', end: '2027-01-14T17:00:00Z', target: 'FINP', created: '2026-11-21T09:00:00Z', proposed: '2026-11-22T09:30:00Z', description: 'Matches invoices in other currencies.' }],
  ['IS-EXP', { app: 'IS', title: 'Retire the old export flow', type: 'normal', status: 'draft', start: null, end: null, target: null, created: '2026-11-23T08:00:00Z', description: 'The workload view replaces the spreadsheet export. No window yet.' }],
  ['AR-713', { app: 'AR', title: 'Asset Register 7.1.3 security patch', type: 'normal', status: 'completed', start: '2026-11-03T18:00:00Z', end: '2026-11-03T20:00:00Z', target: 'FOP', created: '2026-10-28T09:00:00Z', auth: { on: '2026-10-30T14:00:00Z', by: P.HUGO }, result: 'succeeded', description: 'Applies the platform security update.' }],
  ['PCA-24', { app: 'PCA', title: 'Parts Catalog API 2.4: bulk lookup', type: 'normal', status: 'completed', start: '2026-11-05T20:00:00Z', end: '2026-11-05T22:00:00Z', target: 'INTP', created: '2026-10-29T10:00:00Z', auth: { on: '2026-11-03T10:00:00Z', by: P.TOMAS }, result: 'succeeded', description: 'Lets callers look up up to 200 parts in one call.' }],
  ['CNS-41', { app: 'CNS', title: 'Customer Notice Sender 4.1', type: 'normal', status: 'completed', start: '2026-10-28T09:00:00Z', end: '2026-10-28T11:00:00Z', target: 'CUSP', created: '2026-10-21T10:00:00Z', auth: { on: '2026-10-23T15:00:00Z', by: P.NADIA }, result: 'succeeded', description: 'Adds text message notices.' }],
  ['TRN-23', { app: 'TRN', title: 'Training Records 2.3: renewal reminders', type: 'normal', status: 'completed', start: '2026-10-30T18:00:00Z', end: '2026-10-30T19:00:00Z', target: 'PPL', created: '2026-10-22T10:00:00Z', auth: { on: '2026-10-27T11:00:00Z', by: P.ROSA }, result: 'succeeded', description: 'Reminds people 60 days before a certificate expires.' }],
  ['TRV-304', { app: 'TRV', title: 'Travel Requests 3.0.4: daily allowance rates', type: 'normal', status: 'completed', start: '2026-11-06T18:00:00Z', end: '2026-11-06T20:00:00Z', target: 'PPL', created: '2026-10-26T10:00:00Z', auth: { on: '2026-10-28T10:00:00Z', by: P.AMARA }, result: 'succeeded', description: 'Loads the new daily allowance rates.' }],
  ['DB-PARK', { app: 'DB', title: 'Desk Booking parking rules update', type: 'standard', status: 'completed', start: '2026-11-09T08:00:00Z', end: '2026-11-09T09:00:00Z', target: 'DBT', created: '2026-11-05T10:00:00Z', auth: { on: '2026-11-06T10:00:00Z', by: P.HUGO }, result: 'succeeded', description: 'Reserves parking spaces for visitors on Mondays.' }],
  ['RP-194', { app: 'RP', title: 'Route Planner 1.9.4: depot opening hours', type: 'normal', status: 'completed', start: '2026-10-27T18:00:00Z', end: '2026-10-27T19:00:00Z', target: 'FOP', created: '2026-10-20T10:00:00Z', auth: { on: '2026-10-22T10:00:00Z', by: P.INES }, result: 'succeeded', description: 'Plans routes around the winter depot opening hours.' }],
];
const C = {}; // change key -> id
const CHANGE_STATUS = { draft: 'change-draft', submitted: 'change-submitted', 'in-progress': 'change-in-progress', completed: 'change-completed' };
CHANGES.forEach(([key], i) => { C[key] = id(4200 + i); });
// Every built-in change past draft links its change-authorization decision, as submit_for_approval writes it
// (DecisionAndChangeTools.cs SubjectDecision): get_change_schedule, record_deployment and withdraw_record read only that
// link. A proposed decision carries the statement the engine writes; an accepted one carries the decider's reason.
const authReason = c => {
  if (c.auth.reason) return c.auth.reason;
  if (c.type === 'standard') return 'A routine, low-risk change that follows a tested runbook.';
  return T[c.target] && rows.find(r => r.id === T[c.target])?.columns.targetStage === 'production'
    ? 'Tested before release, and the owner agreed the rollback plan.'
    : 'A release to test only, with no effect on live users.';
};
const authorizationOf = {};
CHANGES.forEach(([key, c]) => {
  if (!c.auth && !c.proposed) return;
  const statement = c.proposed
    ? 'Authorize the change: ' + c.title
    : c.type === 'emergency'
      ? 'Authorize the emergency change "' + c.title + '" after it was made.'
      : c.type === 'standard'
        ? 'Pre-authorize the standard change "' + c.title + '" in the ' + monthDay(c.start) + ' window.'
        : 'Authorize "' + c.title + '" in the ' + monthDay(c.start) + ' window.';
  const d = c.auth
    ? { app: c.app, change: C[key], type: 'change-authorization', outcome: 'authorize', status: 'accepted', by: c.auth.by, on: c.auth.on, statement, reason: authReason(c) }
    : { app: c.app, change: C[key], type: 'change-authorization', status: 'proposed', on: c.proposed, statement };
  authorizationOf[key] = decision(key + '-AUTH', d);
});
CHANGES.forEach(([key, c], i) => {
  if (c.status === 'draft' || c.status === 'submitted') return; // open work: seed/demo-director.mjs makes it live
  if (!authorizationOf[key]) throw new Error('The change ' + key + ' is past draft but has no authorization decision');
  const authorizationDecision = authorizationOf[key];
  add('change-request', C[key], clean({
    recordNumber: 'CHG-' + String(201 + i).padStart(4, '0'),
    title: c.title,
    description: c.description,
    businessApplication: A[c.app].id,
    changeType: c.type,
    status: CHANGE_STATUS[c.status],
    statusMeaning: c.status,
    mode: 'built-in',
    windowStart: c.start,
    windowEnd: c.end,
    plannedTarget: c.target ? T[c.target] : undefined,
    authorizationDecision,
    result: c.result,
    createdOn: c.created,
  }));
});

// ---------------------------------------------------------------------------------------------------------------
// 8. Owner assignments (walkthrough 551..555; new 2000..)
// ---------------------------------------------------------------------------------------------------------------

let ownerSeq = 0;
const nextOwnerId = () => id(2000 + ownerSeq++);
// [app, role, holder, deputy, startOn, options]
//   options.holderRow: the walkthrough row that already holds the role (no new holder row)
//   options.holderEnd / options.deputyEnd / options.holderDecision / options.deputyDecision / options.deputyStart
// The six deliberate owner gaps (Balanced: a business owner at every tier and a data owner at tiers 2 and 3 must have a
// holder, and business, data, process, technical and support owners need a deputy):
//   Inspection Scheduler, business owner Nadia Ferreira has no deputy
//   Safety Findings Tracker, no business owner since Kai Ostrowski's assignment ended on 2026-09-30
//   Travel Requests, business owner Amara Quist has no deputy and her account is disabled
//   Desk Booking, the technical owner's deputy Bianca Ferro left the role on 2026-10-31
//   Quote Builder and Partner Rebate Calculator, tier 2 applications still before go-live, have no data owner yet
// Every other tier 2 and 3 application names a data owner with a deputy (document 21, B21-9: Balanced asks for one
// before go-live). Three applications have a sponsor, so the ownership fallback has a holder to ask: Safety Findings
// Tracker and Claims Triage Agent, whose business owner role changes hands in the demo, and Travel Requests, whose
// business owner left.
const OWNERS = [
  ['IS', 'business-owner', P.NADIA, null, null, { holderRow: id(551) }],
  ['IS', 'technical-owner', P.PRIYA, P.LENA, null, { holderRow: id(552), deputyStart: '2026-02-02' }],
  ['IS', 'data-owner', P.MATEO, P.RAVI, '2025-09-01'],
  ['IS', 'support-owner', P.FAS, P.SERVDESK, '2026-03-16'],
  ['IS', 'builder', P.SANA, null, '2025-09-01'],
  ['RP', 'business-owner', P.INES, P.RAVI, null, { holderRow: id(553), deputyStart: '2025-06-02' }],
  ['RP', 'technical-owner', P.OWEN, P.JONAH, '2025-06-02'],
  ['RP', 'builder', P.PRIYA, null, '2025-06-02'],
  ['CR', 'business-owner', P.INES, P.RAVI, null, { holderRow: id(554), deputyStart: '2025-01-13' }],
  ['CR', 'technical-owner', P.JONAH, P.LENA, '2025-01-13'],
  ['CR', 'builder', P.SANA, null, '2025-01-13'],
  ['CSM', 'business-owner', P.INES, P.RAVI, null, { holderRow: id(555), deputyStart: '2025-09-01' }],
  ['CSM', 'builder', P.SANA, null, '2025-09-01'],
  ['AR', 'business-owner', P.MATEO, P.INES, '2024-05-13'],
  ['AR', 'technical-owner', P.OWEN, P.JONAH, '2024-05-13'],
  ['AR', 'data-owner', P.RAVI, P.INES, '2024-05-13'],
  ['AR', 'support-owner', P.FAS, P.SERVDESK, '2024-05-13'],
  ['AR', 'security-owner', P.NOOR, null, '2025-02-03'],
  ['PCA', 'business-owner', P.TOMAS, P.RAVI, '2025-02-17'],
  ['PCA', 'technical-owner', P.OWEN, P.YUSUF, '2025-02-17'],
  ['VRA', 'business-owner', P.RAVI, P.INES, '2026-06-01'],
  ['VRA', 'technical-owner', P.ELIF, P.JONAH, '2026-06-01'],
  ['VRA', 'builder', P.SANA, null, '2026-06-01'],
  ['SFT', 'business-owner', P.KAI, P.LENA, '2025-04-07', { holderEnd: '2026-09-30', holderDecision: 'SFT-END' }],
  ['SFT', 'technical-owner', P.SANA, P.JONAH, '2025-04-07'],
  ['CNS', 'business-owner', P.NADIA, P.IRIS, '2025-08-04'],
  ['CNS', 'technical-owner', P.TOMAS, P.MARCUS, '2025-08-04'],
  ['CP', 'business-owner', P.NADIA, P.WREN, '2024-10-21'],
  ['CP', 'technical-owner', P.PRIYA, P.MARCUS, '2024-10-21'],
  ['CP', 'support-owner', P.SERVDESK, P.PLATTEAM, '2024-10-21'],
  ['CP', 'security-owner', P.NOOR, null, '2025-02-03'],
  ['IM', 'business-owner', P.CLARA, P.GRETA, '2025-11-03'],
  ['IM', 'technical-owner', P.DMITRI, P.YUSUF, '2025-11-03'],
  ['IM', 'builder', P.DMITRI, null, '2025-08-11'],
  ['SO', 'business-owner', P.CLARA, P.GRETA, '2026-05-04'],
  ['SO', 'technical-owner', P.YUSUF, P.DMITRI, '2026-05-04'],
  ['SO', 'builder', P.YUSUF, null, '2026-05-04'],
  ['TRV', 'business-owner', P.AMARA, null, '2024-09-09'],
  ['TRV', 'technical-owner', P.FELIX, P.HANA, '2024-09-09'],
  ['DB', 'business-owner', P.ROSA, P.HANA, '2025-06-23'],
  ['DB', 'technical-owner', P.THEO, P.BIANCA, '2025-06-23', { deputyEnd: '2026-10-31', deputyDecision: 'DB-END' }],
  ['IDS', 'business-owner', P.ELIF, P.HUGO, '2024-03-04'],
  ['IDS', 'support-owner', P.PLATTEAM, P.SERVDESK, '2024-03-04'],
  ['IDS', 'security-owner', P.NOOR, null, '2025-02-03'],
  ['FDL', 'business-owner', P.MATEO, P.RAVI, '2024-06-03'],
  ['FDL', 'technical-owner', P.PRIYA, P.NOOR, '2024-06-03'],
  ['FDL', 'support-owner', P.PLATTEAM, P.FAS, '2024-06-03'],
  ['FDL', 'security-owner', P.NOOR, null, '2025-02-03'],
  ['CFB', 'business-owner', P.WREN, P.IRIS, '2026-09-14'],
  // Noor Safadi holds the security owner role of the bot, as on the other tier 3 agents, so the security review the AI
  // lead requests is due in her Reviews and she completes it (product review, story step 8).
  ['CFB', 'security-owner', P.NOOR, null, '2026-09-14'],
  ['CFB', 'technical-owner', P.MARCUS, P.PRIYA, '2026-09-14'],
  ['CFB', 'builder', P.PRIYA, null, '2026-09-14'],
  ['EA', 'business-owner', P.GRETA, P.CLARA, '2026-08-17'],
  ['EA', 'technical-owner', P.DMITRI, P.YUSUF, '2026-08-17'],
  ['EA', 'builder', P.YUSUF, null, '2026-08-17'],
  ['TRN', 'business-owner', P.ROSA, P.HANA, '2024-02-12'],
  ['TRN', 'technical-owner', P.FELIX, P.THEO, '2024-02-12'],
  ['TRN', 'builder', P.HANA, null, '2024-02-12'],
  ['CSSH', 'business-owner', P.NADIA, P.WREN, '2025-01-27'],
  ['CSSH', 'technical-owner', P.PRIYA, P.MARCUS, '2025-01-27'],
  ['FML', 'business-owner', P.INES, P.LENA, '2025-05-19'],
  ['FML', 'technical-owner', P.JONAH, P.SANA, '2025-05-19'],
  ['FL', 'business-owner', P.CLARA, P.GRETA, '2023-07-03'],
  ['FL', 'technical-owner', P.DMITRI, P.YUSUF, '2023-07-03'],
  ['FL', 'support-owner', P.FINSUP, P.SERVDESK, '2023-07-03'],
  ['FL', 'security-owner', P.NOOR, null, '2025-02-03'],
  ['PH', 'business-owner', P.ROSA, P.HANA, '2024-01-08'],
  ['PH', 'technical-owner', P.FELIX, P.THEO, '2024-01-08'],
  ['PH', 'data-owner', P.HANA, P.ROSA, '2024-01-08'],
  ['PH', 'support-owner', P.SERVDESK, P.PLATTEAM, '2024-01-08'],
  ['PH', 'security-owner', P.NOOR, null, '2025-02-03'],
  ['SDP', 'business-owner', P.HUGO, P.NOOR, '2024-11-11'],
  ['SDP', 'technical-owner', P.OWEN, P.NOOR, '2024-11-11'],
  ['CTF', 'business-owner', P.CLARA, P.GRETA, '2025-03-10'],
  ['CTF', 'technical-owner', P.YUSUF, P.DMITRI, '2025-03-10'],
  ['SAB', 'business-owner', P.MATEO, P.INES, '2025-08-18'],
  ['SAB', 'technical-owner', P.SANA, P.JONAH, '2025-08-18'],
  ['PA', 'business-owner', P.GRETA, P.CLARA, '2024-04-15'],
  ['PA', 'technical-owner', P.YUSUF, P.DMITRI, '2024-04-15'],
  // D-41 (2026-11-24): Nadia Ferreira is the deputy business owner, so a persona answers the story's retirement.
  ['LTA', 'business-owner', P.ROSA, P.NADIA, '2022-09-05'],
  ['LTA', 'technical-owner', P.FELIX, P.THEO, '2022-09-05'],
  ['WSC', 'business-owner', P.TOMAS, P.RAVI, '2026-07-06'],
  ['WSC', 'technical-owner', P.JONAH, P.LENA, '2026-07-06'],
  ['WSC', 'builder', P.PRIYA, null, '2026-07-06'],
  ['CCF', 'business-owner', P.CLARA, P.GRETA, '2026-08-24'],
  ['CCF', 'technical-owner', P.YUSUF, P.DMITRI, '2026-08-24'],
  ['CCF', 'builder', P.YUSUF, null, '2026-08-24'],
  ['MCP', 'business-owner', P.HUGO, P.MATEO, '2026-09-07'],
  ['MCP', 'technical-owner', P.NOOR, P.OWEN, '2026-09-07'],
  ['OBA', 'business-owner', P.HANA, P.ROSA, '2026-08-31'],
  ['OBA', 'technical-owner', P.FELIX, P.THEO, '2026-08-31'],
  ['OBA', 'builder', P.HANA, null, '2026-08-31'],
  ['SSR', 'business-owner', P.INES, P.RAVI, '2026-10-05'],
  ['SSR', 'technical-owner', P.SANA, P.JONAH, '2026-10-05'],
  ['SSR', 'builder', P.PRIYA, null, '2026-10-05'],
  ['COSA', 'business-owner', P.NADIA, P.IRIS, '2026-08-10'],
  ['COSA', 'technical-owner', P.MARCUS, P.PRIYA, '2026-08-10'],
  ['SPA', 'business-owner', P.CLARA, P.GRETA, '2026-10-12'],
  ['SPA', 'technical-owner', P.DMITRI, P.YUSUF, '2026-10-12'],
  ['SPA', 'security-owner', P.NOOR, null, '2026-11-05'],
  ['VSK', 'business-owner', P.ROSA, P.HANA, '2026-10-26'],
  ['VSK', 'technical-owner', P.THEO, P.FELIX, '2026-10-26'],
  ['CTA', 'business-owner', P.WREN, P.NADIA, '2026-10-05'],
  ['CTA', 'technical-owner', P.MARCUS, P.PRIYA, '2026-10-05'],
  ['QB', 'business-owner', P.IRIS, P.WREN, '2026-10-19'],
  ['QB', 'technical-owner', P.MARCUS, P.PRIYA, '2026-10-19'],
  ['SET', 'business-owner', P.INES, P.MATEO, '2026-11-02'],
  ['SET', 'technical-owner', P.SANA, P.JONAH, '2026-11-02'],
  ['TCC', 'business-owner', P.TOMAS, P.INES, '2026-10-12'],
  ['MRS', 'business-owner', P.HANA, P.ROSA, '2026-10-26'],
  ['MC', 'business-owner', P.GRETA, P.CLARA, '2026-11-04'],
  // Priya Castell builds Mileage Claims and is its technical owner, so when she confirms the use Claude Code drafted for
  // her, its data-use decision opens and the card goes to Greta Lund (data design review; docs/architecture/data-taxonomy.md, In the AI build loop).
  ['MC', 'technical-owner', P.PRIYA, P.MARCUS, '2026-11-04'],
  ['VDP', 'business-owner', P.ROSA, P.HANA, '2026-11-12'],
  ['PRC', 'business-owner', P.CLARA, P.GRETA, '2026-11-17'],
  ['TH', 'business-owner', P.IRIS, P.WREN, '2026-11-18'],
  ['LS', 'business-owner', P.HUGO, P.NOOR, '2026-11-05'],
  ['PLF', 'business-owner', P.WREN, P.IRIS, '2026-11-10'],
  ['FPT', 'business-owner', P.RAVI, P.INES, '2026-11-19'],
  // Data owners, appended so the rows above keep their IDs. The data-use rule asks the data owner (Balanced), so these
  // make DEC-0106 (Mateo Brandt, deputy) and DEC-0124 (Clara Voss) decisions their rules allow, and give the data owner
  // persona (Kai Ostrowski) an application whose data use the demo director proposes.
  ['VRA', 'data-owner', P.KAI, P.MATEO, '2026-06-01'],
  ['CCF', 'data-owner', P.CLARA, P.GRETA, '2026-08-24'],
  // Data owners of the other tier 2 and 3 applications (Balanced aligned with the process documents, document 21,
  // B21-9: a data owner is named before go-live at tiers 2 and 3), appended so every row above keeps its ID. Quote
  // Builder and Partner Rebate Calculator stay without one: two deliberate gaps of applications before go-live. Kai
  // Ostrowski, the data owner persona, owns the data of Field Knowledge MCP Server, a tier 3 application, so the demo's
  // restricted data use there needs his yes and the security reviewer's.
  ['CR', 'data-owner', P.RAVI, P.INES, '2025-01-13'],
  ['PCA', 'data-owner', P.RAVI, P.MATEO, '2025-02-17'],
  ['MCP', 'data-owner', P.KAI, P.MATEO, '2026-09-07'],
  ['CP', 'data-owner', P.WREN, P.IRIS, '2024-10-21'],
  ['IDS', 'data-owner', P.HANA, P.ROSA, '2024-03-04'],
  ['FDL', 'data-owner', P.RAVI, P.MATEO, '2024-06-03'],
  ['FL', 'data-owner', P.GRETA, P.CLARA, '2023-07-03'],
  ['SPA', 'data-owner', P.GRETA, P.CLARA, '2026-10-12'],
  ['IM', 'data-owner', P.GRETA, P.CLARA, '2025-11-03'],
  ['SO', 'data-owner', P.GRETA, P.CLARA, '2026-05-04'],
  ['CFB', 'data-owner', P.WREN, P.IRIS, '2026-09-14'],
  ['EA', 'data-owner', P.CLARA, P.GRETA, '2026-08-17'],
  ['CSSH', 'data-owner', P.WREN, P.IRIS, '2025-01-27'],
  ['SDP', 'data-owner', P.HUGO, P.NOOR, '2024-11-11'],
  ['CTF', 'data-owner', P.CLARA, P.GRETA, '2025-03-10'],
  ['PA', 'data-owner', P.GRETA, P.CLARA, '2024-04-15'],
  ['WSC', 'data-owner', P.TOMAS, P.RAVI, '2026-07-06'],
  ['OBA', 'data-owner', P.HANA, P.ROSA, '2026-08-31'],
  ['COSA', 'data-owner', P.NADIA, P.IRIS, '2026-08-10'],
  ['CTA', 'data-owner', P.WREN, P.NADIA, '2026-10-05'],
  // Sponsors (no deputy needed): when the business owner role changes hands, Balanced asks the sponsor in place of the
  // business owner, then the portfolio's accountable person (P08).
  ['SFT', 'sponsor', P.INES, null, '2025-04-07'],
  // D-41 (2026-11-24): Mateo Brandt, who plays the PMO, sponsors the claims agent, so a persona answers its owner change.
  ['CTA', 'sponsor', P.MATEO, null, '2026-10-05'],
  ['TRV', 'sponsor', P.ROSA, null, '2024-09-09'],
];
for (const [app, role, holder, deputy, startOn, opt = {}] of OWNERS) {
  let holderRowId = opt.holderRow;
  if (holderRowId) {
    const row = rowById.get(holderRowId);
    if (row.columns.businessApplication !== A[app].id || row.columns.ownerRole !== role || row.columns.principal !== holder) {
      throw new Error('The walkthrough owner row ' + holderRowId + ' is not ' + app + ' ' + role);
    }
  } else {
    holderRowId = nextOwnerId();
    add('owner-assignment', holderRowId, clean({
      businessApplication: A[app].id,
      ownerRole: role,
      principal: holder,
      isDeputy: false,
      startOn,
      endOn: opt.holderEnd,
      ownershipDecision: opt.holderDecision ? D[opt.holderDecision] : undefined,
    }));
  }
  if (deputy) {
    add('owner-assignment', nextOwnerId(), clean({
      businessApplication: A[app].id,
      ownerRole: role,
      principal: deputy,
      isDeputy: true,
      deputyFor: holderRowId,
      startOn: opt.deputyStart ?? startOn ?? rowById.get(holderRowId)?.columns.startOn,
      endOn: opt.deputyEnd,
      ownershipDecision: opt.deputyDecision ? D[opt.deputyDecision] : undefined,
    }));
  }
}

// ---------------------------------------------------------------------------------------------------------------
// 9. Dependencies (walkthrough 561..563; new 3200..). fromApplication depends on toApplication.
// ---------------------------------------------------------------------------------------------------------------

// Inspection Scheduler: dependants reach 3 hops and stop at a 4th (Customer Feedback Bot, Claims Triage Agent), so the
// map says there is more; dependencies reach 3 hops (Fleet Maintenance Log). Contractor Portal and Asset Register
// also reach two to three hops each way. One type per chain keeps the deeper labels honest where it can.
const DEPENDENCIES = [
  ['IS', 'AR', 'reads-data-from', 'high'],
  ['IS', 'PCA', 'calls-api-of', 'medium'],
  ['IS', 'IDS', 'reads-data-from', 'medium'],
  ['VRA', 'IS', 'reads-data-from', 'high'],
  ['SFT', 'IS', 'reads-data-from', 'medium'],
  ['CNS', 'IS', 'calls-api-of', 'low'],
  ['CP', 'CNS', 'calls-api-of', 'high'],
  ['CSSH', 'CP', 'calls-api-of', null],
  ['CFB', 'CSSH', 'reads-data-from', 'low'],
  ['CTA', 'CSSH', 'reads-data-from', 'medium'],
  ['AR', 'FDL', 'reads-data-from', 'high'],
  ['IDS', 'PH', 'reads-data-from', 'high'],
  ['PCA', 'FL', 'calls-api-of', 'medium'],
  ['FDL', 'FML', 'reads-data-from', 'low'],
  ['IM', 'FL', 'reads-data-from', 'high'],
  ['SO', 'FL', 'writes-data-to', 'high'],
  ['PA', 'FL', 'writes-data-to', 'medium'],
  ['EA', 'CTF', 'reads-data-from', 'high'],
  ['EA', 'FL', 'calls-api-of', 'medium'],
  ['TRV', 'IDS', 'reads-data-from', 'medium'],
  ['TRN', 'PH', 'reads-data-from', 'medium'],
  ['DB', 'IDS', 'reads-data-from', 'low'],
  ['OBA', 'PH', 'reads-data-from', 'medium'],
  ['OBA', 'TRN', 'reads-data-from', 'low'],
  ['CSSH', 'COSA', 'calls-api-of', 'medium'],
  ['CSSH', 'SDP', 'calls-api-of', 'medium'],
  ['QB', 'PCA', 'calls-api-of', 'high'],
  ['WSC', 'PCA', 'calls-api-of', 'medium'],
  ['MCP', 'AR', 'reads-data-from', 'medium'],
  ['SPA', 'PCA', 'calls-api-of', 'high'],
  ['SSR', 'CR', 'writes-data-to', 'medium'],
  ['SAB', 'IDS', 'reads-data-from', 'low'],
  ['PH', 'LTA', 'replaces', null],
];
DEPENDENCIES.forEach(([from, to, type, criticality], i) => {
  add('dependency', id(3200 + i), clean({
    fromApplication: A[from].id,
    toApplication: A[to].id,
    dependencyType: type,
    criticality,
  }));
});

// ---------------------------------------------------------------------------------------------------------------
// 10. Artifact versions (4300..) and deployment records (4500..)
// ---------------------------------------------------------------------------------------------------------------

// Gate levels follow the Balanced gate policy: development off, test flag, production tier 1 flag, tier 2 advise,
// tier 3 enforce, emergency in production flag. Flags are seeded as the engine would have set them.
// [app, target, artifactName, artifactKind, version, finishedOn, minutes, route, result, gate, flags, change, deployedBy, run]
//   flags: o outside window, p other path, m missing approval, a AI-only code approval
const DEPLOYMENTS = [
  // Older than 30 days on 2026-11-24: Application 360 only (latest per target).
  ['CR', 'FOT', 'Crew Roster solution', 'managed-solution', '5.1.0', '2026-09-14T10:20:00Z', 14, 'pipeline-step', 'succeeded', 'flag', '', null, P.FOPIPE, 'field-ops/crew-roster/runs/2140'],
  ['IDS', 'INTP', 'identity-directory-sync', 'container-image', '4.0.1', '2026-09-29T21:10:00Z', 9, 'pipeline-step', 'succeeded', 'enforce', '', null, P.INTPIPE, 'platforms/identity-directory-sync/runs/412'],
  ['CSM', 'FOP', 'Crew Shift Mobile solution', 'managed-solution', '1.4.0', '2026-09-22T19:05:00Z', 11, 'pipeline-step', 'succeeded', 'flag', '', null, P.FOPIPE, 'field-ops/crew-shift-mobile/runs/377'],
  ['FDL', 'INTP', 'field-data-lake', 'code-package', '3.2.0', '2026-10-06T20:40:00Z', 25, 'pipeline-step', 'succeeded', 'enforce', '', null, P.INTPIPE, 'platforms/field-data-lake/runs/958'],
  ['CP', 'CUST', 'Contractor Portal site', 'managed-solution', '2.8.0', '2026-10-13T14:30:00Z', 18, 'pipeline-step', 'succeeded', 'flag', '', null, P.CUSTPIPE, 'customer/contractor-portal/runs/655'],
  ['CP', 'CUSP', 'Contractor Portal site', 'managed-solution', '2.8.0', '2026-10-15T22:35:00Z', 19, 'pipeline-step', 'succeeded', 'enforce', '', null, P.CUSTPIPE, 'customer/contractor-portal/runs/661'],
  ['CSSH', 'CUSP', 'self-service-hub', 'static-site', '6.0.1', '2026-10-19T06:50:00Z', 8, 'pipeline-step', 'succeeded', 'advise', '', null, P.CUSTPIPE, 'customer/self-service-hub/runs/1203'],
  ['AR', 'FOT', 'Asset Register solution', 'managed-solution', '7.1.2', '2026-10-20T11:15:00Z', 16, 'pipeline-step', 'succeeded', 'flag', '', null, P.FOPIPE, 'field-ops/asset-register/runs/3301'],
  // The last 30 days before 2026-11-24.
  ['RP', 'FOP', 'Route Planner solution', 'managed-solution', '1.9.4', '2026-10-27T18:30:00Z', 12, 'pipeline-step', 'succeeded', 'flag', '', 'RP-194', P.FOPIPE, 'field-ops/route-planner/runs/1520'],
  ['VRA', 'FOD', 'visit-report-agent', 'agent-package', '0.9.0', '2026-10-27T13:40:00Z', 6, 'pipeline-step', 'succeeded', 'off', '', null, P.FOPIPE, 'field-ops/visit-report-agent/runs/88'],
  ['CNS', 'CUSP', 'Customer Notice Sender solution', 'managed-solution', '4.1.0', '2026-10-28T10:12:00Z', 11, 'pipeline-step', 'succeeded', 'flag', '', 'CNS-41', P.CUSTPIPE, 'customer/customer-notice-sender/runs/504'],
  ['TRN', 'PPL', 'Training Records solution', 'managed-solution', '2.3.0', '2026-10-30T18:40:00Z', 13, 'pipeline-step', 'succeeded', 'flag', '', 'TRN-23', P.PEOPLEPIPE, 'people/training-records/runs/219'],
  ['PCA', 'INTT', 'parts-catalog-api', 'container-image', '2.4.0', '2026-11-03T15:20:00Z', 7, 'pipeline-step', 'succeeded', 'flag', '', null, P.INTPIPE, 'field-ops/parts-catalog-api/runs/731'],
  ['AR', 'FOP', 'Asset Register solution', 'managed-solution', '7.1.3', '2026-11-03T19:05:00Z', 22, 'pipeline-step', 'succeeded', 'enforce', '', 'AR-713', P.FOPIPE, 'field-ops/asset-register/runs/3318'],
  ['CR', 'FOP', 'Crew Roster solution', 'managed-solution', '5.2.1', '2026-11-04T17:25:00Z', 9, 'ai-tool', 'succeeded', 'advise', 'pm', null, P.JONAH, null],
  ['PCA', 'INTP', 'parts-catalog-api', 'container-image', '2.4.0', '2026-11-05T20:47:00Z', 8, 'pipeline-step', 'succeeded', 'advise', '', 'PCA-24', P.INTPIPE, 'field-ops/parts-catalog-api/runs/736'],
  ['TRV', 'PPL', 'Travel Requests solution', 'managed-solution', '3.0.4', '2026-11-06T19:20:00Z', 10, 'person-in-app', 'succeeded', 'flag', 'p', 'TRV-304', P.FELIX, null],
  ['DB', 'DBT', 'Desk Booking settings', 'other', 'parking-rules-7', '2026-11-09T08:30:00Z', 15, 'person-in-app', 'succeeded', 'flag', '', 'DB-PARK', P.THEO, null],
  ['CR', 'FOP', 'Crew Roster solution', 'managed-solution', '5.2.2', '2026-11-10T06:42:00Z', 10, 'pipeline-step', 'succeeded', 'flag', '', 'CR-EMERG', P.FOPIPE, 'field-ops/crew-roster/runs/2207'],
  ['RP', 'FOD', 'route-planner', 'code-package', '2.0.0-beta.2', '2026-11-10T15:30:00Z', 5, 'ai-tool', 'succeeded', 'off', '', null, P.OWEN, null],
  ['IS', 'FOD', 'Inspection Scheduler solution', 'managed-solution', '3.4.2.1', '2026-11-12T11:05:00Z', 7, 'pipeline-step', 'succeeded', 'off', '', null, P.FOPIPE, 'field-ops/inspection-scheduler/runs/1840'],
  ['WSC', 'FOT', 'Warehouse Stock Count solution', 'managed-solution', '1.0.0-rc.1', '2026-11-12T14:20:00Z', 9, 'pipeline-step', 'succeeded', 'flag', '', null, P.FOPIPE, 'field-ops/warehouse-stock-count/runs/64'],
  ['IS', 'FOP', 'Inspection Scheduler solution', 'managed-solution', '3.4.2.3', '2026-11-13T19:41:00Z', 12, 'pipeline-step', 'succeeded', 'advise', 'o', 'IS-342', P.FOPIPE, 'field-ops/inspection-scheduler/runs/1846'],
  ['IM', 'FINP', 'invoice-matching', 'code-package', '1.12.0', '2026-11-14T16:05:00Z', 9, 'pipeline-step', 'succeeded', 'advise', 'a', 'IM-112', P.FINPIPE, 'finance/invoice-matching/runs/311'],
  ['VRA', 'FOT', 'visit-report-agent', 'agent-package', '0.9.2', '2026-11-16T10:10:00Z', 6, 'pipeline-step', 'succeeded', 'flag', '', null, P.FOPIPE, 'field-ops/visit-report-agent/runs/97'],
  ['RP', 'FOD', 'route-planner', 'code-package', '2.0.0-beta.3', '2026-11-17T13:20:00Z', 4, 'ai-tool', 'failed', 'off', '', null, P.OWEN, null],
  ['IS', 'FOT', 'Inspection Scheduler solution', 'managed-solution', '3.5.0.9', '2026-11-18T10:30:00Z', 11, 'pipeline-step', 'succeeded', 'flag', '', null, P.FOPIPE, 'field-ops/inspection-scheduler/runs/1861'],
  ['CCF', 'FINT', 'contract-clause-finder', 'code-package', '0.8.0', '2026-11-18T16:30:00Z', 6, 'pipeline-step', 'succeeded', 'flag', '', null, P.FINPIPE, 'finance/contract-clause-finder/runs/42'],
  ['MCP', 'INTT', 'field-knowledge-mcp', 'container-image', '0.3.1', '2026-11-19T15:00:00Z', 7, 'pipeline-step', 'succeeded', 'flag', '', null, P.INTPIPE, 'platforms/field-knowledge-mcp/runs/57'],
  ['CP', 'CUSP', 'Contractor Portal site', 'managed-solution', '2.8.1', '2026-11-19T23:40:00Z', 5, 'person-in-app', 'succeeded', 'enforce', 'pm', null, P.MARCUS, null],
  ['IS', 'FOD', 'Inspection Scheduler solution', 'managed-solution', '3.5.0.12', '2026-11-20T14:12:00Z', 8, 'pipeline-step', 'succeeded', 'off', '', null, P.FOPIPE, 'field-ops/inspection-scheduler/runs/1872'],
  ['CSSH', 'CUST', 'self-service-hub', 'static-site', '6.0.2', '2026-11-20T11:45:00Z', 7, 'pipeline-step', 'succeeded', 'flag', '', null, P.CUSTPIPE, 'customer/self-service-hub/runs/1219'],
  ['SO', 'FINT', 'Supplier Onboarding solution', 'managed-solution', '1.0.0-rc.2', '2026-11-21T09:55:00Z', 18, 'pipeline-step', 'partly-succeeded', 'flag', '', 'SO-RC2', P.FINPIPE, 'finance/supplier-onboarding/runs/97'],
  ['IM', 'FINP', 'invoice-matching', 'code-package', '1.12.1', '2026-11-22T12:10:00Z', 6, 'pipeline-step', 'succeeded', 'advise', 'am', null, P.FINPIPE, 'finance/invoice-matching/runs/318'],
  // The 7.1.3 security patch was tested before it reached production.
  ['AR', 'FOT', 'Asset Register solution', 'managed-solution', '7.1.3', '2026-10-29T10:40:00Z', 15, 'pipeline-step', 'succeeded', 'flag', '', null, P.FOPIPE, 'field-ops/asset-register/runs/3312'],
];
const RESULT_STATUS = { succeeded: 'deployment-succeeded', 'partly-succeeded': 'deployment-partly', failed: 'deployment-failed' };
const artifacts = new Map(); // artifactName|version -> id: one row per built version, shared by its deployments
DEPLOYMENTS.forEach(([app, target, artifactName, artifactKind, version, finishedOn, minutes, route, result, gate, flags, change, deployedBy, run], i) => {
  const artifactKey = artifactName + '|' + version;
  if (!artifacts.has(artifactKey)) {
    artifacts.set(artifactKey, add('artifact-version', id(4300 + artifacts.size), {
      businessApplication: A[app].id,
      artifactKind,
      artifactName,
      version,
    }));
  }
  const artifactId = artifacts.get(artifactKey);
  const recordId = id(4500 + i);
  const targetRow = rowById.get(T[target]) ?? rows.find(r => r.id === T[target]);
  add('deployment-record', recordId, clean({
    businessApplication: A[app].id,
    artifactVersion: artifactId,
    deploymentTarget: T[target],
    changeRequest: change ? C[change] : undefined,
    deploymentRoute: route,
    runUrl: run ? 'https://pipelines.example.org/' + run : undefined,
    startedOn: addMinutes(finishedOn, -minutes),
    finishedOn,
    result: RESULT_STATUS[result],
    resultMeaning: result,
    deployedBy,
    gateLevelApplied: gate,
    isOutsideWindow: flags.includes('o'),
    isOtherPath: flags.includes('p'),
    isMissingApproval: flags.includes('m'),
    isAiOnlyCodeApproval: flags.includes('a'),
    idempotencyKey: run ? run + '/' + targetRow.columns.externalId + '/1' : 'recorded-by-person/' + recordId,
  }));
});

// ---------------------------------------------------------------------------------------------------------------
// 11. Document links (5000..), one per required document (Balanced documentation requirements)
// ---------------------------------------------------------------------------------------------------------------

const STAGE_RANK = { idea: 0, 'intake-and-triage': 1, 'prioritize-and-fund': 2, 'design-and-review': 3, 'risk-and-tier': 4, build: 5, test: 6, 'change-and-release': 7, operate: 8, review: 9, retire: 10 };
const REQUIREMENTS = [
  { key: 'wiki-page-all', type: 'wiki-page', tier: null, from: 'build', freshness: 365 },
  { key: 'runbook-tier-2', type: 'runbook', tier: 'tier-2', from: 'change-and-release', freshness: 180 },
  { key: 'runbook-tier-3', type: 'runbook', tier: 'tier-3', from: 'change-and-release', freshness: 180 },
  { key: 'adr-tier-3', type: 'architecture-decision-record', tier: 'tier-3', from: 'design-and-review', freshness: null },
  { key: 'support-guide-tier-3', type: 'support-guide', tier: 'tier-3', from: 'operate', freshness: 365 },
];
const requiredFor = app => REQUIREMENTS.filter(r => (r.tier === null || r.tier === app.tier) && STAGE_RANK[r.from] <= STAGE_RANK[app.stage]);

// The seven documents missing or stale in the story, and one draft. Every other required document is current and was
// checked recently enough to stay current until mid-January 2027.
const DOCUMENT_STORY = {
  'IS:runbook-tier-2': { status: 'stale', checked: '2026-04-10', updated: '2026-03-02' },
  'SFT:wiki-page-all': { status: 'stale', checked: '2025-09-30', updated: '2025-06-18' },
  'LTA:wiki-page-all': { status: 'stale', checked: '2025-06-12', updated: '2025-02-03' },
  'CP:support-guide-tier-3': 'missing',
  'IDS:support-guide-tier-3': 'missing',
  'TRV:wiki-page-all': 'missing',
  'SO:runbook-tier-2': 'missing',
  'SPA:adr-tier-3': { status: 'draft', checked: '2026-11-12', updated: '2026-11-12' },
  'IS:wiki-page-all': { status: 'current', checked: '2026-10-02', updated: '2026-09-28' },
};
const DOCUMENT_STATUS = { current: 'document-current', stale: 'document-stale', draft: 'document-draft' };
const LINK_KIND = { 'wiki-page': 'ado-wiki-page', runbook: 'ado-wiki-page', 'architecture-decision-record': 'repository-file', 'support-guide': 'ado-wiki-page' };
const DOC_PATH = { 'wiki-page': '', runbook: '/runbook', 'architecture-decision-record': '/docs/adr', 'support-guide': '/support-guide' };
const ownerOf = (appKey, role) => OWNERS.find(o => o[0] === appKey && o[1] === role && !o[5]?.holderEnd)?.[2];
let documentSeq = 0;
for (const [appKey, app] of Object.entries(A)) {
  for (const req of requiredFor(app)) {
    const story = DOCUMENT_STORY[appKey + ':' + req.key];
    if (story === 'missing') continue;
    const n = documentSeq;
    const checked = story?.checked ?? addDays('2026-08-03', (n * 7) % 105);
    const updated = story?.updated ?? addDays(checked, -((n * 13) % 40) - 5);
    const status = story?.status ?? 'current';
    const pf = PF[app.pf].slug;
    const base = req.type === 'architecture-decision-record' ? 'https://git.example.org/' + pf + '/' : 'https://wiki.example.org/' + pf + '/';
    add('document-link', id(5000 + documentSeq++), {
      businessApplication: app.id,
      documentationRequirement: req.key,
      documentType: req.type,
      linkKind: LINK_KIND[req.type],
      url: base + slug(app.name) + DOC_PATH[req.type],
      status: DOCUMENT_STATUS[status],
      statusMeaning: status,
      documentOwner: ownerOf(appKey, 'technical-owner') ?? ownerOf(appKey, 'business-owner'),
      lastUpdatedOn: updated,
      lastCheckedOn: checked,
    });
  }
}

// ---------------------------------------------------------------------------------------------------------------
// 12. Reviews (5300..)
// ---------------------------------------------------------------------------------------------------------------

// [app, reviewType, status, dueOn, doneOn, trigger, reviewer, outcomeDecision]
// Overdue on 2026-11-24: Safety Findings Tracker owner check, Travel Requests access check, Contractor Portal access
// check. Due during the demo weeks: Inspection Scheduler owner check (November 30) and access check (December 15),
// Invoice Matching security review after its flagged deployments (December 4), Supplier Onboarding and Warehouse
// Stock Count post-change reviews after their go-live windows (December 9 and 15), Visit Report Agent security review
// (December 10), Field Knowledge MCP Server security review (December 18). The rest are due from mid-January, done,
// or cancelled.
const REVIEWS = [
  ['IS', 'owner-check', 'due', '2026-11-30', null, 'cadence'],
  ['IS', 'access-check', 'due', '2026-12-15', null, 'cadence'],
  ['IS', 'tier-check', 'done', '2026-09-01', '2026-08-28', 'cadence', P.OWEN, 'IS-TIER'],
  ['IS', 'architecture', 'done', '2026-07-01', '2026-06-30', 'request', P.HUGO, 'IS-DESIGN'],
  ['SFT', 'owner-check', 'due', '2026-10-15', null, 'owner-check'],
  ['TRV', 'access-check', 'due', '2026-11-02', null, 'cadence'],
  ['CP', 'access-check', 'due', '2026-11-09', null, 'cadence'],
  ['CP', 'owner-check', 'done', '2026-10-15', '2026-10-12', 'cadence', P.NADIA],
  ['MCP', 'security-review', 'due', '2026-12-18', null, 'request'],
  ['SAB', 'benefit', 'due', '2027-01-20', null, 'cadence'],
  ['PA', 'benefit', 'due', '2027-02-10', null, 'cadence'],
  ['CR', 'tier-check', 'due', '2027-02-04', null, 'cadence'],
  ['CR', 'post-change', 'done', '2026-11-15', '2026-11-13', 'request', P.JONAH],
  ['FDL', 'access-check', 'due', '2027-01-28', null, 'cadence'],
  ['AR', 'owner-check', 'due', '2027-02-08', null, 'cadence'],
  ['AR', 'security-review', 'done', '2026-10-31', '2026-10-20', 'cadence', P.NOOR],
  ['IDS', 'access-check', 'done', '2026-09-30', '2026-09-15', 'cadence', P.ELIF],
  ['PH', 'access-check', 'due', '2027-01-25', null, 'cadence'],
  ['FL', 'tier-check', 'done', '2026-07-15', '2026-07-14', 'cadence', P.DMITRI],
  ['CSSH', 'owner-check', 'due', '2027-03-01', null, 'cadence'],
  ['LTA', 'benefit', 'cancelled', '2026-12-01', null, 'cadence'],
  ['IM', 'security-review', 'due', '2026-12-04', null, 'request'],
  ['SO', 'post-change', 'due', '2026-12-09', null, 'request'],
  ['WSC', 'post-change', 'due', '2026-12-15', null, 'request'],
  ['VRA', 'security-review', 'due', '2026-12-10', null, 'request'],
  ['RP', 'owner-check', 'due', '2027-01-18', null, 'cadence'],
  ['PCA', 'access-check', 'due', '2027-02-17', null, 'cadence'],
  ['TRN', 'owner-check', 'due', '2027-02-12', null, 'cadence'],
  // Quote Builder's architecture review, appended so every review above keeps its ID. Its design decision is accepted
  // (section 16), and the demo director completes the review with it, so the Design gate shows met on the review.
  ['QB', 'architecture', 'due', '2026-11-27', null, 'request'],
];
const REVIEW_STATUS = { due: 'review-due', done: 'review-done', cancelled: 'review-cancelled' };
REVIEWS.forEach(([app, type, status, dueOn, doneOn, trigger, reviewer, outcome], i) => {
  add('review', id(5300 + i), clean({
    businessApplication: A[app].id,
    reviewType: type,
    status: REVIEW_STATUS[status],
    statusMeaning: status,
    trigger,
    dueOn,
    doneOn,
    reviewer,
    outcomeDecision: outcome ? D[outcome] : undefined,
  }));
});

// Next Review On agrees with the application's own reviews: the earliest due review, overdue or not. An application
// with no due review keeps the date given above. (The model says the engine computes it from the review cadences,
// but nothing computes it yet, so Application 360 shows the stored value.)
for (const [key, row] of Object.entries(appRows)) {
  const due = REVIEWS.filter(r => r[0] === key && r[2] === 'due').map(r => r[3]).sort();
  if (due.length) row.columns.nextReviewOn = due[0];
}

// ---------------------------------------------------------------------------------------------------------------
// 13. Delivery links (5500..)
// ---------------------------------------------------------------------------------------------------------------

// A delivery link takes a link kind of the delivery category only (WriteGuardEntities.cs); pipelines are in section 14.
// [app, linkKind, externalId, url, label, linkedOn, lastSeenOn]
const DELIVERY_LINKS = [
  ['IS', 'ado-work-item', '4512', 'https://dev.example.org/field-ops/_workitems/edit/4512', 'Epic: Inspection Scheduler 3.5', '2026-09-07T09:00:00Z', '2026-11-21T08:00:00Z'],
  ['IS', 'ado-work-item', '4519', 'https://dev.example.org/field-ops/_workitems/edit/4519', 'Feature: inspector workload view', '2026-09-14T09:00:00Z', '2026-11-20T14:12:00Z'],
  ['IS', 'planner-plan', 'field-ops-backlog', 'https://plans.example.org/field-ops-backlog', 'Field ops app backlog', '2025-09-01T09:00:00Z', '2026-11-15T09:00:00Z'],
  ['RP', 'ado-work-item', '4630', 'https://dev.example.org/field-ops/_workitems/edit/4630', 'Epic: Route Planner 2.0', '2026-08-17T09:00:00Z', '2026-11-20T10:00:00Z'],
  ['CR', 'ado-work-item', '4588', 'https://dev.example.org/field-ops/_workitems/edit/4588', 'Bug: the shift export breaks after the payroll change', '2026-11-10T05:30:00Z', '2026-11-13T09:00:00Z'],
  ['SO', 'ado-work-item', '3120', 'https://dev.example.org/finance/_workitems/edit/3120', 'Feature: supplier bank detail checks', '2026-05-11T09:00:00Z', '2026-11-21T10:30:00Z'],
  ['SO', 'ado-work-item', '3166', 'https://dev.example.org/finance/_workitems/edit/3166', 'Task: write the go-live runbook', '2026-11-17T10:00:00Z', '2026-11-22T08:00:00Z'],
  ['CP', 'ado-work-item', '2871', 'https://dev.example.org/customer/_workitems/edit/2871', 'Bug: the sign-in page fails on older browsers', '2026-11-19T21:00:00Z', '2026-11-20T10:00:00Z'],
  ['IM', 'ado-work-item', '3237', 'https://dev.example.org/finance/_workitems/edit/3237', 'Bug: duplicate matches after the 1.12 release', '2026-11-18T09:00:00Z', '2026-11-22T12:10:00Z'],
  ['VRA', 'planner-plan', 'visit-report-agent-pilot', 'https://plans.example.org/visit-report-agent-pilot', 'Visit Report Agent pilot plan', '2026-06-01T09:00:00Z', '2026-11-18T09:00:00Z'],
  ['MCP', 'ado-work-item', '912', 'https://dev.example.org/platforms/_workitems/edit/912', 'Feature: safety rules tool for AI clients', '2026-10-06T13:00:00Z', '2026-11-19T15:00:00Z'],
  ['CFB', 'planner-plan', 'customer-feedback-bot-pilot', 'https://plans.example.org/customer-feedback-bot-pilot', 'Customer Feedback Bot pilot', '2026-09-14T09:00:00Z', '2026-11-20T16:00:00Z'],
  ['WSC', 'ado-work-item', '4702', 'https://dev.example.org/field-ops/_workitems/edit/4702', 'Epic: Warehouse Stock Count go-live', '2026-07-06T09:00:00Z', '2026-11-17T11:00:00Z'],
  ['EA', 'ado-work-item', '3204', 'https://dev.example.org/finance/_workitems/edit/3204', 'Feature: match card transactions to receipts', '2026-08-17T09:00:00Z', '2026-11-19T14:00:00Z'],
  ['CSSH', 'ado-work-item', '2915', 'https://dev.example.org/customer/_workitems/edit/2915', 'Feature: holiday opening hours banner', '2026-11-12T09:00:00Z', '2026-11-16T09:00:00Z'],
  ['AR', 'ado-work-item', '4741', 'https://dev.example.org/field-ops/_workitems/edit/4741', 'Feature: Asset Register 7.2 warranty dates', '2026-10-26T09:00:00Z', '2026-11-20T11:00:00Z'],
  ['AR', 'ado-work-item', '4719', 'https://dev.example.org/field-ops/_workitems/edit/4719', 'Task: apply the platform security update 7.1.3', '2026-10-28T09:00:00Z', '2026-11-03T20:00:00Z'],
  ['DB', 'ado-work-item', '1188', 'https://dev.example.org/people/_workitems/edit/1188', 'Feature: move the calendar connector to the vendor\'s new API', '2026-11-18T12:00:00Z', '2026-11-19T08:30:00Z'],
  ['PCA', 'ado-work-item', '4655', 'https://dev.example.org/field-ops/_workitems/edit/4655', 'Feature: supplier stock levels in the part lookup', '2026-11-12T09:00:00Z', '2026-11-19T15:00:00Z'],
];
DELIVERY_LINKS.forEach(([app, linkKind, externalId, url, label, linkedOn, lastSeenOn], i) => {
  add('delivery-link', id(5500 + i), {
    subjectKind: 'business-application',
    subjectApplication: A[app].id,
    linkKind,
    externalId,
    url,
    label,
    linkedOn,
    lastSeenOn,
    isMoved: false,
  });
});

// ---------------------------------------------------------------------------------------------------------------
// 14. Pipeline references (5700..) and pipeline map entries (5720..): the client pipelines that deploy some
//     applications, and the targets each one deploys to, in stage order. The product never runs them (D-19).
// ---------------------------------------------------------------------------------------------------------------

// [linkKind, externalId, url, label, lastSeenOn, [[app, target], ...] in stage order]
const PIPELINES = [
  ['azure-pipelines-pipeline', 'inspection-scheduler-release', 'https://pipelines.example.org/field-ops/inspection-scheduler', 'Inspection Scheduler release', '2026-11-20T14:12:00Z', [['IS', 'FOD'], ['IS', 'FOT'], ['IS', 'FOP']]],
  ['power-platform-pipeline', 'supplier-onboarding-pipeline', 'https://make.example.org/pipelines/supplier-onboarding', 'Supplier Onboarding deployment pipeline', '2026-11-21T09:55:00Z', [['SO', 'FINT'], ['SO', 'FINP']]],
  ['azure-pipelines-pipeline', 'invoice-matching-release', 'https://pipelines.example.org/finance/invoice-matching', 'Invoice Matching build and release', '2026-11-22T12:10:00Z', [['IM', 'FINT'], ['IM', 'FINP']]],
  ['azure-pipelines-pipeline', 'field-knowledge-mcp-release', 'https://pipelines.example.org/platforms/field-knowledge-mcp', 'Field Knowledge MCP Server release', '2026-11-19T15:00:00Z', [['MCP', 'INTT'], ['MCP', 'INTP']]],
  ['azure-pipelines-pipeline', 'self-service-hub-release', 'https://pipelines.example.org/customer/self-service-hub', 'Customer Self-Service Hub release', '2026-11-20T11:45:00Z', [['CSSH', 'CUST'], ['CSSH', 'CUSP']]],
];
let mapSeq = 0;
PIPELINES.forEach(([linkKind, externalId, url, labelAsSeen, lastSeenOn, stages], i) => {
  const pipelineId = add('pipeline-reference', id(5700 + i), { linkKind, externalId, url, labelAsSeen, lastSeenOn, isMoved: false });
  stages.forEach(([app, target], order) => {
    add('pipeline-map-entry', id(5720 + mapSeq++), {
      pipelineReference: pipelineId,
      businessApplication: A[app].id,
      deploymentTarget: T[target],
      stageOrder: order + 1,
    });
  });
});

// ---------------------------------------------------------------------------------------------------------------
// 15. Intake requests (5600..): two old drafts, and the requests behind some early-stage applications. Each one past
//     draft links its triage decision (type intake), as submit_for_approval writes it, accepted by the platform admin.
//     A request in triage is open work, so none is seeded: seed/demo-director.mjs files and submits new ones as the
//     requesters, and the platform admin's triage cards are real.
// ---------------------------------------------------------------------------------------------------------------

const INTAKE_STATUS = { draft: 'intake-draft', submitted: 'intake-submitted', 'in-progress': 'intake-in-progress', completed: 'intake-completed', rejected: 'intake-rejected' };
// [status, requester, title, need, proposedKind, app, horizon, sizeBand, usesAi, createdOn, triage]
//   triage: { proposed } for a triage decision still proposed, or { on, outcome, reason } for one Owen Achterberg accepted
const INTAKE = [
  ['draft', P.PRIYA, 'Vehicle damage photos for insurance claims', 'Drivers need to send damage photos and the location to the insurer from the roadside.', 'power-platform-app', null, 'later', 'small', false, '2026-09-14T10:20:00Z'],
  ['draft', P.TOMAS, 'Canteen menu and pre-orders', 'Depot staff want to see the canteen menu and order lunch before the morning shift.', 'saas', null, 'later', 'small', false, '2026-10-08T15:05:00Z'],
  ['completed', P.INES, 'Track borrowed depot tools', 'Tools go missing because nobody records who borrowed them.', 'power-platform-app', 'TCC', 'next', 'small', false, '2026-10-12T09:00:00Z', { on: '2026-10-19T10:00:00Z', outcome: 'accept', reason: 'A small app on the standard platform, and the depot manager will own it.' }],
  ['in-progress', P.GRETA, 'Mileage claims from the phone', 'Staff type mileage claims by hand and finance checks every route.', 'power-platform-app', 'MC', 'now', 'small', false, '2026-11-02T09:30:00Z', { on: '2026-11-03T09:00:00Z', outcome: 'accept', reason: 'A small tier 1 app for one team, and finance will own it.' }],
  ['in-progress', P.CLARA, 'Supplier rebate tracking', 'Rebates under volume agreements are worked out in spreadsheets and some are never claimed.', 'ai-built-app', 'PRC', 'next', 'medium', true, '2026-11-15T13:00:00Z', { on: '2026-11-16T10:00:00Z', outcome: 'accept', reason: 'Unclaimed rebates are real money, and accounts payable will own the app.' }],
  ['in-progress', P.IRIS, 'Translate customer messages', 'Agents receive customer messages in languages they do not read.', 'code-app', 'TH', 'now', 'medium', true, '2026-11-17T10:00:00Z', { on: '2026-11-18T09:00:00Z', outcome: 'accept', reason: 'Agents lose customers they cannot answer, and customer services will own the app.' }],
  ['rejected', P.SANA, 'Personal task list app', 'A simple app to keep my own task list.', 'power-platform-app', null, 'later', 'small', false, '2026-10-20T11:00:00Z', { on: '2026-10-22T14:00:00Z', outcome: 'decline', reason: 'The standard task app every person already has covers a personal task list.' }],
];
INTAKE.forEach(([status, requester, title, need, proposedKind, app, horizon, sizeBand, usesAi, createdOn, triage], i) => {
  const intakeId = id(5600 + i);
  if (status !== 'draft' && !triage) throw new Error('The intake request ' + title + ' is past draft but has no triage decision');
  const statement = 'Accept the intake request: ' + title;
  const triageDecision = !triage ? undefined : triage.proposed
    ? decision('IR-' + i, { app, intake: intakeId, type: 'intake', status: 'proposed', on: triage.proposed, statement })
    : decision('IR-' + i, { app, intake: intakeId, type: 'intake', outcome: triage.outcome, status: 'accepted', by: P.OWEN, on: triage.on, statement, reason: triage.reason });
  add('intake-request', intakeId, clean({
    recordNumber: 'IR-' + String(101 + i).padStart(4, '0'),
    requester,
    title,
    need,
    proposedKind,
    businessApplication: app ? A[app].id : undefined,
    status: INTAKE_STATUS[status],
    statusMeaning: status,
    horizon,
    sizeBand,
    usesAi,
    createdOn,
    triageDecision,
  }));
});

// ---------------------------------------------------------------------------------------------------------------
// 16. Facts for the wave 2 screens (decisions after the intake decisions, change 4231, approval records 5800..).
//     Appended after every row above, so each earlier row keeps its ID and record number. The demo director builds
//     on them: it closes the removal change, completes the Quote Builder review and raises the vanished claim.
// ---------------------------------------------------------------------------------------------------------------

// The deployment record of an application's version, by its place in DEPLOYMENTS.
const deploymentOf = (app, version) => {
  const i = DEPLOYMENTS.findIndex(d => d[0] === app && d[4] === version);
  if (i < 0) throw new Error('No deployment ' + version + ' of ' + app);
  return id(4500 + i);
};

// Legacy Timesheet App on its way out (P09, the Retirement tracker). The portfolio decision to replace it (LTA-PORT)
// is accepted, the data owner's retention decision is made, and the removal change is authorized and done in its
// window. The director closes the change with its evidence; the gap check it runs no longer sees the Timesheets canvas
// app, so a vanished claim waits; the platform admin proposes retirement complete in the live demo.
decision('LTA-DATA', {
  app: 'LTA', type: 'data-use', outcome: 'retain', status: 'accepted', by: P.ROSA, on: '2026-07-01T10:00:00Z',
  statement: 'Keep the timesheet entries read-only until the end of March 2027, then archive them to the HR records store.',
  context: 'People Hub holds every new time entry. Payroll audits can still ask for entries made in the old app.',
  reason: 'The HR records store keeps entries for seven years, as payroll audits need, and the old app can then be removed.',
});
const LATER_CHANGES = [
  ['LTA-REMOVE', { app: 'LTA', title: 'Remove the Timesheets canvas app', type: 'normal', status: 'in-progress', start: '2026-11-17T18:00:00Z', end: '2026-11-17T19:00:00Z', target: 'PPL', created: '2026-11-10T10:00:00Z', auth: { on: '2026-11-12T10:00:00Z', by: P.ROSA, reason: 'The entries are read-only in the HR records store, and nobody has opened the app since June.' }, description: 'Turns off and deletes the Timesheets canvas app in People Prod. The entries stay in the HR records store.' }],
];
LATER_CHANGES.forEach(([key, c], i) => {
  const n = CHANGES.length + i;
  C[key] = id(4200 + n);
  const authorizationDecision = decision(key + '-AUTH', {
    app: c.app, change: C[key], type: 'change-authorization', outcome: 'authorize', status: 'accepted', by: c.auth.by, on: c.auth.on,
    statement: 'Authorize "' + c.title + '" in the ' + monthDay(c.start) + ' window.', reason: c.auth.reason,
  });
  add('change-request', C[key], clean({
    recordNumber: 'CHG-' + String(201 + n).padStart(4, '0'),
    title: c.title,
    description: c.description,
    businessApplication: A[c.app].id,
    changeType: c.type,
    status: CHANGE_STATUS[c.status],
    statusMeaning: c.status,
    mode: 'built-in',
    windowStart: c.start,
    windowEnd: c.end,
    plannedTarget: T[c.target],
    authorizationDecision,
    createdOn: c.created,
  }));
});

// Quote Builder's design decision, accepted by the architect. It is the outcome the director completes the
// architecture review with (section 12), so the review shows the Design gate met.
decision('QB-DESIGN', {
  app: 'QB', type: 'design', outcome: 'approve', status: 'accepted', by: P.ELIF, on: '2026-11-20T15:00:00Z',
  statement: 'Build Quote Builder as a code app that reads prices from Parts Catalog API and keeps no copy of them.',
  context: 'Quote Builder needs current part prices and labor rates. Parts Catalog API already serves the prices to other apps.',
  options: [
    { option: 'Copy the price list into the app every night.', whyNot: 'A second copy of prices that drifts from the catalog.' },
    { option: 'A canvas app on the default environment.', whyNot: 'Quotes go to customers, so the app needs the pro build path and a release pipeline.' },
  ],
  reason: 'One source of prices keeps every quote right, and the catalog API is already tested and owned.',
});

// Standing exceptions for the exceptions register (P04 step 6): each sets a standard aside for one flagged deployment,
// with the date it is reviewed again. Contractor Portal's review is ahead; Travel Requests' is overdue on 2026-11-24.
// The architect decides an exception; an exception about a normal change is the change board's (Balanced).
decision('CP-EXC-281', {
  app: 'CP', deployment: deploymentOf('CP', '2.8.1'), type: 'exception', outcome: 'grant', status: 'accepted', by: P.ELIF, on: '2026-11-21T09:30:00Z',
  statement: 'Accept the sign-in fix made directly in the live portal on November 19.',
  context: 'Contractors could not sign in on older browsers. The fix was made in the live portal that night, outside the release pipeline and with no change request.',
  options: [
    { option: 'Roll the fix back until it comes through the pipeline.', whyNot: 'Contractors would be locked out again.' },
    { option: 'Grant the exception until the pipeline release of December 4.', whyNot: 'Chosen.' },
  ],
  reason: 'Contractors needed to sign in the next morning, and the change "Contractor Portal sign-in page update" brings the fix into the pipeline.',
  exception: { standard: 'Every change reaches Customer Prod through the release pipeline.', reviewOn: '2026-12-18' },
});
decision('TRV-EXC-304', {
  app: 'TRV', deployment: deploymentOf('TRV', '3.0.4'), type: 'exception', outcome: 'grant', status: 'accepted', by: P.TOMAS, on: '2026-11-09T11:00:00Z',
  statement: 'Accept the daily allowance rates loaded by hand in the live app on November 6.',
  context: 'The new daily allowance rates applied from November 7. The release pipeline for People Prod was down, so the rates were loaded in the live app.',
  options: [
    { option: 'Wait for the pipeline and pay the old rates.', whyNot: 'Staff would be paid the wrong allowance.' },
    { option: 'Grant the exception and rebuild the release in the pipeline.', whyNot: 'Chosen.' },
  ],
  reason: 'The rates are data, not code, and the pipeline release repeats them once it runs again.',
  exception: { standard: 'Every change reaches People Prod through the release pipeline.', reviewOn: '2026-11-23' },
});

// Two answers on the Invoice Matching 1.12 authorization, as the approval runner recorded them from Teams and Outlook:
// the Finance Approvals Agent's answer is stored as a recommendation only (AI never approves), and Clara Voss's answer,
// a person's, is the one that counts. They show on the AI activity page and in the Invoice Matching evidence pack.
const im112 = authorizationOf['IM-112'];
const approvalRecord = (n, columns) => add('approval-record', id(5800 + n), clean({
  decision: im112,
  approvalRule: 'change-auth-owner',
  requiredOwnerRole: 'business-owner',
  askedPrincipal: P.CLARA,
  isConsultation: false,
  requestedOn: '2026-11-10T09:05:00Z',
  expiresOn: '2026-11-15T09:05:00Z',
  ...columns,
}));
approvalRecord(0, {
  approver: P.FINAGENT, approverActorKind: 'agent-identity', approverActorMeaning: 'agent-identity', approverPrincipalKind: 'agent-identity',
  outcome: 'approval-recommendation', outcomeMeaning: 'recommendation-only', countsTowardRequirement: false, approverHoldsRequiredRole: false,
  typedReason: 'The release notes match the work items, and the tests passed.', respondedOn: '2026-11-11T16:40:00Z', channel: 'teams',
  flowRunUrl: 'https://runner.example.org/runs/approvals/7311',
});
approvalRecord(1, {
  approver: P.CLARA, approverActorKind: 'person', approverActorMeaning: 'person', approverPrincipalKind: 'user',
  outcome: 'approval-approved', outcomeMeaning: 'approved', countsTowardRequirement: true, approverHoldsRequiredRole: true,
  respondedOn: '2026-11-12T11:30:00Z', channel: 'outlook', flowRunUrl: 'https://runner.example.org/runs/approvals/7342',
});

// ---------------------------------------------------------------------------------------------------------------
// 17. The data taxonomy (D-37; docs/architecture/data-taxonomy.md; slice 5). Appended after every row above, so
//     each earlier row keeps its ID and record number. Ownership decisions continue the decision numbers, owner
//     assignments continue at 2000 + n, locations take 6000.., mappings 6100.., data uses 6200.. (DU-0001..) and the
//     tier assessment 6300. The 25-row domain tree is configuration, not rows: this script writes it, with the one
//     Fabric domain match, into the draftOperations of seed/demo-binding.patch.json (the demo configuration patch), so
//     the seed command compiles it into version 1.
//     Open work stays live through seed/demo-director.mjs: the proposed data-use decisions (the 9:30 card waiting for
//     Kai Ostrowski and Noor Safadi among them), the copy decision, the retention decision, Claude Code's draft use and
//     draft mapping, and its refused never-rule use. The choices follow D-41.
// ---------------------------------------------------------------------------------------------------------------

// The domain tree (25 rows). [key, label, parent, level, classification, rules, retention, examples]
// Levels: domain, subdomain, data-set (Balanced). An inherited value is an absent value here.
const DOMAINS = [
  ['customers', 'Customers', null, 'domain', 'confidential', null, 'keep-7-years', 'Who the customers are and how the business serves them: accounts, contacts, orders, cases and feedback.'],
  ['client-accounts', 'Client accounts and contacts', 'customers', 'subdomain', null, null, null, 'Customer organizations, their sites, and the named people to contact at each one.'],
  ['client-orders', 'Client orders', 'customers', 'subdomain', null, null, null, 'Orders customers place for parts and repair work, with their status and history.'],
  ['client-cases', 'Client cases and feedback', 'customers', 'subdomain', null, null, null, 'Faults and damage claims customers report, complaints, and what customers say about finished work.'],
  ['commercial', 'Commercial', null, 'domain', 'confidential', null, null, 'How the business sells: proposals, quotes and contracts.'],
  ['proposals', 'Proposals and quotes', 'commercial', 'subdomain', null, null, null, 'Opportunities, quotes and bids for repair work, with their prices and status.'],
  ['contracts', 'Contracts', 'commercial', 'subdomain', null, null, 'keep-10-years', 'Signed agreements with clients and suppliers: terms, prices, liability and renewal dates.'],
  ['client-contracts', 'Client contracts', 'contracts', 'data-set', null, null, null, 'Service agreements signed with clients and contractors.'],
  ['supplier-contracts', 'Supplier contracts', 'contracts', 'data-set', null, null, null, 'Agreements with suppliers: payment terms, volume rebates, liability and renewal clauses.'],
  ['people', 'People', null, 'domain', 'confidential', null, 'keep-7-years', 'Everyone who works for the business: personnel records, pay and time. Kept for seven years after a person leaves.'],
  ['personnel-records', 'Personnel records', 'people', 'subdomain', 'restricted', null, null, 'Workers, positions, managers, training and certificates.'],
  ['payroll', 'Payroll', 'people', 'subdomain', 'restricted', { aiUseRule: 'never' }, 'keep-10-years', 'Salary, deductions, tax codes and the bank details pay goes to.'],
  ['paychecks', 'Paychecks', 'payroll', 'data-set', null, null, null, 'Each pay run: gross pay, deductions, net pay and mileage or expense payments.'],
  ['bank-details', 'Bank details', 'payroll', 'data-set', null, null, null, 'The bank account each person is paid into.'],
  ['timesheets', 'Timesheets', 'people', 'subdomain', null, null, null, 'Hours worked per job and day, and their approvals.'],
  ['field-operations', 'Field operations', null, 'domain', 'internal', null, 'keep-3-years', 'The work done at field sites: inspections, safety findings, crews and assets.'],
  ['inspections', 'Inspections', 'field-operations', 'subdomain', null, null, null, 'Site inspections, their checklists, notes and photos.'],
  ['safety-findings', 'Safety findings', 'field-operations', 'subdomain', 'restricted', null, null, 'Hazards found at sites until they are fixed, which can hold injury details.'],
  ['crew-schedules', 'Crew schedules', 'field-operations', 'subdomain', null, null, null, 'Crew shifts, members and their skills.'],
  ['assets', 'Assets and equipment', 'field-operations', 'subdomain', null, null, null, 'Field assets with their location, warranty and maintenance history.'],
  ['warehouse', 'Warehouse', null, 'domain', 'internal', null, null, 'What the stores hold and what parts cost.'],
  ['stock-levels', 'Stock levels', 'warehouse', 'subdomain', null, null, null, 'Stock per store and part, and the counts that check it.'],
  ['parts-catalog', 'Parts catalog', 'warehouse', 'subdomain', null, null, null, 'Every spare part with its supplier and price.'],
  ['workplace', 'Workplace', null, 'domain', 'internal', null, null, 'Offices, desks, rooms and parking.'],
  ['visitor-log', 'Visitor log', 'workplace', 'subdomain', 'confidential', null, null, 'Who visited which office, when, and whom they came to see.'],
];
const DOMAIN_KEYS = new Set(DOMAINS.map(d => d[0]));
const domainLabel = key => DOMAINS.find(d => d[0] === key)[1];
const domainPath = key => {
  const parts = [];
  for (let k = key; k; k = DOMAINS.find(d => d[0] === k)[2]) parts.unshift(domainLabel(k));
  return parts.join(' / ');
};

// The demo configuration patch: the tree and People's match to the Fabric domain "HR" (a C11 row of vocabulary
// fabric-domain, marked W in the design: a working-set fact, never in a shipped preset).
const configurationOperations = [
  ...DOMAINS.map(([key, label, parent, level, classification, rules, retention, examples], i) => ({
    op: 'upsert',
    entity: 'data-domain',
    key,
    values: clean({ label, parentKey: parent, levelKey: level, classificationKey: classification, ...(rules ?? {}), retentionClassKey: retention, examples, sortOrder: i + 1 }),
  })),
  {
    op: 'upsert',
    entity: 'vocabulary-mapping',
    key: 'people-fabric-domain',
    values: {
      label: 'People as a Fabric domain',
      sourceEntity: 'data-domain',
      sourceKey: 'people',
      vocabularyKey: 'fabric-domain',
      term: 'HR',
      sourceUrl: 'https://app.fabric.example.org/domains/hr',
      checkedOn: '2026-11-20',
    },
  },
];

// Locations: 16 top-level and 6 nested. [key, label, platform, parent, stage, target, hostedBy,
// linkKind, externalId, url, lastSeenOn, technical owner, deputy]. Every technical owner has a deputy, because Balanced's
// technical-owner role needs one (DC-03); PAYR01 has none on purpose (DC-12). TIMESQL01 was last seen in May (DC-12).
const SEEN = '2026-11-20T06:00:00Z';
const LOCATIONS = [
  ['FOP', 'Field Ops Prod', 'dataverse', null, 'production', T.FOP, null, 'dataverse-environment', 'field-ops-prod', 'https://make.example.org/environments/field-ops-prod', SEEN, P.SANA, P.JONAH],
  ['FOT', 'Field Ops Test', 'dataverse', null, 'test', T.FOT, null, 'dataverse-environment', 'field-ops-test', 'https://make.example.org/environments/field-ops-test', SEEN, P.SANA, P.JONAH],
  ['CUSP', 'Customer Prod', 'dataverse', null, 'production', T.CUSP, null, 'dataverse-environment', 'customer-prod', 'https://make.example.org/environments/customer-prod', SEEN, P.MARCUS, P.PRIYA],
  ['CUST', 'Customer Test', 'dataverse', null, 'test', T.CUST, null, 'dataverse-environment', 'customer-test', 'https://make.example.org/environments/customer-test', SEEN, P.MARCUS, P.PRIYA],
  ['FINP', 'Finance Prod', 'dataverse', null, 'production', T.FINP, null, 'dataverse-environment', 'finance-prod', 'https://make.example.org/environments/finance-prod', SEEN, P.DMITRI, P.YUSUF],
  ['FINT', 'Finance Test', 'dataverse', null, 'test', T.FINT, null, 'dataverse-environment', 'finance-test', 'https://make.example.org/environments/finance-test', SEEN, P.DMITRI, P.YUSUF],
  ['PPL', 'People Prod', 'dataverse', null, 'production', T.PPL, null, 'dataverse-environment', 'people-prod', 'https://make.example.org/environments/people-prod', SEEN, P.FELIX, P.THEO],
  ['SFDC', 'Sales CRM production org', 'salesforce', null, 'production', null, null, 'external-data-system', '00D000000000001', 'https://sales.example.org/orgs/00D000000000001', SEEN, P.THEO, P.MARCUS],
  ['CLIB', 'Contracts library', 'sharepoint', null, 'production', null, null, 'sharepoint-location', 'finance-site/contracts-library', 'https://sites.example.org/finance/contracts-library', SEEN, P.DMITRI, P.YUSUF],
  ['COSMOS', 'cosmos-orders-prod', 'cosmos-db', null, 'production', T.INTP, null, 'azure-data-resource', 'integration-prod/cosmos-orders-prod', 'https://portal.example.org/platforms/cosmos-orders-prod', SEEN, P.YUSUF, P.JONAH],
  ['ORDERS', 'orders', 'cosmos-db', 'COSMOS', null, null, null, 'azure-data-resource', 'integration-prod/cosmos-orders-prod/orders', 'https://portal.example.org/platforms/cosmos-orders-prod/databases/orders', SEEN],
  ['SQLOPS', 'sql-ops-prod', 'azure-sql', null, 'production', T.INTP, null, 'azure-data-resource', 'integration-prod/sql-ops-prod', 'https://portal.example.org/platforms/sql-ops-prod', SEEN, P.JONAH, P.YUSUF],
  ['WAREHOUSE', 'warehouse', 'azure-sql', 'SQLOPS', null, null, null, 'azure-data-resource', 'integration-prod/sql-ops-prod/warehouse', 'https://portal.example.org/platforms/sql-ops-prod/databases/warehouse', SEEN],
  ['QUOTES', 'quotes', 'azure-sql', 'SQLOPS', null, null, null, 'azure-data-resource', 'integration-prod/sql-ops-prod/quotes', 'https://portal.example.org/platforms/sql-ops-prod/databases/quotes', SEEN],
  ['TIMESQL', 'TIMESQL01', 'sql-server', null, 'production', null, null, 'external-data-system', 'timesql01.corp.example.org', 'https://servers.example.org/timesql01', '2026-05-02T06:00:00Z', P.FELIX, P.THEO],
  ['TIMESHEETS', 'Timesheets', 'sql-server', 'TIMESQL', null, null, 'LTA', 'external-data-system', 'timesql01.corp.example.org/Timesheets', 'https://servers.example.org/timesql01/databases/timesheets', '2026-05-02T06:00:00Z'],
  ['PAYR01', 'PAYR01', 'ibm-i', null, 'production', null, null, 'external-data-system', 'payr01.corp.example.org', 'https://servers.example.org/payr01', '2026-11-18T06:00:00Z'],
  ['PAYLIB', 'PAYLIB', 'ibm-i', 'PAYR01', null, null, null, 'external-data-system', 'payr01.corp.example.org/PAYLIB', 'https://servers.example.org/payr01/libraries/paylib', '2026-11-18T06:00:00Z'],
  ['PHT', 'People Hub tenant', 'saas-application', null, 'production', T.PHT, 'PH', 'external-data-system', 'peoplehub-tenant-0042', 'https://peoplehub.example.org/tenants/0042', SEEN, P.FELIX, P.THEO],
  ['DBT', 'Desk Booking tenant', 'saas-application', null, 'production', T.DBT, 'DB', 'external-data-system', 'deskbooking-tenant-0117', 'https://deskbooking.example.org/tenants/0117', SEEN, P.PLATTEAM, P.SERVDESK],
  ['FDW', 'Field Data workspace', 'fabric-lakehouse', null, 'production', null, null, 'fabric-item', 'fabric-ws-field-data', 'https://app.fabric.example.org/groups/fabric-ws-field-data', SEEN, P.OWEN, P.PRIYA],
  ['FDLH', 'Field Data Lake', 'fabric-lakehouse', 'FDW', null, null, 'FDL', 'fabric-item', 'fabric-ws-field-data/field-data-lake', 'https://app.fabric.example.org/groups/fabric-ws-field-data/lakehouses/field-data-lake', SEEN],
];
const L = {};
LOCATIONS.forEach(([key], i) => { L[key] = id(6000 + i); });
LOCATIONS.forEach(([key, label, platform, parent, stage, target, hostedBy, linkKind, externalId, url, lastSeenOn], i) => {
  add('data-location', L[key], clean({
    dataPlatform: platform,
    parentLocation: parent ? L[parent] : undefined,
    targetStage: stage,
    deploymentTarget: target,
    hostedByApplication: hostedBy ? A[hostedBy].id : undefined,
    linkKind,
    externalId,
    url,
    labelAsSeen: label,
    lastSeenOn,
    status: 'data-location-registered',
    statusMeaning: 'registered',
  }));
});
const locationLabel = key => LOCATIONS.find(l => l[0] === key)[1];

// Ownership decisions and owner assignments on domains and locations (docs/architecture/data-taxonomy.md, Owners at any
// level). Every assignment carries its accepted ownership decision. A top-level domain's appointment is the data
// governance lead's to accept, a subdomain's the parent domain's data owner's (Balanced; the same page, Appointing owners); Hana Lindqvist leads data governance and owns People, so the
// platform admin accepted People's. Contracts has no owner above it (Commercial has none), so the data governance lead
// accepted it. The tree was set up in October 2026, before Amara Quist left. The platform admin accepts a location's
// technical owner.
// [domain, owner, deputy, steward, decider, decided on (moment)]
const DOMAIN_OWNERS = [
  ['customers', P.NADIA, P.WREN, P.IRIS, P.HANA, '2026-10-12T10:00:00Z'],
  ['client-orders', P.WREN, null, null, P.NADIA, '2026-10-13T10:00:00Z'],
  ['contracts', P.CLARA, P.GRETA, null, P.HANA, '2026-10-13T11:00:00Z'],
  ['people', P.HANA, P.ROSA, null, P.OWEN, '2026-10-12T11:00:00Z'],
  ['payroll', P.GRETA, null, null, P.HANA, '2026-10-13T12:00:00Z'],
  ['field-operations', P.INES, P.RAVI, P.LENA, P.HANA, '2026-10-12T12:00:00Z'],
  ['inspections', P.MATEO, P.RAVI, null, P.INES, '2026-10-13T13:00:00Z'],
  ['safety-findings', P.KAI, P.RAVI, null, P.INES, '2026-10-13T14:00:00Z'],
  ['assets', P.RAVI, P.INES, null, P.INES, '2026-10-13T15:00:00Z'],
  ['warehouse', P.TOMAS, P.AMARA, null, P.HANA, '2026-10-12T13:00:00Z'],
  ['workplace', P.HUGO, P.LENA, null, P.HANA, '2026-10-12T14:00:00Z'],
];
const personName = objectId => rows.find(r => r.entity === 'principal-reference' && r.columns.entraObjectId === objectId).columns.displayLabel;
// Assigns an owner role on a domain or a location: the holder, the deputy (when named), each starting on the day the
// ownership decision was accepted.
function subjectOwner(subjectColumns, role, holder, deputy, startOn, ownershipDecision) {
  const holderRowId = nextOwnerId();
  add('owner-assignment', holderRowId, { ...subjectColumns, ownerRole: role, principal: holder, isDeputy: false, startOn, ownershipDecision });
  if (deputy) add('owner-assignment', nextOwnerId(), { ...subjectColumns, ownerRole: role, principal: deputy, isDeputy: true, deputyFor: holderRowId, startOn, ownershipDecision });
}
for (const [domain, owner, deputy, steward, by, on] of DOMAIN_OWNERS) {
  const path = domainPath(domain);
  const names = personName(owner) + ' data owner of ' + path + (deputy ? ', with ' + personName(deputy) + ' as deputy' : '') + (steward ? ', and ' + personName(steward) + ' as data steward' : '');
  const decisionId = decision('OWN-' + domain, {
    subject: { kind: 'data-domain', domain }, type: 'ownership', outcome: 'assign', status: 'accepted', by, on,
    statement: 'Appoint ' + names + '.',
    context: 'The data domain tree was set up in October 2026, and each domain with data of its own needs a person who answers for it.',
    reason: personName(owner) + ' decides how ' + path + ' data is used in daily work' + (deputy ? ', and ' + personName(deputy) + ' covers when they are away' : '') + '.',
  });
  const start = on.slice(0, 10);
  const subjectColumns = { subjectKind: 'data-domain', subjectDataDomain: domain };
  subjectOwner(subjectColumns, 'data-owner', owner, deputy, start, decisionId);
  if (steward) subjectOwner(subjectColumns, 'data-steward', steward, null, start, decisionId);
}
for (const [key, label, , , , , , , , , , owner, deputy] of LOCATIONS) {
  if (!owner) continue;
  const decisionId = decision('OWN-' + key, {
    subject: { kind: 'data-location', location: L[key] }, type: 'ownership', outcome: 'assign', status: 'accepted', by: P.OWEN, on: '2026-10-14T10:00:00Z',
    statement: 'Name ' + personName(owner) + ' technical owner of ' + label + ', with ' + personName(deputy) + ' as deputy.',
    context: 'Each place that holds business data needs a technical owner who keeps it running and confirms what it holds.',
    reason: personName(owner) + ' runs ' + label + ' day to day, and ' + personName(deputy) + ' covers when they are away.',
  });
  subjectOwner({ subjectKind: 'data-location', subjectDataLocation: L[key] }, 'technical-owner', owner, deputy, '2026-10-14', decisionId);
}

// Mappings (28 rows): [key, domain, location, origin, containers, copied from]. Customer Test holds an unmasked production copy of client accounts, which breaks the copy
// rule (DC-08); Finance Test's masked copy of supplier contracts needs a copy decision, which the director proposes.
const MAPPINGS = [
  ['ACC-SFDC', 'client-accounts', 'SFDC', 'system-of-record', ['Account', 'Contact']],
  ['ACC-CUSP', 'client-accounts', 'CUSP', 'replica', ['account', 'contact'], 'ACC-SFDC'],
  ['ACC-CUST', 'client-accounts', 'CUST', 'production-copy', ['account', 'contact'], 'ACC-CUSP'],
  ['ORD-ORDERS', 'client-orders', 'ORDERS', 'system-of-record', ['orders', 'orderEvents']],
  ['CASE-CUSP', 'client-cases', 'CUSP', 'system-of-record', ['incident', 'feedback']],
  ['PROP-SFDC', 'proposals', 'SFDC', 'system-of-record', ['Opportunity', 'Quote']],
  ['CCON-CLIB', 'client-contracts', 'CLIB', 'system-of-record', ['Client']],
  ['SCON-CLIB', 'supplier-contracts', 'CLIB', 'system-of-record', ['Supplier']],
  ['SCON-FINP', 'supplier-contracts', 'FINP', 'replica', ['supplier_contract'], 'SCON-CLIB'],
  ['SCON-FINT', 'supplier-contracts', 'FINT', 'masked-copy', ['supplier_contract'], 'SCON-FINP'],
  ['PERS-PHT', 'personnel-records', 'PHT', 'system-of-record', ['Worker', 'Position']],
  ['PERS-PPL', 'personnel-records', 'PPL', 'replica', ['hr_trainingrecord'], 'PERS-PHT'],
  ['PAY-PAYLIB', 'paychecks', 'PAYLIB', 'system-of-record', ['PAYMAST', 'PAYHIST']],
  ['BANK-PAYLIB', 'bank-details', 'PAYLIB', 'system-of-record', ['BANKDTL']],
  ['TIME-TIMESHEETS', 'timesheets', 'TIMESHEETS', 'system-of-record', ['dbo.TimeEntry', 'dbo.TimeApproval']],
  ['TIME-PHT', 'timesheets', 'PHT', 'replica', ['Time Tracking'], 'TIME-TIMESHEETS'],
  ['INSP-FOP', 'inspections', 'FOP', 'system-of-record', ['fo_inspection']],
  ['INSP-FOT', 'inspections', 'FOT', 'synthetic', ['fo_inspection']],
  ['INSP-FDLH', 'inspections', 'FDLH', 'replica', ['inspections'], 'INSP-FOP'],
  ['SAFE-FOP', 'safety-findings', 'FOP', 'system-of-record', ['fo_safetyfinding']],
  ['SAFE-FDLH', 'safety-findings', 'FDLH', 'replica', ['safety_findings'], 'SAFE-FOP'],
  ['CREW-FOP', 'crew-schedules', 'FOP', 'system-of-record', ['fo_crewshift', 'fo_crewmember']],
  ['ASSET-FOP', 'assets', 'FOP', 'system-of-record', ['fo_asset']],
  ['ASSET-FDLH', 'assets', 'FDLH', 'replica', ['assets'], 'ASSET-FOP'],
  ['STOCK-WAREHOUSE', 'stock-levels', 'WAREHOUSE', 'system-of-record', ['stock.Level', 'stock.Count']],
  ['PARTS-WAREHOUSE', 'parts-catalog', 'WAREHOUSE', 'system-of-record', ['parts.Part', 'parts.Price']],
  ['WORK-DBT', 'workplace', 'DBT', 'system-of-record', ['Bookings', 'Rooms']],
  ['VIS-PPL', 'visitor-log', 'PPL', 'system-of-record', ['vis_visit']],
];
const M = {};
MAPPINGS.forEach(([key], i) => { M[key] = id(6100 + i); });
MAPPINGS.forEach(([key, domain, location, origin, containers, copiedFrom]) => {
  add('data-mapping', M[key], clean({
    dataDomain: domain,
    dataLocation: L[location],
    containers,
    dataOrigin: origin,
    copiedFrom: copiedFrom ? M[copiedFrom] : undefined,
    status: 'data-mapping-registered',
    statusMeaning: 'registered',
    confirmedOn: '2026-11-16',
    startOn: '2026-10-19',
  }));
});

// Data uses: [app, domain, location, access, AI, purpose, allowed by { by, on, reason } or a reused
// decision key]. Confirmed by a person (registered). Left out: Expense Assistant's AI use of bank details, which is
// refused when it is saved (G-25), and Mileage Claims' draft, which Claude Code drafts live through the director.
const AI_SERVICE = 'The approved AI model, in the region';
const USES = [
  ['IS', 'inspections', 'FOP', 'read-write', false, 'Plans inspections and records their checklists and findings.'],
  ['IS', 'client-accounts', 'CUSP', 'read', false, 'Reads the site contacts, so inspectors can call ahead of a visit.', { by: P.NADIA, on: '2026-03-10T10:00:00Z', reason: 'Inspectors need a name and number for each site, and the app shows only the site contact.' }],
  ['VRA', 'inspections', 'FOP', 'read', true, 'Drafts visit reports from inspector notes and photos.', 'VRA-DATA'],
  ['VRA', 'safety-findings', 'FOP', 'read', true, 'Adds the open safety findings of the site to the draft visit report.'],
  ['SFT', 'safety-findings', 'FOP', 'read-write', false, 'Lists and closes safety findings from inspections.', { by: P.KAI, on: '2025-04-01T10:00:00Z', reason: 'The tracker is where the safety team works the findings, and only the team can open it.' }],
  ['AR', 'assets', 'FOP', 'read-write', false, 'Keeps every field asset with its warranty and inspection history.'],
  ['MCP', 'assets', 'FDLH', 'read', true, 'Answers AI clients\' questions about field assets from the lake.'],
  ['MCP', 'safety-findings', 'FDLH', 'read', true, 'Answers AI clients\' questions about open safety findings.'],
  ['CR', 'crew-schedules', 'FOP', 'read-write', false, 'Keeps crew shifts, members and their skills.'],
  ['CNS', 'client-accounts', 'CUSP', 'read', false, 'Reads customer contacts to send planned work notices.'],
  ['CSSH', 'client-cases', 'CUSP', 'read-write', false, 'Lets customers report faults and follow their cases.', { by: P.NADIA, on: '2025-01-20T10:00:00Z', reason: 'Customers see only their own cases, and the hub signs them in.' }],
  ['COSA', 'client-orders', 'ORDERS', 'read', false, 'Gives partners and the hub the live status of customer orders.', { by: P.WREN, on: '2026-10-01T10:00:00Z', reason: 'The API returns the status only, never the customer\'s details.' }],
  ['CTA', 'client-cases', 'CUSP', 'read', true, 'Reads damage claims and routes each one with a summary.'],
  ['QB', 'proposals', 'QUOTES', 'write', false, 'Saves customer quotes for repair work.'],
  ['CCF', 'supplier-contracts', 'CLIB', 'read', true, 'Finds and summarizes clauses in supplier contracts.', 'CCF-DATA'],
  ['CP', 'client-contracts', 'CLIB', 'read', false, 'Shows contractors the agreement that covers their jobs.', { by: P.CLARA, on: '2024-10-15T10:00:00Z', reason: 'Each contractor sees only their own agreement.' }],
  ['IM', 'supplier-contracts', 'FINP', 'read', false, 'Reads supplier payment terms to match invoices.', { by: P.CLARA, on: '2025-10-28T10:00:00Z', reason: 'Matching needs the agreed prices and terms, and nothing leaves Finance Prod.' }],
  ['SO', 'supplier-contracts', 'FINP', 'write', false, 'Records the agreement of each new supplier.', { by: P.CLARA, on: '2026-05-01T10:00:00Z', reason: 'Finance enters each agreement once, where invoices are matched.' }],
  ['FL', 'paychecks', 'PAYLIB', 'read', false, 'Posts each pay run to the general ledger.', { by: P.GRETA, on: '2023-06-30T10:00:00Z', reason: 'The ledger takes the totals of each pay run, not each person\'s pay.' }],
  ['PH', 'personnel-records', 'PHT', 'read-write', false, 'Holds people records, positions and managers.', { by: P.HANA, on: '2024-01-02T10:00:00Z', reason: 'People Hub is the system of record for personnel data.' }],
  ['PH', 'timesheets', 'PHT', 'read-write', false, 'Takes time entry from the old timesheet app.', { by: P.HANA, on: '2024-01-02T11:00:00Z', reason: 'Time entry moves into People Hub, where people records already are.' }],
  ['IDS', 'personnel-records', 'PHT', 'read', false, 'Copies people, teams and manager lines to the directory.', { by: P.HANA, on: '2024-02-26T10:00:00Z', reason: 'The directory needs names, teams and managers, and the sync leaves out everything else.' }],
  ['TRN', 'personnel-records', 'PPL', 'write', false, 'Records completed training and certificates.', { by: P.HANA, on: '2024-02-05T10:00:00Z', reason: 'Training records belong with the personnel record, and HR checks every certificate.' }],
  ['OBA', 'personnel-records', 'PHT', 'read', true, 'Answers new starters\' questions with their manager, team and training plan.'],
  ['LTA', 'timesheets', 'TIMESHEETS', 'write', false, 'Recorded time entries until People Hub took over time entry.', { by: P.HANA, on: '2022-09-01T10:00:00Z', reason: 'The timesheet app was the only place to enter time.' }],
  ['WSC', 'stock-levels', 'WAREHOUSE', 'write', false, 'Posts stock counts and their differences.'],
  ['PCA', 'parts-catalog', 'WAREHOUSE', 'read', false, 'Looks up parts, prices and stock for other apps.'],
  ['SPA', 'parts-catalog', 'WAREHOUSE', 'read', true, 'Compares supplier price lists with the catalog.'],
  ['DB', 'workplace', 'DBT', 'read-write', false, 'Books desks, rooms and parking.'],
  ['VSK', 'visitor-log', 'PPL', 'write', false, 'Signs visitors in and tells their host.'],
];
const U = {};
USES.forEach(([app, domain, location, access, ai, purpose, allowed], i) => {
  const useId = id(6200 + i);
  U[app + '-' + domain] = useId;
  add('data-use', useId, clean({
    recordNumber: 'DU-' + String(1 + i).padStart(4, '0'),
    businessApplication: A[app].id,
    dataDomain: domain,
    dataLocation: L[location],
    access,
    purpose,
    reachesAi: ai,
    aiServiceAsSeen: ai ? AI_SERVICE : undefined,
    isDeidentified: false,
    status: 'data-use-registered',
    statusMeaning: 'registered',
    startOn: '2026-10-19',
  }));
  if (typeof allowed === 'string') {
    // DEC-0106 and DEC-0124 were about the application; they are re-pointed to the use they allowed.
    Object.assign(rows.find(r => r.id === D[allowed]).columns, { subjectKind: 'data-use', subjectDataUse: useId });
  } else if (allowed) {
    decision('DU-' + app + '-' + domain, {
      app, subject: { kind: 'data-use', use: useId }, type: 'data-use', outcome: 'allow', status: 'accepted', by: allowed.by, on: allowed.on,
      statement: 'Allow ' + A[app].name + ' to ' + { read: 'read', write: 'write', 'read-write': 'read and write' }[access] + ' ' + domainPath(domain) + ' at ' + locationLabel(location) + '.',
      context: purpose,
      reason: allowed.reason,
    });
  }
});

// Onboarding Buddy Agent's tier answers, which say "internal" to the sensitivity question while its use of personnel
// records implies "restricted" (DC-09). Stored as answer_tier_questions stores them. Visit Report Agent's assessment is
// left out: its restricted answer would compute tier 3 against its recorded tier 2 (a tier story the demo does not tell).
add('tier-assessment', id(6300), {
  businessApplication: A.OBA.id,
  answersJson: [
    { questionKey: 'data-change', answerKey: 'as-user' },
    { questionKey: 'sensitivity', answerKey: 'internal' },
    { questionKey: 'reach', answerKey: 'department' },
    { questionKey: 'agent-autonomy', answerKey: 'assists' },
    { questionKey: 'failure-impact', answerKey: 'team-slowed' },
  ],
  computedTier: 'tier-2',
  assessedBy: P.HANA,
  engineCheckedOn: '2026-10-19T09:00:00Z',
});

// The daily due-state refresh opens one review in the demo (UC-08): Identity Directory Sync's access check, done on
// September 15, came due again on November 23. The director runs the refresh as the platform admin.
const idsAccessCheck = rows.find(r => r.entity === 'review' && r.columns.businessApplication === A.IDS.id && r.columns.reviewType === 'access-check');
idsAccessCheck.columns.nextDueOn = '2026-11-23';

// Writes the demo configuration patch: every operation of seed/demo-binding.patch.json is kept, except the data domain
// and vocabulary mapping operations of draftOperations, which this script owns.
const bindingPath = join(here, 'demo-binding.patch.json');
const binding = JSON.parse(readFileSync(bindingPath, 'utf8'));
const OWNED = new Set(['data-domain', 'vocabulary-mapping']);
binding.draftOperations = [...(binding.draftOperations ?? []).filter(o => !OWNED.has(o.entity)), ...configurationOperations];

// ---------------------------------------------------------------------------------------------------------------
// 18. Checks: IDs, references, store keys, value keys, dates and wording. The script stops on the first problem.
// ---------------------------------------------------------------------------------------------------------------

const problems = [];
const fail = message => problems.push(message);
const ID_RE = /^00000000-0000-4000-8000-[0-9a-f]{12}$/;
const ids = new Map();
for (const row of rows) {
  if (!ID_RE.test(row.id)) fail('Bad ID ' + row.id);
  if (ids.has(row.id)) fail('Duplicate ID ' + row.id);
  ids.set(row.id, row);
}
for (const wid of walkthroughIds) if (!ids.has(wid)) fail('Walkthrough row missing ' + wid);
// IDs of the demo binding and the dev settings that a new row must not take.
const RESERVED = [0xaa, 0xab, 0xac, 0xad, 0xae].map(n => PREFIX + n.toString(16).padStart(12, '0')).concat([id(203)]);
for (const r of RESERVED) if (ids.has(r)) fail('Reserved ID used ' + r);

const principals = new Map(rows.filter(r => r.entity === 'principal-reference').map(r => [r.columns.entraObjectId, r]));
for (const p of principals.keys()) if (ids.has(p)) fail('An Entra object ID is also a row ID ' + p);
const REF_COLUMNS = {
  businessApplication: 'business-application', portfolio: 'portfolio', parentPortfolio: 'portfolio', deploymentTarget: 'deployment-target',
  plannedTarget: 'deployment-target', artifactVersion: 'artifact-version', changeRequest: 'change-request', authorizationDecision: 'decision',
  subjectApplication: 'business-application', subjectChangeRequest: 'change-request', tierDecision: 'decision', stageDecision: 'decision',
  autonomyDecision: 'decision', outcomeDecision: 'decision', ownershipDecision: 'decision', deputyFor: 'owner-assignment',
  fromApplication: 'business-application', toApplication: 'business-application', subjectIntakeRequest: 'intake-request',
  triageDecision: 'decision', pipelineReference: 'pipeline-reference', subjectDeploymentRecord: 'deployment-record', decision: 'decision',
  subjectDataUse: 'data-use', subjectDataLocation: 'data-location', dataLocation: 'data-location', parentLocation: 'data-location',
  copiedFrom: 'data-mapping', hostedByApplication: 'business-application',
};
const PRINCIPAL_COLUMNS = ['principal', 'documentOwner', 'decider', 'deployedBy', 'reviewer', 'requester', 'accountablePrincipal', 'approver', 'askedPrincipal', 'assessedBy'];
for (const row of rows) {
  for (const [col, entity] of Object.entries(REF_COLUMNS)) {
    const v = row.columns[col];
    if (v === undefined) continue;
    if (ids.get(v)?.entity !== entity) fail(row.entity + ' ' + row.id + ' ' + col + ' does not name a ' + entity + ': ' + v);
  }
  for (const col of PRINCIPAL_COLUMNS) {
    const v = row.columns[col];
    if (v !== undefined && !principals.has(v)) fail(row.entity + ' ' + row.id + ' ' + col + ' names no principal: ' + v);
  }
  // Data domains are configuration keys of the demo configuration patch.
  for (const col of ['dataDomain', 'subjectDataDomain']) {
    const v = row.columns[col];
    if (v !== undefined && !DOMAIN_KEYS.has(v)) fail(row.entity + ' ' + row.id + ' ' + col + ' names no data domain: ' + v);
  }
  // An owner assignment names exactly one subject (the store checks it too): an application, a domain or a location.
  if (row.entity === 'owner-assignment') {
    const c = row.columns;
    const kind = c.subjectKind ?? 'business-application';
    const set = [c.businessApplication, c.subjectDataDomain, c.subjectDataLocation].filter(v => v !== undefined).length;
    const named = { 'business-application': c.businessApplication, 'data-domain': c.subjectDataDomain, 'data-location': c.subjectDataLocation }[kind];
    if (set !== 1 || named === undefined) fail('owner-assignment ' + row.id + ' does not name exactly one subject of kind ' + kind);
  }
}
for (const [key, , parent] of DOMAINS) if (parent && !DOMAIN_KEYS.has(parent)) fail('data domain ' + key + ' has an unknown parent ' + parent);

// Store keys (logical-model.json keys the store enforces, plus the two it holds back).
function unique(entity, keyOf, label) {
  const seen = new Map();
  for (const row of rows.filter(r => r.entity === entity)) {
    const k = keyOf(row.columns);
    if (k === null) continue;
    if (seen.has(k)) fail('Duplicate ' + label + ' ' + k + ' in ' + row.id + ' and ' + seen.get(k));
    seen.set(k, row.id);
  }
}
const lower = v => (v ?? '').toLowerCase();
unique('principal-reference', c => lower(c.entraObjectId), 'principal-reference entraObjectId');
unique('portfolio', c => (c.parentPortfolio ? c.name + '|' + c.parentPortfolio : null), 'portfolio name and parent');
unique('portfolio', c => c.name, 'portfolio name');
for (const e of ['business-application', 'intake-request', 'decision', 'change-request']) unique(e, c => c.recordNumber, e + ' recordNumber');
unique('business-application', c => c.name, 'business-application name');
unique('technical-component', c => (c.retiredOn ? null : c.linkKind + '|' + c.externalId), 'technical-component linkKind and externalId');
unique('deployment-target', c => c.linkKind + '|' + c.externalId, 'deployment-target linkKind and externalId');
unique('deployment-target', c => c.labelAsSeen, 'deployment-target label');
unique('deployment-record', c => c.idempotencyKey, 'deployment-record idempotencyKey');
unique('artifact-version', c => c.artifactName + '|' + c.version, 'artifact-version name and version');
unique('document-link', c => c.businessApplication + '|' + c.documentationRequirement, 'document-link application and requirement');
unique('review', c => c.businessApplication + '|' + c.reviewType + '|' + c.dueOn, 'review application, type and due date');
unique('dependency', c => [c.fromApplication, c.toApplication].sort().join('|'), 'dependency pair');
unique('pipeline-reference', c => c.linkKind + '|' + c.externalId, 'pipeline-reference linkKind and externalId');
unique('pipeline-map-entry', c => c.businessApplication + '|' + c.pipelineReference + '|' + c.deploymentTarget, 'pipeline-map-entry application, pipeline and target');
unique('data-location', c => c.dataPlatform + '|' + lower(c.externalId), 'data-location platform and externalId');
unique('data-mapping', c => c.dataDomain + '|' + c.dataLocation + '|' + c.dataOrigin, 'data-mapping domain, location and origin');
unique('data-use', c => c.recordNumber, 'data-use recordNumber');
unique('data-use', c => c.businessApplication + '|' + c.dataDomain + '|' + (c.dataLocation ?? '') + '|' + c.reachesAi, 'data-use application, domain, location and AI');

// Value keys against the Balanced preset.
const KEYS = {
  applicationKind: ['power-platform-app', 'agent', 'ai-built-app', 'code-app', 'runtime-app', 'saas', 'external-system', 'api', 'mcp-server', 'practice-app'],
  tier: ['tier-1', 'tier-2', 'tier-3'],
  lifecycleStage: Object.keys(STAGE_RANK),
  buildPath: ['citizen', 'fusion', 'pro', 'ai-built'],
  agreedAutonomyLevel: ['assist', 'route', 'execute'],
  ownerRole: ['business-owner', 'sponsor', 'data-owner', 'data-steward', 'process-owner', 'technical-owner', 'support-owner', 'security-owner', 'builder'],
  dataPlatform: ['dataverse', 'salesforce', 'sharepoint', 'cosmos-db', 'azure-sql', 'sql-server', 'ibm-i', 'saas-application', 'fabric-lakehouse'],
  dataOrigin: ['system-of-record', 'replica', 'production-copy', 'masked-copy', 'synthetic'],
  access: ['read', 'write', 'read-write'],
  dependencyType: ['reads-data-from', 'writes-data-to', 'calls-api-of', 'hosted-on', 'run-time-need-in-target', 'replaces'],
  criticality: ['low', 'medium', 'high'],
  targetStage: ['development', 'test', 'production'],
  targetKind: ['power-platform-environment', 'microsoft-365-tenant', 'azure-resource-group', 'github-environment', 'saas-tenant', 'other'],
  changePath: ['pipeline-only', 'pipeline-or-ai-tool', 'any-route'],
  artifactKind: ['managed-solution', 'container-image', 'code-package', 'static-site', 'agent-package', 'commit', 'other'],
  deploymentRoute: ['pipeline-step', 'ai-tool', 'person-in-app', 'other'],
  gateLevelApplied: ['off', 'record', 'flag', 'advise', 'enforce'],
  changeType: ['standard', 'normal', 'emergency'],
  componentKind: ['canvas-app', 'model-driven-app', 'code-app', 'cloud-flow', 'copilot-studio-agent', 'copilot-managed-runtime-app', 'power-pages-site', 'solution', 'entra-agent-identity', 'repository', 'azure-resource'],
  documentType: ['wiki-page', 'runbook', 'architecture-decision-record', 'support-guide'],
  documentationRequirement: REQUIREMENTS.map(r => r.key),
  reviewType: ['owner-check', 'tier-check', 'access-check', 'benefit', 'architecture', 'post-change', 'security-review'],
  trigger: ['cadence', 'tier-answer-trigger', 'owner-check', 'request'],
  principalKind: ['user', 'group', 'agent-identity', 'agent-user', 'service-principal', 'managed-identity'],
  directoryState: ['enabled', 'disabled', 'deleted', 'unknown'],
  level: ['portfolio', 'application-group'],
  horizon: ['now', 'next', 'later'],
  sizeBand: ['small', 'medium', 'large'],
};
// The link kinds of the category each entity takes (link-kind.json, WriteGuardEntities.cs). repository-file on a
// technical component is the one deliberate exception (see the header).
const LINK_KINDS = {
  'technical-component': ['power-platform-resource', 'entra-object', 'repository-file'],
  'deployment-target': ['power-platform-resource', 'entra-object'],
  'data-location': ['dataverse-environment', 'azure-data-resource', 'sharepoint-location', 'fabric-item', 'external-data-system'],
  'document-link': ['ado-wiki-page', 'repository-file', 'sharepoint-page'],
  'delivery-link': ['ado-work-item', 'planner-plan'],
  'pipeline-reference': ['azure-pipelines-pipeline', 'power-platform-pipeline'],
};
const NUMERIC_ID_KINDS = new Set(['ado-work-item']); // idPattern ^[0-9]+$
const DECISION_OUTCOMES = {
  intake: ['accept', 'park', 'redirect', 'decline'], design: ['approve', 'rework'], 'data-use': ['allow', 'deny', 'retain'], tier: ['confirm', 'raise', 'lower'], 'risk-acceptance': ['accept', 'decline'],
  acceptance: ['accept', 'reject'], 'change-authorization': ['authorize', 'reject'], 'go-live': ['go', 'no-go'], ownership: ['assign', 'end'],
  portfolio: ['keep', 'invest', 'replace', 'retire'], exception: ['grant', 'deny'], retirement: ['complete', 'keep'], 'autonomy-level': ['agree', 'decline'],
  'flagged-agent': ['keep', 'reassign', 'consolidate', 'retire'], 'data-retention': ['keep', 'archive', 'delete', 'transfer'],
};
const STATUS_PAIRS = {
  'business-application': ['status', { 'app-registered': 'registered', 'app-draft': 'draft', 'app-archived': 'archived' }],
  'change-request': ['status', Object.fromEntries(Object.entries(CHANGE_STATUS).map(([m, s]) => [s, m]))],
  decision: ['status', { 'decision-proposed': 'proposed', 'decision-accepted': 'accepted' }],
  review: ['status', Object.fromEntries(Object.entries(REVIEW_STATUS).map(([m, s]) => [s, m]))],
  'document-link': ['status', Object.fromEntries(Object.entries(DOCUMENT_STATUS).map(([m, s]) => [s, m]))],
  'deployment-record': ['result', Object.fromEntries(Object.entries(RESULT_STATUS).map(([m, s]) => [s, m]))],
  'intake-request': ['status', Object.fromEntries(Object.entries(INTAKE_STATUS).map(([m, s]) => [s, m]))],
  'approval-record': ['outcome', { 'approval-approved': 'approved', 'approval-recommendation': 'recommendation-only' }],
  'data-location': ['status', { 'data-location-registered': 'registered', 'data-location-draft': 'draft' }],
  'data-mapping': ['status', { 'data-mapping-registered': 'registered', 'data-mapping-draft': 'draft' }],
  'data-use': ['status', { 'data-use-registered': 'registered', 'data-use-draft': 'draft' }],
};
const DATE_RE = /^\d{4}-\d{2}-\d{2}$/;
const MOMENT_RE = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/;
const DATE_COLUMNS = ['startOn', 'endOn', 'dueOn', 'doneOn', 'nextReviewOn', 'nextDueOn', 'lastUpdatedOn', 'lastCheckedOn', 'retiredOn', 'leavesOn', 'exceptionReviewOn', 'confirmedOn', 'endedOn'];
const MOMENT_COLUMNS = ['stageSince', 'windowStart', 'windowEnd', 'startedOn', 'finishedOn', 'decidedOn', 'engineCheckedOn', 'createdOn', 'lastSeenOn', 'ownerCheckedOn', 'linkedOn', 'directoryStateOn', 'requestedOn', 'respondedOn', 'expiresOn'];
const BOOLEAN_COLUMNS = ['isErased', 'isActive', 'usesAi', 'isDeputy', 'ownerDiffers', 'isOutsideWindow', 'isOtherPath', 'isMissingApproval', 'isAiOnlyCodeApproval', 'isMoved', 'isConsultation', 'countsTowardRequirement', 'approverHoldsRequiredRole', 'reachesAi', 'isDeidentified'];
for (const row of rows) {
  const c = row.columns;
  for (const [col, allowed] of Object.entries(KEYS)) {
    if (c[col] !== undefined && !allowed.includes(c[col])) fail(row.entity + ' ' + row.id + ' ' + col + ' is not a Balanced key: ' + c[col]);
  }
  if (c.linkKind !== undefined && LINK_KINDS[row.entity] && !LINK_KINDS[row.entity].includes(c.linkKind)) fail(row.entity + ' ' + row.id + ' linkKind ' + c.linkKind);
  if (NUMERIC_ID_KINDS.has(c.linkKind) && !/^[0-9]+$/.test(c.externalId)) fail(row.entity + ' ' + row.id + ' externalId is not a number: ' + c.externalId);
  if (row.entity === 'decision') {
    if (c.outcome !== undefined && !DECISION_OUTCOMES[c.decisionType]?.includes(c.outcome)) fail('decision ' + row.id + ' outcome ' + c.outcome);
    if (c.statusMeaning === 'proposed' && (c.decidedOn || c.outcome)) fail('decision ' + row.id + ' is proposed but decided');
    if (c.statusMeaning === 'accepted' && !(c.reason && c.outcome && c.decider && c.decidedOn)) fail('decision ' + row.id + ' is accepted without a reason, outcome, decider or date');
    if (c.subjectChangeRequest && ids.get(c.subjectChangeRequest)?.columns.authorizationDecision !== row.id) fail('decision ' + row.id + ' is not the authorization decision its change links');
    if (c.subjectIntakeRequest && ids.get(c.subjectIntakeRequest)?.columns.triageDecision !== row.id) fail('decision ' + row.id + ' is not the triage decision its intake request links');
    // An exception names the standard it sets aside and its review date, and only an exception does (B24-9).
    if ((c.decisionType === 'exception') !== !!(c.exceptionStandard && c.exceptionReviewOn)) fail('decision ' + row.id + ' exception fields do not match its type');
    if (c.subjectDeploymentRecord && ids.get(c.subjectDeploymentRecord)?.columns.businessApplication !== c.subjectApplication) fail('decision ' + row.id + ' names a deployment of another application');
  }
  if (row.entity === 'approval-record') {
    // Only a person signed in as a user counts (AN-03); any other answer is a recommendation only.
    const person = c.approverActorMeaning === 'person' && c.approverPrincipalKind === 'user';
    if (c.countsTowardRequirement !== (person && c.outcomeMeaning === 'approved')) fail('approval-record ' + row.id + ' counts wrongly');
    if (!person && c.outcomeMeaning !== 'recommendation-only') fail('approval-record ' + row.id + ' is an AI answer that is not a recommendation only');
    if (!(c.requestedOn < c.respondedOn && c.respondedOn < c.expiresOn)) fail('approval-record ' + row.id + ' dates are out of order');
  }
  if (row.entity === 'change-request' && c.statusMeaning !== 'draft' && !c.authorizationDecision) fail('change ' + row.id + ' is past draft with no authorization decision');
  if (row.entity === 'intake-request' && c.statusMeaning !== 'draft' && !c.triageDecision) fail('intake ' + row.id + ' is past draft with no triage decision');
  const pair = STATUS_PAIRS[row.entity];
  if (pair && pair[1][c[pair[0]]] !== c[pair[0] + 'Meaning']) fail(row.entity + ' ' + row.id + ' status and meaning do not pair: ' + c[pair[0]] + ' / ' + c[pair[0] + 'Meaning']);
  for (const col of DATE_COLUMNS) if (c[col] !== undefined && !DATE_RE.test(c[col])) fail(row.entity + ' ' + row.id + ' ' + col + ' is not a date: ' + c[col]);
  for (const col of MOMENT_COLUMNS) if (c[col] !== undefined && !MOMENT_RE.test(c[col])) fail(row.entity + ' ' + row.id + ' ' + col + ' is not a UTC moment: ' + c[col]);
  for (const col of BOOLEAN_COLUMNS) if (c[col] !== undefined && typeof c[col] !== 'boolean') fail(row.entity + ' ' + row.id + ' ' + col + ' is not a boolean');
  for (const col of ['url', 'runUrl']) if (c[col] !== undefined && !c[col].startsWith('https://')) fail(row.entity + ' ' + row.id + ' ' + col + ' is not https');
  if (row.entity === 'change-request' && c.windowStart && c.windowEnd && !(c.windowStart < c.windowEnd)) fail('change ' + row.id + ' window ends before it starts');
  if (row.entity === 'deployment-record' && !(c.startedOn < c.finishedOn)) fail('deployment ' + row.id + ' ends before it starts');
}
// A location linked to a deployment target has the target's stage (docs/architecture/data-taxonomy.md, Stages and data
// origin); a nested location has no stage of its own.
for (const row of rows.filter(r => r.entity === 'data-location')) {
  const c = row.columns;
  if (c.parentLocation ? c.targetStage !== undefined : c.targetStage === undefined) fail('data-location ' + row.id + ' stage does not fit its level');
  if (c.deploymentTarget && ids.get(c.deploymentTarget).columns.targetStage !== c.targetStage) fail('data-location ' + row.id + ' stage differs from its deployment target');
}
// Every flagged-looking record must be believable: other path means the route is outside the target's change path.
const CHANGE_PATH_ROUTES = { 'pipeline-only': ['pipeline-step'], 'pipeline-or-ai-tool': ['pipeline-step', 'ai-tool'], 'any-route': ['pipeline-step', 'ai-tool', 'person-in-app', 'other'] };
for (const row of rows.filter(r => r.entity === 'deployment-record')) {
  const c = row.columns;
  const allowed = CHANGE_PATH_ROUTES[ids.get(c.deploymentTarget).columns.changePath];
  if (c.isOtherPath !== !allowed.includes(c.deploymentRoute)) fail('deployment ' + row.id + ' isOtherPath does not match its route and target');
  if (c.changeRequest) {
    const ch = ids.get(c.changeRequest).columns;
    const inWindow = ch.windowStart <= c.finishedOn && c.finishedOn <= ch.windowEnd;
    if (c.isOutsideWindow === inWindow) fail('deployment ' + row.id + ' isOutsideWindow does not match its change window');
    if (ch.plannedTarget !== c.deploymentTarget) fail('deployment ' + row.id + ' target differs from its change');
  }
}

const text = JSON.stringify({
  description: 'The fictional demo estate for a demo install (D-33), generated by seed/demo-estate.gen.mjs (node seed/demo-estate.gen.mjs from src/adapters/azure). '
    + 'A superset of seed/walkthrough-world.json: every walkthrough row, enriched, plus 44 more business applications in five portfolios, their owners, '
    + 'dependencies, deployment targets, components, deployments, change requests, decisions (two standing exceptions among them), two past approval answers, reviews, documents, delivery links, pipeline references and intake requests, '
    + 'and the data taxonomy: data locations, data mappings, data uses, the owners of data domains and locations with their ownership decisions, and one tier assessment (the domain tree itself is configuration, in seed/demo-binding.patch.json). '
    + 'The story is seen from about 2026-11-24. Every ID is a patterned placeholder and every name, organization and URL is fictional.',
  rows,
}, null, 2) + '\n';

if (/[\u2013\u2014]/.test(text)) fail('The output holds an en dash or an em dash');
// Printable ASCII and line breaks only.
if (/[^\n\x20-\x7e]/.test(text)) fail('The output holds a character outside ASCII');
// British spellings to keep out of free text. Configuration keys and meanings such as review-cancelled and
// cancelled are preset values the seed cannot change.
const freeText = text.replace(/"(?:(?:change|review|intake|approval)-)?cancelled"/g, '""');
const BRITISH = /\b(catalogue|colour|behaviour|organis|centre|licence|labour|favour|honour|programme|travelled|cancelled|modelling|analyse|judgement|enrolment|defence|grey|cheque|tyre)/i;
const british = freeText.match(BRITISH);
if (british) fail('British spelling in the output: ' + british[0]);

if (problems.length) {
  console.error('demo-estate.gen.mjs found ' + problems.length + ' problem(s):\n  ' + problems.join('\n  '));
  process.exit(1);
}

writeFileSync(outputPath, text, 'utf8');
writeFileSync(bindingPath, JSON.stringify(binding, null, 2) + '\n', 'utf8');

// ---------------------------------------------------------------------------------------------------------------
// 19. Summary: row counts, and the key figures the Overview would show on a few days, by the read rules of the
//     product's tools (get_portfolio_summary, get_deployments, get_change_schedule) with the Balanced preset.
// ---------------------------------------------------------------------------------------------------------------

const byEntity = {};
for (const r of rows) byEntity[r.entity] = (byEntity[r.entity] ?? 0) + 1;

const of = entity => rows.filter(r => r.entity === entity).map(r => ({ id: r.id, ...r.columns }));
const apps = of('business-application').filter(a => a.statusMeaning !== 'archived');
const owners = of('owner-assignment');
const REQUIRES_DEPUTY = new Set(['business-owner', 'data-owner', 'process-owner', 'technical-owner', 'support-owner']);
function figures(day) {
  const current = o => (!o.startOn || o.startOn <= day) && (!o.endOn || o.endOn > day);
  const gaps = [];
  for (const app of [...apps].sort((x, y) => x.name.localeCompare(y.name))) {
    const mine = owners.filter(o => o.businessApplication === app.id && current(o));
    for (const role of KEYS.ownerRole) {
      const held = mine.some(o => o.ownerRole === role && !o.isDeputy);
      const deputy = mine.some(o => o.ownerRole === role && o.isDeputy);
      // Owner-assigned gate rows of Balanced: a business owner at every tier, a data owner at tiers 2 and 3 (B21-9).
      const required = role === 'business-owner' || (role === 'data-owner' && (app.tier === 'tier-2' || app.tier === 'tier-3'));
      if (!held && required) gaps.push(app.name + ' (' + role + ', holder)');
      else if (held && !deputy && REQUIRES_DEPUTY.has(role)) gaps.push(app.name + ' (' + role + ', deputy)');
    }
  }
  const appIds = new Set(apps.map(a => a.id));
  const overdue = of('review').filter(r => r.statusMeaning === 'due' && appIds.has(r.businessApplication) && r.dueOn < day).length;
  let documents = 0;
  const links = of('document-link');
  for (const app of apps) {
    for (const req of requiredFor({ tier: app.tier ?? null, stage: app.lifecycleStage })) {
      const link = links.find(l => l.businessApplication === app.id && l.documentationRequirement === req.key);
      if (!link || !link.url) { documents++; continue; }
      if (link.statusMeaning === 'stale') { documents++; continue; }
      if (link.statusMeaning === 'current' && req.freshness && addDays(link.lastCheckedOn ?? link.lastUpdatedOn, req.freshness) < day) documents++;
    }
  }
  const draftLimit = addDays(day, -30);
  const drafts = ['intake-request', 'change-request', 'business-application']
    .flatMap(e => of(e)).filter(r => r.statusMeaning === 'draft' && r.createdOn && r.createdOn.slice(0, 10) < draftLimit).length;
  const deployments = of('deployment-record');
  const flagged = deployments.filter(d => d.finishedOn.slice(0, 10) >= addDays(day, -30) && d.finishedOn.slice(0, 10) <= day && (d.isOutsideWindow || d.isOtherPath || d.isMissingApproval)).length;
  const timelineDeployments = deployments.filter(d => d.finishedOn.slice(0, 10) >= addDays(day, -14) && d.finishedOn.slice(0, 10) <= day).length;
  const changes = of('change-request').filter(c => c.windowStart);
  const touching = (from, to) => changes.filter(c => c.windowStart.slice(0, 10) <= to && (c.windowEnd ?? c.windowStart).slice(0, 10) >= from).length;
  return {
    applications: apps.length, ownerGaps: gaps.length, reviewsOverdue: overdue, documentsMissingOrStale: documents, oldDrafts: drafts,
    flaggedDeployments30d: flagged, comingUp14d: touching(day, addDays(day, 14)), timelineChanges: touching(addDays(day, -14), addDays(day, 28)),
    timelineDeployments14d: timelineDeployments, gapList: gaps,
  };
}

console.log('Wrote ' + outputPath + ' with ' + rows.length + ' rows.');
console.log('Rows per entity: ' + JSON.stringify(byEntity));
for (const day of ['2026-11-24', '2026-12-08', '2026-12-22', '2027-01-12']) {
  const { gapList, ...f } = figures(day);
  console.log(day + ': ' + JSON.stringify(f));
  if (day === '2026-11-24') console.log('  owner gaps: ' + gapList.join('; '));
}
