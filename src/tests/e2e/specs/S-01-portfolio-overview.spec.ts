// S-01 PMO: the portfolio at a glance (src/docs/demo/scenarios/S-01-portfolio-overview.md). Reads only.
import { apiData, portfolioId } from '../lib/api';
import { scenario } from '../lib/demo';

scenario('S-01', 'PMO: the portfolio at a glance', step => {
  step('S-01.1 The whole register', async ({ demo }) => {
    await demo.as('mateo');
    await demo.open('overview');
    await demo.see('Portfolio overview');
    await demo.see('All portfolios. 48 applications in the register.');
    await demo.count('Applications', 48);
    await demo.count('Owner gaps', 6);
    await demo.count('Reviews overdue', 4);
    await demo.count('Documents missing or stale', 5);
    for (const tile of ['Approvals waiting', 'AI drafts waiting', 'Exceptions', 'Old drafts', 'Data gaps']) await demo.see(tile, { what: `the tile "${tile}" shows` });
  });

  step('S-01.2 One portfolio', async ({ demo }) => {
    await demo.as('mateo');
    await demo.open('overview');
    await demo.select('All portfolios', 'Field operations');
    const field = await portfolioId('Field operations');
    await demo.at(new RegExp(`[?&]portfolio=${field}`), 'the address gains the portfolio');
    await demo.see('Field operations. ', { what: 'the overview narrows to Field operations' });
    await demo.count('Applications', 15);
    await demo.count('Owner gaps', 2);
    await demo.count('Reviews overdue', 1);
    await demo.count('Documents missing or stale', 2);
    // Every tile follows the portfolio choice (wave 4b review): the approvals and the AI drafts waiting too.
    await demo.count('Approvals waiting', 14);
    await demo.see('Field operations, asked and not answered yet', { what: 'Approvals waiting names Field operations' });
    await demo.count('AI drafts waiting', 1);
    await demo.count('Exceptions', 0);
    await demo.count('Old drafts', 0);
    await demo.see(/Old drafts.{0,30}About its applications/i, { what: 'Old drafts counts the drafts about the applications of the portfolio' });
    await demo.count('Data gaps', 5);
    await demo.notSee(/all portfolios/i, { scope: 'main', what: 'no tile counts all portfolios any more' });
    await demo.api('the portfolio summary of Field operations', async () => {
      const s = await apiData('mateo', 'get_portfolio_summary', { portfolioId: field });
      demo.check(s.ownerGaps === 2, 'get_portfolio_summary: two owner gaps in Field operations', JSON.stringify({ ownerGaps: s.ownerGaps }));
    });
  });

  step('S-01.3 The owner gaps', async ({ demo }) => {
    await demo.as('mateo');
    const field = await portfolioId('Field operations');
    await demo.open(`overview?portfolio=${field}`);
    await demo.click({ text: 'Owner gaps', prefix: true, scope: 'main' });
    await demo.at(new RegExp(`/owners\\?view=gaps&portfolio=${field}`), 'Owner gaps opens Owners, Gaps, filtered to Field operations');
    await demo.count('Owner gaps', 2);
    await demo.see(/Inspection Scheduler.{0,80}Business owner: no deputy/, { what: 'Inspection Scheduler has no deputy business owner' });
    await demo.see(/Safety Findings Tracker.{0,80}Business owner: no holder/, { what: 'Safety Findings Tracker has no business owner' });
    await demo.see('Owner change waiting', { what: 'the gap is marked "Owner change waiting"' });
  });

  step('S-01.4 The reviews overdue', async ({ demo }) => {
    await demo.as('mateo');
    const field = await portfolioId('Field operations');
    await demo.open(`overview?portfolio=${field}`);
    await demo.click({ text: 'Reviews overdue', prefix: true, scope: 'main' });
    await demo.at(new RegExp(`/reviews\\?state=overdue&portfolio=${field}`), 'Reviews overdue opens Reviews, Overdue, filtered to Field operations');
    await demo.count('Overdue reviews', 1);
    await demo.seeAll(['Owner check', 'Safety Findings Tracker', 'Overdue by']);
    // A phone shows each review as a card, without the column that says what opened it.
    if (!demo.phone) await demo.see('Opened by an owner check or a leave');
  });

  step('S-01.5 What needs attention', async ({ demo }) => {
    await demo.as('mateo');
    await demo.open('overview');
    await demo.see(/FLAGGED DEPLOYMENTS/i, { what: 'the Overview has its flagged deployments' });
    if (demo.seeded('route-planner 2.0.0-rc.1, made from Claude Code')) {
      await demo.see(/route-planner 2\.0\.0-rc\.1 to Field Ops Test.{0,120}AI-only code approval/, { what: 'route-planner 2.0.0-rc.1 is flagged for an AI-only code approval' });
    }
    await demo.see(/invoice-matching 1\.12\.1 to Finance Prod.{0,160}Missing approval/, { what: 'invoice-matching 1.12.1 is flagged for a missing approval' });
    await demo.see(/invoice-matching 1\.12\.1 to Finance Prod.{0,200}AI-only code approval/, { what: 'invoice-matching 1.12.1 is flagged for an AI-only code approval' });
    await demo.see('Coming up');
    await demo.see('Release 3.5: inspector workload view');
    await demo.see('Retire the old export flow');
  });
});
