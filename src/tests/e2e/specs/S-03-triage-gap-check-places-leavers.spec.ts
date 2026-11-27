// S-03 Platform admin: triage, the gap check, places and leavers
// (src/docs/demo/scenarios/S-03-triage-gap-check-places-leavers.md).
import { apiAll } from '../lib/api';
import { scenario, type Demo } from '../lib/demo';
import { CREW, crewSubmitted, WEATHER } from '../lib/story';

async function openWeatherIntake(demo: Demo) {
  await demo.open('intake?view=triage', 'owen');
  await demo.click({ text: WEATHER, contains: true, role: 'link', scope: 'main' });
  await demo.at(/\/records\/intake-request\/[0-9a-f-]{36}/, `"${WEATHER}" opens its intake request`);
}

scenario('S-03', 'Platform admin: triage, the gap check, places and leavers', step => {
  step('S-03.1 The triage queue', async ({ demo }) => {
    await demo.open('intake?view=triage', 'owen');
    const crew = await crewSubmitted();
    await demo.count('Waiting for triage', crew ? 3 : 2);
    if (crew) await demo.see(new RegExp(`${CREW}.{0,200}Drafted by Claude Code for Priya Castell`), { what: 'the crew overtime tracker Priya submitted waits, drafted by Claude Code' });
    await demo.see('Spare parts returns from field vans');
    await demo.see(WEATHER);
    await demo.see('Suggested Tier 2');
  });

  step('S-03.2 Who is asked, and who only advises', async ({ demo }) => {
    await openWeatherIntake(demo);
    await demo.see('You are asked. As Platform admin.');
    await demo.see(/WHO IS ASKED/i);
    await demo.see('Nadia Ferreira, the owner the request names, everyone must approve');
    await demo.see('Platform admin, the first answer counts');
    await demo.see(/Architect.{0,120}Consulted: a recommendation only, never counts/, { what: 'the architect is consulted: a recommendation only' });
    await demo.see(/PMO.{0,120}Consulted: a recommendation only, never counts/, { what: 'the PMO is consulted: a recommendation only' });
    await demo.see(/Security reviewer.{0,120}Consulted: a recommendation only, never counts/, { what: 'the security reviewer is consulted: a recommendation only' });
  });

  step('S-03.3 The platform admin approves', async ({ demo }) => {
    demo.writes('answer_approval@components/actions#AnswerSheet');
    await openWeatherIntake(demo);
    await demo.act('Owen approves the intake with a typed reason', async () => {
      await demo.answer('No current app covers weather checks before mast jobs; Crew Roster plans crews but has no forecast.', {
        near: 'You are asked',
        preview: true,
        previewShows: ['Your answer would count toward the approval. The decision would stay proposed.'],
        answerShows: ['Your approval is recorded and counts. The decision still waits for other answers.'],
        what: 'Owen approves the intake request',
      });
      await demo.see('Still needed: 1 from Nadia Ferreira, the owner the request names.', { timeout: 15000 });
    });
  });

  step('S-03.4 Link the delivery work item', async ({ demo }) => {
    demo.writes('save_link@pages/intake/sheets#LinkSheet');
    await openWeatherIntake(demo);
    await demo.act('Owen links the Azure DevOps work item on the request', async () => {
      await demo.sheet({
        open: { text: 'Add a link', scope: 'main' },
        fill: async () => {
          await demo.select('Choose a kind', 'Azure DevOps work item');
          await demo.fill('Address', 'https://dev.example.org/field-ops/_workitems/edit/4182', 'dialog');
          await demo.fill('Label', WEATHER, 'dialog');
          await demo.fill('ID in its own tool', '4182', 'dialog');
        },
        submit: 'Save the link',
        what: 'Link the work item',
        answerShows: ['The delivery link is saved. It stores an ID, a URL and a label, never a copy.'],
      });
      await demo.count('Links', 1);
      await demo.see(/Azure DevOps work item.{0,40}Delivery|Delivery.{0,40}Azure DevOps work item/, { what: 'Links shows the work item, a delivery link' });
    });
  });

  step('S-03.5 The gap check', async ({ demo }) => {
    await demo.open('gap-check', 'owen');
    await demo.count('Not registered', 2);
    await demo.count('Vanished', 1);
    await demo.count('Claimed', 1);
    await demo.count('Dismissed', 1);
    await demo.count('Runs', 1);
    await demo.see('Finished');
    await demo.see('33 items seen, 4 new claims, 1 vanished');
  });

  step('S-03.6 Claim the photo flow', async ({ demo }) => {
    demo.writes('claim_resource@pages/Records#ClaimSheet');
    await demo.open('gap-check?view=open', 'owen');
    await demo.see(/Copy visit photos to SharePoint.{0,200}Suggested to Priya Castell/, { what: '"Copy visit photos to SharePoint" is suggested to Priya Castell' });
    await demo.act('Owen claims the photo flow into Visit Report Agent', async () => {
      await demo.sheet({
        open: { text: 'Claim', near: 'Copy visit photos to SharePoint', scope: 'main' },
        fill: async () => {
          await demo.fill('Application', 'Visit Report', 'dialog');
          await demo.choose('Visit Report Agent', 'dialog');
        },
        preview: 'Check first',
        previewShows: [/WOULD BE ASKED:? ?Business owner of the application \(everyone must approve\)/],
        submit: 'Claim it',
        confirm: 'Claim it',
        what: 'Claim the photo flow into Visit Report Agent',
        answerShows: ['The item is claimed and added to Visit Report Agent. 1 owner is asked to confirm it on the decision page.'],
      });
    });
  });

  step('S-03.7 Dismiss the personal test flow', async ({ demo }) => {
    demo.writes('dismiss_claim@pages/Records#DismissSheet');
    await demo.open('gap-check?view=open', 'owen');
    await demo.see('Test reminder flow (personal)');
    await demo.act('Owen dismisses the test flow with a reason', async () => {
      await demo.sheet({
        open: { text: 'Dismiss', near: 'Test reminder flow (personal)', scope: 'main' },
        fill: () => demo.fill('Reason', 'A personal test flow; its maker removes it this week.', 'dialog'),
        submit: 'Dismiss',
        what: 'Dismiss the test flow',
        answerShows: ['The claim is dismissed. Reopen brings it back.'],
      });
      await demo.see('Nothing unregistered is waiting.');
      await demo.api('the two claims after triage', async () => {
        const open = await apiAll<any>('owen', 'list_claims', { statusMeanings: ['open'] });
        demo.check(!open.some(c => /Copy visit photos|Test reminder flow/.test(c.labelAsSeen)), 'list_claims: neither item is open any more', JSON.stringify(open.map(c => c.labelAsSeen)));
      });
    });
    await demo.open('gap-check?view=vanished');
    await demo.see('Timesheets', { what: 'Vanished lists the Timesheets canvas app' });
  });

  step('S-03.8 Places', async ({ demo }) => {
    await demo.open('places', 'owen');
    await demo.count('Targets', 13);
    await demo.count('Development', 1);
    await demo.count('Test', 5);
    await demo.count('Production', 7);
    await demo.see(/People Test.{0,200}Last seen/, { what: 'People Test shows when it was last seen' });
    await demo.see('Nothing deployed yet');
    await demo.open('places?view=pipelines');
    await demo.count('Pipelines', 5);
    await demo.see(/Inspection Scheduler release.{0,200}1\. Field Ops Dev.{0,20}then.{0,20}2\. Field Ops Test.{0,20}then.{0,20}3\. Field Ops Prod/, { what: 'the Inspection Scheduler release pipeline reaches Dev, Test and Prod in order' });
  });

  step('S-03.9 Leavers', async ({ demo }) => {
    await demo.open('owners?view=leavers', 'owen');
    await demo.count('Leavers', 2);
    await demo.see(/Amara Quist.{0,120}Account disabled in Entra/, { what: 'Amara Quist: account disabled in Entra' });
    await demo.see(/Travel Requests.{0,80}Business owner/, { what: 'Amara Quist held the Travel Requests business owner role' });
    await demo.see('No deputy: the role has nobody once they leave.');
    await demo.see(/Wren Abernathy.{0,80}Leaves/, { what: 'Wren Abernathy leaves' });
    await demo.see('Holds 7 owner roles and 4 deputy roles on 7 applications.');
    await demo.see(/Claims Triage Agent.{0,300}Owner change proposed/, { what: 'Claims Triage Agent: owner change proposed' });
  });
});
