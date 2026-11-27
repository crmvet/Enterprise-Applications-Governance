// S-09 PMO, business owner and platform admin: demand, the portfolio and retirement
// (src/docs/demo/scenarios/S-09-demand-portfolio-retirement.md). It needs S-02 (the crew overtime tracker on the table)
// and S-04.5 (Weather alerts for outdoor jobs registered); played alone, the counts that depend on them follow.
import { appId, decisionByStatement, hasApp, portfolioId } from '../lib/api';
import { scenario } from '../lib/demo';
import { crewSubmitted, STATEMENT, waitingFor, WEATHER } from '../lib/story';

scenario('S-09', 'PMO, business owner and platform admin: demand, the portfolio and retirement', step => {
  step('S-09.1 The demand board', async ({ demo }) => {
    await demo.open('demand', 'mateo');
    const crew = await crewSubmitted();
    await demo.count('On the table', crew ? 7 : 6);
    await demo.count('To score', crew ? 3 : 2);
    await demo.count('Priority waiting', 1);
    await demo.count('Scored', 4);
    await demo.see('Priorities waiting');
    await demo.see(/Supplier rebate tracking.{0,200}Proposed priority:? ?Fund/, { what: 'Supplier rebate tracking: proposed priority Fund' });
    await demo.see(new RegExp(`Still to score.{0,600}${WEATHER}`), { what: `"${WEATHER}" is still to score` });
    await demo.see(new RegExp(`${WEATHER}.{0,200}Now`), { what: 'its horizon is Now' });
  });

  step('S-09.2 Score the request', async ({ demo }) => {
    demo.writes('score_intake_request@pages/intake/sheets#ScoreSheet');
    await demo.open('demand', 'mateo');
    await demo.act(`Mateo scores "${WEATHER}"`, async () => {
      await demo.sheet({
        open: { text: 'Score', near: WEATHER, scope: 'main' },
        fill: async () => {
          await demo.fill('Score', '80', 'dialog');
          await demo.select('Not set', 'Small');
        },
        submit: 'Record',
        what: 'Score the request',
        answerShows: [`The demand fields of ${WEATHER} are recorded, as reported.`],
      });
      await demo.see(new RegExp(`${WEATHER}.{0,300}80.{0,100}Small`), { what: 'it sits in the demand list with 80 and Small' });
    });
  });

  step('S-09.3 Propose the priority', async ({ demo }) => {
    demo.writes('save_decision_proposal@pages/work-approvals/decisionSheets#NewProposalSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
    await demo.open('demand', 'mateo');
    await demo.act('Mateo proposes priority "fund" and sends it for approval', async () => {
      await demo.proposeAndSubmit({
        open: { text: 'Propose priority', near: WEATHER, scope: 'main' },
        fill: async () => {
          await demo.fill('Statement', STATEMENT.weatherPriority, 'dialog');
          await demo.fill('Context', 'Planners check the forecast by hand today. The app is small and the winter storms start in December.', 'dialog');
          await demo.click({ text: 'Choose an outcome', role: 'combobox', prefix: true });
          for (const outcome of ['Defer', 'Fund', 'Stop']) await demo.see(outcome, { what: `the outcomes include ${outcome}` });
          await demo.click({ text: 'Fund', role: 'option' });
          await demo.fill('Reason', 'A high score, a small size, and it is needed now.', 'dialog');
        },
        preview: 'Check first',
        previewShows: [/Business owner/],
        submit: 'Propose',
        what: 'Propose the priority',
        sendShows: ['Submitted. 1 approval is asked.', /Business owner of the application \(the first answer counts\)/],
      });
    });
  });

  step('S-09.4 The business owner funds it', async ({ demo }) => {
    demo.writes('answer_approval@components/actions#AnswerSheet');
    await demo.as('nadia');
    if (!(await waitingFor('nadia')).includes(STATEMENT.weatherPriority)) {
      if (!demo.readOnly) throw new Error('no priority card waits for Nadia: S-09.3 did not run on this store');
      demo.note('The priority card exists once S-09.3 has run.');
      await demo.open('approvals?view=mine');
      return;
    }
    await demo.openCard(STATEMENT.weatherPriority);
    await demo.see('You are asked as Business owner of the application.');
    await demo.act('Nadia funds the request', async () => {
      await demo.answer('Fund it: the planners need it before the storms.', { answerShows: ['The engine accepted the decision.'] });
      await demo.decisionIs('accepted', 'the engine accepts the priority: the Commit gate');
    });
  });

  step('S-09.5 The gate passed on Application 360', async ({ demo }) => {
    await demo.as('nadia');
    if (!(await hasApp(WEATHER))) {
      demo.note(`"${WEATHER}" is registered once S-03.3 and S-04.5 have run.`);
      await demo.open('applications');
      return;
    }
    await demo.open(`records/business-application/${await appId(WEATHER)}?tab=lifecycle`);
    if (!demo.readOnly) {
      await demo.see(/Design and architecture review,? since/, { what: 'the stage is Design and architecture review, since today' });
      await demo.see(/Accept gate:? ?passed/i, { what: 'the Accept gate passed' });
      await demo.see(/Commit gate:? ?passed/i, { what: 'the Commit gate passed' });
      await demo.see(/Fund weather alerts for outdoor jobs this quarter\.? ?Accepted\.? ?Outcome:? ?Fund/, { what: 'the funded priority is the Commit gate decision' });
      await demo.see(/Design gate:? ?ready/i, { what: 'the Design gate is ready' });
    }
  });

  step('S-09.6 The portfolio review pack', async ({ demo }) => {
    await demo.open(`portfolios/${await portfolioId('Field operations')}`, 'mateo');
    await demo.count('Review pack', 15);
    await demo.see(/Crew Shift Mobile.{0,300}No accepted decision/, { what: 'Crew Shift Mobile has no accepted decision' });
    await demo.see(/Crew Shift Mobile.{0,400}Proposed:? ?Replace/, { what: 'Crew Shift Mobile: proposed Replace' });
    await demo.see('Portfolio decisions');
    await demo.see('Replace Crew Shift Mobile with the phone view of Crew Roster 5.3.');
  });

  step('S-09.7 Applications on their way out', async ({ demo }) => {
    await demo.open('retirement', 'mateo');
    await demo.count('Retire proposed', 1);
    await demo.count('On the way out', 1);
    await demo.see(/Crew Shift Mobile.{0,300}Retire proposed/, { what: 'Crew Shift Mobile: retire proposed' });
    await demo.see(/Legacy Timesheet App.{0,300}Replace.{0,40}Accepted/i, { what: 'Legacy Timesheet App: Replace, accepted' });
    await demo.see(/Legacy Timesheet App.{0,400}On the way out/, { what: 'Legacy Timesheet App is on the way out' });
  });

  step('S-09.8 Confirm the component retired', async ({ demo }) => {
    demo.writes('link_component@pages/pmo-portfolio/sheets#RetireComponentSheet');
    await demo.open(`retirement/${await appId('Legacy Timesheet App')}`, 'owen');
    const done = /6\. Gap check confirmation:? ?Done/i.test(await demo.text('main'));
    if (!done) {
      for (const [n, label] of [[1, 'Portfolio decision: retire'], [2, 'Dependants told'], [3, 'Data retention decision'], [4, 'Removal change'], [5, 'Removal evidence']] as const) {
        await demo.see(new RegExp(`${n}\\. ${label}:? ?Done`, 'i'), { what: `step ${n} "${label}" is done` });
      }
      await demo.see('What is left: 6. gap check confirmation; 7. retirement complete.');
    }
    await demo.act('Owen confirms the Timesheets canvas app retired', async () => {
      await demo.sheet({
        open: { text: 'Confirm retired', scope: 'main' },
        submit: 'Confirm retired',
        confirm: 'Mark retired',
        what: 'Confirm the component retired',
        answerShows: ['The component is recorded as retired, as reported.', 'The component is marked as gone.', "The claim's status is now claimed."],
      });
      await demo.see(/6\. Gap check confirmation:? ?Done/i, { what: 'step 6 turns done (the vanished claim is resolved)' });
    });
  });

  step('S-09.9 Propose retirement complete', async ({ demo }) => {
    demo.writes('save_decision_proposal@pages/pmo-portfolio/sheets#ProposeDecisionSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
    await demo.open(`retirement/${await appId('Legacy Timesheet App')}`, 'owen');
    await demo.act('Owen proposes retirement complete and submits it', async () => {
      await demo.proposeAndSubmit({
        open: { text: 'Propose retirement complete', scope: 'main' },
        fill: async () => {
          await demo.fill('Context', 'The canvas app is removed, its data is kept read-only, and no live application depends on it.', 'dialog');
          await demo.fill('Reason', 'Every step of the tracker is done.', 'dialog');
        },
        preview: 'Check first',
        previewShows: ['The decider would be Rosa Tamaki.', /WILL BE ASKED ONCE SUBMITTED:? ?Business owner of the application \(the first answer counts\)/],
        submit: 'Propose',
        what: 'Propose retirement complete',
      });
      const d = await decisionByStatement('owen', STATEMENT.retired);
      if (!d) throw new Error('the retirement complete decision is not on the decision log');
      await demo.open(`records/decision/${String(d.decision.decisionId).toLowerCase()}`);
      await demo.see('Waiting on Rosa Tamaki, or deputy Nadia Ferreira');
    });
  });

  step('S-09.10 Retention on Data gaps', async ({ demo }) => {
    await demo.open(`data/gaps?app=${await appId('Legacy Timesheet App')}`, 'owen');
    // The application is retiring once S-09.9 proposed retirement complete; before that no retention gap is open. A
    // read-only run cannot know which earlier writes its store holds, so it checks the page only (review, fix round 1).
    if (demo.readOnly) {
      demo.note('Read only: the DC-13 count depends on S-09.8 and S-09.9 having run on this store, so it is not checked.');
      await demo.see('DC-13');
      return;
    }
    if (!(await decisionByStatement('owen', STATEMENT.retired))) {
      demo.note('DC-13 opens once S-09.9 has proposed retirement complete.');
      await demo.count('DC-13 Retention open', 0);
      return;
    }
    await demo.count('DC-13 Retention open', 1);
    await demo.see('Legacy Timesheet App is retiring and writes People / Timesheets, with no retention decision yet.');
    await demo.see('Propose a retention decision');
  });

  step('S-09.11 The deputy accepts retirement complete', async ({ demo }) => {
    demo.writes('answer_approval@components/actions#AnswerSheet');
    await demo.as('nadia');
    if (!(await waitingFor('nadia')).includes(STATEMENT.retired)) {
      if (!demo.readOnly) throw new Error('no retirement complete card waits for Nadia: S-09.9 did not run on this store');
      demo.note('The retirement complete card exists once S-09.9 has run.');
      await demo.open('approvals?view=mine');
      return;
    }
    await demo.openCard(STATEMENT.retired);
    await demo.act('Nadia, deputy business owner of Legacy Timesheet App, accepts retirement complete', async () => {
      await demo.answer('Confirmed with the HR team: nobody uses it.', { answerShows: ['The engine accepted the decision.'] });
      await demo.decisionIs('accepted', 'the engine accepts retirement complete');
      await demo.see(/PROPOSED BY Owen Achterberg/i, { what: 'the decision page still names Owen Achterberg as the proposer' });
      await demo.open(`retirement/${await appId('Legacy Timesheet App')}`);
      await demo.see('Retired');
      await demo.see(/Stage:? ?Retire/, { what: 'the stage is Retire' });
      // Step 3 follows the retention rule (DC-13, fix round 1): it is done only when the data owner of each domain the
      // application writes has decided; until Hana Lindqvist answers the archive decision it is what is left.
      await demo.see(/Every step is done\. Legacy Timesheet App is retired, with the evidence on record\.|What is left: 3\. data retention decision\. The application is retired, and these steps still have nothing on record\./, { what: 'the tracker says the application is retired, and names the retention decision if it is still open' });
      // Who decides retention from now on, apart from who decided the record above (fix round 2).
      await demo.see(/Each domain's retention is its data owner's to decide: People \/ Timesheets, Hana Lindqvist/, { what: 'step 3 names the data owner who decides each domain from now on' });
    });
  });
});
