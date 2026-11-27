// S-23 Every other action: drafts, answers, rules and records (src/docs/demo/scenarios/S-23-every-other-action.md).
// It plays after S-22, on the same store: each step performs one more write action of the web app for real and checks
// its proof on screen, so every side sheet the web app offers is played in a presenter step and tested here (wave
// 4b; tools/check-actions.mjs holds the list). Claude Code's drafts come from S-23.1 on a local run; on a demo install
// those steps skip, as S-02's do. In read-only mode each step opens its page and checks the control; the write is
// skipped.
import { apiAll, apiData, appId, day, decisionByStatement, intakeByTitle, mcpCall, portfolioId } from '../lib/api';
import { scenario, type Demo } from '../lib/demo';
import { kitLocal, kitReady } from '../lib/kit';
import { CREW, STATEMENT } from '../lib/story';

// What Claude Code drafts in S-23.1. All fictional.
const TIER_LAKE = 'Confirm tier 2 for Warehouse Stock Count: it posts the stock counts that the stores order from.';
const TIER_MILEAGE = 'Confirm tier 2 for Customer Feedback Bot: it answers customers in writing on the company\'s behalf.';
// The director's draft for Priya (a local run; a demo install rebuilt by the kit has it too).
const TIER_HUB = 'Confirm tier 1 for Shift Swap Requests: it only swaps shifts inside one crew, and a supervisor approves each swap.';
const LAKE_CHANGE = 'Field Data Lake 1.4: fleet table refresh';
const KEY_CABINET = 'Depot key cabinet';
const CANTEEN_MENU = 'Depot canteen menu';
const EMERGENCY = 'Inspection Scheduler 3.4.4: reminder flow outage';
const SUPERSEDING = 'Accept that inspection photos stay on devices for up to 3 days while offline.';
const NEW_REQUEST = 'Canteen pre-orders';
const IMPORT_RATIONALE = 'Desk Booking keeps its desk bookings on the parent Workplace domain; they belong to their own subdomain.';
const ROUTE_PURPOSE = 'Plans the routes of the day from the inspections due at each site.';
const EVIDENCE_LABEL = 'Invoice Matching audit sample, November';

const escaped = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

// Picks an option in the select a field label names, in the open side sheet (as in S-22).
async function pickIn(demo: Demo, label: string, option: string) {
  const sheet = demo.page.getByRole('dialog').last();
  await sheet.getByRole('combobox', { name: new RegExp(`^${escaped(label)}( Required)?$`) }).first().click();
  await demo.page.getByRole('option', { name: new RegExp(`^${escaped(option)}`) }).first().click();
  await demo.settle();
}

// The decision with this statement, read as a persona who may see it, or null.
async function decisionId(persona: Parameters<typeof decisionByStatement>[0], statement: string): Promise<string | null> {
  const d = await decisionByStatement(persona, statement);
  return d ? String(d.decision.decisionId).toLowerCase() : null;
}

// A change by its title, in the weeks around today.
async function changeByTitle(title: string) {
  return (await apiAll<any>('hugo', 'get_change_schedule', { from: day(-60), to: day(90) })).find(c => c.title === title) ?? null;
}

// A data location by its name.
async function locationByName(name: string) {
  return (await apiAll<any>('owen', 'find_data_locations', { text: name })).find(l => l.label === name) ?? null;
}

// The page of the newest configuration draft a configuration admin keeps open (draft or submitted).
async function newestDraftOf(persona: 'yusuf'): Promise<string> {
  const data = await apiData<any>(persona, 'list_configuration_changes', { view: 'drafts', top: 50 });
  const mine = (data.drafts ?? []).filter((d: any) => ['draft', 'submitted'].includes(d.state?.key)).sort((a: any, b: any) => String(b.createdOn ?? b.performedBy?.stampedOn ?? '').localeCompare(String(a.createdOn ?? a.performedBy?.stampedOn ?? '')));
  if (!mine.length) throw new Error('no open configuration draft');
  return `records/configuration-draft/${String(mine[0].configurationDraftId).toLowerCase()}`;
}

// Types into the field of the open sheet whose label starts with the words (a label that names the chosen outcome).
async function fillByLabelStart(demo: Demo, start: string, value: string) {
  const ok = await demo.page.evaluate(prefix => {
    const dialogs = Array.from(document.querySelectorAll('[role="dialog"]')).filter(d => d.getBoundingClientRect().width > 0);
    const sheet = dialogs[dialogs.length - 1];
    const label = Array.from(sheet?.querySelectorAll('label') ?? []).find(l => {
      const text = (l.textContent ?? '').trim();
      return text.startsWith(prefix) && !text.startsWith('Why not');
    });
    const control = label ? ((label.htmlFor && document.getElementById(label.htmlFor)) || label.querySelector('input, textarea')) : null;
    if (!control) return false;
    control.setAttribute('data-e2e-field', '1');
    return true;
  }, start);
  if (!ok) throw new Error(`cannot type: no field whose label starts "${start}"`);
  await demo.page.locator('[data-e2e-field="1"]').first().fill(value);
}

// Answers each tier question of the open sheet that has no answer yet with its first option.
async function answerEveryQuestion(demo: Demo) {
  await demo.page.evaluate(() => {
    const dialogs = Array.from(document.querySelectorAll('[role="dialog"]')).filter(d => d.getBoundingClientRect().width > 0);
    const sheet = dialogs[dialogs.length - 1];
    if (!sheet) return;
    for (const group of Array.from(sheet.querySelectorAll('[role="radiogroup"], fieldset'))) {
      if (group.querySelector('[role="radio"][aria-checked="true"], input[type="radio"]:checked')) continue;
      const first = group.querySelector('[role="radio"], input[type="radio"]') as HTMLElement | null;
      first?.click();
    }
  });
  await demo.settle();
}

scenario('S-23', 'Every other action: drafts, answers, rules and records', step => {
  step('S-23.1 Claude Code drafts the rest', async ({ demo }) => {
    await demo.mcp('Claude Code drafts two tier decisions, a change and two data uses for Priya, and two locations with their mappings for Owen', async () => {
      for (const [app, statement] of [['Warehouse Stock Count', TIER_LAKE], ['Customer Feedback Bot', TIER_MILEAGE]] as const) {
        const r = await mcpCall('save_decision_proposal', {
          decisionTypeKey: 'tier', subjectKind: 'business-application', subjectId: await appId(app), statement, proposedOutcomeKey: 'confirm',
          context: `${app} was never given a tier decision of its own.`, reason: 'The tier answers on record suggest tier 2.',
        });
        demo.check(r.ok && /The decision is recorded as proposed\. Nobody is asked yet/.test(String(r.summary)), `Claude Code drafts the tier decision of ${app}`, String(r.summary ?? JSON.stringify(r.error)));
      }
      const change = await mcpCall('save_change_request_draft', {
        title: LAKE_CHANGE, businessApplicationId: await appId('Field Data Lake'), changeTypeKey: 'normal',
        description: 'Refreshes the fleet table every hour instead of every night, so the depot boards show the vans out now.',
        windowStart: `${day(10)}T09:00:00Z`, windowEnd: `${day(10)}T10:00:00Z`,
      });
      demo.check(change.ok, `Claude Code drafts the change "${LAKE_CHANGE}"`, String(change.summary ?? JSON.stringify(change.error)));
      for (const [domainKey, purpose] of [['personnel-records', "Reads each claimant's cost center from the personnel records, to book the mileage."], ['client-orders', 'Shows customers the orders of their own account.']] as const) {
        const app = domainKey === 'personnel-records' ? 'Mileage Claims' : 'Customer Self-Service Hub';
        const r = await mcpCall('save_data_use', { applicationId: await appId(app), domainKey, access: 'read', purpose, reachesAi: false, proposeDecision: false });
        demo.check(r.ok, `Claude Code drafts the data use of ${app} on ${domainKey}`, String(r.summary ?? JSON.stringify(r.error)));
      }
      for (const label of [KEY_CABINET, CANTEEN_MENU]) {
        const slug = label.toLowerCase().replace(/ /g, '-');
        const r = await mcpCall('save_data_location', { items: [{ platformKey: 'sharepoint', targetStageKey: 'production', labelAsSeen: label, linkKindKey: 'sharepoint-location', url: `https://fieldops.example.org/sites/${slug}` }] }, 'owen');
        demo.check(r.ok, `Claude Code drafts the location ${label} for Owen`, String(r.summary ?? JSON.stringify(r.error)));
        const location = await locationByName(label);
        const m = await mcpCall('save_data_mapping', { items: [{ domainKey: label === KEY_CABINET ? 'assets' : 'crew-schedules', locationId: location?.dataLocationId, origin: 'system-of-record' }] }, 'owen');
        demo.check(m.ok, `Claude Code drafts the mapping at ${label} for Owen`, String(m.summary ?? JSON.stringify(m.error)));
      }
    });
    await demo.open('ai-activity?view=waiting', 'priya');
    if (demo.seeded('Claude Code drafted the rest')) await demo.see(TIER_LAKE);
  });

  step('S-23.2 Edit the AI draft and accept it', async ({ demo }) => {
    const id = await decisionId('priya', TIER_LAKE);
    if (!id) {
      demo.missing('Claude Code drafted no tier decision for Warehouse Stock Count on this store (S-23.1)');
      await demo.open('ai-activity?view=waiting', 'priya');
      return;
    }
    await demo.open(`records/decision/${id}`, 'priya');
    await demo.see('An AI draft, not submitted yet');
    await demo.act('Priya changes the reason of the AI draft, then accepts and submits it', async () => {
      demo.writes('save_decision_proposal@pages/work-approvals/decisionSheets#EditProposalSheet', 'submit_record@pages/work-approvals/parts#SubmitSheet');
      await demo.sheet({
        open: { text: 'Edit', scope: 'main' },
        fill: () => demo.fill('Reason', 'The lake only holds copies; the field apps own the data, so tier 2 matches the risk.', 'dialog'),
        submit: 'Save the changes',
        what: 'Edit the AI draft',
      });
      await demo.sheet({
        open: { text: 'Accept and submit', scope: 'main' },
        submit: 'Accept and submit',
        confirm: 'Accept and submit',
        what: 'Accept and submit the AI draft',
        answerShows: ['Submitted. 1 approval is asked.'],
      });
    });
  });

  step('S-23.3 Accept and submit from AI activity', async ({ demo }) => {
    await demo.open('ai-activity?view=waiting', 'priya');
    if (!(await decisionId('priya', TIER_MILEAGE))) {
      demo.missing('Claude Code drafted no tier decision for Customer Feedback Bot on this store (S-23.1)');
      return;
    }
    await demo.see(TIER_MILEAGE);
    await demo.act('Priya accepts and submits the Customer Feedback Bot draft from AI activity', async () => {
      demo.writes('submit_record@pages/work-approvals/parts#SubmitSheet');
      await demo.sheet({
        open: { text: 'Accept and submit', near: TIER_MILEAGE, scope: 'main' },
        submit: 'Accept and submit',
        confirm: 'Accept and submit',
        what: 'Accept and submit from AI activity',
        answerShows: ['Submitted. 1 approval is asked.'],
      });
    });
  });

  step('S-23.4 Accept and submit from My work', async ({ demo }) => {
    await demo.open('work', 'priya');
    if (!(await decisionId('priya', TIER_HUB))) {
      demo.missing('Claude Code drafted no tier decision for Shift Swap Requests on this store (the director)');
      return;
    }
    await demo.see(TIER_HUB);
    await demo.act('Priya accepts and submits the Shift Swap Requests draft from My work', async () => {
      demo.writes('submit_record@pages/work-approvals/parts#SubmitSheet');
      await demo.sheet({
        open: { text: 'Accept and submit', near: TIER_HUB, scope: 'main' },
        submit: 'Accept and submit',
        confirm: 'Accept and submit',
        what: 'Accept and submit from My work',
        answerShows: ['Submitted. 1 approval is asked.'],
      });
      await demo.open('work');
      await demo.notSee(TIER_HUB, { scope: 'main', what: 'My work no longer lists the draft' });
    });
  });

  step('S-23.5 Accept and submit an AI-drafted change', async ({ demo }) => {
    const change = await changeByTitle(LAKE_CHANGE);
    if (!change) {
      demo.missing(`Claude Code drafted no change "${LAKE_CHANGE}" on this store (S-23.1)`);
      await demo.open('changes', 'priya');
      return;
    }
    await demo.open(`records/change-request/${String(change.changeRequestId).toLowerCase()}`, 'priya');
    await demo.act(`Priya accepts and submits "${LAKE_CHANGE}"`, async () => {
      demo.writes('submit_record@pages/changes-records/ChangeActions#SubmitChangeSheet');
      await demo.sheet({
        open: { text: 'Accept and submit', scope: 'main' },
        submit: 'Submit',
        confirm: 'Submit',
        what: 'Accept and submit the AI-drafted change',
        answerShows: ['Submitted. 1 approval is asked.'],
      });
    });
  });

  step('S-23.6 Confirm the AI-drafted use, send it, withdraw the other', async ({ demo }) => {
    const uses = await apiAll<any>('priya', 'list_data_uses', {});
    const first = uses.find(u => u.status?.meaning === 'draft' && /Mileage Claims/.test(JSON.stringify(u)) && /Personnel records/.test(JSON.stringify(u)));
    const second = uses.find(u => u.status?.meaning === 'draft' && /Customer Self-Service Hub/.test(JSON.stringify(u)) && /Client orders/.test(JSON.stringify(u)));
    if (!first || !second) {
      demo.missing('Claude Code drafted no data uses for Priya on this store (S-23.1)');
      await demo.open('data/uses', 'priya');
      return;
    }
    await demo.open(`records/data-use/${String(first.dataUseId).toLowerCase()}`, 'priya');
    await demo.see('Check and confirm');
    await demo.act('Priya confirms the use of Mileage Claims, which sends its decision, then withdraws the other draft', async () => {
      demo.writes('save_data_use@pages/data/sheets#DataUseSheet', 'withdraw_record@pages/work-approvals/parts#WithdrawSheet');
      await demo.sheet({
        open: { text: 'Check and confirm', scope: 'main' },
        submit: 'Confirm the use',
        what: 'Confirm the data use',
        answerShows: ['The data use is confirmed.', 'A data-use decision was opened and sent to the people who answer it.'],
      });
      await demo.open(`records/data-use/${String(second.dataUseId).toLowerCase()}`);
      await demo.sheet({
        open: { text: 'Withdraw', scope: 'main' },
        fill: () => demo.fill('Reason', 'The hub reads the orders through the order status API instead.', 'dialog'),
        submit: 'Withdraw',
        confirm: 'Withdraw',
        what: 'Withdraw the data use draft',
        closes: true,
      });
      await demo.see(/withdrawn/i, { what: 'the record says the draft use is withdrawn' });
    });
  });

  step('S-23.7 Confirm the AI-drafted location and mapping, withdraw the others', async ({ demo }) => {
    const cabinet = await locationByName(KEY_CABINET);
    const menu = await locationByName(CANTEEN_MENU);
    if (!cabinet || !menu) {
      demo.missing('Claude Code drafted no data locations for Owen on this store (S-23.1)');
      await demo.open('data/locations', 'owen');
      return;
    }
    await demo.open(`records/data-location/${String(cabinet.dataLocationId).toLowerCase()}`, 'owen');
    await demo.see('Check and confirm');
    await demo.act(`Owen confirms ${KEY_CABINET} and its mapping, and withdraws ${CANTEEN_MENU} and its mapping`, async () => {
      demo.writes('save_data_location@pages/data/sheets#DataLocationSheet', 'save_data_mapping@pages/data/sheets#DataMappingSheet', 'withdraw_record@pages/work-approvals/parts#WithdrawSheet');
      const location = await demo.sheet({
        open: { text: 'Check and confirm', scope: 'main' },
        preview: 'Check first',
        submit: 'Confirm the location',
        what: 'Confirm the location',
        answerShows: ['1 location is changed. 1 draft is confirmed: no longer a draft.'],
      });
      demo.check(!/Drafts wait for confirmation/.test(location.text), 'the location is no longer a draft', location.text);
      await demo.open(`records/data-location/${String(cabinet.dataLocationId).toLowerCase()}`);
      demo.check(!(await demo.has({ text: 'Check and confirm', scope: 'main' })), 'the location record no longer offers "Check and confirm"');
      await demo.click({ text: 'The mapping', role: 'link', scope: 'main' });
      await demo.at(/\/records\/data-mapping\//, 'the mapping record opens');
      const mapping = await demo.sheet({
        open: { text: 'Check and confirm', scope: 'main' },
        preview: 'Check first',
        submit: 'Confirm the mapping',
        what: 'Confirm the mapping',
        answerShows: ['1 mapping is saved. 1 draft is confirmed: no longer a draft.'],
      });
      demo.check(!/Drafts wait for confirmation/.test(mapping.text), 'the mapping is no longer a draft', mapping.text);
      await demo.page.reload({ waitUntil: 'domcontentloaded' });
      await demo.settle();
      demo.check(!(await demo.has({ text: 'Check and confirm', scope: 'main' })), 'the mapping record no longer offers "Check and confirm"');
      await demo.notSee('Not yet: a draft', { scope: 'main', what: 'the mapping record shows the day it was confirmed' });
      // AI activity counts both as taken forward by Owen, not waiting.
      await demo.api('AI activity counts the confirmed location as taken forward', async () => {
        const items = await apiAll<any>('owen', 'get_ai_activity', { view: 'drafts', mineOnly: true });
        const row = items.find(i => i.record?.label === KEY_CABINET);
        demo.check(row?.state === 'decided' && row?.takenForward?.actorKind === 'person', `AI activity: ${KEY_CABINET} is taken forward by a person`, JSON.stringify(row ?? null).slice(0, 400));
      });
      await demo.open(`records/data-location/${String(menu.dataLocationId).toLowerCase()}`);
      await demo.click({ text: 'The mapping', role: 'link', scope: 'main' });
      await demo.at(/\/records\/data-mapping\//, 'the mapping record of the menu opens');
      await demo.sheet({
        open: { text: 'Withdraw', scope: 'main' },
        fill: () => demo.fill('Reason', 'The canteen menu is not business data.', 'dialog'),
        submit: 'Withdraw',
        confirm: 'Withdraw',
        what: 'Withdraw the mapping draft',
        closes: true,
      });
      await demo.see(/withdrawn/i, { what: 'the record says the draft mapping is withdrawn' });
      await demo.open(`records/data-location/${String(menu.dataLocationId).toLowerCase()}`);
      await demo.sheet({
        open: { text: 'Withdraw', scope: 'main' },
        fill: () => demo.fill('Reason', 'The canteen menu holds no business data.', 'dialog'),
        submit: 'Withdraw',
        confirm: 'Withdraw',
        what: 'Withdraw the location draft',
      });
      await demo.see(/withdrawn|Archived/i, { what: 'the record says the draft location is withdrawn' });
      // A withdrawn location has no gaps: its badges say Withdrawn, never "No data mapped" (wave 4b verify).
      await demo.see('Withdrawn', { scope: 'main', what: 'the withdrawn location reads Withdrawn' });
      await demo.notSee('No data mapped', { scope: 'main', what: 'the withdrawn location shows no gap badge' });
    });
  });

  step('S-23.8 A named deputy declines', async ({ demo }) => {
    await demo.open('approvals?view=mine', 'hugo');
    const handOver = (await apiAll<any>('hugo', 'list_approvals', { view: 'mine' })).find(a => /technical owner of Inspection Scheduler/.test(String(a.decision?.statement ?? '')));
    if (!handOver) {
      demo.missing('no hand-over of Inspection Scheduler asks Hugo on this store (S-04.10)');
      return;
    }
    await demo.act('Hugo, asked by name as the new deputy, rejects the hand-over', async () => {
      demo.writes('answer_approval@components/actions#AnswerSheet');
      await demo.openCard(String(handOver.decision.statement));
      await demo.answer('I cannot take the deputy role this quarter.', {
        answer: 'reject',
        preview: true,
        answerShows: [/Your answer counts toward the approval\./, /The decision is now rejected\./],
      });
    });
  });

  step('S-23.9 A consulted architect gives advice', async ({ demo }) => {
    const crew = await intakeByTitle('elif', CREW);
    await demo.open(crew ? `records/intake-request/${String(crew.intakeRequestId).toLowerCase()}` : 'intake', 'elif');
    if (!crew) {
      demo.missing(`no request "${CREW}" on this store (S-02.7)`);
      return;
    }
    await demo.act('Elif gives her advice on the crew overtime tracker', async () => {
      demo.writes('answer_approval@components/actions#AnswerSheet');
      await demo.openCard(`Accept the intake request: ${CREW}`);
      await demo.answer('Build it in the existing Crew Roster environment, beside the shift data it reads.', {
        preview: true,
        answerShows: [/Your answer is recorded as advice\. A consultation never counts toward the approval\./],
      });
    });
  });

  step('S-23.10 Supersede a decision and add evidence', async ({ demo }) => {
    const id = await decisionId('nadia', STATEMENT.photosRisk);
    if (!id) throw new Error('no risk acceptance about inspection photos (S-04.4)');
    await demo.open(`records/decision/${id}`, 'nadia');
    await demo.see('Supersede');
    await demo.act('Nadia proposes a stricter risk acceptance that supersedes hers and links the evidence', async () => {
      demo.writes('save_decision_proposal@pages/work-approvals/decisionSheets#SupersedeSheet', 'save_link@pages/work-approvals/decisionSheets#EvidenceSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
      await demo.proposeAndSubmit({
        open: { text: 'Supersede', scope: 'main' },
        fill: async () => {
          await demo.fill('Statement', SUPERSEDING, 'dialog');
          await demo.fill('Context', 'The new devices sync over the depot wifi every evening, so photos rarely stay longer than a day.', 'dialog');
          await demo.fill('Reason', 'Three days covers a long weekend in the field; the risk is smaller than before.', 'dialog');
        },
        submit: 'Save the proposal',
        what: 'Propose the superseding decision',
        sendShows: ['Submitted. 1 approval is asked.'],
      });
      await demo.open(`records/decision/${id}`);
      await demo.sheet({
        open: { text: 'Add evidence', scope: 'main' },
        fill: async () => {
          if (await demo.has({ text: 'Kind of link', role: 'combobox', scope: 'dialog', prefix: true })) await pickIn(demo, 'Kind of link', 'Evidence link');
          await demo.fill('Address', 'https://dev.example.org/field-ops/_wiki/wikis/ops/photo-sync-test', 'dialog');
          await demo.fill('Label', 'Photo sync test on the new devices, November', 'dialog');
        },
        submit: 'Add the link',
        what: 'Add the evidence link',
        answerShows: ['The evidence link is saved. It stores an ID, a URL and a label, never a copy.'],
      });
    });
  });

  step('S-23.11 Ask again before a request runs out', async ({ demo }) => {
    await demo.open(`changes/new?app=${await appId('Inspection Scheduler')}`, 'priya');
    await demo.act('Priya submits an emergency change, then asks the change manager again before the two days run out', async () => {
      demo.writes('renew_approval_request@pages/demo-admin/AskAgain#AskAgainSheet', 'save_change_request_draft@pages/changes-records/ChangeDraftSheet#ChangeDraftSheet', 'submit_record@pages/changes-records/ChangeActions#SubmitChangeSheet');
      await demo.sheet({
        open: { text: 'Plan the change', scope: 'main' },
        fill: async () => {
          await demo.fill('Title', EMERGENCY, 'dialog');
          await demo.select('Choose the type', 'Emergency');
          await demo.fill('What changes, and why', 'The reminder flow stopped sending: a fixed connection goes in at once.', 'dialog');
        },
        submit: 'Save draft',
        what: 'Save the emergency change',
        answerShows: ['The change request is saved as a draft (built-in mode).'],
      });
      await demo.sheet({ open: { text: 'Submit', scope: 'main' }, submit: 'Submit', confirm: 'Submit', what: 'Submit the emergency change', answerShows: [/Submitted\. 1 approval is asked\./] });
      await demo.open('work');
      const asked = await demo.sheet({
        open: { text: 'Ask again', near: EMERGENCY, scope: 'main' },
        preview: 'Check first',
        submit: 'Ask again',
        confirm: 'Ask again',
        what: 'Ask again',
        answerShows: [/Decision DEC-\d+ asks the change manager again\. The new request expires in 2 days\./],
      });
      // The expiry is said once in words; the Asked now block gives its date and time (wave 4b verify).
      demo.check(!/The new request expires on/.test(asked.text), 'the answer states the new expiry once, not again as a date', asked.text);
    });
  });

  step('S-23.12 Tier answers and a tier decision on Application 360', async ({ demo }) => {
    await demo.open(`records/business-application/${await appId('Contractor Portal')}?tab=tier`, 'nadia');
    await demo.act('Nadia answers the tier questions of Contractor Portal again; Owen proposes the tier decision', async () => {
      demo.writes('answer_tier_questions@pages/app360/sheets#TierAnswersSheet', 'save_decision_proposal@pages/app360/sheets#ProposeDecisionSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
      const again = await demo.has({ text: 'Answer again', scope: 'main' });
      await demo.sheet({
        open: { text: again ? 'Answer again' : 'Answer the tier questions', scope: 'main' },
        fill: () => answerEveryQuestion(demo),
        preview: 'Check with the engine',
        submit: 'Save the answers',
        what: 'Answer the tier questions',
        answerShows: ['The answers suggest Tier 3. This is a suggestion; only an accepted tier decision sets the tier.'],
      });
      // Under Balanced the builder or the platform admin proposes a tier decision.
      await demo.open(`records/business-application/${await appId('Contractor Portal')}?tab=tier`, 'owen');
      await demo.proposeAndSubmit({
        open: { text: 'Propose a tier decision', scope: 'main' },
        fill: async () => {
          await demo.fill('Context', 'The answers Nadia saved say the portal holds contractor identities and personal data.', 'dialog');
          await demo.fill('Reason', 'Contractors sign in from their own companies now, so the portal keeps tier 3.', 'dialog');
        },
        submit: 'Propose',
        what: 'Propose the tier decision',
        sendShows: ['Submitted. 2 approvals are asked.'],
      });
    });
  });

  step('S-23.13 Propose the design decision', async ({ demo }) => {
    await demo.open(`records/business-application/${await appId('Weather alerts for outdoor jobs')}?tab=lifecycle`, 'elif');
    await demo.act('Elif proposes the design decision of the weather alerts application', async () => {
      demo.writes('save_decision_proposal@pages/app360/sheets#ProposeDecisionSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
      await demo.proposeAndSubmit({
        open: { text: 'Propose the design decision', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'Proposed outcome', '');
          await demo.fill('Decision', 'Send weather alerts from a cloud flow that reads the public forecast and the day\'s outdoor jobs.', 'dialog');
          await demo.fill('Context', 'Crews work outdoors; storms and heat need a warning the morning before.', 'dialog');
          await demo.fill('Reason', 'A flow over the existing job data needs no new store and no new app.', 'dialog');
          await demo.fill('An option weighed', 'A new canvas app that crews open each morning.', 'dialog');
          await demo.fill('Why not', 'Crews would have to remember to open it; an alert reaches them on its own.', 'dialog');
        },
        submit: 'Propose',
        what: 'Propose the design decision',
        sendShows: ['Submitted. 1 approval is asked.'],
      });
    });
  });

  step('S-23.14 Hand over the data owner role', async ({ demo }) => {
    await demo.open(`records/business-application/${await appId('Field Knowledge MCP Server')}?tab=data`, 'kai');
    await demo.see('Hand over');
    await demo.act('Kai hands the data owner role of Field Knowledge MCP Server to Mateo Brandt', async () => {
      demo.writes('propose_owner_change@pages/app360/sheets#HandOverSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
      await demo.proposeAndSubmit({
        open: { text: 'Hand over', scope: 'main' },
        fill: async () => {
          await demo.person('New data owner', 'Mateo Brandt', 'dialog');
          await demo.person('New deputy', 'Ravi Holm', 'dialog');
          await demo.fill('Reason', 'Mateo runs the field knowledge base from next month; Ravi stands in for him.', 'dialog');
        },
        preview: 'Check first',
        submit: 'Propose the hand-over',
        what: 'Hand over the data owner role',
        sendShows: ['Submitted. 3 approvals are asked.'],
      });
    });
  });
  step('S-23.15 It holds no business data', async ({ demo }) => {
    await demo.open(`records/business-application/${await appId('Site Access Badges')}?tab=data`, 'owen');
    await demo.see('It holds no business data');
    await demo.act('Owen marks that Site Access Badges holds no business data', async () => {
      demo.writes('save_application_draft@pages/data-records/App360DataPanel#NoBusinessDataSheet');
      await demo.sheet({
        open: { text: 'It holds no business data', scope: 'main' },
        submit: 'Mark it',
        what: 'Mark that it holds no business data',
        answerShows: ['Site Access Badges is marked as holding no business data. It stays registered.'],
      });
      await demo.open(`records/business-application/${await appId('Site Access Badges')}?tab=data`);
      demo.check(!(await demo.has({ text: 'It holds no business data', scope: 'main' })), 'the Data tab no longer offers "It holds no business data"');
      await demo.api('the application holds no business data', async () => {
        const card = await apiData<any>('owen', 'get_application_card', { businessApplicationId: await appId('Site Access Badges'), sections: ['data'] });
        demo.check(card.data?.holdsNoBusinessData === true, 'Site Access Badges holds no business data', JSON.stringify({ holds: card.data?.holdsNoBusinessData ?? null }));
      });
    });
  });

  step('S-23.16 Data decisions from Data gaps', async ({ demo }) => {
    await demo.open('data/gaps', 'owen');
    await demo.see('DC-05');
    await demo.act('Owen proposes a data-use decision and a retention decision from Data gaps', async () => {
      demo.writes('save_decision_proposal@pages/data/sheets#DataDecisionSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
      await demo.proposeAndSubmit({
        open: { text: 'Propose a decision', scope: 'main', prefix: true },
        fill: async () => {
          await demo.fill('Statement', 'Allow this use of the data for the purpose the application declared.', 'dialog');
          await demo.fill('Context', 'The use is declared and confirmed, and its domain asks for a decision before it goes ahead.', 'dialog');
          await demo.choose('Allow', 'dialog');
          await demo.fill('Reason', 'The application needs the data for its purpose, and the data owner can judge it now.', 'dialog');
        },
        submit: 'Propose',
        what: 'Propose the data-use decision',
        sendShows: ['Submitted. 2 approvals are asked, and 1 person is consulted.'],
      });
      await demo.open('data/gaps');
      if (!(await demo.has({ text: 'Propose a retention decision', scope: 'main' }))) throw new Error('DC-13 offers no retention decision on Data gaps');
      {
        await demo.proposeAndSubmit({
          open: { text: 'Propose a retention decision', scope: 'main' },
          fill: async () => {
            await demo.fill('Statement', 'Keep the timesheets of Legacy Timesheet App read-only for the payroll audit.', 'dialog');
            await demo.fill('Context', 'Legacy Timesheet App is retired; its timesheets stay for the payroll audit.', 'dialog');
            await demo.choose('Keep', 'dialog');
            await demo.fill('Reason', 'Payroll audits look back seven years.', 'dialog');
          },
          submit: 'Propose',
          what: 'Propose the retention decision',
          sendShows: ['Submitted. 1 approval is asked, and 1 person is consulted.'],
        });
      }
    });
  });

  step('S-23.17 A copy outside production', async ({ demo }) => {
    await demo.open('data/mapping', 'hana');
    await demo.see('Copies outside production');
    await demo.act('Hana changes the copy at Customer Test to a masked copy; Owen proposes its copy decision', async () => {
      demo.writes('save_data_mapping@pages/data/sheets#DataMappingSheet', 'save_decision_proposal@pages/data/sheets#DataDecisionSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
      await demo.sheet({
        open: { text: 'Change the copy', near: 'Customer Test', scope: 'main', prefix: true },
        fill: () => demo.choose('Masked copy', 'dialog'),
        submit: 'Save the change',
        what: 'Change the copy',
        answerShows: ['1 mapping is saved. 1 copy outside production needs a decision or breaks the copy rule.'],
      });
      // Under Balanced the platform admin proposes a copy decision.
      await demo.open('data/mapping', 'owen');
      await demo.proposeAndSubmit({
        open: { text: 'Propose a copy decision', near: 'Customer Test', scope: 'main', prefix: true },
        fill: async () => {
          await demo.fill('Statement', 'Allow a masked copy of the client accounts in Customer Test.', 'dialog');
          await demo.fill('Context', 'Testers need real-looking accounts; the copy is masked every night.', 'dialog');
          await demo.choose('Allow', 'dialog');
          await demo.fill('Reason', 'A masked copy keeps the shapes and hides the people.', 'dialog');
        },
        submit: 'Propose',
        what: 'Propose the copy decision',
        sendShows: ['Submitted. 1 approval is asked.'],
      });
    });
  });

  step('S-23.18 Change and end a data use; confirm a stale location', async ({ demo }) => {
    const use = (await apiAll<any>('owen', 'list_data_uses', {})).find(u => /Route Planner/.test(JSON.stringify(u)) && /Inspections/.test(JSON.stringify(u)) && !u.endedOn);
    if (!use) throw new Error('no use of Route Planner on Field operations / Inspections (S-07.11)');
    await demo.open(`records/data-use/${String(use.dataUseId).toLowerCase()}`, 'owen');
    await demo.act('Owen changes the purpose of the Route Planner use and ends it; Hana confirms TIMESQL01 seen', async () => {
      demo.writes('save_data_location@pages/data/sheets#DataLocationSheet', 'save_data_use@pages/data/sheets#DataUseSheet');
      await demo.sheet({
        open: { text: 'Change', scope: 'main' },
        fill: () => demo.fill('Purpose', ROUTE_PURPOSE, 'dialog'),
        submit: 'Save the change',
        what: 'Change the data use',
        answerShows: ['The data use is confirmed.'],
      });
      await demo.see(ROUTE_PURPOSE, { scope: 'main', what: 'the record shows the new purpose' });
      await demo.sheet({
        open: { text: 'End the use', scope: 'main' },
        submit: 'End the use',
        confirm: 'End the use',
        what: 'End the data use',
        closes: true,
      });
      await demo.see('The product lists open uses only, and this one has ended or was withdrawn.', { scope: 'main', what: 'the record says the use has ended' });
      await demo.api('the ended use keeps the new purpose', async () => {
        const ended = (await apiAll<any>('owen', 'list_data_uses', { dataUseIds: [use.dataUseId] }))[0] ?? null;
        demo.check(ended?.purpose === ROUTE_PURPOSE && !!ended?.endedOn, 'the ended use keeps the purpose saved a moment before', JSON.stringify({ purpose: ended?.purpose, endedOn: ended?.endedOn }));
      });
      await demo.open('data/locations', 'hana');
      await demo.sheet({
        open: { text: 'Confirm seen', near: 'TIMESQL01', scope: 'main' },
        submit: 'Confirm seen',
        what: 'Confirm TIMESQL01 seen',
        answerShows: ['1 location is changed.'],
      });
    });
  });

  step('S-23.19 Propose a data domain owner', async ({ demo }) => {
    await demo.open('records/data-domain/commercial', 'hana');
    await demo.act('Hana proposes Clara Voss as data owner of Commercial', async () => {
      demo.writes('propose_owner_change@pages/data/sheets#ProposeDataOwnerSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
      await demo.proposeAndSubmit({
        open: { text: (await demo.has({ text: 'Name one here', scope: 'main' })) ? 'Name one here' : 'Propose an owner', scope: 'main' },
        fill: async () => {
          await demo.person('New holder', 'Clara Voss', 'dialog');
          await demo.fill('Reason', 'Clara leads the sales desk that writes every proposal.', 'dialog');
        },
        submit: 'Propose',
        what: 'Propose the data owner',
        sendShows: ['Submitted. 2 approvals are asked.'],
      });
    });
  });

  step('S-23.20 Import data domains, change the draft, submit it', async ({ demo }) => {
    await demo.open('configuration', 'yusuf');
    await demo.see('Import data domains');
    await demo.act('Yusuf imports a Desk bookings subdomain, changes the draft and submits it', async () => {
      demo.writes('import_data_taxonomy@pages/configuration/importSheet#ImportDomainsSheet', 'propose_configuration_change@pages/configuration/sheets#DraftBuilderSheet', 'submit_record@pages/work-approvals/parts#SubmitSheet');
      await demo.sheet({
        open: { text: 'Import data domains', scope: 'main' },
        fill: async () => {
          await demo.fill('The CSV text', 'label,parent\nDesk bookings,workplace', 'dialog');
          await demo.fill('Why the import is wanted', IMPORT_RATIONALE, 'dialog');
        },
        submit: 'Save the draft',
        confirm: 'Save the draft',
        what: 'Import the data domains',
        answerShows: ['The configuration draft is stored: 1 domain added, 0 changed, 0 unchanged.'],
      });
      await demo.open(await newestDraftOf('yusuf'));
      await demo.sheet({
        open: { text: 'Change the draft', scope: 'main' },
        fill: () => demo.fill('Why the change is wanted', `${IMPORT_RATIONALE} Desk Booking moves its rows there once it is in force.`, 'dialog'),
        submit: 'Save the draft again',
        what: 'Change the draft',
        // The sheet opens the new draft it saved, so the step reads the draft page after it.
        closes: true,
      });
      await demo.see('Desk Booking moves its rows there once it is in force.', { what: 'the draft shows the new reason' });
      await demo.sheet({ open: { text: 'Submit', scope: 'main' }, submit: 'Submit', confirm: 'Submit', what: 'Submit the draft', answerShows: ['Submitted. 1 approval is asked.'] });
    });
  });

  step('S-23.21 Propose a rule change from a page, then withdraw it', async ({ demo }) => {
    await demo.open('roles?role=reader', 'yusuf');
    await demo.act('Yusuf proposes a binding change from Roles and access, then withdraws the draft', async () => {
      demo.writes('propose_configuration_change@pages/configuration/sheets#DraftBuilderSheet', 'withdraw_record@pages/work-approvals/parts#WithdrawSheet');
      await demo.sheet({
        open: { text: 'Propose a binding change', scope: 'main' },
        fill: () => demo.fill('Why the change is wanted', 'Contractors who read the register get their own group next quarter.', 'dialog'),
        submit: 'Save the draft',
        what: 'Propose the binding change',
        answerShows: ['The configuration draft is stored. Nothing is in force until a person accepts its decision on the decision page. 1 row changes.'],
      });
      await demo.open(await newestDraftOf('yusuf'));
      // The binding is named by its role, and a row proposed as it is reads "Keep" (QA walk, fix round 2).
      await demo.see('Keep the group binding of Reader as it is.', { scope: 'main', what: 'the draft names the binding by its role' });
      await demo.sheet({
        open: { text: 'Withdraw', scope: 'main' },
        fill: () => demo.fill('Reason', 'The contractor group waits for the new quarter.', 'dialog'),
        submit: 'Withdraw',
        confirm: 'Withdraw',
        what: 'Withdraw the configuration draft',
        answerShows: ['Withdrawn. No approval was waiting.'],
      });
      await demo.see('The contractor group waits for the new quarter. It changed nothing.', { scope: 'main', what: 'the draft says why it ended, with one full stop' });
    });
  });

  step('S-23.22 Agents: decide on a flagged agent, request a security review', async ({ demo }) => {
    await demo.open('agents?view=flagged', 'dmitri');
    await demo.act('Dmitri proposes a decision on a flagged agent and requests a security review', async () => {
      demo.writes('request_security_review@pages/ai-audit/agentSheets#SecurityReviewSheet', 'save_decision_proposal@pages/ai-audit/agentSheets#AgentDecisionSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
      await demo.proposeAndSubmit({
        open: { text: 'Decide on a flagged agent', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'Agent', '');
          await pickIn(demo, 'What you propose', '');
          await demo.fill('Statement', 'Keep the agent running: its maker answered the flag.', 'dialog');
          await demo.fill('Why it was flagged', 'It was published without a security review.', 'dialog');
          await demo.fill('Reason', 'Its maker answered the flag: the agent reads public product pages only.', 'dialog');
        },
        submit: 'Propose',
        what: 'Propose the decision on the flagged agent',
        sendShows: ['Submitted. 1 approval is asked.'],
      });
      await demo.open('agents');
      await demo.sheet({
        open: { text: 'Request a security review', scope: 'main', prefix: true },
        fill: async () => {
          await demo.fill('What to look at', 'The data the agent reads, the tools it may call, and what it may write back.', 'dialog');
          await demo.setValue('Wanted by', day(14), 'dialog');
        },
        submit: 'Request the review',
        what: 'Request the security review',
        answerShows: [/The security review of .{1,60} is requested\. It is a request, never a decision/],
      });
    });
  });

  step('S-23.23 Reviews: propose the outcome and add evidence', async ({ demo }) => {
    const reviews = await apiAll<any>('elif', 'list_reviews', { businessApplicationId: await appId('Contractor Portal') });
    const review = reviews.find(r => r.reviewType?.key === 'architecture' && r.status?.meaning === 'due');
    if (!review) throw new Error('no architecture review of Contractor Portal is due (S-22.3)');
    await demo.open(`records/review/${String(review.reviewId).toLowerCase()}`, 'elif');
    await demo.act('Elif proposes the outcome of the Contractor Portal review and adds evidence', async () => {
      demo.writes('save_decision_proposal@pages/owners-reviews/ReviewSheets#ProposeOutcomeSheet', 'save_link@pages/owners-reviews/ReviewSheets#AddEvidenceSheet', 'submit_record@pages/owners-reviews/OwnerSheets#SubmitDecisionSheet');
      await demo.proposeAndSubmit({
        open: { text: 'Propose the outcome', scope: 'main' },
        fill: async () => {
          // The reason's label names the outcome once one is chosen, so the reason comes first.
          await demo.fill('Why this outcome', 'Their own accounts end with their contracts; no guest account stays behind.', 'dialog');
          await pickIn(demo, 'Outcome', '');
          await demo.fill('Decision', 'Contractors sign in with their own company accounts through the portal\'s external sign-in.', 'dialog');
          await demo.fill('What the review looked at', 'The sign-in flow, the guest account settings and the access review of the portal.', 'dialog');
          await demo.fill('Option 1', 'Guest accounts in the company tenant', 'dialog');
          await demo.fill('Why not', 'Guest accounts stay after a contract ends unless someone removes them.', 'dialog');
        },
        submit: 'Propose',
        what: 'Propose the outcome',
        via: 'page',
        sendShows: ['Submitted. 1 approval is asked.'],
      });
      await demo.open(`records/review/${String(review.reviewId).toLowerCase()}`);
      await demo.sheet({
        open: { text: 'Add evidence', scope: 'main' },
        fill: async () => {
          if (await demo.has({ text: 'Kind of link', role: 'combobox', scope: 'dialog', prefix: true })) await pickIn(demo, 'Kind of link', 'Evidence link');
          await demo.fill('Link', 'https://dev.example.org/contractor-portal/_wiki/wikis/portal/sign-in-review', 'dialog');
          await demo.fill('Label', 'Sign-in design review notes', 'dialog');
        },
        submit: 'Add the link',
        what: 'Add evidence to the review',
        answerShows: ['The evidence link is saved. It stores an ID, a URL and a label, never a copy.'],
      });
    });
  });

  step('S-23.24 Documents: confirm current, mark stale, update the link', async ({ demo }) => {
    await demo.open('documents?view=drafts', 'nadia');
    await demo.act('Nadia confirms a draft current, marks a runbook stale and updates its link', async () => {
      demo.writes('confirm_document_checked@pages/Records#CheckedSheet', 'confirm_document_checked@pages/guide-log-docs/DocumentSheets#ConfirmDocumentSheet', 'record_document_link@pages/Records#DocumentLinkSheet');
      await demo.sheet({ open: { text: 'Confirm current', scope: 'main' }, submit: 'Confirm current', what: 'Confirm the draft current', answerShows: [/The check is recorded, as reported\./] });
      const status = await apiData<any>('nadia', 'get_documentation_status', { businessApplicationId: await appId('Inspection Scheduler') });
      const runbook = (status.applications?.[0]?.documents ?? []).find((d: any) => d.documentTypeKey === 'runbook');
      if (!runbook) throw new Error('Inspection Scheduler has no runbook link');
      await demo.open(`records/document-link/${String(runbook.documentLinkId).toLowerCase()}`);
      await demo.sheet({ open: { text: 'Mark stale', scope: 'main' }, fill: () => demo.fill('Note', 'The notices moved to the new flow; the runbook still names the old one.', 'dialog'), submit: 'Mark stale', what: 'Mark the runbook stale', answerShows: ['The document is marked stale, as reported.'] });
      await demo.sheet({ open: { text: 'Update the link', scope: 'main' }, fill: () => demo.fill('Link', 'https://dev.example.org/customer-services/_wiki/wikis/customer-notice-sender/runbook-v2', 'dialog'), submit: 'Save the link', what: 'Update the runbook link', answerShows: ['The document link is recorded with status current, as reported. The product never writes the document.'] });
    });
  });

  step('S-23.25 Decision log and Measures proposals', async ({ demo }) => {
    await demo.open('decisions', 'mateo');
    await demo.act('Mateo proposes a portfolio decision from the decision log and sends it for approval; Mateo records a decision about a slide-back signal; Elif proposes an exception', async () => {
      demo.writes('save_decision_proposal@pages/guide-log-docs/DecisionSheets#ProposeDecisionSheet', 'save_decision_proposal@pages/guide-log-docs/DecisionSheets#ProposeExceptionSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
      await demo.proposeAndSubmit({
        open: { text: 'Propose a decision', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'Decision type', 'Portfolio');
          await demo.fill('Application', 'Fleet Maint', 'dialog');
          await demo.click({ text: 'Fleet Maintenance Log', prefix: true, scope: 'dialog' });
          await demo.fill('Statement', 'Keep Fleet Maintenance Log: the tire checks it records are due every month.', 'dialog');
          await demo.fill('Context', 'The depot crews log every tire check in it.', 'dialog');
          await pickIn(demo, 'Proposed outcome', 'Keep');
          await demo.fill('Reason', 'It is used daily and costs little to run.', 'dialog');
        },
        submit: 'Propose',
        what: 'Propose the portfolio decision',
        sendShows: ['Submitted. 1 approval is asked.'],
      });
      await demo.open('measures', 'mateo');
      await demo.proposeAndSubmit({
        open: { text: 'Record a decision about signal', scope: 'main', prefix: true },
        fill: async () => {
          await pickIn(demo, 'Decision type', 'Portfolio');
          await demo.fill('Application', 'Fleet Maint', 'dialog');
          await demo.click({ text: 'Fleet Maintenance Log', prefix: true, scope: 'dialog' });
          await demo.fill('Statement', 'Hold a change board slot twice a week, so normal changes stop waiting for the monthly meeting.', 'dialog');
          await pickIn(demo, 'Proposed outcome', '');
          await demo.fill('Context', 'The signal "Changes wait for a meeting" fired this quarter.', 'dialog');
          await demo.fill('Reason', 'A month of waiting for a meeting pushes people to work around the change process.', 'dialog');
        },
        submit: 'Propose',
        what: 'Record a decision about a signal',
        sendShows: ['Submitted. 1 approval is asked.'],
      });
      await demo.open('decisions', 'elif');
      await demo.proposeAndSubmit({
        open: { text: 'Propose an exception', scope: 'main' },
        fill: async () => {
          await demo.fill('Application', 'Training Rec', 'dialog');
          await demo.click({ text: 'Training Records', prefix: true, scope: 'dialog' });
          await demo.fill('Statement', 'Grant an exception: Training Records keeps its own sign-in page until the portal moves in March.', 'dialog');
          await demo.fill('Standard it departs from', 'Every application signs in through the shared company sign-in page.', 'dialog');
          await demo.fill('Context', 'Training Records has its own sign-in page for the external trainers.', 'dialog');
          await pickIn(demo, 'Proposed outcome', '');
          await demo.setValue('Review on', day(90), 'dialog');
          await demo.fill('Reason', 'The move is planned with the portal; a second change now would confuse the trainers.', 'dialog');
          await demo.fill('An option weighed', 'Move Training Records to the shared sign-in page now', 'dialog');
          await demo.fill('Why not', 'The trainers would move twice in three months.', 'dialog');
        },
        submit: 'Propose',
        what: 'Propose the exception',
        sendShows: ['Submitted. 1 approval is asked.'],
      });
    });
  });

  step('S-23.26 Portfolio and retirement decisions', async ({ demo }) => {
    await demo.open(`portfolios/${await portfolioId('Field operations')}`, 'mateo');
    await demo.act('Mateo proposes a portfolio decision from the review pack, and the retirement steps of the first-aid kit checks', async () => {
      demo.writes('save_change_request_draft@pages/changes-records/ChangeDraftSheet#ChangeDraftSheet', 'save_decision_proposal@pages/pmo-portfolio/sheets#ProposeDecisionSheet', 'save_link@pages/changes-records/parts#SaveLinkSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
      await demo.proposeAndSubmit({
        open: { text: 'Propose', near: 'No portfolio decision', scope: 'main' },
        fill: async () => {
          await demo.fill('Statement', 'Keep it: it is in daily use and fits the field operations plan.', 'dialog');
          await pickIn(demo, 'Proposed outcome', 'Keep');
          await demo.fill('Context', 'The portfolio review found it in daily use.', 'dialog');
          await demo.fill('Reason', 'It is in daily use and fits the field operations plan.', 'dialog');
        },
        submit: 'Propose',
        what: 'Propose from the review pack',
        sendShows: ['Submitted. 1 approval is asked.'],
      });
      const firstAid = await appId('Depot first-aid kit checks').catch(() => null);
      if (!firstAid) throw new Error('Depot first-aid kit checks is not registered on this store (S-20.5)');
      await demo.open(`retirement/${firstAid}`);
      await demo.proposeAndSubmit({
        open: { text: 'Propose a portfolio decision', scope: 'main' },
        fill: async () => {
          await demo.fill('Statement', 'Retire Depot first-aid kit checks: the kits are now checked in Asset Register.', 'dialog');
          if (await demo.has({ text: 'Proposed outcome', role: 'combobox', scope: 'dialog', prefix: true })) await pickIn(demo, 'Proposed outcome', 'Retire');
          await demo.fill('Context', 'Asset Register now holds each first-aid kit and its checks.', 'dialog');
          await demo.fill('Reason', 'The kits are now checked in Asset Register.', 'dialog');
        },
        submit: 'Propose',
        what: 'Propose the retirement decision',
        sendShows: ['Submitted. 1 approval is asked.'],
      });
      await demo.open(`retirement/${firstAid}`, 'hugo');
      if (!(await demo.has({ text: 'Draft the removal change', scope: 'main' }))) throw new Error('the retirement tracker offers no "Draft the removal change"');
      {
        await demo.sheet({
          open: { text: 'Draft the removal change', scope: 'main' },
          fill: async () => {
            await demo.fill('Title', 'Remove the Depot first-aid kit checks app', 'dialog');
            await demo.select('Choose the type', 'Normal');
            await demo.fill('What changes, and why', 'The app is switched off and removed; the kits are checked in Asset Register.', 'dialog');
            await demo.setValue('Window starts', `${day(21)}T09:00`, 'dialog');
            await demo.setValue('Window ends', `${day(21)}T10:00`, 'dialog');
          },
          submit: 'Save draft',
          what: 'Draft the removal change',
          // The sheet closes into the tracker, which then lists the removal change.
          closes: true,
        });
        await demo.see('Remove the Depot first-aid kit checks app', { what: 'the tracker lists the removal change' });
      }
      await demo.sheet({
        open: { text: 'Add evidence', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'Kind', 'Evidence link');
          await demo.fill('Link', 'https://dev.example.org/field-ops/_wiki/wikis/ops/first-aid-kits-in-asset-register', 'dialog');
          await demo.fill('Label', 'First-aid kits move into Asset Register', 'dialog');
        },
        submit: 'Add evidence',
        what: 'Add evidence on the tracker',
        answerShows: ['The evidence link is saved. It stores an ID, a URL and a label, never a copy.'],
      });
    });
  });

  step('S-23.27 A new intake draft, then withdraw it', async ({ demo }) => {
    await demo.open('intake/new', 'lena');
    await demo.act(`Lena saves "${NEW_REQUEST}" as a draft and withdraws it`, async () => {
      demo.writes('save_intake_draft@pages/intake/Wizard#Wizard', 'withdraw_record@pages/intake/sheets#WithdrawSheet');
      await demo.fill('Title', NEW_REQUEST, 'page');
      await demo.fill('The need', 'Crews pre-order lunch from the depot canteen before they leave for the day.', 'page');
      await demo.sheet({ open: { text: 'Save draft', scope: 'main' }, submit: 'Save draft', what: 'Save the request as a draft', answerShows: ['The intake request is saved as a draft. It goes nowhere until it is submitted.'] });
      await demo.sheet({
        open: { text: 'Withdraw', scope: 'main' },
        fill: () => demo.fill('Reason', 'The canteen takes orders on its own tablet now.', 'dialog'),
        submit: 'Withdraw',
        confirm: 'Withdraw',
        what: 'Withdraw the draft request',
        answerShows: ['Withdrawn. No approval was waiting.'],
      });
    });
  });

  step('S-23.28 The approval outbox: send again, close', async ({ demo }) => {
    await demo.open('admin', 'owen');
    if (demo.readOnly) {
      demo.skip('read only: the kit would fail two approval cards');
      return;
    }
    if (!kitReady()) {
      demo.missing('the approval outbox needs a failed card, which only the kit makes on a local run (pass --kit-folder)');
      return;
    }
    const open = (await apiAll<any>('owen', 'list_approvals', { view: 'open' }))[0];
    const pending = open ? await apiData<any>('owen', 'get_decision', { decisionId: open.decision.decisionId }) : null;
    const settled = await decisionByStatement('owen', STATEMENT.exportChange);
    if (!pending || !settled) throw new Error('no open approval request or settled decision to fail a card for');
    kitLocal('fail-card', ['--decision', String(pending.decision.recordNumber)]);
    kitLocal('fail-card', ['--decision', String(settled.decision.recordNumber), '--settled']);
    await demo.open('admin');
    await demo.act('Owen sends one failed card again and closes the card of a settled request', async () => {
      demo.writes('retry_approval_card@pages/admin/AdminPage#CardSheet');
      await demo.sheet({
        open: { text: `Send again: ${pending.decision.statement}`, scope: 'main', prefix: true },
        preview: 'Check first',
        submit: 'Send again',
        confirm: 'Send again',
        what: 'Send the card again',
        answerShows: [/The card goes back to the approval runner, which sends it again on its next pull\./],
      });
      await demo.sheet({
        open: { text: 'Close the card:', scope: 'main', prefix: true },
        preview: 'Check first',
        submit: 'Close the card',
        confirm: 'Close the card',
        what: 'Close the card',
        answerShows: [/so its outbox row is closed\./],
      });
    });
  });

  step('S-23.29 Confirm a draft document on its record', async ({ demo }) => {
    const status = await apiData<any>('noor', 'get_documentation_status', { businessApplicationId: await appId('Identity Directory Sync') });
    const guide = (status.applications?.[0]?.documents ?? []).find((d: any) => d.documentTypeKey === 'support-guide');
    if (!guide) throw new Error('Identity Directory Sync has no support guide link');
    await demo.open(`records/document-link/${String(guide.documentLinkId).toLowerCase()}`, 'noor');
    await demo.see('Confirm it is current', { scope: 'main', what: 'the draft support guide offers "Confirm it is current"' });
    await demo.act('Noor, its document owner, confirms the support guide of Identity Directory Sync current', async () => {
      demo.writes('record_document_link@pages/Records#ConfirmCurrentSheet');
      await demo.sheet({
        open: { text: 'Confirm it is current', scope: 'main' },
        preview: 'Check first',
        submit: 'Confirm current',
        what: 'Confirm the draft current on its record',
        answerShows: ['The document link is recorded with status current, as reported.'],
      });
      await demo.page.reload({ waitUntil: 'domcontentloaded' });
      await demo.settle();
      demo.check(!(await demo.has({ text: 'Confirm it is current', scope: 'main' })), 'the record no longer offers "Confirm it is current"');
      await demo.see(/Current/, { scope: 'main', what: 'the record reads Current' });
    });
  });

  step('S-23.30 Confirm a draft document on Application 360', async ({ demo }) => {
    await demo.open(`records/business-application/${await appId('Supplier Price Agent')}`, 'dmitri');
    await demo.see('Confirm it is current', { scope: 'main', what: 'the draft decision record offers "Confirm it is current"' });
    await demo.act('Dmitri, its document owner, confirms the architecture decision record of Supplier Price Agent current', async () => {
      demo.writes('record_document_link@pages/app360/sheets#DocumentCurrentSheet');
      await demo.sheet({
        open: { text: 'Confirm it is current', scope: 'main' },
        preview: 'Check first',
        submit: 'Confirm current',
        what: 'Confirm the draft current on Application 360',
        answerShows: ['The document link is recorded with status current, as reported.'],
      });
      await demo.open(`records/business-application/${await appId('Supplier Price Agent')}`);
      demo.check(!(await demo.has({ text: 'Confirm it is current', scope: 'main' })), 'Application 360 no longer offers "Confirm it is current"');
    });
  });

  step('S-23.31 Add evidence on the evidence pack', async ({ demo }) => {
    await demo.open(`evidence/business-application/${await appId('Invoice Matching')}`, 'owen');
    await demo.see('Add evidence', { scope: 'main', what: 'the evidence pack offers "Add evidence"' });
    await demo.act('Owen adds an audit sample to the evidence pack of Invoice Matching', async () => {
      demo.writes('save_link@pages/changes-records/parts#SaveLinkSheet');
      await demo.sheet({
        open: { text: 'Add evidence', scope: 'main' },
        fill: async () => {
          if (await demo.has({ text: 'Kind', role: 'combobox', scope: 'dialog', prefix: true })) await pickIn(demo, 'Kind', 'Evidence link');
          await demo.fill('Link', 'https://dev.example.org/finance/_wiki/wikis/ap/invoice-matching-audit', 'dialog');
          await demo.fill('Label', EVIDENCE_LABEL, 'dialog');
        },
        preview: 'Check first',
        submit: 'Add evidence',
        what: 'Add evidence on the evidence pack',
        answerShows: ['The evidence link is saved. It stores an ID, a URL and a label, never a copy.'],
      });
      await demo.open(`evidence/business-application/${await appId('Invoice Matching')}`);
      await demo.see(EVIDENCE_LABEL, { scope: 'main', what: 'the evidence pack lists the new link' });
    });
  });
});
