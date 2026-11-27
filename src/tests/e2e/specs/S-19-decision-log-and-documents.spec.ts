// S-19 Auditor: the decision log and the documents (src/docs/demo/scenarios/S-19-decision-log-and-documents.md).
// Reads only; the counts follow what was played before, as the script says.
import { apiAll } from '../lib/api';
import { scenario } from '../lib/demo';
import { decisionStatus, STATEMENT } from '../lib/story';

scenario('S-19', 'Auditor: the decision log and the documents', step => {
  step('S-19.1 Every decision in one line', async ({ demo }) => {
    await demo.open('decisions', 'bianca');
    for (const figure of ['Waiting for answers', 'Accepted', 'Rejected or withdrawn', 'Drafted by AI']) await demo.see(figure, { what: `the figure "${figure}"` });
    await demo.see('Decisions');
    await demo.see(/\d+ of \d+ approvals? in/, { what: 'each decision says how many approvals are in' });
    await demo.see('1 of 1 approval in');
  });

  step('S-19.2 What an AI client drafted', async ({ demo }) => {
    await demo.open('decisions', 'bianca');
    await demo.choose('Only those drafted by an AI client');
    if (demo.seeded("the tier decisions Claude Code drafted")) {
      await demo.see('Raise Route Planner to tier 2');
      await demo.see('Confirm tier 1 for Shift Swap Requests');
    }
    await demo.see('Type', { what: 'the Type filter' });
  });

  step('S-19.3 Exceptions and their review dates', async ({ demo }) => {
    await demo.open('decisions?view=exceptions', 'bianca');
    const deskAccepted = (await decisionStatus('bianca', STATEMENT.deskBooking)) === 'accepted';
    await demo.count('Standing', deskAccepted ? 3 : 2);
    await demo.count('Review overdue', 1);
    // The page counts the review dates in the next 30 days from the browser's own day, and the Desk Booking exception's
    // review date falls 30 or 31 days out, by the hour of the director's run: it counts only when it is inside the window.
    const desk = (await apiAll<any>('bianca', 'get_decision_log', { decisionTypeKeys: ['exception'] })).find(d => d.statement === STATEMENT.deskBooking);
    // The review date sits on the exception (exception.reviewOn); the window ends 30 days after the browser's own day.
    const local = new Date();
    const end = new Date(local.getFullYear(), local.getMonth(), local.getDate() + 30);
    const limit = `${end.getFullYear()}-${String(end.getMonth() + 1).padStart(2, '0')}-${String(end.getDate()).padStart(2, '0')}`;
    const reviewOn = desk?.exception?.reviewOn ?? desk?.reviewOn ?? null;
    const deskInWindow = !!reviewOn && String(reviewOn).slice(0, 10) <= limit;
    await demo.count('Review in 30 days', deskAccepted && deskInWindow ? 2 : 1);
    await demo.count('Waiting for answers', deskAccepted ? 1 : 2);
    await demo.see(/Travel Requests.{0,400}Review overdue by/, { what: 'the Travel Requests exception is overdue for review' });
  });

  step('S-19.4 Documents by state', async ({ demo }) => {
    await demo.open('documents?state=stale', 'bianca');
    // S-04.9 confirmed the runbook of Inspection Scheduler; S-04.5 registered an application with its documents missing.
    const runbookStale = /Inspection Scheduler.{0,200}Runbook|Runbook.{0,200}Inspection Scheduler/.test(await demo.text('main'));
    // S-06.4 met Claims Triage Agent's Design gate, so it moved to the build stage, where its wiki page is required.
    await demo.open('documents?state=missing');
    const buildWiki = /Wiki page.{0,200}Claims Triage Agent|Claims Triage Agent.{0,200}Wiki page/.test(await demo.text('main'));
    await demo.open('documents');
    await demo.count('Missing', buildWiki ? 3 : 2);
    await demo.count('Stale', runbookStale ? 3 : 2);
    await demo.count('Drafts to confirm', 3);
    if (!buildWiki && runbookStale) await demo.count('Current', 56);
    await demo.open('documents?state=stale');
    await demo.see('Stale documents');
    await demo.see(/Wiki page.{0,200}Legacy Timesheet App|Legacy Timesheet App.{0,200}Wiki page/, { what: 'the Wiki page of Legacy Timesheet App is stale' });
    await demo.see(/Wiki page.{0,200}Safety Findings Tracker|Safety Findings Tracker.{0,200}Wiki page/, { what: 'the Wiki page of Safety Findings Tracker is stale' });
    await demo.see('Runbook. Needed current for the Go-live gate, at Tier 2 and Tier 3.');
  });
});
