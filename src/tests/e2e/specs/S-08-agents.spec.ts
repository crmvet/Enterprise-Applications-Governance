// S-08 AI lead and security reviewer: agents (src/docs/demo/scenarios/S-08-agents.md).
import { scenario } from '../lib/demo';
import { STATEMENT, waitingFor } from '../lib/story';

scenario('S-08', 'AI lead and security reviewer: agents', step => {
  step('S-08.1 The agent register', async ({ demo }) => {
    await demo.open('agents', 'dmitri');
    await demo.count('Agents', 6);
    await demo.count('No autonomy level', 3);
    await demo.count('Autonomy decisions', 1);
    await demo.see(/Supplier Price Agent.{0,120}Tier 3/, { what: 'Supplier Price Agent is tier 3' });
    await demo.see('Risk and tier');
    await demo.see(/Supplier Price Agent.{0,400}Not agreed/, { what: 'its autonomy is not agreed' });
    await demo.see('Needed before go-live');
    await demo.see('No security review');
    await demo.see('Propose autonomy level');
  });

  step('S-08.2 Propose an autonomy level', async ({ demo }) => {
    demo.writes('save_decision_proposal@pages/ai-audit/agentSheets#ProposeAutonomySheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
    await demo.open('agents', 'dmitri');
    await demo.act('The AI lead proposes "assist" for Supplier Price Agent and sends it for approval', async () => {
      await demo.proposeAndSubmit({
        open: { text: 'Propose autonomy level', near: 'Supplier Price Agent', scope: 'main' },
        fill: async () => {
          await demo.select('Choose a level', 'Assist');
          for (const level of ['Execute', 'Route']) await demo.see(level, { scope: 'dialog', what: `the levels include ${level}` });
          await demo.fill('What the agent does', 'Compares supplier price lists with the parts catalog and drafts a price change list for the buyer.', 'dialog');
          await demo.fill('Why this level', 'A buyer checks and sends every price change; the agent changes nothing itself.', 'dialog');
        },
        preview: 'Check first',
        previewShows: ['The decider would hold the role "Security reviewer".'],
        submit: 'Propose',
        what: 'Propose the autonomy level',
        sendShows: ['Submitted. 1 approval is asked, and 1 person is consulted.', /Security reviewer/, /AI lead/],
      });
    });
  });

  step('S-08.3 Flagged agents', async ({ demo }) => {
    await demo.open('agents?view=flagged', 'dmitri');
    await demo.count('Agents that need attention', 6);
    await demo.see(/Onboarding Buddy Agent.{0,300}A flagged-agent decision waits for people to decide\./, { what: 'Onboarding Buddy Agent: a flagged-agent decision waits' });
    await demo.see('Decisions on flagged agents');
    await demo.see('Keep Onboarding Buddy Agent, shared with the pilot group only until its go-live gate is met.');
    await demo.see('Proposed by Owen Achterberg');
    await demo.see(/Keep Onboarding Buddy Agent.{0,300}Answer/, { what: 'the AI lead can answer it' });
  });

  step('S-08.4 An AI answer is a recommendation only', async ({ demo }) => {
    await demo.open('ai-activity?view=answers', 'dmitri');
    await demo.count('Approval answers by an AI actor', 1);
    await demo.see('Authorize "Invoice Matching 1.12" in the November 14 window.');
    await demo.see('Finance Approvals Agent answered "Recommendation only"');
    await demo.see('Not counted');
  });

  step('S-08.5 The security reviewer agrees the level', async ({ demo }) => {
    demo.writes('answer_approval@components/actions#AnswerSheet');
    await demo.as('noor');
    const card = (await waitingFor('noor')).find(s => s.startsWith(STATEMENT.autonomy));
    if (!card) {
      if (!demo.readOnly) throw new Error('no autonomy card waits for the security reviewer: S-08.2 did not run on this store');
      demo.note('The autonomy card exists once S-08.2 has run.');
      await demo.open('approvals?view=mine');
      return;
    }
    await demo.openCard(card);
    await demo.act('The security reviewer agrees the level on its decision page', async () => {
      await demo.answer('Assist fits: a buyer sends every price change, and the agent writes nothing.', {
        answerShows: ['The engine accepted the decision.', '1 request that is no longer needed was canceled.'],
      });
      await demo.decisionIs('accepted', 'the engine accepts the autonomy level');
    });
  });

  step('S-08.6 A security review before the pilot', async ({ demo }) => {
    await demo.open('reviews?state=due&type=security-review', 'noor');
    await demo.count('Due reviews', 4);
    await demo.see(/Security review.{0,300}Customer Feedback Bot/, { what: "Customer Feedback Bot's security review is due" });
    if (!demo.phone) {
      await demo.see('Asked for by a person');
      await demo.see(/Security owner:? ?Noor Safadi/, { what: 'its security owner is Noor Safadi' });
    }
    await demo.see('Yours');
  });
});
