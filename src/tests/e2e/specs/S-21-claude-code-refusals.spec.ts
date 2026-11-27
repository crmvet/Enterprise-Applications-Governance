// S-21 Claude Code over MCP: what an AI client may do, and what it may not
// (src/docs/demo/scenarios/S-21-claude-code-refusals.md; the calls of claude-code.md, section "S-21"). The calls go to
// the MCP endpoint as the development identity "priya". The reads run in read-only mode too; the refused calls write
// no row but stay on the audit trail, so read-only mode skips them.
import { apiAll, appId, mcpCall } from '../lib/api';
import { scenario } from '../lib/demo';

const CHANGE_BOARD = '00000000-0000-4000-8000-000000000111';

scenario('S-21', 'Claude Code over MCP: what an AI client may do, and what it may not', step => {
  step('S-21.1 Who am I', async ({ demo }) => {
    await demo.mcpRead('whoami for Priya Castell', async () => {
      const r = await mcpCall('whoami', {});
      demo.check(r.ok && String(r.summary).includes('You hold maker, requester and may use 73 tools.'), 'whoami: "You hold maker, requester and may use 73 tools."', String(r.summary ?? JSON.stringify(r.error)));
    });
    await demo.open('ai-activity', 'priya');
  });

  step('S-21.2 What needs me today', async ({ demo }) => {
    await demo.mcpRead('get_my_queue for Priya Castell', async () => {
      const r = await mcpCall('get_my_queue', {});
      demo.check(r.ok && /\d+ items? needs? you\./.test(String(r.summary)), 'get_my_queue: "N items need you."', String(r.summary ?? JSON.stringify(r.error)));
    });
    await demo.open('work', 'priya');
  });

  step('S-21.3 What breaks if it changes', async ({ demo }) => {
    await demo.mcpRead('get_dependency_impact on Inspection Scheduler', async () => {
      const r = await mcpCall('get_dependency_impact', { businessApplicationId: await appId('Inspection Scheduler') });
      demo.check(r.ok && String(r.summary).includes('9 applications are reached from Inspection Scheduler.'), 'get_dependency_impact: "9 applications are reached from Inspection Scheduler."', String(r.summary ?? JSON.stringify(r.error)));
      const text = JSON.stringify(r.data ?? {});
      for (const name of ['Route Planner', 'Crew Roster', 'Visit Report Agent']) demo.check(text.includes(name), `the walk reaches ${name}`);
    });
    await demo.open(`records/business-application/${await appId('Inspection Scheduler')}/map`, 'priya');
  });

  step('S-21.4 Who answers', async ({ demo }) => {
    await demo.mcpRead('list_teams and get_team for Priya Castell', async () => {
      const r = await mcpCall('list_teams', {});
      const summary = String(r.summary ?? '');
      demo.check(r.ok && summary.includes('19 teams: 9 committees, 4 support teams and 6 role groups.'), 'list_teams: "19 teams: 9 committees, 4 support teams and 6 role groups."', summary || JSON.stringify(r.error));
      demo.check(summary.includes("Members are the demo's fictional people."), `list_teams: "Members are the demo's fictional people."`, summary);
      const team = await mcpCall('get_team', { teamId: CHANGE_BOARD });
      demo.check(team.ok && JSON.stringify(team.data ?? {}).includes('Tomas Rendel') && JSON.stringify(team.data ?? {}).includes('Ines Varga'), "get_team: the Change board's members include Tomas Rendel and Ines Varga", JSON.stringify(team.error ?? team.data?.members ?? null).slice(0, 400));
    });
    await demo.open('teams?kind=committee', 'priya');
  });

  step('S-21.5 It cannot approve', async ({ demo }) => {
    await demo.mcp('Claude Code tries answer_approval for Nadia', async () => {
      const asked = await apiAll<any>('priya', 'list_approvals', { view: 'asked-by-me' }).catch(() => [] as any[]);
      const record = asked.find(a => String(a.decision?.statement ?? '').includes('Retire the old export flow')) ?? asked[0];
      const r = await mcpCall('answer_approval', { approvalRecordId: record?.approvalRecordId ?? '00000000-0000-4000-8000-000000000000', answer: 'approve', reason: 'Approve it for Nadia.' });
      demo.check(!r.ok && r.error?.code === 'ai_cannot_do_this', 'answer_approval is refused: ai_cannot_do_this', JSON.stringify(r.error ?? r.data));
      demo.check(String(r.error?.message ?? '').includes("The tool answer_approval is served only to a person in the product's web app. AI never approves: the person answers on the decision page."), 'the refusal says only a person answers, on the decision page', String(r.error?.message));
      demo.check(/G17/.test(JSON.stringify(r.error ?? {})), 'the refusal carries guideline G17', JSON.stringify(r.error ?? {}));
    });
    await demo.open('ai-activity?view=refused', 'priya');
  });

  step("S-21.6 It cannot decide for someone else's application", async ({ demo }) => {
    await demo.mcp('Claude Code proposes a risk acceptance on Asset Register', async () => {
      const r = await mcpCall('save_decision_proposal', {
        decisionTypeKey: 'risk-acceptance',
        subjectKind: 'business-application',
        subjectId: await appId('Asset Register'),
        statement: 'Accept that warranty dates are typed in by hand until the supplier feed is ready.',
        context: 'The warranty dates of Asset Register 7.2 come from supplier invoices until the supplier feed goes live.',
        proposedOutcomeKey: 'accept',
        reason: 'Only the stores team types the dates, and the feed is planned for March.',
      });
      demo.check(!r.ok && r.error?.code === 'not_authorized', 'save_decision_proposal is refused: not_authorized', JSON.stringify(r.error ?? r.data));
      demo.check(String(r.error?.message ?? '').includes('Only a holder of a responsible, accountable or consulted Decision Right for decision type `risk-acceptance` in this scope can propose it, and the person behind this call holds none. They are held by owner role `business-owner` (accountable).'), 'the refusal names the decision right Priya does not hold', String(r.error?.message));
      demo.check(/G4\b/.test(JSON.stringify(r.error ?? {})), 'the refusal carries guideline G4', JSON.stringify(r.error ?? {}));
    });
    await demo.open('ai-activity?view=refused', 'priya');
  });

  step('S-21.7 It cannot break a never rule', async ({ demo }) => {
    await demo.mcpRead('check_data_use for Expense Assistant and the bank details', async () => {
      const r = await mcpCall('check_data_use', { applicationId: await appId('Expense Assistant'), domainKey: 'bank-details', access: 'read', reachesAi: true });
      const text = `${r.summary ?? ''} ${JSON.stringify(r.data ?? {})}`;
      demo.check(r.ok && text.includes('Refused when saved, for everyone: the AI use rule is never, set on People / Payroll and inherited by People / Payroll / Bank details.'), 'check_data_use: refused when saved, for everyone', text.slice(0, 600));
    });
    await demo.open('data/uses', 'priya');
  });

  step('S-21.8 Every refusal on the record', async ({ demo }) => {
    await demo.open('ai-activity?view=refused', 'priya');
    if (!demo.readOnly && demo.mode === 'dev') {
      await demo.see('Claude Code called save_decision_proposal, and the product refused it');
      await demo.see('Not allowed for this person');
      await demo.see(/G4\b/, { what: 'the G4 chip' });
      await demo.see('AI clients cannot do this');
    }
    await demo.open('admin', 'owen');
    await demo.click({ text: 'Clients that called', prefix: true, scope: 'main' });
    await demo.see(/Claude Code.{0,300}calls?/, { what: "Claude Code's row counts its calls" });
    await demo.see(/Claude Code.{0,400}refused/i, { what: "Claude Code's row counts its refused calls" });
    await demo.see(/Claude Code.{0,500}drafts?/i, { what: "Claude Code's row counts its drafts" });
  });
});
