// S-02 Maker and Claude Code: governance inside the AI build loop (src/docs/demo/scenarios/S-02-ai-build-loop.md).
// Claude Code's part goes to the MCP endpoint as the development identity "priya" (Claude Code acting for Priya
// Castell), with the tool calls claude-code.md names. Without Claude Code (a Demo install, or read only) the browser
// steps read the seeded AI draft "Toolbox talk sign-off" instead, and the writes are skipped.
import { apiData, intakeByTitle, mcpCall } from '../lib/api';
import { scenario, type Demo } from '../lib/demo';
import { CREW, TOOLBOX } from '../lib/story';

// The intake request the wizard steps work on: Claude Code's new draft, or the seeded one when Claude Code is not here.
async function draftUnderTest(demo: Demo) {
  const crew = await intakeByTitle('priya', CREW, 'mine');
  if (crew) return crew;
  return demo.readOnly || demo.mode === 'demo' ? intakeByTitle('priya', TOOLBOX, 'mine') : null;
}

async function wizardAt(demo: Demo, step: number) {
  const draft = await draftUnderTest(demo);
  if (!draft) throw new Error(`no "${CREW}" request: S-02.1 did not run on this store`);
  await demo.open(`records/intake-request/${String(draft.intakeRequestId).toLowerCase()}/edit`, 'priya');
  if (step > 1) {
    await demo.page.locator('nav[aria-label="Steps of the request"] button').nth(step - 1).click();
    await demo.settle();
  }
  return draft;
}

scenario('S-02', 'Maker and Claude Code: governance inside the AI build loop', step => {
  step('S-02.1 Claude Code drafts the request', async ({ demo }) => {
    await demo.mcp('Claude Code drafts the crew overtime tracker for Priya', async () => {
      const dup = await mcpCall('check_duplicates', { text: 'A crew overtime tracker for my team: crew leaders log the overtime of each shift.', proposedKindKey: 'power-platform-app' });
      demo.check(dup.ok && /registered applications look similar\. Consider reuse before a new intake\./.test(String(dup.summary)), 'check_duplicates: "N registered applications look similar. Consider reuse before a new intake."', String(dup.summary ?? JSON.stringify(dup.error)));
      const names = (dup.data?.matches ?? []).map((m: any) => m.name);
      demo.check(names.includes('Crew Roster') && names.includes('Crew Shift Mobile'), 'check_duplicates suggests Crew Roster and Crew Shift Mobile', JSON.stringify(names));
      const questions = await mcpCall('get_tier_questions', { applicationKindKey: 'power-platform-app' });
      demo.check(questions.ok && String(questions.summary).startsWith('4 tier questions apply'), 'get_tier_questions: "4 tier questions apply."', String(questions.summary ?? JSON.stringify(questions.error)));
      // When Claude Code names no kind, the same four questions apply (the script names both answers).
      const all = await mcpCall('get_tier_questions', {});
      demo.check(all.ok && String(all.summary).startsWith('4 tier questions apply.'), 'get_tier_questions with no kind: "4 tier questions apply."', String(all.summary ?? JSON.stringify(all.error)));
      const draft = await mcpCall('save_intake_draft', {
        title: CREW,
        need: 'Crew leaders log overtime on paper sheets, and payroll types them in again at month end.',
        proposedKindKey: 'power-platform-app',
        horizonKey: 'next',
        usesAi: false,
        clientRequestId: 'e2e:S-02.1:crew-overtime',
      });
      demo.check(draft.ok && String(draft.summary).includes('The intake request is saved as a draft. It goes nowhere until it is submitted.'), 'save_intake_draft: saved as a draft that goes nowhere until submitted', String(draft.summary ?? JSON.stringify(draft.error)));
      demo.check(draft.data?.isAiCreated === true && draft.data?.performedBy?.actorKind === 'ai-client-for-person', 'the draft is marked AI-made, by Claude Code for Priya', JSON.stringify(draft.data?.performedBy ?? draft.error));
      demo.check(/\/app\/records\/intake-request\/[0-9a-f-]{36}/i.test(JSON.stringify(draft)), 'the answer gives the record link /app/records/intake-request/<id>', JSON.stringify(draft.data ?? {}).slice(0, 400));
    });
    await demo.as('priya');
    await demo.open('ai-activity');
  });

  step('S-02.2 Claude Code cannot approve', async ({ demo }) => {
    await demo.mcp('Claude Code is asked to approve the request', async () => {
      const crew = await intakeByTitle('priya', CREW, 'mine');
      const approve = await mcpCall('approve_intake_request', { intakeRequestId: crew?.intakeRequestId ?? '00000000-0000-4000-8000-000000000000' });
      demo.check(!approve.ok && approve.error?.code === 'not_found', 'a made-up approve tool answers not_found: no tool approves', JSON.stringify(approve.error ?? approve.data));
      demo.check(/No tool named approve_intake_request is served to you here\. Call tools\/list for the tools you may use\./.test(String(approve.error?.message)), 'the refusal says no such tool is served and names tools/list', String(approve.error?.message));
    });
    await demo.as('priya');
    await demo.open('ai-activity?view=refused');
  });

  step('S-02.3 The draft waits for a person', async ({ demo }) => {
    await demo.as('priya');
    await demo.open('work');
    await demo.see('AI drafts waiting for you');
    const draft = (await intakeByTitle('priya', CREW, 'mine')) ? CREW : TOOLBOX;
    if (draft === CREW || demo.seeded(`My work shows "${TOOLBOX}"`)) {
      await demo.see(draft, { what: `My work lists "${draft}"` });
      await demo.see('An AI client drafted this for you. It goes nowhere until it is submitted.');
      await demo.see(new RegExp(`${draft}.{0,300}Drafted by Claude Code for Priya Castell`), { what: `"${draft}" is drafted by Claude Code for Priya Castell` });
      await demo.see('Open in the wizard');
      await demo.see('Accept and submit');
    }
  });

  step('S-02.4 The wizard and the reuse step', async ({ demo }) => {
    await demo.act('Priya opens the AI draft in the wizard and goes to the reuse step', async () => {
      await demo.as('priya');
      await demo.open('work');
      await demo.click({ text: 'Open in the wizard', near: CREW, scope: 'main' });
      await demo.at(/\/records\/intake-request\/[0-9a-f-]+\/edit$/, 'the draft opens in the intake wizard');
      await demo.see('Drafted by Claude Code for Priya Castell');
      await demo.see('Review and submit');
      await demo.see('Edit it first');
      await demo.click({ text: 'Next', scope: 'main' });
      await demo.see(/Step 2 of 5/, { what: 'the wizard moves to step 2, Reuse' });
      await demo.see('Crew Roster', { scope: 'main', what: 'the reuse step shows Crew Roster' });
      await demo.see('Crew Shift Mobile', { scope: 'main', what: 'the reuse step shows Crew Shift Mobile' });
      await demo.see('It is about this app');
    });
  });

  step('S-02.5 The tier questions', async ({ demo }) => {
    demo.writes('answer_tier_questions@pages/intake/Wizard#TierAnswersSheet');
    await demo.act('Priya answers the four tier questions and saves them', async () => {
      await wizardAt(demo, 3);
      await demo.see(/Step 3 of 5/, { what: 'the wizard is on step 3, Tier questions' });
      for (const answer of ['Changes shared business data as the signed-in person', 'Little', 'The builder or a small team', 'Internal data only']) await demo.choose(answer);
      await demo.see(/YOUR ANSWERS POINT TO Tier 2/i, { what: 'the answers point to Tier 2' });
      await demo.see('4 of 4 questions answered');
      await demo.see('Set by: Does it change data?; Does it touch sensitive data?', { what: 'the tier names the answers that drive it' });
      await demo.sheet({
        open: { text: 'Save tier answers', scope: 'main' },
        submit: 'Save answers',
        what: 'Save tier answers',
        answerShows: ['The answers suggest Tier 2. This is a suggestion; only an accepted tier decision sets the tier.'],
      });
    });
  });

  step('S-02.6 What the tier needs', async ({ demo }) => {
    await demo.act('Priya reads what Tier 2 needs', async () => {
      await wizardAt(demo, 4);
      await demo.see('What Tier 2 for Power Platform app needs');
      await demo.see(/GATES.{0,300}Design.{0,300}G2/, { what: 'the gates: Design (G2)' });
      await demo.see(/Go-live.{0,300}G9/, { what: 'the gates: Go-live (G9)' });
      await demo.see('APPROVALS');
      await demo.see(/DOCUMENTS.{0,120}Runbook/, { what: 'the documents: the runbook' });
      await demo.see('Wiki page');
      await demo.see(/REVIEWS.{0,200}Access check.{0,40}every 180 days/i, { what: 'the reviews: an access check every 180 days' });
      await demo.see(/Benefit review.{0,40}every 365 days/i, { what: 'the reviews: a benefit review every 365 days' });
      await demo.see('THE GUIDELINES BEHIND THESE');
    });
  });

  step('S-02.7 The person accepts and submits', async ({ demo }) => {
    demo.writes('submit_record@pages/intake/sheets#SubmitSheet');
    await demo.act('Priya accepts the AI draft and submits it', async () => {
      const draft = await wizardAt(demo, 5);
      await demo.see(/Step 5 of 5/);
      await demo.sheet({
        open: { text: 'Accept and submit', scope: 'main' },
        preview: 'Check first',
        previewShows: ['Nothing was written: send it to make the change.', /WOULD BE ASKED.{0,80}Platform admin/, /Architect \(consulted, advice only/, /PMO \(consulted, advice only/, /Security reviewer \(consulted, advice only/],
        submit: 'Accept and submit',
        what: 'Accept and submit',
        answerShows: ['Submitted. 1 approval is asked, and 3 people are consulted. Only people answer, on the decision page.', /ASKED NOW/, /Platform admin/, /Architect/, /PMO/, /Security reviewer/, /consulted/i],
        close: false,
      });
      await demo.closeSheet();
      await demo.api('the crew overtime tracker after the submit', async () => {
        const intake = await apiData('priya', 'get_intake_request', { intakeRequestId: draft.intakeRequestId });
        demo.check(intake.intake?.status?.meaning === 'submitted', 'get_intake_request: the request is submitted', JSON.stringify(intake.intake?.status));
      });
    });
  });

  step('S-02.8 AI activity', async ({ demo }) => {
    await demo.as('priya');
    await demo.open('ai-activity');
    await demo.see('AI activity');
    if (await intakeByTitle('priya', CREW, 'mine')) await demo.see(new RegExp(`${CREW}.{0,200}Taken forward by Priya Castell`), { what: 'the crew overtime tracker is taken forward by Priya Castell', timeout: 15000 });
    if (demo.seeded("AI activity: Claude Code's earlier work for Priya")) {
      for (const row of ['Route Planner 2.1: depot closures', 'Route Planner 2.0: skip closed depots', 'Raise Route Planner to tier 2', TOOLBOX]) {
        await demo.see(row, { what: `AI activity shows Claude Code's earlier work: "${row}"` });
      }
      await demo.see('Changed by a person first');
      await demo.see(/route-planner 2\.0\.0-rc\.1 to Field Ops Test/, { what: 'the deployment made from Claude Code' });
      await demo.see('AI-only code approval');
    }
    await demo.open('ai-activity?view=refused');
    await demo.see('AI clients cannot do this', { what: 'answer_approval: AI clients cannot do this' });
    await demo.see('No such tool', { what: 'approve_decision: No such tool' });
    if (demo.seeded('the refusals of the director\'s calls')) {
      await demo.see('Not allowed for this person', { what: 'save_decision_proposal on Asset Register: Not allowed for this person' });
      await demo.see('A rule refused it', { what: 'save_data_use: A rule refused it' });
      await demo.see(/G17 Let AI draft/, { what: 'a refused call carries its guideline chip G17' });
      await demo.see(/G4\b/, { what: 'the G4 chip' });
      await demo.see(/G16\b/, { what: 'the G16 chip' });
    }
  });
});
