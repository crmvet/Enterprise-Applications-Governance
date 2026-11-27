// S-05 Change manager: changes and flagged deployments (src/docs/demo/scenarios/S-05-changes-and-deployments.md).
import { apiAll, appId, day } from '../lib/api';
import { scenario, type Demo } from '../lib/demo';

const NEW_CHANGE = 'Crew Roster 5.4: overtime column';

async function newChange(demo: Demo) {
  const crew = await appId('Crew Roster');
  return (await apiAll<any>('hugo', 'get_change_schedule', { businessApplicationId: crew, from: day(-2), to: day(40) })).find(c => c.title === NEW_CHANGE) ?? null;
}

scenario('S-05', 'Change manager: changes and flagged deployments', step => {
  step('S-05.1 A new change on Crew Roster', async ({ demo }) => {
    await demo.open(`changes/new?app=${await appId('Crew Roster')}`, 'hugo');
    await demo.see(/1\. The application/i);
    await demo.see(/Crew Roster.{0,200}Tier 2.{0,200}Operate/, { what: 'Crew Roster, Tier 2, Operate' });
    await demo.see(/Business owner:? ?Ines Varga/, { what: 'the business owner is Ines Varga' });
    await demo.see(/Who authorizes it, by type/i);
    await demo.see(/Emergency.{0,80}change manager/i, { what: 'an emergency change: the change manager' });
    await demo.see(/Normal.{0,80}business owner/i, { what: 'a normal change: the business owner' });
    await demo.see(/Standard.{0,80}no authorization when an approved standard model fits/i, { what: 'a standard change: no authorization when a model fits' });
    await demo.see(/Applications that depend on it/i);
    await demo.see(/Crew Shift Mobile.{0,80}Directly/, { what: 'Crew Shift Mobile depends on it directly' });
    await demo.see(/Shift Swap Requests.{0,80}Directly/, { what: 'Shift Swap Requests depends on it directly' });
  });

  step('S-05.2 Plan the change', async ({ demo }) => {
    demo.writes('save_change_request_draft@pages/changes-records/ChangeDraftSheet#ChangeDraftSheet');
    await demo.open(`changes/new?app=${await appId('Crew Roster')}`, 'hugo');
    await demo.act('Hugo plans a normal change on Crew Roster, checks it and saves the draft', async () => {
      const start = day(7);
      await demo.sheet({
        open: { text: 'Plan the change', scope: 'main' },
        fill: async () => {
          await demo.fill('Title', NEW_CHANGE, 'dialog');
          await demo.select('Choose the type', 'Normal');
          await demo.fill('What changes, and why', 'Adds an overtime column to the shift view, so supervisors see who is near the limit.', 'dialog');
          await demo.setValue('Window starts', `${start}T13:00`, 'dialog');
          await demo.setValue('Window ends', `${start}T14:00`, 'dialog');
          await demo.select('No target yet', 'Field Ops Prod, production');
        },
        preview: 'Check first',
        previewShows: ['This change needs authorization before it is deployed. 2 other applications would be marked as affected.', /WILL BE ASKED ONCE SUBMITTED:? ?Business owner of the application \(everyone must approve\)/],
        submit: 'Save draft',
        what: 'Save the change draft',
        answerShows: ['The change request is saved as a draft (built-in mode).'],
      });
    });
  });

  step('S-05.3 Check and submit', async ({ demo }) => {
    demo.writes('submit_record@pages/changes-records/ChangeActions#SubmitChangeSheet');
    await demo.act('Hugo reads the checks and submits the change', async () => {
      await demo.as('hugo');
      if (!/3\. Check and submit/i.test(await demo.text('main'))) {
        const change = await newChange(demo);
        if (!change) throw new Error(`no change "${NEW_CHANGE}": S-05.2 did not run on this store`);
        await demo.open(`records/change-request/${String(change.changeRequestId).toLowerCase()}`);
      }
      await demo.see(/STANDARD CHANGE CHECK.{0,80}Does not fit a standard change model/i, { what: 'the standard change check: it does not fit a model' });
      await demo.see(/WHO WILL AUTHORIZE IT.{0,80}Business owner of the application/i, { what: 'who will authorize it: the business owner' });
      await demo.see(/GATE LEVEL AT THE TARGET.{0,40}Advise/i, { what: 'the gate level at the target: Advise' });
      await demo.sheet({ open: { text: 'Submit', scope: 'main' }, submit: 'Submit', confirm: 'Submit', what: 'Submit the change', answerShows: ['Submitted. 1 approval is asked.'] });
      await demo.api('the new change after the submit', async () => {
        const change = await newChange(demo);
        demo.check(change && /submitted/i.test(JSON.stringify(change.status)), 'get_change_schedule: the change is submitted', JSON.stringify(change?.status ?? null));
      });
    });
  });

  step('S-05.4 The window on the Timeline', async ({ demo }) => {
    await demo.open('timeline', 'hugo');
    if (!(await newChange(demo))) {
      demo.note('The Crew Roster 5.4 window shows once S-05.2 and S-05.3 have run.');
      return;
    }
    await demo.see(NEW_CHANGE, { what: 'the Timeline shows the window of the new change' });
    await demo.click({ text: NEW_CHANGE, contains: true, scope: 'main' });
    await demo.at(/\/records\/change-request\//, 'the window opens the change record');
    await demo.see(NEW_CHANGE);
    await demo.see('Submitted');
    await demo.see('Normal');
    await demo.see('Not authorized yet');
    await demo.count('Affected applications', 2);
  });

  step('S-05.5 Changes closed before the demo', async ({ demo }) => {
    await demo.open('changes?view=list', 'hugo');
    await demo.see(/Remove the Timesheets canvas app.{0,200}Legacy Timesheet App/, { what: 'the removal change on Legacy Timesheet App is listed' });
    await demo.see(/Remove the Timesheets canvas app.{0,300}Closed/, { what: 'the removal change is closed' });
    await demo.see(/Fix the shift export after a payroll change.{0,200}Crew Roster.{0,200}Emergency/, { what: 'the emergency fix on Crew Roster is listed' });
    await demo.see(/Fix the shift export after a payroll change.{0,300}Closed/, { what: 'the emergency fix is closed' });
    await demo.see('Authorized');
  });

  step('S-05.6 Deployments that only an AI approved', async ({ demo }) => {
    await demo.open('deployments?view=flagged&flag=ai-only-code-approval', 'hugo');
    await demo.count('Flagged deployments', demo.aiSeeded ? 3 : 2);
    if (demo.seeded('Route Planner 2.0.0-rc.1, made from Claude Code')) {
      await demo.see(/Route Planner 2\.0\.0-rc\.1.{0,200}Drafted by Claude Code for Priya Castell/, { what: 'Route Planner 2.0.0-rc.1 was drafted by Claude Code for Priya Castell' });
      await demo.see("Owner's AI tool");
    }
    await demo.see(/Invoice Matching 1\.12\.1.{0,200}Exception waiting/, { what: 'Invoice Matching 1.12.1 has an exception waiting' });
    await demo.see('Invoice Matching 1.12.0');
    await demo.click({ text: 'Invoice Matching 1.12.1', role: 'link', scope: 'main' });
    await demo.see(/Missing approval:? ?raised.{0,300}G12/, { what: 'the missing approval flag, with G12' });
    await demo.see(/AI-only code approval:? ?raised\.? ?The only code approval came from an AI\. It never counts as authorization\..{0,200}G17/, { what: 'the AI-only code approval flag, with G17' });
    await demo.see('Open the exception waiting');
  });
});
