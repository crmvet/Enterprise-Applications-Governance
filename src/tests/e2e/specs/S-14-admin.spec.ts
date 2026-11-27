// S-14 Platform admin: run the product (src/docs/demo/scenarios/S-14-admin.md).
import { scenario } from '../lib/demo';

scenario('S-14', 'Platform admin: run the product', step => {
  step('S-14.1 The product at a glance', async ({ demo }) => {
    await demo.open('admin', 'owen');
    for (const figure of ['Configuration', 'Open drafts', 'Refused calls', 'Notices sent']) await demo.see(figure, { what: `the figure "${figure}"` });
    await demo.count('Clients that called', 2);
    await demo.count('Outbox needs a look', 0);
  });

  step('S-14.2 What the daily check did', async ({ demo }) => {
    await demo.open('admin', 'owen');
    await demo.see('The daily check');
    await demo.see(/RECENT RUNS/i);
    await demo.see(/SENT IN THE LAST 7 DAYS/i);
    await demo.see('run by Owen Achterberg: 1 review opened, 4 reviews marked overdue');
    await demo.see('Opened: Access check: Identity Directory Sync');
    await demo.see(/Access check: Identity Directory Sync.{0,300}To Elif Dunmore, the business owner, by teams/, { what: 'the notice went to Elif Dunmore, the business owner, by Teams' });
    await demo.see('Overdue');
  });

  step('S-14.3 Run it now, twice', async ({ demo }) => {
    demo.writes('run_due_state_refresh@pages/admin/AdminPage#RefreshSheet');
    await demo.open('admin', 'owen');
    await demo.act('Owen runs the daily check now: a check first, then the run', async () => {
      await demo.sheet({
        open: { text: 'Run the daily check now', scope: 'main' },
        preview: 'Check what it would do',
        previewShows: ['Checked: nothing was written yet.', 'Nothing was due. A rerun today changes nothing.'],
        submit: 'Run it now',
        confirm: 'Run it now',
        what: 'Run the daily check now',
        answerShows: ['The due-state refresh ran: nothing was due today.'],
      });
    });
  });

  step('S-14.4 Clients and the approval outbox', async ({ demo }) => {
    await demo.open('admin', 'owen');
    await demo.click({ text: 'Clients that called', prefix: true, scope: 'main' });
    await demo.see(/Claude Code.{0,120}AI client/, { what: 'Claude Code is an AI client' });
    await demo.see('Sanctioned');
    await demo.see(/Web app.{0,120}The product's own web app, where people work/, { what: "the web app: the product's own web app" });
    // The figure "Outbox needs a look" filters the outbox to the cards that need a look; the presenter scrolls instead.
    await demo.see('Approval outbox');
    await demo.count('Failed', 0);
    await demo.count('Expired', 0);
    for (const state of ['Waiting to send', 'Answered', 'Canceled']) await demo.see(state, { what: `the outbox state "${state}"` });
    await demo.see('No request needs a look: nothing failed, and nothing waits too long.');
  });

  step('S-14.5 Host, engine and the Tool Policy', async ({ demo }) => {
    await demo.open('admin', 'owen');
    await demo.see('Host and engine');
    await demo.see('The product never shows a key, a connection string or a tenant.');
    for (const label of ['ENVIRONMENT', 'DAILY CHECK', 'STORE', 'ENGINE VERSION']) await demo.see(new RegExp(label, 'i'), { what: `the setting group ${label}` });
    await demo.count('Tool Policy', 102);
    for (const group of ['SESSION', 'PORTFOLIO', 'INTAKE', 'DECISIONS', 'CHANGE', 'DEPLOYMENT', 'DOCUMENTATION', 'GAP CHECK', 'CONFIGURATION', 'LINKS', 'DATA']) {
      await demo.see(new RegExp(`\\b${group}\\b`), { what: `the Tool Policy group ${group}` });
    }
  });
});
