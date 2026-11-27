// S-11 Platform admin: roles and access (src/docs/demo/scenarios/S-11-roles-and-access.md).
import { scenario } from '../lib/demo';

scenario('S-11', 'Platform admin: roles and access', step => {
  step('S-11.1 Every product role', async ({ demo }) => {
    await demo.open('roles', 'owen');
    await demo.count('Product roles', 15);
    await demo.count('No group bound', 0);
    await demo.count('Workload grants', 2);
    await demo.count('Separation rules', 2);
    await demo.count('Roles', 19);
    await demo.see(/Requester.{0,120}Through Product requesters/, { what: 'Requester: through Product requesters' });
    await demo.see(/Maker.{0,120}Through Field ops makers/, { what: 'Maker: through Field ops makers' });
    await demo.see(/Application owner.{0,120}Through 5 owner roles/, { what: 'Application owner: through 5 owner roles' });
    await demo.see(/Platform admin.{0,120}You hold it/, { what: 'Platform admin: you hold it' });
    await demo.see('Reads only');
  });

  step('S-11.2 The data governance lead', async ({ demo }) => {
    await demo.open('roles?role=data-governance-lead', 'owen');
    await demo.see('Appoints the owners of top-level data domains, keeps data locations and mappings current, and sees every data gap.');
    await demo.count('Tools it may call', 67);
    await demo.see('AI clients may read it for the person');
    await demo.see('An AI client goes as far as a proposed decision, which a person submits.');
    await demo.see('Groups and holders');
    await demo.see('Decision rights');
    await demo.see('Approvals it gives');
  });

  step('S-11.3 Two configuration admins', async ({ demo }) => {
    await demo.open('roles?role=configuration-admin', 'owen');
    await demo.see("On this install the members are the demo's fictional people in each group (the personas and the estate's other people), not people read from Entra.");
    await demo.see(/HOLDERS NOW.{0,300}Jonah Pereira/i, { what: 'Jonah Pereira holds it now' });
    await demo.see(/HOLDERS NOW.{0,300}Yusuf Brennan/i, { what: 'Yusuf Brennan holds it now' });
    await demo.count('Decision rights', 2);
    await demo.see('Configuration change');
    await demo.see('Gate policy change');
    await demo.count('Approvals it gives', 4);
  });

  step('S-11.4 The binding check', async ({ demo }) => {
    await demo.open('roles', 'owen');
    await demo.act('Owen runs the binding check', async () => {
      await demo.click({ text: 'Run the binding check', scope: 'main' });
      await demo.see(/Checked.{0,80}against the demo personas/, { what: 'the check ran against the demo personas', timeout: 20000 });
      await demo.see('Every human role is bound to a group that exists.');
      await demo.see('The findings below are about workload identities.');
      await demo.see('Group exists');
    });
  });
});
