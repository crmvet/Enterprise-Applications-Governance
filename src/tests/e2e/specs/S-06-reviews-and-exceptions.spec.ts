// S-06 Architect: reviews and exceptions (src/docs/demo/scenarios/S-06-reviews-and-exceptions.md).
import { appId } from '../lib/api';
import { scenario, type Demo } from '../lib/demo';
import { STATEMENT } from '../lib/story';

async function openReview(demo: Demo, persona: 'elif' | 'marcus' = 'elif') {
  await demo.open('reviews?state=due&type=architecture', persona);
  await demo.click({ text: 'Architecture review', role: 'link', near: 'Claims Triage Agent', scope: 'main' });
  await demo.see('Architecture review: Claims Triage Agent');
}

scenario('S-06', 'Architect: reviews and exceptions', step => {
  step('S-06.1 The architecture queue', async ({ demo }) => {
    await demo.open('reviews?state=due&type=architecture', 'elif');
    await demo.count('Due reviews', 1);
    await demo.see(/Architecture review.{0,200}Claims Triage Agent/, { what: 'the architecture review of Claims Triage Agent is due' });
    if (!demo.phone) {
      await demo.see('Asked for by a person');
      await demo.see('Customer services');
      await demo.see(/Technical owner:? ?Marcus Oyelaran/, { what: 'its technical owner is Marcus Oyelaran' });
    }
    await demo.see('Due');
    await demo.see('Yours');
  });

  step('S-06.2 What completing the review needs', async ({ demo }) => {
    await openReview(demo);
    await demo.see('What completing it needs: Propose the outcome, have it accepted, then complete the review');
    await demo.see(/Propose the outcome: a design decision:? ?Done/, { what: 'the outcome is proposed' });
    await demo.see(/People accept the decision:? ?To do/, { what: 'people still accept the decision' });
    await demo.see(/Feeds the design gate.{0,200}Not met yet/, { what: 'the review feeds the design gate, not met yet' });
    await demo.see('ADR 2: claim routing', { what: 'the evidence: ADR 2' });
  });

  step('S-06.3 Approve the design decision', async ({ demo }) => {
    demo.writes('answer_approval@components/actions#AnswerSheet');
    // Marcus Oyelaran, a second architect, answers: Elif proposed it (wave 4b product review).
    await openReview(demo, 'marcus');
    await demo.click({ text: STATEMENT.design, prefix: true, role: 'link', scope: 'main' });
    await demo.at(/\/records\/decision\//, 'the design decision opens');
    await demo.see(/OPTIONS WEIGHED/i);
    await demo.see("Copy the photos into the agent's own store. Why not: A second copy of personal photos to protect and delete.");
    await demo.see(/A custom model in a code app\.? ?Why not:/, { what: 'the second option and why not' });
    await demo.notSee('You proposed this decision', { what: 'Marcus did not propose it, so the page does not say he did' });
    await demo.act('Marcus approves the design decision Elif proposed', async () => {
      await demo.answer('The agent routes only; a person decides each claim. Photos stay in the claims system.', {
        preview: true,
        answerShows: ['The engine accepted the decision.'],
      });
      await demo.decisionIs('accepted', 'the engine accepts the design decision');
    });
  });

  step('S-06.4 Complete the review', async ({ demo }) => {
    demo.writes('complete_review@pages/owners-reviews/ReviewSheets#CompleteReviewSheet');
    await openReview(demo);
    await demo.act('Elif completes the review', async () => {
      await demo.see(/People accept the decision:? ?Done/, { what: 'the outcome decision is accepted' });
      await demo.sheet({
        open: { text: 'Complete the review', prefix: true, scope: 'main' },
        submit: 'Complete the review',
        what: 'Complete the review',
        answerShows: ['The architecture review of Claims Triage Agent is done. It feeds 1 gate, 1 met now.'],
      });
      await demo.open(`records/business-application/${await appId('Claims Triage Agent')}?tab=lifecycle`);
      await demo.see(/Design gate:? ?(passed|met|ready)/i, { what: 'on Application 360 the Design gate turns met' });
    });
  });

  step('S-06.5 A review done before the demo', async ({ demo }) => {
    await demo.open('reviews?state=done&type=architecture', 'elif');
    await demo.see(/Architecture review.{0,200}Quote Builder/, { what: "Quote Builder's architecture review is done" });
    if (!demo.readOnly) await demo.see(/Architecture review.{0,200}Claims Triage Agent/, { what: "Claims Triage Agent's review is done beside it" });
    await demo.see('Done');
  });

  step('S-06.6 The exceptions register', async ({ demo }) => {
    await demo.open('decisions?view=exceptions', 'elif');
    await demo.count('Exceptions register', 4);
    await demo.see(/Desk Booking.{0,300}Proposed/, { what: 'the Desk Booking calendar connector update is proposed' });
    await demo.see(/1\.12\.1 hotfix.{0,300}Proposed/, { what: 'the 1.12.1 hotfix exception is proposed' });
    await demo.see(/Contractor Portal.{0,300}Accepted/, { what: 'the Contractor Portal sign-in fix is accepted' });
    await demo.see(/Review in \d+ days?/, { what: 'it is reviewed again in some days' });
    await demo.see(/Travel Requests.{0,300}Accepted/, { what: 'the Travel Requests daily allowance rates are accepted' });
    await demo.see('Review overdue');
    // The register's column heading; a phone shows each exception as a card.
    if (!demo.phone) await demo.see('Standard it sets aside', { what: 'each exception names the standard it sets aside' });
  });
});
