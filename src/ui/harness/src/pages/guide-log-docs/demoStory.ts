// The demo scenarios of src/docs/demo/scenarios.md as data for the Demo guide page: S-01 to S-23, each with the
// personas to act as, its presenter steps by step ID (S-03.2 and so on), the pages to open and the use cases it covers.
// S-01 to S-16 are the 15-minute guided story (src/docs/demo/run-15-minutes.md), S-17 to S-23 the deep dive.
//
// Design choices (D-41, 2026-11-24, and wave 4 on 2026-11-25):
// - A link names the application or portfolio it needs by name ("Invoice Matching"), never by ID. The page finds the
//   IDs in the install's own register at run time (find_applications, list_portfolios), so the guide works on any demo
//   store the demo director prepared. A name the register does not hold falls back to the register searched by that
//   name, and the page says so.
// - Each link names the persona it is for. A scenario played by two personas (the data owner, then the security
//   reviewer) gives each link its own persona, so one click switches to the right person and opens the page.
// - A link to a data domain, a role or a guideline names it by its key ("payroll", "data-governance-lead", "G17"):
//   configuration keys are the same on every install the demo binding compiles, so they need no lookup. A team is
//   reached by the role it holds (teams?role=change-board), for the same reason.
// - The persona names are those of seed/demo-personas.json. The page shows the install's own label for each and says
//   so when the install has no persona of that name.
// - The steps are the presenter scripts' steps (src/docs/demo/scenarios/<ID>-<slug>.md), one line each, with the same
//   step IDs as the scripts and the end-to-end tests, so the guide, the scripts and the tests name a step alike. A link
//   to one section of a page (Application 360's Data section) carries that page's own address parameter, so the guide
//   opens the exact section the step talks about.
import type { AppRoute } from '../../app/routes';

export interface StoryPersona {
  // The persona's name in the install's demo persona list (config.json demoPersonas).
  name: string;
  // The label to show when the install does not list the persona.
  label: string;
}

// What a link needs resolved before it can open the exact page.
export interface StoryIds {
  app: string | null;
  portfolio: string | null;
}

export interface StoryLink {
  label: string;
  // The persona the page is for. Default: the step's first persona.
  as?: string;
  // The application the page is about, by name.
  app?: string;
  // The portfolio the page is about, by name.
  portfolio?: string;
  to: (ids: StoryIds) => AppRoute;
  // The page's own address parameters, such as { tab: 'data' } for a section of Application 360. Added only when the
  // link found its application, so a fallback search never carries them.
  params?: Record<string, string>;
}

// One presenter step: its step ID (S-03.2) and what to do and show, in one line.
export interface StoryPoint {
  id: string;
  text: string;
}

export interface StoryStep {
  // The guide's own position of the scenario, 1 to 21, for its address (?step=3).
  number: number;
  // The scenario ID of src/docs/demo/scenarios.md, such as S-03.
  id: string;
  // "0:00 to 0:45" in the 15-minute run, or null for a deep-dive scenario outside it.
  time: string | null;
  title: string;
  personas: StoryPersona[];
  // What the presenter types in the AI client beside the browser, when the scenario has a Claude Code step.
  aiClient?: { prompt: string; note: string };
  points: StoryPoint[];
  links: StoryLink[];
  useCases: string[];
  // A page the scenario needs that the web app does not have yet, and what shows the same thing today.
  notBuilt?: string;
}

const P = {
  lena: { name: 'lena', label: 'Lena Okoro, requester' },
  mateo: { name: 'mateo', label: 'Mateo Brandt, PMO' },
  priya: { name: 'priya', label: 'Priya Castell, maker' },
  owen: { name: 'owen', label: 'Owen Achterberg, platform admin' },
  nadia: { name: 'nadia', label: 'Nadia Ferreira, application owner' },
  hugo: { name: 'hugo', label: 'Hugo Brandvold, change manager' },
  tomas: { name: 'tomas', label: 'Tomas Rendel, change board' },
  marcus: { name: 'marcus', label: 'Marcus Oyelaran, architect and change board' },
  elif: { name: 'elif', label: 'Elif Dunmore, architect' },
  kai: { name: 'kai', label: 'Kai Ostrowski, data owner' },
  noor: { name: 'noor', label: 'Noor Safadi, security reviewer' },
  dmitri: { name: 'dmitri', label: 'Dmitri Halloran, AI lead' },
  yusuf: { name: 'yusuf', label: 'Yusuf Brennan, configuration admin' },
  bianca: { name: 'bianca', label: 'Bianca Ferro, auditor' },
  theo: { name: 'theo', label: 'Theo Marsh, reader' },
  hana: { name: 'hana', label: 'Hana Lindqvist, data governance lead' },
  jonah: { name: 'jonah', label: 'Jonah Pereira, configuration admin' },
} satisfies Record<string, StoryPersona>;

const FIELD_OPERATIONS = 'Field operations';

// The guided story's length (src/docs/demo/run-15-minutes.md). Its scenarios carry a time; the deep-dive ones do not.
export const DEMO_MINUTES = 15;

const app360 = (name: string) => (ids: StoryIds): AppRoute => (ids.app ? { page: 'app', id: ids.app } : { page: 'applications', filter: { text: name } });
const appMap = (name: string) => (ids: StoryIds): AppRoute => (ids.app ? { page: 'map', id: ids.app } : { page: 'applications', filter: { text: name } });

const pt = (id: string, text: string): StoryPoint => ({ id, text });

export const STORY_STEPS: StoryStep[] = [
  {
    number: 1,
    id: 'S-01',
    time: '0:00 to 0:45',
    title: 'The portfolio at a glance',
    personas: [P.mateo],
    points: [
      pt('S-01.1', 'Overview, All portfolios: "48 applications in the register", with the owner gaps, reviews overdue, documents, approvals and AI drafts that wait.'),
      pt('S-01.2', 'Pick Field operations: every tile follows it, Applications 15, Owner gaps 2, Reviews overdue 1, Approvals waiting 14.'),
      pt('S-01.3', 'Owner gaps opens Owners, Gaps: Inspection Scheduler has no deputy business owner, and Safety Findings Tracker has no business owner.'),
      pt('S-01.4', 'Back, then Reviews overdue: the owner check of Safety Findings Tracker.'),
      pt('S-01.5', 'Needs attention: the flagged deployments and the change windows of the next two weeks.'),
    ],
    links: [
      { label: 'Overview', to: () => ({ page: 'overview' }) },
      { label: 'Field operations applications', portfolio: FIELD_OPERATIONS, to: ids => ({ page: 'applications', filter: ids.portfolio ? { portfolioIds: [ids.portfolio] } : {} }) },
      { label: 'Owner gaps', portfolio: FIELD_OPERATIONS, to: ids => ({ page: 'owners', view: 'gaps', ...(ids.portfolio ? { portfolioId: ids.portfolio } : {}) }) },
      { label: 'Reviews overdue', portfolio: FIELD_OPERATIONS, to: ids => ({ page: 'reviews', state: 'overdue', ...(ids.portfolio ? { portfolioId: ids.portfolio } : {}) }) },
    ],
    useCases: ['UC-05', 'UC-06', 'UC-02', 'UC-03'],
  },
  {
    number: 2,
    id: 'S-02',
    time: '0:45 to 3:00',
    title: 'Governance inside the AI build loop',
    personas: [P.priya],
    aiClient: {
      prompt: 'I want a crew overtime tracker for my team.',
      note: 'Claude Code calls check_duplicates (Crew Roster among the matches), get_tier_questions and save_intake_draft: "saved as a draft". Then type "Approve it for me.": no tool approves, and a tried name answers not_found.',
    },
    points: [
      pt('S-02.1', 'In Claude Code, ask for the crew overtime tracker. It checks for reuse and the tier questions, then saves a draft.'),
      pt('S-02.2', 'Ask it to approve. It cannot: the product serves no tool that approves.'),
      pt('S-02.3', 'My work: "Crew overtime tracker", drafted by Claude Code for Priya Castell, with Open in the wizard and Accept and submit.'),
      pt('S-02.4', 'Open in the wizard: the AI draft banner, then the reuse step with Crew Roster and Crew Shift Mobile.'),
      pt('S-02.5', 'Tier questions: the answers point to Tier 2, set by "Does it change data?" and "Does it touch sensitive data?". Save tier answers.'),
      pt('S-02.6', 'What Tier 2 needs: the Design and Go-live gates, the approvals, the runbook and wiki page, and the reviews.'),
      pt('S-02.7', 'Accept and submit, with Check first: who would be asked, nothing stored; then "1 approval is asked, and 3 people are consulted." Platform admin asked; architect, PMO and security reviewer advise.'),
      pt('S-02.8', 'AI activity: the draft taken forward by Priya, and each refused call with its rule and guideline chip.'),
    ],
    links: [
      { label: 'My work', to: () => ({ page: 'work' }) },
      { label: 'Intake drafts', to: () => ({ page: 'intake', view: 'drafts' }) },
      { label: 'AI activity', to: () => ({ page: 'ai-activity' }) },
      { label: 'Refused AI attempts', to: () => ({ page: 'ai-activity', view: 'refused' }) },
    ],
    useCases: ['P01', 'P02', 'P10', 'UC-05', 'UC-10', 'A-AI-BUILD-POLICY'],
  },
  {
    number: 3,
    id: 'S-03',
    time: '3:00 to 4:15',
    title: 'Triage, the gap check, places and leavers',
    personas: [P.owen],
    points: [
      pt('S-03.1', 'Intake, Triage: three requests wait, the crew overtime tracker among them.'),
      pt('S-03.2', 'Open "Weather alerts for outdoor jobs": Nadia is asked by name, the platform admin decides, three roles only advise.'),
      pt('S-03.3', 'Approve with a typed reason: "Your approval is recorded and counts. The decision still waits for other answers."'),
      pt('S-03.4', 'Add a link: the Azure DevOps work item, stored as an ID, an address and a label.'),
      pt('S-03.5', 'Gap check: 2 not registered, 1 vanished, 1 claimed, 1 dismissed; the run saw 33 items.'),
      pt('S-03.6', 'Claim "Copy visit photos to SharePoint" into Visit Report Agent: its owner is asked to confirm.'),
      pt('S-03.7', 'Dismiss "Test reminder flow (personal)" with a reason. Vanished shows the Timesheets canvas app.'),
      pt('S-03.8', 'Places: 13 targets by stage, People Test with nothing deployed yet, and 5 pipelines in order.'),
      pt('S-03.9', 'Owners, Leavers: Amara Quist (disabled) and Wren Abernathy, who leaves in about three weeks.'),
    ],
    links: [
      { label: 'Intake triage', to: () => ({ page: 'intake', view: 'triage' }) },
      { label: 'Gap check', to: () => ({ page: 'gap-check', view: 'open' }) },
      { label: 'Vanished items', to: () => ({ page: 'gap-check', view: 'vanished' }) },
      { label: 'Places by stage', to: () => ({ page: 'places', view: 'targets' }) },
      { label: 'Pipeline map', to: () => ({ page: 'places', view: 'pipelines' }) },
      { label: 'Leavers', to: () => ({ page: 'owners', view: 'leavers' }) },
    ],
    useCases: ['P01', 'P06', 'P08', 'A-DELIVERY', 'A-PLACES'],
  },
  {
    number: 4,
    id: 'S-04',
    time: '4:15 to 6:00',
    title: 'Approvals, owners and the lifecycle',
    personas: [P.nadia, P.mateo],
    points: [
      pt('S-04.1', 'Approvals, Waiting for me: seven requests asked of Nadia.'),
      pt('S-04.2', 'Answer on "Authorize the change: Retire the old export flow": the decision page names her rule and the expiry.'),
      pt('S-04.3', 'Approve with a reason: "The engine accepted the decision."'),
      pt('S-04.4', 'Her own risk acceptance: the page says the proposer may answer it under the rules in force. Approve.'),
      pt('S-04.5', 'Accept the business owner role of "Weather alerts for outdoor jobs": the application is registered.'),
      pt('S-04.6', 'The Claims Triage Agent data-use card: the use, the rule and the AI service. Approve.'),
      pt('S-04.7', 'The owner change on Claims Triage Agent: what acceptance changes; her answer leaves it proposed.'),
      pt('S-04.8', 'Switch to Mateo, the sponsor: he approves, and the engine accepts the owner change.'),
      pt('S-04.9', 'Inspection Scheduler, Lifecycle and gates: the runbook is stale; confirm it, and "Runbook current" turns met.'),
      pt('S-04.10', 'Hand over the technical owner role to Owen, with Hugo as deputy, and send it for approval.'),
      pt('S-04.11', 'Owners, Gaps: propose Lena Okoro as deputy business owner of Inspection Scheduler, and submit it.'),
    ],
    links: [
      { label: 'Approvals asked of Nadia', to: () => ({ page: 'approvals', view: 'mine' }) },
      { label: 'Approvals asked of Mateo, the sponsor', as: 'mateo', to: () => ({ page: 'approvals', view: 'mine' }) },
      { label: 'Inspection Scheduler gates', app: 'Inspection Scheduler', params: { tab: 'lifecycle' }, to: app360('Inspection Scheduler') },
      { label: 'Inspection Scheduler 360', app: 'Inspection Scheduler', to: app360('Inspection Scheduler') },
      { label: 'Owner gaps', to: () => ({ page: 'owners', view: 'gaps' }) },
    ],
    useCases: ['UC-01', 'UC-02', 'UC-14', 'P03', 'P07', 'P08', 'A-LIFECYCLE', 'A-GRADUATION'],
  },
  {
    number: 5,
    id: 'S-05',
    time: '6:00 to 7:00',
    title: 'Changes and flagged deployments',
    personas: [P.hugo],
    points: [
      pt('S-05.1', 'New change on Crew Roster: its tier, who authorizes each change type, and the two applications that depend on it.'),
      pt('S-05.2', 'Plan the change "Crew Roster 5.4: overtime column", Normal, to Field Ops Prod. Check first: 2 other applications affected.'),
      pt('S-05.3', 'Check and submit: does not fit a standard model, the business owner authorizes, gate level Advise. Submit.'),
      pt('S-05.4', 'Timeline: open the new window, and the change record opens.'),
      pt('S-05.5', 'Changes, List: the closed removal of the Timesheets canvas app and the closed emergency on Crew Roster.'),
      pt('S-05.6', 'Deployments flagged AI-only code approval: three. Open Invoice Matching 1.12.1: each flag explained, the exception waiting.'),
    ],
    links: [
      { label: 'New change on Crew Roster', app: 'Crew Roster', to: ids => ({ page: 'change-new', ...(ids.app ? { applicationId: ids.app } : {}) }) },
      { label: 'Timeline', to: () => ({ page: 'timeline' }) },
      { label: 'Changes list', to: () => ({ page: 'changes', view: 'list' }) },
      { label: 'AI-only code approvals', to: () => ({ page: 'deployments', view: 'flagged', flagKeys: ['ai-only-code-approval'] }) },
    ],
    useCases: ['P03', 'P05', 'A-EXCEPTIONS', 'A-DEPENDENCIES'],
  },
  {
    number: 6,
    id: 'S-06',
    time: '7:00 to 7:45',
    title: 'Reviews and exceptions',
    personas: [P.elif, P.marcus],
    points: [
      pt('S-06.1', 'Reviews, Due, Architecture review: Claims Triage Agent, yours.'),
      pt('S-06.2', 'Open the review: what completing it needs, and the ADR evidence link.'),
      pt('S-06.3', 'Switch to Marcus, a second architect: open the design decision Elif proposed, the options weighed. Approve, and the engine accepts it.'),
      pt('S-06.4', 'Complete the review: "It feeds 1 gate, 1 met now."'),
      pt('S-06.5', 'Reviews, Done: Quote Builder\'s architecture review, done today.'),
      pt('S-06.6', 'Decisions, Exceptions: four, each with the standard it sets aside and its review date.'),
    ],
    links: [
      { label: 'Architecture queue', to: () => ({ page: 'reviews', state: 'due', typeKeys: ['architecture'] }) },
      { label: 'Claims Triage Agent gates', app: 'Claims Triage Agent', params: { tab: 'lifecycle' }, to: app360('Claims Triage Agent') },
      { label: 'Reviews done', to: () => ({ page: 'reviews', state: 'done' }) },
      { label: 'Exceptions register', to: () => ({ page: 'decisions', view: 'exceptions' }) },
    ],
    useCases: ['P04', 'UC-03', 'UC-04', 'A-LIFECYCLE', 'A-EXCEPTIONS'],
  },
  {
    number: 7,
    id: 'S-07',
    time: '7:45 to 9:15',
    title: 'Data: domains, a data-use card, the never rule and copies',
    personas: [P.hana, P.kai, P.noor, P.priya, P.owen],
    aiClient: {
      prompt: 'Expense Assistant should check the bank account on each claim. Declare its use of the bank details with AI.',
      note: 'Claude Code calls save_data_use for Priya, and the product refuses it with rule_violation and G16: "Data rule never: the AI use rule is never, set on People / Payroll and inherited by People / Payroll / Bank details." Nothing is written.',
    },
    points: [
      pt('S-07.1', 'Data, Domains, as Hana: the tree with owners. Commercial has no data owner; Payroll reads AI use Never and no deputy.'),
      pt('S-07.2', 'Switch to Kai: the Visit Report Agent card on My work, 0 of 2 approvals.'),
      pt('S-07.3', 'Kai approves: the decision stays proposed, 1 of 2.'),
      pt('S-07.4', 'Switch to Noor, who approves: the use is allowed.'),
      pt('S-07.5', 'In Claude Code, as Priya: the bank details use with AI is refused by the never rule.'),
      pt('S-07.6', 'As Priya in the browser, Declare a use for Expense Assistant: "This use cannot be saved", and a save is refused with the same rule.'),
      pt('S-07.7', 'Uses: Mileage Claims holds the draft use Claude Code made for her, waiting to be confirmed.'),
      pt('S-07.8', 'Mapping, as Hana: Customer Test\'s unmasked copy breaks the copy rule; Finance Test\'s masked copy waits for its decision.'),
      pt('S-07.9', 'Locations: PAYR01 has no technical owner; TIMESQL01 has not been seen for months.'),
      pt('S-07.10', 'Gaps: 31 findings, each with the one act that fixes it.'),
      pt('S-07.11', 'Switch to Owen: Route Planner reads data it declares no use for. Declare its use of Inspections: no decision needed.'),
    ],
    links: [
      { label: 'Data domains', as: 'hana', to: () => ({ page: 'data' }) },
      { label: 'Payroll', as: 'hana', to: () => ({ page: 'data-domain', key: 'payroll' }) },
      { label: 'My work for Kai', as: 'kai', to: () => ({ page: 'work' }) },
      { label: 'Approvals asked of Noor', as: 'noor', to: () => ({ page: 'approvals', view: 'mine' }) },
      { label: 'Data uses', as: 'priya', to: () => ({ page: 'data', tab: 'uses' }) },
      { label: 'Data mapping', as: 'hana', to: () => ({ page: 'data', tab: 'mapping' }) },
      { label: 'Data locations', as: 'hana', to: () => ({ page: 'data', tab: 'locations' }) },
      { label: 'Data gaps', as: 'hana', to: () => ({ page: 'data', tab: 'gaps' }) },
      { label: 'Route Planner data', as: 'owen', app: 'Route Planner', params: { tab: 'data' }, to: app360('Route Planner') },
    ],
    useCases: ['P02-DATA', 'UC-13', 'P10', 'A-AI-BUILD-POLICY'],
  },
  {
    number: 8,
    id: 'S-08',
    time: '9:15 to 10:00',
    title: 'Agents',
    personas: [P.dmitri, P.noor],
    points: [
      pt('S-08.1', 'Agents, as the AI lead: six agents; Supplier Price Agent is tier 3 with no autonomy level.'),
      pt('S-08.2', 'Propose autonomy level Assist and send it: the security reviewer is asked, the AI lead consulted.'),
      pt('S-08.3', 'Flagged: the decision on Onboarding Buddy Agent waits for the AI lead.'),
      pt('S-08.4', 'AI activity, AI answers: the Finance Approvals Agent\'s answer, a recommendation only, not counted.'),
      pt('S-08.5', 'Switch to Noor: she agrees the Assist level on its decision page.'),
      pt('S-08.6', 'Reviews, security reviews due: Customer Feedback Bot\'s, before its pilot.'),
    ],
    links: [
      { label: 'Agent register', as: 'dmitri', to: () => ({ page: 'agents', view: 'register' }) },
      { label: 'Supplier Price Agent 360', as: 'dmitri', app: 'Supplier Price Agent', to: app360('Supplier Price Agent') },
      { label: 'Flagged agents', as: 'dmitri', to: () => ({ page: 'agents', view: 'flagged' }) },
      { label: 'AI answers', as: 'dmitri', to: () => ({ page: 'ai-activity', view: 'answers' }) },
      { label: 'Approvals asked of Noor', as: 'noor', to: () => ({ page: 'approvals', view: 'mine' }) },
      { label: 'Security reviews due', as: 'noor', to: () => ({ page: 'reviews', state: 'due', typeKeys: ['security-review'] }) },
    ],
    useCases: ['A-AGENTS', 'P02', 'P10'],
  },
  {
    number: 9,
    id: 'S-09',
    time: '10:00 to 11:15',
    title: 'Demand, the portfolio and retirement',
    personas: [P.mateo, P.nadia, P.owen],
    points: [
      pt('S-09.1', 'Demand: four scored requests, one priority waiting, "Weather alerts for outdoor jobs" still to score.'),
      pt('S-09.2', 'Score it 80, Small, Now.'),
      pt('S-09.3', 'Propose priority Fund and send it: no sponsor is named, so the business owner is asked.'),
      pt('S-09.4', 'Switch to Nadia: she funds it on the decision page. This is the Commit gate.'),
      pt('S-09.5', 'Its Application 360: Accept and Commit gates passed, the stage Design and architecture review.'),
      pt('S-09.6', 'Portfolios, Field operations: the review pack; Crew Shift Mobile\'s replace decision waits.'),
      pt('S-09.7', 'Retirement: Crew Shift Mobile retire proposed, Legacy Timesheet App on the way out.'),
      pt('S-09.8', 'As Owen, the tracker: confirm the Timesheets component retired, and its vanished claim closes.'),
      pt('S-09.9', 'Propose retirement complete and submit it: the business owner, or the deputy, is asked.'),
      pt('S-09.10', 'Data, Gaps for the app: its retention is open, and the archive decision waits for Hana.'),
      pt('S-09.11', 'Switch to Nadia, the deputy: she accepts, and the application is retired; the tracker names the retention decision still open.'),
    ],
    links: [
      // The whole board: the story's requests sit in several portfolios, and a new request has none until it is placed.
      { label: 'Demand board', as: 'mateo', to: () => ({ page: 'demand' }) },
      { label: 'Approvals asked of Nadia', as: 'nadia', to: () => ({ page: 'approvals', view: 'mine' }) },
      { label: 'Field operations portfolio', as: 'mateo', portfolio: FIELD_OPERATIONS, to: ids => (ids.portfolio ? { page: 'portfolio', id: ids.portfolio } : { page: 'portfolios' }) },
      { label: 'Applications on their way out', as: 'mateo', to: () => ({ page: 'retirement' }) },
      { label: 'Legacy Timesheet App retirement', as: 'owen', app: 'Legacy Timesheet App', to: ids => ({ page: 'retirement', ...(ids.app ? { applicationId: ids.app } : {}) }) },
      { label: 'Legacy Timesheet App data gaps', as: 'owen', app: 'Legacy Timesheet App', to: ids => ({ page: 'data', tab: 'gaps', ...(ids.app ? { applicationId: ids.app } : {}) }) },
    ],
    useCases: ['A-DEMAND', 'UC-06', 'P09'],
  },
  {
    number: 10,
    id: 'S-10',
    time: '11:15 to 12:00',
    title: 'Configuration: change the rules safely',
    personas: [P.yusuf, P.jonah],
    aiClient: {
      prompt: 'Propose a standard change model called Monthly content refresh: a tested content update to one application at tier 1 or 2, in a window of two hours or less. Check it first and do not save it.',
      note: 'Optional, step S-10.6, with Claude Code connected as Yusuf Brennan (yusuf, on a local run and on a demo install). propose_configuration_change with validateOnly: "The change is valid. Nothing was stored", the diff and who accepts it.',
    },
    points: [
      pt('S-10.1', 'Configuration, as Yusuf: version 1 in force, 692 rules of 43 kinds in plain words, the data taxonomy among them.'),
      pt('S-10.2', 'Drafts: CFG-0001, the AI use rule of Workplace / Visitor log changes from not set to never.'),
      pt('S-10.3', 'The draft: the diff, the rules check passed, and the data use it touches.'),
      pt('S-10.4', 'Switch to Jonah, the second configuration admin: he accepts it on the decision page; Hugo, the data owner, is consulted.'),
      pt('S-10.5', 'Versions: version 2 is in force, accepted by Jonah, instructed by Yusuf.'),
      pt('S-10.6', 'Optional, in Claude Code: a standard change model checked with validateOnly, never saved.'),
    ],
    links: [
      { label: 'Rules in force', as: 'yusuf', to: () => ({ page: 'configuration' }) },
      { label: 'Configuration drafts', as: 'yusuf', to: () => ({ page: 'configuration', view: 'drafts' }) },
      { label: 'Approvals asked of Jonah', as: 'jonah', to: () => ({ page: 'approvals', view: 'mine' }) },
      { label: 'Configuration versions', as: 'jonah', to: () => ({ page: 'configuration', view: 'versions' }) },
      { label: 'Visitor log', as: 'jonah', to: () => ({ page: 'data-domain', key: 'visitor-log' }) },
    ],
    useCases: ['P11', 'UC-11'],
  },
  {
    number: 11,
    id: 'S-11',
    time: '12:45 to 13:15',
    title: 'Roles and access',
    personas: [P.owen],
    points: [
      pt('S-11.1', 'Roles and access: 15 product roles, none without a group; each role through its Entra group.'),
      pt('S-11.2', 'The data governance lead: the tools it may call and how far an AI client may go with each.'),
      pt('S-11.3', 'The configuration admin: two holders, Jonah Pereira and Yusuf Brennan.'),
      pt('S-11.4', 'Run the binding check: every human role is bound to a group that exists, checked against the demo personas.'),
    ],
    links: [
      { label: 'Roles and access', to: () => ({ page: 'roles' }) },
      { label: 'The data governance lead role', to: () => ({ page: 'roles', roleKey: 'data-governance-lead' }) },
      { label: 'The configuration admin role', to: () => ({ page: 'roles', roleKey: 'configuration-admin' }) },
    ],
    useCases: ['A-ROLES', 'UC-07'],
  },
  {
    number: 12,
    id: 'S-12',
    time: '13:15 to 13:30',
    title: 'Guidelines and the playbook',
    personas: [P.priya],
    points: [
      pt('S-12.1', 'AI activity, Refused: the G17 chip opens Guidelines at G17.'),
      pt('S-12.2', 'G17: no AI agent or tool approves work; the approval rule and gate requirement that apply it, and its playbook chapters.'),
      pt('S-12.3', 'G2: owners and deputies for every application and data domain; the playbook chapters by track.'),
    ],
    links: [
      { label: 'Refused AI attempts', to: () => ({ page: 'ai-activity', view: 'refused' }) },
      { label: 'Guideline G17', to: () => ({ page: 'guidelines', guideline: 'G17' }) },
      { label: 'Guideline G2', to: () => ({ page: 'guidelines', guideline: 'G2' }) },
    ],
    useCases: ['UC-11', 'A-AI-BUILD-POLICY'],
  },
  {
    number: 13,
    id: 'S-13',
    time: '13:30 to 13:45',
    title: 'Measures and slide-back signals',
    personas: [P.mateo],
    points: [
      pt('S-13.1', 'Measures: nine measures read in pairs, each with its target from the configuration.'),
      pt('S-13.2', 'Short of their target: Owner coverage, and the rows behind it.'),
      pt('S-13.3', 'The ten slide-back signals: which fire, which are quiet, which are read outside the product.'),
    ],
    links: [
      { label: 'Measures', to: () => ({ page: 'measures' }) },
    ],
    useCases: ['A-MEASURES'],
  },
  {
    number: 14,
    id: 'S-14',
    time: '13:45 to 14:00',
    title: 'Admin: run the product',
    personas: [P.owen],
    points: [
      pt('S-14.1', 'Admin: the configuration, the clients that called, the refused calls, the notices sent and the outbox.'),
      pt('S-14.2', 'The daily check ran before the demo: it opened the access check of Identity Directory Sync and sent its notices.'),
      pt('S-14.3', 'Run the daily check now: check first, then run; nothing was due, and a rerun changes nothing.'),
      pt('S-14.4', 'Clients and the approval outbox: Claude Code sanctioned; nothing failed.'),
      pt('S-14.5', 'Host and engine, and the Tool Policy: 102 tools, who may call each.'),
    ],
    links: [
      { label: 'Admin', to: () => ({ page: 'admin' }) },
      { label: 'Identity Directory Sync reviews', app: 'Identity Directory Sync', to: ids => ({ page: 'reviews', state: 'all', typeKeys: ['access-check'], ...(ids.app ? { applicationId: ids.app } : {}) }) },
      { label: 'AI activity', to: () => ({ page: 'ai-activity' }) },
    ],
    useCases: ['A-ADMIN', 'UC-08'],
  },
  {
    number: 15,
    id: 'S-15',
    time: '14:00 to 15:00',
    title: 'Answer from the record',
    personas: [P.bianca],
    points: [
      pt('S-15.1', 'The Invoice Matching evidence pack: decisions, answers, changes, deployments, reviews and documents.'),
      pt('S-15.2', 'The 1.12 decision: the Finance Approvals Agent\'s answer is a recommendation only; Clara Voss\'s counted.'),
      pt('S-15.3', 'Both deployments carry the AI-only code approval flag, each with the rules version it was checked under.'),
      pt('S-15.4', 'The audit trail of AI calls: each refused call with the rule and the guideline.'),
    ],
    links: [
      { label: 'Invoice Matching evidence pack', app: 'Invoice Matching', to: ids => (ids.app ? { page: 'evidence', subjectKind: 'business-application', id: ids.app } : { page: 'applications', filter: { text: 'Invoice Matching' } }) },
      { label: 'Invoice Matching audit trail', app: 'Invoice Matching', to: ids => ({ page: 'audit', ...(ids.app ? { applicationId: ids.app } : {}) }) },
      { label: 'AI calls in the audit trail', to: () => ({ page: 'audit', actorKinds: ['ai-client-for-person'] }) },
      { label: 'Invoice Matching decisions', app: 'Invoice Matching', to: ids => ({ page: 'decisions', ...(ids.app ? { applicationId: ids.app } : {}) }) },
    ],
    useCases: ['UC-09', 'UC-11', 'A-EVIDENCE', 'P10'],
  },
  {
    number: 16,
    id: 'S-16',
    time: '12:00 to 12:45',
    title: 'Teams and committees',
    personas: [P.owen, P.tomas, P.marcus],
    points: [
      pt('S-16.1', 'Teams: 19 groups, 9 committees, 4 support teams and 6 role groups; members are the personas and the estate\'s other people.'),
      pt('S-16.2', 'Committees, Change board: four members, the exception decisions it answers, the Desk Booking card waiting, and what its members own.'),
      pt('S-16.3', 'Support teams, Service Desk: six applications, and the owner gap on Inspection Scheduler.'),
      pt('S-16.4', 'Switch to Marcus, a change board member who did not propose it: Answer on the decision page, approve with a reason.'),
      pt('S-16.5', 'Back on the Change board, as Tomas: nothing waits, and Recent answers shows Marcus\'s.'),
      pt('S-16.6', 'Role groups, Data owners: what is asked of each data owner by name and what they own; then Kai Ostrowski\'s Data it owns.'),
    ],
    links: [
      { label: 'Teams and committees', to: () => ({ page: 'teams' }) },
      { label: 'Committees', to: () => ({ page: 'teams', kind: 'committee' }) },
      { label: 'Support teams', to: () => ({ page: 'teams', kind: 'support-team' }) },
      { label: 'The Change board, as Tomas', as: 'tomas', to: () => ({ page: 'teams', roleKey: 'change-board' }) },
      { label: 'The Change board, as Marcus', as: 'marcus', to: () => ({ page: 'teams', roleKey: 'change-board' }) },
    ],
    useCases: ['UC-07', 'UC-02', 'A-ROLES'],
  },
  {
    number: 17,
    id: 'S-17',
    time: null,
    title: 'The dependency map and what breaks',
    personas: [P.elif],
    points: [
      pt('S-17.1', 'Inspection Scheduler\'s map: 9 applications depend on it within 3 hops; it needs 7.'),
      pt('S-17.2', 'What breaks, as a list: each dependant with how it depends and its criticality.'),
      pt('S-17.3', 'Application 360, Components: what depends on the Inspection Scheduler solution, hop by hop.'),
      pt('S-17.4', 'Finance Ledger, Lifecycle and gates: "See what breaks": 14 applications within 3 hops.'),
      pt('S-17.5', 'The PAYLIB data location: what depends on it, starting from the applications with a data use there.'),
    ],
    links: [
      { label: 'Inspection Scheduler map', app: 'Inspection Scheduler', to: appMap('Inspection Scheduler') },
      { label: 'Inspection Scheduler components', app: 'Inspection Scheduler', to: app360('Inspection Scheduler') },
      { label: 'Finance Ledger gates', app: 'Finance Ledger', params: { tab: 'lifecycle' }, to: app360('Finance Ledger') },
      { label: 'Finance Ledger map', app: 'Finance Ledger', to: appMap('Finance Ledger') },
      { label: 'Data locations', to: () => ({ page: 'data', tab: 'locations' }) },
    ],
    useCases: ['A-DEPENDENCIES', 'P03', 'P09', 'UC-13'],
  },
  {
    number: 18,
    id: 'S-18',
    time: null,
    title: 'Search, filters and what a reader may do',
    personas: [P.theo],
    points: [
      pt('S-18.1', 'Search "invoice": Invoice Matching.'),
      pt('S-18.2', 'Search "Wren": her person page, with what she owns now.'),
      pt('S-18.3', 'Search a decision number: the decision opens; it does not ask a reader.'),
      pt('S-18.4', 'Applications, Uses AI yes: 14 applications; every filter lives in the address.'),
      pt('S-18.5', 'A reader\'s Application 360 offers nothing that writes.'),
      pt('S-18.6', 'Teams, search "change": the tabs count only the matches; Approvals and Retirement search the same way, in the address.'),
    ],
    links: [
      { label: 'Search for invoice', to: () => ({ page: 'search', text: 'invoice' }) },
      { label: 'Search for Wren', to: () => ({ page: 'search', text: 'Wren' }) },
      { label: 'Applications that use AI', to: () => ({ page: 'applications', filter: { usesAi: true } }) },
      { label: 'Inspection Scheduler 360, as a reader', app: 'Inspection Scheduler', to: app360('Inspection Scheduler') },
      { label: 'Teams matching change', to: () => ({ page: 'teams', text: 'change' }) },
    ],
    useCases: ['UC-05', 'UC-07', 'UC-02'],
  },
  {
    number: 19,
    id: 'S-19',
    time: null,
    title: 'The decision log and the documents',
    personas: [P.bianca],
    points: [
      pt('S-19.1', 'Decisions: every decision in one line, what, who, when and why.'),
      pt('S-19.2', 'Only those drafted by an AI client: the two tier decisions Claude Code drafted.'),
      pt('S-19.3', 'Exceptions: standing, review overdue, review coming and waiting.'),
      pt('S-19.4', 'Documents: missing, stale, drafts to confirm, and which gate needs each.'),
    ],
    links: [
      { label: 'Decision log', to: () => ({ page: 'decisions' }) },
      { label: 'Exceptions', to: () => ({ page: 'decisions', view: 'exceptions' }) },
      { label: 'Documents', to: () => ({ page: 'documents' }) },
      { label: 'Stale documents', to: () => ({ page: 'documents', state: 'stale' }) },
    ],
    useCases: ['UC-04', 'UC-09', 'P07', 'UC-14'],
  },
  {
    number: 20,
    id: 'S-20',
    time: null,
    title: 'A request from the web app, with no AI',
    personas: [P.lena, P.nadia],
    points: [
      pt('S-20.1', 'Intake, My requests, as Lena: one submitted, and the draft "Depot first-aid kit checks".'),
      pt('S-20.2', 'Continue the draft: name Nadia Ferreira as business owner, and save it.'),
      pt('S-20.3', 'Tier questions: Tier 1.'),
      pt('S-20.4', 'Submit: at the lowest tier only the named business owner is asked.'),
      pt('S-20.5', 'Switch to Nadia: she accepts the role, and the application is registered.'),
    ],
    links: [
      { label: 'Lena\'s requests', as: 'lena', to: () => ({ page: 'intake', view: 'mine' }) },
      { label: 'New request', as: 'lena', to: () => ({ page: 'intake-new' }) },
      { label: 'Approvals asked of Nadia', as: 'nadia', to: () => ({ page: 'approvals', view: 'mine' }) },
    ],
    useCases: ['P01', 'P02', 'UC-05', 'UC-07'],
  },
  {
    number: 21,
    id: 'S-21',
    time: null,
    title: 'What an AI client may do, and what it may not',
    personas: [P.priya, P.owen],
    aiClient: {
      prompt: 'Who am I in the governance product, and what may you do for me?',
      note: 'Then, in order: "What needs me today?", "What depends on Inspection Scheduler?", "Which committees decide on changes and exceptions, and who is in them?", an approval (refused, G17), a risk acceptance on Asset Register (not_authorized, G4) and the bank details use (refused by the never rule, G16). The exact prompts are in src/docs/demo/claude-code.md.',
    },
    points: [
      pt('S-21.1', 'whoami: the persona\'s roles and the tools Claude Code may use for her.'),
      pt('S-21.2', 'Her queue, read through the AI client.'),
      pt('S-21.3', 'The dependants of Inspection Scheduler: 9 applications.'),
      pt('S-21.4', 'The committees and their members.'),
      pt('S-21.5', 'An approval: refused, AI never approves (G17).'),
      pt('S-21.6', 'A risk acceptance on an application she does not own: not_authorized (G4).'),
      pt('S-21.7', 'The bank details with AI: refused when saved, for everyone (G16).'),
      pt('S-21.8', 'AI activity, Refused, and Admin, Clients: every refused call on the record.'),
    ],
    links: [
      { label: 'Refused AI attempts', to: () => ({ page: 'ai-activity', view: 'refused' }) },
      { label: 'Admin clients', as: 'owen', to: () => ({ page: 'admin' }) },
    ],
    useCases: ['UC-10', 'P10', 'A-AI-BUILD-POLICY', 'UC-11'],
  },
  {
    number: 22,
    id: 'S-22',
    time: null,
    title: 'Everyday records: the work between the decisions',
    personas: [P.priya, P.elif, P.nadia, P.owen, P.hugo, P.hana, P.lena, P.dmitri, P.mateo],
    points: [
      pt('S-22.1', 'Applications, as Priya: register "Depot key register", built before the product came, and send its ownership for confirmation.'),
      pt('S-22.2', "Visit Report Agent's map, as Elif: add that it calls the API of Field Knowledge MCP Server."),
      pt('S-22.3', 'Contractor Portal, Work, as Nadia: request an architecture review.'),
      pt('S-22.4', 'Owners, as Owen: record that Ines Varga leaves in 30 days; owner checks open on what she owns.'),
      pt('S-22.5', 'Record a deployment, as Hugo: the standard change "Rotate the reminder flow connection" reached Field Ops Prod by hand, and the engine flags the route.'),
      pt('S-22.6', 'The change record: record that it succeeded, then close the change.'),
      pt('S-22.7', 'Documents, as Dmitri: link the runbook of Supplier Onboarding and confirm it current; a second link is refused.'),
      pt('S-22.8', 'Gap check, as Owen: compare a list from the CoE kit; each item no application holds opens a claim.'),
      pt('S-22.9', 'Places: register the target Field Ops Training, and map a pipeline to it.'),
      pt('S-22.10', 'Data, Locations, as Hana: add "Depot safety binders" and map the safety findings there.'),
      pt('S-22.11', "Lena's request \"Spare parts returns from field vans\": withdraw it; its approval requests are canceled."),
      pt('S-22.12', 'Field operations, as Owen: a new portfolio inside it, "Depot services".'),
      pt('S-22.13', 'Contractor Portal, as Nadia: a risk acceptance at tier 3 asks her; Waiting for me lists it apart, and the separation rule keeps her from answering.'),
      pt('S-22.14', 'A domain record, as Hana: move the rows of Client cases and feedback into Client accounts and contacts.'),
      pt('S-22.15', "Gap check, as Owen: run it over the demo's fictional inventory and continue the run; one new claim, two vanished."),
      pt('S-22.16', 'Vanished: link the new place of one item, and say the other is still there.'),
      pt('S-22.17', 'Dismissed: reopen the test flow; then claim "Canteen orders" into a new draft application.'),
      pt('S-22.18', "Gap check: check the owners in the demo's fictional directory."),
      pt('S-22.19', 'Depot key register, as Priya: withdraw the draft; its ownership approvals are canceled.'),
      pt('S-22.20', 'Owners, Leavers, as Owen: change the leave date of Ines Varga.'),
      pt('S-22.21', 'Leavers: propose Ravi Holm as successor to Ines Varga on Crew Roster, and send it.'),
      pt('S-22.22', 'Asset Register: end Ines Varga as deputy data owner, and send it.'),
      pt('S-22.23', 'Visit Report Agent, Data, as Elif: add a data link to Field Data Lake.'),
      pt('S-22.24', 'The map: change a link, then end the data link.'),
      pt('S-22.25', 'The hand deployment, as Hugo: record a correction of its finish time.'),
      pt('S-22.26', 'The corrected deployment, as Elif: propose an exception, and send it.'),
      pt('S-22.27', 'The deployment, as Hugo: add the deployment log as evidence.'),
      pt('S-22.28', '"Retire the old export flow": record the result, then change it.'),
      pt('S-22.29', 'New change: plan a draft, edit it twice, link its work item and evidence.'),
      pt('S-22.30', '"Crew Roster 5.4: overtime column": withdraw it, and the draft too.'),
      pt('S-22.31', 'Places, as Owen: change Field Ops Training; retire People Test and return it.'),
      pt('S-22.32', 'Pipelines: change the map of the training pipeline.'),
      pt('S-22.33', 'Depot safety binders, as Hana: confirm it, change it, add a location inside it.'),
      pt('S-22.34', 'Data, Locations: propose Felix Arendt as technical owner of PAYR01, and send it.'),
      pt('S-22.35', 'The mapping: change it and end it; retire the location inside.'),
      pt('S-22.36', 'The risk acceptance, as Nadia: withdraw it.'),
      pt('S-22.37', 'Depot services: Mateo edits it; Owen retires it and brings it back.'),
      pt('S-22.38', 'The Canteen orders request, as Owen: answer its tier questions; the request shows the suggested tier.'),
    ],
    links: [
      { label: 'Applications register', as: 'priya', to: () => ({ page: 'applications', filter: {} }) },
      { label: 'Visit Report Agent map', as: 'elif', app: 'Visit Report Agent', to: appMap('Visit Report Agent') },
      { label: 'Contractor Portal work', as: 'nadia', app: 'Contractor Portal', params: { tab: 'work' }, to: app360('Contractor Portal') },
      { label: 'Owners', as: 'owen', to: () => ({ page: 'owners' }) },
      { label: 'Record a deployment', as: 'hugo', app: 'Inspection Scheduler', to: ids => ({ page: 'deployment-new', ...(ids.app ? { applicationId: ids.app } : {}) }) },
      { label: 'Documents', as: 'dmitri', to: () => ({ page: 'documents' }) },
      { label: 'Gap check', as: 'owen', to: () => ({ page: 'gap-check' }) },
      { label: 'Leavers', as: 'owen', to: () => ({ page: 'owners', view: 'leavers' }) },
      { label: 'Places', as: 'owen', to: () => ({ page: 'places' }) },
      { label: 'Data locations', as: 'hana', to: () => ({ page: 'data', tab: 'locations' }) },
      { label: "Lena's requests", as: 'lena', to: () => ({ page: 'intake', view: 'mine' }) },
      { label: 'Field operations portfolio', as: 'owen', portfolio: FIELD_OPERATIONS, to: ids => (ids.portfolio ? { page: 'portfolio', id: ids.portfolio } : { page: 'portfolios' }) },
      { label: 'Contractor Portal decisions', as: 'nadia', app: 'Contractor Portal', params: { tab: 'decisions' }, to: app360('Contractor Portal') },
    ],
    useCases: ['UC-05', 'A-DEPENDENCIES', 'P04', 'P08', 'P05', 'P03', 'P07', 'UC-14', 'P06', 'A-PLACES', 'P02-DATA', 'UC-13', 'P01', 'UC-06', 'UC-01'],
  },
  {
    number: 23,
    id: 'S-23',
    time: null,
    title: 'Every other action: drafts, answers, rules and records',
    personas: [P.priya, P.owen, P.hugo, P.elif, P.nadia, P.kai, P.hana, P.yusuf, P.dmitri, P.mateo, P.lena, P.noor],
    points: [
      pt('S-23.1', 'Claude Code drafts tier decisions, a change and data uses for Priya, and locations with mappings for Owen.'),
      pt('S-23.2', 'The Warehouse Stock Count draft, as Priya: edit it, then accept and submit it.'),
      pt('S-23.3', 'AI activity, Waiting: accept and submit the Customer Feedback Bot draft.'),
      pt('S-23.4', 'My work: accept and submit the Shift Swap Requests draft.'),
      pt('S-23.5', 'The change Claude Code drafted: accept and submit it.'),
      pt('S-23.6', 'Data uses: confirm one, which sends its decision; withdraw the other.'),
      pt('S-23.7', 'As Owen: confirm the drafted location and mapping; withdraw the others.'),
      pt('S-23.8', 'Approvals, as Hugo: reject the hand-over that names him deputy.'),
      pt('S-23.9', 'Approvals, as Elif: give advice on the crew overtime tracker; it never counts.'),
      pt('S-23.10', 'The photos risk acceptance, as Nadia: supersede it, and add evidence.'),
      pt('S-23.11', 'As Priya: submit an emergency change, then ask again before its two days run out.'),
      pt('S-23.12', 'Contractor Portal, Tier: Nadia answers the questions; Owen proposes the tier decision.'),
      pt('S-23.13', 'Weather alerts, Lifecycle and gates, as Elif: propose the design decision.'),
      pt('S-23.14', 'Field Knowledge MCP Server, Data, as Kai: hand over the data owner role.'),
      pt('S-23.15', 'Site Access Badges, Data, as Owen: it holds no business data.'),
      pt('S-23.16', 'Data, Gaps: propose a data-use decision and a retention decision.'),
      pt('S-23.17', 'Mapping: Hana changes a copy to masked; Owen proposes its copy decision.'),
      pt('S-23.18', 'A data use, as Owen: change it and end it; Hana confirms TIMESQL01 seen.'),
      pt('S-23.19', 'The Commercial domain, as Hana: name Clara Voss its data owner.'),
      pt('S-23.20', 'Configuration, as Yusuf: import a domain, change the draft, submit it.'),
      pt('S-23.21', 'Roles and access: propose a binding change, then withdraw the draft.'),
      pt('S-23.22', 'Agents, as Dmitri: decide on a flagged agent; request a security review.'),
      pt('S-23.23', "Contractor Portal's architecture review, as Elif: propose the outcome, add evidence."),
      pt('S-23.24', 'Documents, as Nadia: confirm a draft current; mark a runbook stale and update its link.'),
      pt('S-23.25', 'Decisions and Measures: propose a decision, record one about a signal, propose an exception.'),
      pt('S-23.26', 'Review pack and retirement tracker: propose, draft the removal change, add evidence.'),
      pt('S-23.27', 'Intake, as Lena: save a new request as a draft, then withdraw it.'),
      pt('S-23.28', 'Admin, approval outbox, as Owen: send a failed card again; close a settled one.'),
      pt('S-23.29', 'The Identity Directory Sync support guide, as Noor, its owner: confirm the draft current on its record.'),
      pt('S-23.30', 'Supplier Price Agent, Summary, as Dmitri: confirm the draft decision record current.'),
      pt('S-23.31', "Invoice Matching's evidence pack, as Owen: add an audit sample as evidence."),
    ],
    links: [
      { label: 'AI activity, Waiting', as: 'priya', to: () => ({ page: 'ai-activity', view: 'waiting' }) },
      { label: 'My work', as: 'priya', to: () => ({ page: 'work' }) },
      { label: 'Approvals, Waiting for me', as: 'hugo', to: () => ({ page: 'approvals', view: 'mine' }) },
      { label: 'Data gaps', as: 'owen', to: () => ({ page: 'data', tab: 'gaps' }) },
      { label: 'Configuration', as: 'yusuf', to: () => ({ page: 'configuration' }) },
      { label: 'Agents, Flagged', as: 'dmitri', to: () => ({ page: 'agents', view: 'flagged' }) },
      { label: 'Documents', as: 'nadia', to: () => ({ page: 'documents' }) },
      { label: 'Admin', as: 'owen', to: () => ({ page: 'admin' }) },
    ],
    useCases: ['P10', 'UC-10', 'UC-01', 'P04', 'UC-04', 'P03', 'P02', 'A-LIFECYCLE', 'P08', 'P02-DATA', 'UC-13', 'P11', 'A-AGENTS', 'UC-03', 'P07', 'UC-14', 'A-MEASURES', 'UC-06', 'P09', 'P01', 'UC-08'],
  },
];

// The freshness figure's words on the Demo guide: "expires within 48 hours" for one request, "expire within 48 hours"
// for none or several (wave 4b QA).
export function soonLabel(count: number, withinHours: number): string {
  return `${count === 1 ? 'expires' : 'expire'} within ${withinHours} ${withinHours === 1 ? 'hour' : 'hours'}`;
}

// Use cases the story shows across the whole run, or does not show, rather than in one scenario.
export const COVERAGE_NOTES: { useCase: string; text: string }[] = [
  { useCase: 'UC-07', text: 'Every scenario, by role: each persona sees only what their roles allow.' },
  { useCase: 'UC-12', text: 'The whole run.' },
];

// The persona a link is for: its own, else the step's first.
export function personaOf(step: StoryStep, link: StoryLink): string {
  return link.as ?? step.personas[0].name;
}

// Names to IDs, from the install's own register. Names compare without case and spacing differences.
export interface StoryLookup {
  apps: ReadonlyMap<string, string>;
  portfolios: ReadonlyMap<string, string>;
}

export const nameKey = (name: string) => name.trim().replace(/\s+/g, ' ').toLowerCase();

export function lookupOf(apps: { name: string; id: string }[], portfolios: { name: string; id: string }[]): StoryLookup {
  const map = (rows: { name: string; id: string }[]) => {
    const m = new Map<string, string>();
    for (const r of rows) if (!m.has(nameKey(r.name))) m.set(nameKey(r.name), r.id.toLowerCase());
    return m;
  };
  return { apps: map(apps), portfolios: map(portfolios) };
}

// The route a link opens, and the name it could not find (it then opens the closest list instead).
// The page's own parameters come along only when the link found its application (a fallback search takes none).
export function resolveLink(link: StoryLink, lookup: StoryLookup | null): { route: AppRoute; missing: string | null; params: Record<string, string> | null } {
  const app = link.app && lookup ? lookup.apps.get(nameKey(link.app)) ?? null : null;
  const portfolio = link.portfolio && lookup ? lookup.portfolios.get(nameKey(link.portfolio)) ?? null : null;
  const missing = link.app && !app ? link.app : link.portfolio && !portfolio ? link.portfolio : null;
  const params = link.params && (!link.app || app) ? link.params : null;
  return { route: link.to({ app, portfolio }), missing, params };
}

// Each use case and the scenarios that show it (by the guide's number), in story order.
export function coverageOf(steps: StoryStep[]): { useCase: string; steps: number[] }[] {
  const byCase = new Map<string, number[]>();
  for (const s of steps) {
    for (const u of s.useCases) {
      const list = byCase.get(u) ?? [];
      if (!list.includes(s.number)) list.push(s.number);
      byCase.set(u, list);
    }
  }
  const rank = (u: string) => (u.startsWith('P') ? 0 : u.startsWith('UC') ? 1 : 2);
  return [...byCase.entries()]
    .map(([useCase, list]) => ({ useCase, steps: list.sort((a, b) => a - b) }))
    .sort((a, b) => rank(a.useCase) - rank(b.useCase) || a.useCase.localeCompare(b.useCase, 'en', { numeric: true }));
}

// The scenario in the guide's address (?step=3), or null.
export function stepOf(value: string | undefined, steps: StoryStep[] = STORY_STEPS): number | null {
  if (!value) return null;
  const n = Number(value);
  return steps.some(s => s.number === n) ? n : null;
}
