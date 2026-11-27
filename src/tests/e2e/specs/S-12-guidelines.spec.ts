// S-12 Maker: guidelines and the playbook (src/docs/demo/scenarios/S-12-guidelines.md). Reads only.
import { scenario } from '../lib/demo';

scenario('S-12', 'Maker: guidelines and the playbook', step => {
  step('S-12.1 From a refusal to its guideline', async ({ demo }) => {
    await demo.open('ai-activity?view=refused', 'priya');
    const chip = await demo.see(/G17 Let AI draft and recommend; people approve/, { what: 'a refused call carries the chip G17' });
    if (chip) {
      await demo.click({ text: 'G17', prefix: true, role: 'link', scope: 'main' });
      await demo.at(/\/guidelines\?g=G17/i, 'the chip opens Guidelines at G17');
    } else {
      await demo.open('guidelines?g=G17');
    }
    await demo.see('Guidelines and playbook');
    await demo.count('In force', 22);
    await demo.count('Retired', 2);
    await demo.count('Playbook chapters', 22);
  });

  step('S-12.2 What G17 says and where it applies', async ({ demo }) => {
    await demo.open('guidelines?g=G17', 'priya');
    await demo.see('G17 Let AI draft and recommend; people approve.');
    await demo.see('No AI agent or tool approves work, including its own.');
    await demo.see('Applied by 1 approval rule, 1 gate requirement. The engine names it when it refuses a call.');
    await demo.see(/APPROVAL RULES.{0,100}AI lead decides on a flagged agent/i, { what: 'it is applied by the approval rule "AI lead decides on a flagged agent"' });
    await demo.see(/GATE REQUIREMENTS.{0,100}Autonomy level agreed/i, { what: 'it is applied by the gate requirement "Autonomy level agreed"' });
    await demo.see('chapter 21, Governance inside the AI build loop');
  });

  step('S-12.3 Owners and data, G2', async ({ demo }) => {
    await demo.open('guidelines?g=G2', 'priya');
    await demo.see('G2 Name the owners of every app, each with a deputy.');
    await demo.see('Every application names a business owner and a technical owner, each with a deputy, and declares its data uses. Every data domain has a data owner and a deputy, who decides about the uses of its data.');
    await demo.click({ text: 'Playbook chapters', prefix: true, scope: 'main' });
    await demo.count('Playbook chapters', 22);
    await demo.see(/START HERE/i);
  });
});
