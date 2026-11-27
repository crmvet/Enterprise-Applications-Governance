// S-15 Auditor: answer from the record (src/docs/demo/scenarios/S-15-auditor.md). Reads only.
import { apiCall, appId } from '../lib/api';
import { scenario } from '../lib/demo';

scenario('S-15', 'Auditor: answer from the record', step => {
  step('S-15.1 The evidence pack', async ({ demo }) => {
    await demo.open(`evidence/business-application/${await appId('Invoice Matching')}`, 'bianca');
    await demo.see(/Decisions ?5.{0,80}2 approval answers/, { what: 'Decisions 5, with 2 approval answers' });
    await demo.count('Changes', 2);
    await demo.see(/Deployments ?2.{0,40}2 flagged/, { what: 'Deployments 2, 2 flagged' });
    await demo.count('Reviews', 1);
    await demo.count('Documents', 2);
    await demo.count('Evidence links', 0);
    await demo.see('Print the pack');
  });

  step('S-15.2 Who answered, and whose answer counted', async ({ demo }) => {
    await demo.open(`evidence/business-application/${await appId('Invoice Matching')}`, 'bianca');
    await demo.see('Decisions and approval answers');
    await demo.see('Invoice Matching 1.12');
    await demo.see('Finance Approvals Agent, asked as Business owner, Recommendation only');
    await demo.see('A recommendation only: an AI answer never counts.');
    await demo.see('Agent identity');
    await demo.see('Clara Voss, asked as Business owner, Approved, Counted toward the approval');
  });

  step('S-15.3 Deployments, flags and the rules version', async ({ demo }) => {
    await demo.open(`evidence/business-application/${await appId('Invoice Matching')}`, 'bianca');
    await demo.see(/invoice-matching 1\.12\.1.{0,60}To Finance Prod.{0,300}Missing approval.{0,100}AI-only code approval/, { what: 'invoice-matching 1.12.1 to Finance Prod: missing approval and AI-only code approval' });
    await demo.see(/invoice-matching 1\.12\.0.{0,300}AI-only code approval/, { what: 'invoice-matching 1.12.0: AI-only code approval' });
    await demo.see('Checked under version 1');
    const r = await apiCall('bianca', 'whoami', {});
    if (Number(r.body?.configurationSetVersion) >= 2) await demo.see('Version 2 is in force now, so one older version applies to some rows.');
    await demo.notSee(/Invoice Matching 1\.13 Normal\.[^.]*\. Authorized on its decision/, { what: 'the pack does not call the submitted change "Invoice Matching 1.13" authorized while its decision is only proposed' });
  });

  step('S-15.4 The audit trail of AI calls', async ({ demo }) => {
    await demo.open('audit?actor=ai-client-for-person', 'bianca');
    for (const figure of ['Entries', 'Refused calls', 'By AI clients']) await demo.see(figure, { what: `the figure "${figure}"` });
    if (demo.seeded("the audit entries of Claude Code's seeded calls")) {
      await demo.see(/Refused the call to answer an approval request.{0,300}By Priya Castell through Claude Code/, { what: 'the refused answer_approval call, by Priya Castell through Claude Code' });
      await demo.see('The rule: AI never approves');
      await demo.see(/G17/);
      await demo.see('Refused the call to save a data use');
      await demo.see('For the audit: code rule_violation, reason data.rule-never');
      await demo.see('Everything for Priya Castell');
      await demo.see('Everything through Claude Code');
    }
    await demo.notSee(/On a intake/, { what: 'the audit trail writes "an intake request", not "a intake request"' });
  });
});
