// S-04 Business owner: approvals, owners and the lifecycle (src/docs/demo/scenarios/S-04-business-owner-approvals.md).
import { apiAll, apiData, appId, hasApp } from '../lib/api';
import { scenario } from '../lib/demo';
import { STATEMENT, waitingFor, WEATHER } from '../lib/story';

scenario('S-04', 'Business owner: approvals, owners and the lifecycle', step => {
  step('S-04.1 What waits for the business owner', async ({ demo }) => {
    await demo.open('approvals?view=mine', 'nadia');
    await demo.count('Waiting for me', 7);
    for (const statement of [STATEMENT.exportChange, STATEMENT.weatherIntake, STATEMENT.photosRisk, STATEMENT.claimsDataUse, 'End Wren Abernathy as business owner, make Nadia Ferreira business owner']) {
      await demo.see(statement, { what: `Waiting for me lists "${statement}"` });
    }
    await demo.see(/Accept that inspection photos stay on devices for up to 7 days while offline\..{0,200}You proposed this/, { what: 'the risk acceptance is marked "You proposed this"' });
  });

  step('S-04.2 A change on the decision page', async ({ demo }) => {
    await demo.as('nadia');
    await demo.openCard(STATEMENT.exportChange);
    await demo.see('Your answer is asked. You answer here, signed in as yourself.');
    await demo.see(/You are asked as Business owner of the application\. Expires in \d+ days?/, { what: 'the page says which rule asks her and when the request expires' });
    await demo.see(/WHAT THE RULES REQUIRE/i);
    await demo.see('Rule: Business owner authorizes the change.');
  });

  step('S-04.3 Approve the change', async ({ demo }) => {
    demo.writes('answer_approval@components/actions#AnswerSheet');
    await demo.as('nadia');
    await demo.act('Nadia approves the change on its decision page', async () => {
      await demo.openCard(STATEMENT.exportChange);
      await demo.answer('The workload view replaces the export, and the inspectors tested it last week.', {
        preview: true,
        previewShows: ['Your answer would count toward the approval. The decision would be accepted.'],
        answerShows: ['Your approval is recorded and counts. The engine accepted the decision.'],
      });
      await demo.decisionIs('accepted', 'the engine accepts the change authorization');
      await demo.see(/Accepted/, { what: 'the decision reads Accepted' });
      await demo.see(/PROPOSED BY Priya Castell/i, { what: 'the decision page still names Priya Castell as the proposer' });
    });
  });

  step('S-04.4 Her own risk acceptance', async ({ demo }) => {
    demo.writes('answer_approval@components/actions#AnswerSheet');
    await demo.as('nadia');
    await demo.openCard(STATEMENT.photosRisk);
    await demo.see('You proposed this decision. The configuration in force lets the person who proposed a decision answer it, unless a separation rule applies.');
    await demo.see('A typed reason is needed to approve');
    await demo.act('Nadia answers the risk acceptance she proposed', async () => {
      await demo.answer('The devices are encrypted and the photos upload within the week; the risk is small and known.', {
        answerShows: ['The engine accepted the decision.'],
      });
    });
  });

  step('S-04.5 She accepts the business owner role', async ({ demo }) => {
    demo.writes('answer_approval@components/actions#AnswerSheet');
    await demo.as('nadia');
    await demo.openCard(STATEMENT.weatherIntake);
    // S-03.3 comes first: the platform admin's answer is in, and only the gate waits for Nadia's consent.
    const owenAnswered = (await apiAll<any>('owen', 'list_approvals', { view: 'answered' }).catch(() => [])).some(a => a.decision?.statement === STATEMENT.weatherIntake);
    await demo.see('The accept gate is not ready yet. If it is still not ready once every approval is in, the decision stays proposed until it is');
    await demo.act('Nadia accepts the business owner role the request names her for', async () => {
      if (!owenAnswered) {
        demo.note('S-04.5 played without S-03.3: the platform admin has not answered, so the intake stays proposed.');
        await demo.answer('I will own it: the planners in my team asked for it.', { answerShows: ['Your approval is recorded and counts.'] });
        return;
      }
      await demo.answer('I will own it: the planners in my team asked for it.', {
        answerShows: ['The decision is now accepted. 3 requests that are no longer needed were canceled.'],
      });
      await demo.api('the register after Nadia\'s consent', async () => {
        demo.check(await hasApp(WEATHER), `the register now holds "${WEATHER}"`);
      });
    });
  });

  step('S-04.6 A data-use card', async ({ demo }) => {
    demo.writes('answer_approval@components/actions#AnswerSheet');
    await demo.as('nadia');
    await demo.openCard(STATEMENT.claimsDataUse);
    await demo.see(/The data use it decides/i);
    await demo.see('DU-0013: Reads at Customer Prod');
    await demo.see('AI use: Decision');
    await demo.see('Sends data to AI: The approved AI model, in the region');
    await demo.see('0 of 1 approval');
    await demo.act('Nadia, data owner of Customers, approves the data use', async () => {
      await demo.answer('Claims stay in the region, the model does not train on them, and the claims team checks each routing.', {
        answerShows: ['The decision is now accepted. 1 request that is no longer needed was canceled.'],
      });
    });
  });

  step('S-04.7 The owner change, first answer', async ({ demo }) => {
    demo.writes('answer_approval@components/actions#AnswerSheet');
    await demo.as('nadia');
    await demo.openCard(STATEMENT.ownerChange);
    await demo.see(/WHAT ACCEPTANCE CHANGES/i);
    await demo.see('Wren Abernathy stops being business owner. Nadia Ferreira becomes business owner from');
    await demo.act('Nadia approves the owner change that names her business owner', async () => {
      await demo.answer('Nadia has deputized on Claims Triage Agent for a year and knows the claims team.', { answerShows: ['The decision stays proposed.'] });
      await demo.api('the owner change after Nadia\'s answer', async () => {
        demo.check((await waitingFor('mateo')).includes(STATEMENT.ownerChange), 'the sponsor, Mateo Brandt, is still asked');
      });
    });
  });

  step('S-04.8 The sponsor answers', async ({ demo }) => {
    demo.writes('answer_approval@components/actions#AnswerSheet');
    await demo.open('approvals?view=mine', 'mateo');
    await demo.see(STATEMENT.ownerChange, { what: 'the owner change waits for Mateo, the sponsor' });
    await demo.see(/Asks Sponsor of the application/, { what: 'his card says it asks the sponsor of the application' });
    await demo.act('Mateo, the sponsor of Claims Triage Agent, approves the owner change', async () => {
      await demo.openCard(STATEMENT.ownerChange);
      await demo.answer('Nadia takes the business owner role when Wren leaves. The claims team agrees.', { answerShows: ['The engine accepted the decision.'] });
      await demo.decisionIs('accepted', 'the engine accepts the owner change after the sponsor\'s answer');
      await demo.api('the owners of Claims Triage Agent', async () => {
        const app = await appId('Claims Triage Agent');
        const owners = await apiData<any>('mateo', 'list_owner_assignments', { businessApplicationId: app, state: 'all' });
        const nadia = (owners.items ?? []).find((o: any) => o.ownerRoleKey === 'business-owner' && !o.isDeputy && /Nadia Ferreira/.test(o.principal?.displayLabel ?? ''));
        demo.check(!!nadia, 'from Wren\'s leave date Nadia is business owner of Claims Triage Agent', JSON.stringify((owners.items ?? []).map((o: any) => [o.ownerRoleKey, o.isDeputy, o.principal?.displayLabel, o.startOn, o.state])));
      });
    });
  });

  step('S-04.9 The Go-live gate and the runbook', async ({ demo }) => {
    demo.writes('confirm_document_checked@pages/app360/sheets#DocumentCheckedSheet');
    await demo.open(`records/business-application/${await appId('Inspection Scheduler')}?tab=lifecycle`, 'nadia');
    const confirmed = !demo.readOnly && !(await demo.has({ text: 'Confirm it is still true', near: 'Runbook current', scope: 'main' }));
    if (!confirmed) {
      await demo.see('Go-live passed, but 2 requirements are not met now.');
      await demo.see(/Owner accepted after testing:? ?Not met/, { what: '"Owner accepted after testing" is not met' });
      await demo.see(/Runbook current:? ?Not met/, { what: '"Runbook current" is not met' });
    }
    await demo.act('Nadia confirms the runbook current', async () => {
      await demo.sheet({
        open: { text: 'Confirm it is still true', near: 'Runbook current', scope: 'main' },
        submit: 'Confirm',
        what: 'Confirm the runbook current',
        answerShows: ['The check is recorded, as reported.'],
      });
      await demo.see(/Runbook current:? ?Met/, { what: 'the runbook requirement turns met' });
      await demo.see('1 requirement is not met now');
    });
  });

  step('S-04.10 Hand over to IT', async ({ demo }) => {
    demo.writes('propose_owner_change@pages/app360/sheets#HandOverSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
    await demo.open(`records/business-application/${await appId('Inspection Scheduler')}`, 'nadia');
    await demo.act('Nadia hands the technical owner role over to IT', async () => {
      await demo.proposeAndSubmit({
        open: { text: 'Hand over', scope: 'main' },
        fill: async () => {
          await demo.see('Now: Priya Castell holds the role, with Lena Okoro as deputy.', { scope: 'dialog' });
          await demo.person('New technical owner', 'Owen Achterberg', 'dialog');
          await demo.person('New deputy', 'Hugo Brandvold', 'dialog');
          await demo.fill('Reason', 'The app graduates from the maker to IT: it runs every field inspection now.', 'dialog');
        },
        preview: 'Check first',
        submit: 'Propose the hand-over',
        what: 'Hand over the technical owner role',
        sendShows: ['Submitted. 3 approvals are asked.', /ASKED NOW/, /Business owner of the application/, /Owen Achterberg.{0,80}asked by name about the technical owner role/, /Hugo Brandvold.{0,80}asked by name about the technical owner role/],
      });
    });
  });

  step('S-04.11 Propose a deputy', async ({ demo }) => {
    demo.writes('propose_owner_change@pages/owners-reviews/OwnerSheets#ProposeOwnerChangeSheet', 'submit_record@pages/owners-reviews/OwnerSheets#SubmitDecisionSheet');
    await demo.open('owners?view=gaps', 'nadia');
    await demo.see(/Inspection Scheduler.{0,80}Business owner: no deputy/, { what: 'Inspection Scheduler has no deputy business owner' });
    await demo.act('Nadia proposes a deputy business owner for Inspection Scheduler and submits it', async () => {
      await demo.proposeAndSubmit({
        open: { text: 'Propose a deputy', near: 'Inspection Scheduler', scope: 'main' },
        fill: async () => {
          if (await demo.has({ text: 'Choose a person', role: 'combobox', prefix: true, scope: 'dialog' })) await demo.select('Choose a person', 'Lena Okoro');
          else await demo.person('New deputy', 'Lena Okoro', 'dialog');
          await demo.fill('Reason', 'Lena runs the inspection requests and stands in for Nadia when she is away.', 'dialog');
        },
        submit: 'Propose',
        what: 'Propose a deputy business owner',
        // The Owners page's own banner sends it: Cancel in the follow-up, then "Submit for approval" (wave 4b QA).
        via: 'page',
        sendShows: ['Submitted. 2 approvals are asked.'],
      });
      await demo.open('owners?view=gaps');
      await demo.see(/Inspection Scheduler.{0,300}Owner change waiting/, { what: 'Inspection Scheduler shows "Owner change waiting"' });
      if (await hasApp(WEATHER)) await demo.see(new RegExp(`${WEATHER}.{0,80}Business owner: no deputy`), { what: `${WEATHER}, registered in S-04.5, has no deputy business owner` });
    });
  });
});

