// S-07 Data: domains, a data-use card, the never rule and copies (src/docs/demo/scenarios/S-07-data-taxonomy.md).
import { appId, decisionByStatement, mcpCall } from '../lib/api';
import { scenario } from '../lib/demo';
import { STATEMENT } from '../lib/story';

const NEVER = 'Data rule never: the AI use rule is never, set on People / Payroll and inherited by People / Payroll / Bank details. Nobody has the privilege to save this data use; change the rule where it is set through a configuration change, or leave the data out.';

scenario('S-07', 'Data: domains, a data-use card, the never rule and copies', step => {
  step('S-07.1 The domain tree', async ({ demo }) => {
    await demo.open('data', 'hana');
    await demo.see('Domain tree');
    await demo.count('Domains', 25);
    await demo.count('Locations', 22);
    await demo.count('No data owner', 2);
    await demo.count('Decisions missing', 4);
    await demo.count('Breaking a rule', 0);
    await demo.count('Copies outside production', 2);
    await demo.see(/Commercial.{0,200}No data owner/, { what: 'Commercial reads "No data owner"' });
    await demo.see('Propose an owner');
    // A phone shows the tree a level at a time: open People, then Payroll, as a presenter does.
    if (demo.phone) await demo.click({ text: 'domains under People', contains: true, scope: 'main' });
    await demo.see(/Payroll.{0,200}Highly Confidential/, { what: 'Payroll is Highly Confidential' });
    await demo.see(/Payroll.{0,300}AI use:? ?Never/, { what: 'Payroll: AI use Never' });
    await demo.see(/Payroll.{0,300}Copy:? ?Never/, { what: 'Payroll: Copy Never' });
    await demo.see(/Payroll.{0,400}No deputy/, { what: 'Payroll has no deputy' });
    if (demo.phone) await demo.click({ text: 'domains under Payroll', contains: true, scope: 'main' });
    await demo.see(/Paychecks.{0,200}from Payroll/, { what: 'Paychecks inherits from Payroll' });
    await demo.see(/Bank details.{0,200}from Payroll/, { what: 'Bank details inherits from Payroll' });
  });

  step("S-07.2 The data owner's card", async ({ demo }) => {
    await demo.open('work', 'kai');
    await demo.see('Data approvals asked of you');
    await demo.see(STATEMENT.visitDataUse);
    await demo.click({ text: 'Answer', near: STATEMENT.visitDataUse, scope: 'main' });
    await demo.at(/\/records\/decision\//, 'the card opens its decision page');
    await demo.see(/The data use it decides/i);
    await demo.see('DU-0004: Reads at Field Ops Prod');
    await demo.see('AI use: Decision');
    await demo.see('0 of 2 approvals');
    await demo.see(/WHO IS ASKED/i);
    await demo.see(/Kai Ostrowski.{0,80}Data owner/, { what: 'Kai Ostrowski is asked as data owner' });
    await demo.see('Any security reviewer');
    await demo.see(/Lena Okoro.{0,120}Data steward.{0,120}Consulted/, { what: 'Lena Okoro, the data steward, is consulted' });
  });

  step('S-07.3 The data owner approves', async ({ demo }) => {
    demo.writes('answer_approval@components/actions#AnswerSheet');
    await demo.open('work', 'kai');
    await demo.act('Kai approves the data use on its decision page', async () => {
      await demo.click({ text: 'Answer', near: STATEMENT.visitDataUse, scope: 'main' });
      await demo.answer('Only open findings of one site go to the approved model, and the inspector checks each draft.', {
        preview: true,
        answerShows: ['The decision stays proposed.'],
      });
      await demo.see('1 of 2 approvals', { timeout: 15000 });
    });
  });

  step('S-07.4 The security reviewer approves', async ({ demo }) => {
    demo.writes('answer_approval@components/actions#AnswerSheet');
    await demo.as('noor');
    const d = await decisionByStatement('noor', STATEMENT.visitDataUse);
    if (!d) throw new Error('the Visit Report Agent data-use decision is not on the decision log');
    await demo.open(`records/decision/${String(d.decision.decisionId).toLowerCase()}`);
    await demo.act('Noor, the security reviewer, approves the data use', async () => {
      await demo.answer('The model stays in the region, and a person checks every draft.', {
        answerShows: ['The engine accepted the decision.', '1 request that is no longer needed was canceled.'],
      });
      await demo.decisionIs('accepted', 'the engine accepts the data-use decision');
      await demo.see('Allowed: An accepted decision allows this use.', { timeout: 15000 });
    });
  });

  step('S-07.5 Claude Code meets the never rule', async ({ demo }) => {
    await demo.mcp("Claude Code declares Expense Assistant's use of the bank details with AI", async () => {
      const app = await appId('Expense Assistant');
      const r = await mcpCall('save_data_use', { applicationId: app, domainKey: 'bank-details', access: 'read', reachesAi: true, purpose: 'Checks the bank account on each expense claim.', clientRequestId: 'e2e:S-07.5:bank-details' });
      demo.check(!r.ok && r.error?.code === 'rule_violation', 'save_data_use is refused when it is saved: rule_violation', JSON.stringify(r.error ?? r.data));
      demo.check(String(r.error?.message ?? '').includes(NEVER), 'the refusal names the never rule, where it is set and the domain that inherits it', String(r.error?.message));
      demo.check(/G16/.test(JSON.stringify(r.error ?? {})), 'the refusal carries guideline G16', JSON.stringify(r.error ?? {}));
    });
    await demo.open('ai-activity?view=refused', 'priya');
    await demo.see(/save_data_use|A rule refused it/, { what: 'AI activity, Refused lists the call' });
  });

  step('S-07.6 A person meets the same rule', async ({ demo }) => {
    // The refusal is the proof: nothing is written, so the step declares no write (wave 4b QA; S-07.11 declares a use).
    await demo.open('data/uses', 'priya');
    await demo.see('Data uses');
    await demo.act('Priya declares the same use on the web page: refused before and at the save', async () => {
      await demo.sheet({
        open: { text: 'Declare a use', scope: 'main' },
        fill: async () => {
          await demo.fill('Application', 'Expense', 'dialog');
          await demo.click({ text: 'Expense Assistant', prefix: true, scope: 'dialog' });
          await demo.select('Choose a domain', 'People / Payroll / Bank details');
          await demo.choose('Reads', 'dialog');
          await demo.fill('Purpose', 'Checks the bank account on each expense claim.', 'dialog');
          await demo.choose('The data reaches an AI model or agent', 'dialog');
          await demo.see(/WHAT THE RULES SAY/i, { scope: 'dialog' });
          await demo.see('Breaks a rule', { scope: 'dialog' });
          await demo.see('The AI use rule is Never, set on Payroll.', { scope: 'dialog' });
          await demo.see('This use cannot be saved', { scope: 'dialog', what: 'before saving, the sheet says this use cannot be saved' });
        },
        submit: 'Declare the use',
        expect: 'refused',
        what: 'Declare the use',
        answerShows: ['A rule refused this', NEVER, /The rule:? ?Payroll/, /G16/, 'Nothing was written.'],
      });
    });
  });

  step('S-07.7 An AI draft waits to be confirmed', async ({ demo }) => {
    await demo.open('data/uses', 'priya');
    if (!demo.seeded("Mileage Claims' draft use from Claude Code")) return;
    await demo.see(/Mileage Claims.{0,300}DU-0031, a draft/, { what: 'Mileage Claims has the draft use DU-0031' });
    await demo.see(/DU-0031.{0,300}Drafted by Claude Code for Priya Castell/, { what: 'drafted by Claude Code for Priya Castell' });
    await demo.see(/DU-0031.{0,400}People \/ Payroll \/ Paychecks/, { what: 'it touches People / Payroll / Paychecks' });
    await demo.see('Decision needed');
    await demo.see('Confirm');
  });

  step('S-07.8 Copies outside production', async ({ demo }) => {
    await demo.open('data/mapping', 'hana');
    await demo.count('Copies outside production', 2);
    await demo.see(/Customers \/ Client accounts and contacts.{0,300}Copy of production at Customer Test \(Test\)/, { what: 'a copy of client accounts sits in Customer Test' });
    await demo.see(/Copy of production at Customer Test \(Test\).{0,200}Breaks a rule/, { what: 'the client accounts copy breaks a rule' });
    await demo.see('Change the copy');
    await demo.see('End the mapping');
    await demo.see(/Commercial \/ Contracts \/ Supplier contracts.{0,300}Masked copy at Finance Test \(Test\)/, { what: 'a masked copy of supplier contracts sits in Finance Test' });
    await demo.see(/Masked copy at Finance Test \(Test\).{0,200}Waiting for approval/, { what: 'the masked copy waits for approval' });
  });

  step('S-07.9 Locations', async ({ demo }) => {
    await demo.open('data/locations', 'hana');
    await demo.see(/PAYR01.{0,80}Production\.? ?No technical owner/, { what: 'PAYR01 has no technical owner' });
    await demo.see(/PAYLIB.{0,300}Bank details/, { what: 'PAYLIB holds Bank details' });
    await demo.see(/PAYLIB.{0,300}Paychecks/, { what: 'PAYLIB holds Paychecks' });
    await demo.see(/TIMESQL01.{0,200}Technical owner:? ?Felix Arendt/, { what: 'TIMESQL01 has Felix Arendt as technical owner' });
    const m = await demo.see(/TIMESQL01.{0,300}Not seen for (\d+) days/, { what: 'TIMESQL01 has not been seen for days' });
    if (m) demo.check(Number(m[1]) > 200, 'TIMESQL01 was not seen for more than 200 days', m[0]);
  });

  step('S-07.10 Data gaps', async ({ demo }) => {
    await demo.open('data/gaps', 'hana');
    await demo.count('All', 31);
    await demo.count('High', 10);
    await demo.count('Medium', 7);
    await demo.count('Low', 14);
    await demo.count('DC-01 No owner', 2);
    await demo.count('DC-03 No deputy', 2);
    await demo.count('DC-04 Owner or deputy inactive', 3);
    await demo.count('DC-05 Decision missing', 4);
    await demo.count('DC-08 Copy outside production', 2);
    await demo.count('DC-10 Undeclared data', 13);
    await demo.see(/Wren Abernathy.{0,200}Client orders|Client orders.{0,200}Wren Abernathy/, { what: 'Wren Abernathy on Client orders' });
    await demo.see(/Greta Lund.{0,200}Payroll|Payroll.{0,200}Greta Lund/, { what: 'Greta Lund on Payroll' });
    await demo.see('Propose an owner');
  });

  step('S-07.11 Declare what Route Planner reads', async ({ demo }) => {
    demo.writes('save_data_use@pages/data/sheets#DataUseSheet');
    await demo.open(`records/business-application/${await appId('Route Planner')}?tab=data`, 'owen');
    if (demo.readOnly || (await demo.has({ text: 'Declare it', scope: 'main' }))) {
      await demo.see('Route Planner reads or writes data of another application and declares no data use.');
      await demo.see(/Inspection Scheduler.{0,40}Reads data from/, { what: 'it reads data from Inspection Scheduler' });
    }
    await demo.act("Owen, technical owner of Route Planner, declares its use of Inspections", async () => {
      await demo.sheet({
        open: { text: 'Declare it', scope: 'main' },
        fill: async () => {
          await demo.select('Choose a domain', 'Field operations / Inspections');
          await demo.choose('Reads', 'dialog');
          await demo.fill('Purpose', "Reads the day's inspections to plan the crews' routes.", 'dialog');
          await demo.see(/WHAT THE RULES SAY/i, { scope: 'dialog' });
          await demo.see('Not needed', { scope: 'dialog' });
          await demo.see('No decision is needed: the rule allows this use.', { scope: 'dialog' });
        },
        preview: 'Check first',
        submit: 'Declare the use',
        what: "Declare Route Planner's use of Inspections",
      });
      await demo.count('Data uses', 1);
      await demo.see(/Field operations \/ Inspections.{0,200}Reads/, { what: 'the new use reads Field operations / Inspections' });
      await demo.see(/DU-\d{4}/, { what: 'the use has a DU number' });
      await demo.see(/Data owners from its uses.{0,200}Mateo Brandt/, { what: 'its data owners from its uses: Mateo Brandt' });
    });
  });
});
