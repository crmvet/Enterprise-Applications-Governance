// S-10 Configuration admins: change the rules safely (src/docs/demo/scenarios/S-10-configuration.md). S-10.6 is the
// optional Claude Code step: the configuration admin's draft, checked and not stored (validateOnly).
import { apiCall, mcpCall } from '../lib/api';
import { scenario } from '../lib/demo';
import { STATEMENT, waitingFor } from '../lib/story';

async function versionInForce(): Promise<number> {
  const r = await apiCall('yusuf', 'whoami', {});
  return Number(r.body?.configurationSetVersion ?? 0);
}

scenario('S-10', 'Configuration admins: change the rules safely', step => {
  step('S-10.1 The rules in force', async ({ demo }) => {
    await demo.open('configuration', 'yusuf');
    const version = await versionInForce();
    await demo.see(`Version ${version} is in force since`);
    await demo.count('Version in force', version);
    if (version === 1) {
      await demo.count('Open drafts', 1);
      await demo.count('Rules in force', 693);
      await demo.see('Configuration rows of 43 kinds');
    }
    await demo.count('Health findings', 0);
    await demo.see(/Data taxonomy/i);
    await demo.count('Data classification', 4);
    await demo.count('Data domain level', 3);
    await demo.count('Data domain', 25);
    await demo.count('Data platform', 20);
    await demo.see('Gate levels for a normal change');
  });

  step('S-10.2 The draft', async ({ demo }) => {
    await demo.open('configuration?view=drafts', 'yusuf');
    if ((await versionInForce()) === 1) {
      await demo.count('Open drafts', 1);
      await demo.see(/Configuration draft CFG-0001.{0,40}Submitted/, { what: 'Configuration draft CFG-0001 is submitted' });
    }
    await demo.see('The AI use rule of Workplace / Visitor log changes from not set to never.');
    await demo.see('Instructed by Yusuf Brennan');
    await demo.see('From version 1');
    await demo.see('1 row change');
    await demo.notSee(/`[a-z-]+`/, { what: 'the draft names rows in plain words, not raw keys in backticks' });
  });

  step('S-10.3 What the change touches', async ({ demo }) => {
    await demo.open('configuration?view=drafts', 'yusuf');
    await demo.click({ text: 'Configuration draft', prefix: true, role: 'link', scope: 'main' });
    await demo.at(/\/records\/configuration-draft\//, 'the draft opens its record');
    await demo.see(/Data domain.{0,40}Visitor log.{0,40}AI use rule.{0,40}Not set.{0,20}becomes.{0,20}Never/i, { what: 'what it changes: the AI use rule of Visitor log, not set becomes never' });
    await demo.see('The rules check passed');
    await demo.see('A second configuration admin answers on the decision page.');
    await demo.see('No gate level moves');
    await demo.see(/DATA USES.{0,200}DU-0030:? ?Visitor Sign-In Kiosk/i, { what: 'the data use it touches: DU-0030, Visitor Sign-In Kiosk' });
    await demo.see(/Workplace \/ Visitor log.{0,100}Stays needs a decision/, { what: 'the kiosk use stays "needs a decision"' });
  });

  step('S-10.4 Another person accepts it', async ({ demo }) => {
    demo.writes('answer_approval@components/actions#AnswerSheet');
    await demo.as('jonah');
    if (!(await waitingFor('jonah')).includes(STATEMENT.configChange)) {
      if (!demo.readOnly) throw new Error('no configuration card waits for Jonah: is version 2 in force already?');
      await demo.open('approvals?view=mine');
      return;
    }
    await demo.openCard(STATEMENT.configChange);
    await demo.see(/WHAT CHANGES/i);
    await demo.see('Version 1 becomes version 2 once accepted.');
    await demo.see(/WHAT THE RULES REQUIRE/i);
    await demo.see('Configuration admin. The first answer counts.');
    await demo.see(/CONSULTATIONS.{0,200}Hugo Brandvold.{0,300}Consulted, advice only/i, { what: 'Hugo Brandvold, data owner of Workplace, is consulted by name' });
    // The draft list names the change in plain words (S-10.2); the decision page's subject should too.
    await demo.notSee(/What it decides about.{0,80}`[a-z-]+`/, { what: 'the decision page names the configuration draft in plain words, not a raw key in backticks' });
    await demo.see(/What it decides about.{0,80}The AI use rule of Workplace \/ Visitor log changes from not set to never/i, { what: 'the decision page names the change in plain words' });
    await demo.act('Jonah, the second configuration admin, accepts the change', async () => {
      await demo.answer('Visitor names must not reach AI tools. The kiosk use is checked again.', {
        answerShows: ['The engine accepted the decision.', '1 request that is no longer needed was canceled.'],
      });
      await demo.decisionIs('accepted', 'the engine accepts the configuration change');
    });
  });

  step('S-10.5 Version 2 is in force', async ({ demo }) => {
    await demo.open('configuration?view=versions', 'jonah');
    if ((await versionInForce()) !== 2) {
      if (!demo.readOnly) demo.check(false, 'version 2 is in force after S-10.4');
      return;
    }
    await demo.see('Version 2 is in force since');
    await demo.see(/Version 2.{0,40}In force/, { what: 'Version 2, In force' });
    await demo.see('Accepted by Jonah Pereira');
    await demo.see('Instructed by Yusuf Brennan');
    await demo.see(/fingerprint/i);
    await demo.see('Configuration version 2.');
  });

  step('S-10.6 Optional: Claude Code proposes a standard change model', async ({ demo }) => {
    await demo.mcpRead('Claude Code drafts a standard change model for the configuration admin, checked and not stored', async () => {
      const version = await versionInForce();
      const r = await mcpCall('propose_configuration_change', {
        baseVersionNumber: version,
        proposalRationale: 'A monthly content refresh is routine, tested and small: it should fit a standard change model.',
        operations: [{
          op: 'upsert',
          entity: 'standard-change-model',
          key: 'monthly-content-refresh',
          values: {
            label: 'Monthly content refresh',
            description: 'A tested content update to one application at tier 1 or 2, in a window of two hours or less.',
            changeTypeKey: 'standard',
            tierKey: 'tier-2',
            conditions: [{ conditionKind: 'max-affected-applications', value: 1 }, { conditionKind: 'max-window-minutes', value: 120 }],
          },
        }],
        validateOnly: true,
      }, 'yusuf');
      demo.check(r.ok && String(r.summary).includes('The change is valid. Nothing was stored (validateOnly).'), 'propose_configuration_change: valid, nothing stored', String(r.summary ?? JSON.stringify(r.error)));
      const diff = JSON.stringify(r.data?.diffSummary ?? r.data ?? {});
      demo.check(/standard-change-model/.test(diff) && /monthly-content-refresh/.test(diff) && /added/.test(diff), 'the diff adds standard-change-model monthly-content-refresh', diff.slice(0, 400));
      demo.check(r.data?.isAiCreated === true, 'the draft is marked AI-made', JSON.stringify(r.data?.isAiCreated));
      demo.check(r.data?.performedBy?.principal?.displayLabel === 'Yusuf Brennan', 'the draft is stamped for Yusuf Brennan, not an unknown principal', JSON.stringify(r.data?.performedBy ?? null));
      demo.check(JSON.stringify(r.data ?? {}).includes('Submit the draft. A person accepts the decision on its decision page. Nothing changes until then.'), 'the activation says a person accepts it on its decision page', JSON.stringify(r.data?.activation ?? null));
    });
    await demo.open('configuration?view=drafts', 'yusuf');
  });
});
