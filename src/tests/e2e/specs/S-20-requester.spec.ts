// S-20 Requester: a request from the web app, with no AI (src/docs/demo/scenarios/S-20-requester.md).
import { appId, hasApp, intakeByTitle } from '../lib/api';
import { scenario, shows, type Demo } from '../lib/demo';
import { FIRST_AID, STATEMENT, waitingFor } from '../lib/story';

async function wizardAt(demo: Demo, step: number) {
  const draft = await intakeByTitle('lena', FIRST_AID, 'mine');
  if (!draft) throw new Error(`no "${FIRST_AID}" request for Lena Okoro`);
  await demo.open(`records/intake-request/${String(draft.intakeRequestId).toLowerCase()}/edit`, 'lena');
  if (step > 1) {
    await demo.page.locator('nav[aria-label="Steps of the request"] button').nth(step - 1).click();
    await demo.settle();
  }
  return draft;
}

scenario('S-20', 'Requester: a request from the web app, with no AI', step => {
  step("S-20.1 The requester's own requests", async ({ demo }) => {
    await demo.open('intake?view=mine', 'lena');
    await demo.count('My requests', 2);
    await demo.see(/Spare parts returns from field vans.{0,300}Submitted/, { what: 'Spare parts returns from field vans is submitted' });
    await demo.see('Suggested Tier 2');
    const draft = await intakeByTitle('lena', FIRST_AID, 'mine');
    if (draft?.status?.meaning === 'draft') {
      await demo.see(new RegExp(`${FIRST_AID}.{0,300}Draft\\. Last saved today\\.`), { what: `"${FIRST_AID}" is a draft, last saved today` });
      await demo.see('Continue');
    }
  });

  step('S-20.2 Finish the draft', async ({ demo }) => {
    demo.writes('save_intake_draft@pages/intake/Wizard#Wizard');
    await demo.open('intake?view=mine', 'lena');
    await demo.act('Lena names Nadia Ferreira as business owner and saves the draft', async () => {
      await demo.click({ text: 'Continue', near: FIRST_AID, scope: 'main' });
      await demo.at(/\/records\/intake-request\/[0-9a-f-]+\/edit$/, 'Continue opens the wizard');
      await demo.person('Business owner', 'Nadia Ferreira');
      await demo.sheet({
        open: { text: 'Save draft', scope: 'main' },
        fill: async () => {
          await demo.see(/Title.{0,20}Depot first-aid kit checks/, { scope: 'dialog', what: 'the sheet lists the title' });
          await demo.see(/Business owner.{0,20}Nadia Ferreira/, { scope: 'dialog', what: 'the sheet lists Nadia Ferreira as business owner' });
          await demo.see(/Proposed kind.{0,20}Power Platform app/, { scope: 'dialog', what: 'the sheet lists the proposed kind' });
          await demo.see(/Horizon.{0,20}Later/, { scope: 'dialog', what: 'the sheet lists the horizon Later' });
        },
        submit: 'Save draft',
        what: 'Save the draft',
        answerShows: ['The intake request is saved as a draft. It goes nowhere until it is submitted.'],
      });
    });
  });

  step('S-20.3 A tier 1 answer', async ({ demo }) => {
    demo.writes('answer_tier_questions@pages/intake/Wizard#TierAnswersSheet');
    await demo.act('Lena answers the tier questions for a small, low-risk app', async () => {
      await wizardAt(demo, 3);
      for (const answer of ['Reads only, or changes only the person', 'Little', 'The builder or a small team', 'No']) await demo.choose(answer);
      const answer = await demo.sheet({ open: { text: 'Save tier answers', scope: 'main' }, submit: 'Save answers', what: 'Save tier answers', close: false });
      const page = await demo.text('main');
      demo.check(shows(answer.text, 'Suggested tier: Tier 1. Only an accepted tier decision sets the tier.') || shows(page, 'Suggested tier: Tier 1. Only an accepted tier decision sets the tier.') || /Tier 1/.test(answer.text), 'the suggested tier is Tier 1', answer.text);
      await demo.closeSheet();
    });
  });

  step('S-20.4 Submit', async ({ demo }) => {
    demo.writes('submit_record@pages/intake/sheets#SubmitSheet');
    await demo.act('Lena submits the request', async () => {
      await wizardAt(demo, 5);
      await demo.see(/WHO DECIDES NEXT/i);
      await demo.see('The rules in force list no approval for the intake decision at this tier. At the lowest tier the engine accepts the intake on submit, with no approval request beyond a yes from the named business owner');
      await demo.sheet({
        open: { text: 'Submit', scope: 'main' },
        submit: 'Submit',
        confirm: 'Submit',
        what: 'Submit the request',
        answerShows: ['Submitted. The engine accepts the decision with no other approval request once the business owner the request names accepts the role.', /Nadia Ferreira.{0,60}asked by name about the business owner role/],
      });
    });
  });

  step('S-20.5 The owner says yes', async ({ demo }) => {
    demo.writes('answer_approval@components/actions#AnswerSheet');
    await demo.as('nadia');
    if (!(await waitingFor('nadia')).includes(STATEMENT.firstAidIntake)) {
      if (!demo.readOnly) throw new Error('no intake card waits for Nadia: S-20.4 did not run on this store');
      await demo.open('approvals?view=mine');
      return;
    }
    await demo.openCard(STATEMENT.firstAidIntake);
    await demo.see('You are asked by name.');
    await demo.see('The accept gate is not ready yet. If it is still not ready once every approval is in, the decision stays proposed until it is');
    await demo.act('Nadia accepts the business owner role', async () => {
      await demo.answer('Yes: the depots in my area need the checks, and I will own the app.', { answerShows: ['The decision is now accepted.'] });
      await demo.api(`"${FIRST_AID}" in the register`, async () => {
        demo.check(await hasApp(FIRST_AID), `the register holds "${FIRST_AID}"`);
      });
      await demo.open(`records/business-application/${await appId(FIRST_AID)}`);
      await demo.see('Prioritize and fund');
      await demo.see(/Owners.{0,120}Nadia Ferreira.{0,20}Business owner/, { what: 'Nadia Ferreira is its business owner' });
    });
  });
});
