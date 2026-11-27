// S-16 Teams and committees: who answers, who supports, what waits
// (src/docs/demo/scenarios/S-16-teams-and-committees.md).
import { apiAll } from '../lib/api';
import { scenario, type Demo } from '../lib/demo';
import { decisionStatus, STATEMENT } from '../lib/story';

// The Change board's group, a fictional ID of the demo binding (seed/demo-binding.patch.json).
const CHANGE_BOARD = '00000000-0000-4000-8000-000000000111';

async function openTeam(demo: Demo, name: string, tab: string | null = null) {
  await demo.open(tab ? `teams?kind=${tab}` : 'teams');
  await demo.click({ text: name, role: 'link', scope: 'main' });
  await demo.at(/\/teams\/[0-9a-f-]{36}/, `${name} opens its team page`);
}

scenario('S-16', 'Teams and committees: who answers, who supports, what waits', step => {
  step('S-16.1 Every team the product knows', async ({ demo }) => {
    await demo.open('teams', 'owen');
    await demo.count('All', 19);
    await demo.count('Committees', 9);
    await demo.count('Support teams', 4);
    await demo.count('Role groups', 6);
    await demo.see("The members are the demo's fictional people in each group: the personas and the estate's other people. A real install reads them live from Entra and never stores them.");
    await demo.see('Change board');
    await demo.see('Architects');
    await demo.see('Security reviewers');
    await demo.see(/Service Desk.{0,400}6 applications,? 7 owner roles held/, { what: 'Service Desk holds 7 owner roles on 6 applications' });
    await demo.see(/Service Desk.{0,600}1 gap/, { what: 'Service Desk has 1 gap' });
    await demo.api('the committees work waits on', async () => {
      const committees = await apiAll<any>('owen', 'list_teams', { kind: 'committee' });
      const waiting = committees.filter(t => (t.waitingCount ?? 0) > 0).map(t => t.team?.displayLabel);
      // After S-10 the Configuration admins have nothing waiting (Jonah accepted their change); played alone, 8.
      const expected = waiting.includes('Configuration admins') ? 8 : 7;
      demo.check(committees.length === 9 && waiting.length === expected && !waiting.includes('Data governance leads'), `work waits on ${expected} of the 9 committees, not the Data governance leads`, JSON.stringify(waiting));
    });
  });

  step('S-16.2 A committee and what waits on it', async ({ demo }) => {
    await demo.as('owen');
    await openTeam(demo, 'Change board', 'committee');
    await demo.see('Committee');
    await demo.see('A committee that holds 1 product role and answers 1 decision type.');
    for (const member of ['Felix Arendt', 'Ines Varga', 'Marcus Oyelaran', 'Tomas Rendel']) await demo.see(new RegExp(`Members.{0,400}${member}`), { what: `${member} is a member` });
    await demo.see(/Exception.{0,200}Through the Change board role/, { what: 'it answers exceptions through the Change board role' });
    await demo.see('Approves');
    await demo.see('Accountable');
    if ((await decisionStatus('owen', STATEMENT.deskBooking)) === 'proposed') {
      await demo.see(new RegExp(`Waiting now.{0,2000}${STATEMENT.deskBooking.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}`), { what: 'the Desk Booking exception waits on the Change board' });
      await demo.see('Answer on the decision page');
    }
    await demo.see('What its members own');
  });

  step('S-16.3 A support team and its gaps', async ({ demo }) => {
    await demo.open('teams', 'owen');
    await demo.click({ text: 'Support teams', prefix: true, role: 'tab', scope: 'main' });
    await demo.click({ text: 'Service Desk', role: 'link', scope: 'main' });
    await demo.at(/\/teams\/[0-9a-f-]{36}/, 'Service Desk opens its team page');
    await demo.see('Support team');
    await demo.see('A support team that holds owner roles on 6 applications.');
    await demo.see(/Members.{0,300}Clara Voss/, { what: 'Clara Voss is a member' });
    await demo.see(/Members.{0,200}Hugo Brandvold/, { what: 'Hugo Brandvold is a member' });
    await demo.see(/Members.{0,300}Theo Marsh/, { what: 'Theo Marsh is a member' });
    await demo.count('Applications and owner roles', 6);
    await demo.see(/Inspection Scheduler.{0,200}Support owner \(deputy\)/, { what: 'Inspection Scheduler: support owner (deputy)' });
    await demo.see('1 gap');
    await demo.see(/Owner gaps.{0,400}Inspection Scheduler.{0,200}Business owner: no deputy/, { what: 'the gap: Inspection Scheduler has no deputy business owner' });
    await demo.see('Propose on Owners');
  });

  step('S-16.4 The committee member answers', async ({ demo }) => {
    demo.writes('answer_approval@components/actions#AnswerSheet');
    // Marcus Oyelaran, a change board member who did not propose it, answers (wave 4b product review).
    await demo.as('marcus');
    if ((await decisionStatus('marcus', STATEMENT.deskBooking)) !== 'proposed') {
      if (!demo.readOnly) throw new Error('the Desk Booking exception is not waiting on the Change board');
      await demo.open(`teams/${CHANGE_BOARD}`);
      return;
    }
    await openTeam(demo, 'Change board');
    await demo.click({ text: 'Answer on the decision page', scope: 'main' });
    await demo.at(/\/records\/decision\//, 'the answer link opens the decision page');
    await demo.see(/You are asked as Change board\. Expires in/, { what: 'Marcus is asked as the Change board' });
    await demo.notSee('You proposed this decision', { what: 'Marcus did not propose it, so the page does not say he did' });
    await demo.act('Marcus, a member of the Change board, approves the exception Tomas proposed', async () => {
      await demo.answer('The connector only reads calendars, and the vendor can put the old version back in minutes.', {
        answerShows: ['The engine accepted the decision.'],
      });
      await demo.decisionIs('accepted', 'the engine accepts the exception');
    });
  });

  step('S-16.5 The answer shows on the team', async ({ demo }) => {
    await demo.open(`teams/${CHANGE_BOARD}`, 'tomas');
    await demo.see('Change board');
    if ((await decisionStatus('tomas', STATEMENT.deskBooking)) === 'accepted') {
      await demo.see(/Nothing waits on (the team as a whole|this team or its members) now\./, { what: 'nothing waits on the Change board itself' });
      await demo.count('Recent answers', 1);
      await demo.see(/Desk Booking.{0,300}Approved.{0,200}Marcus Oyelaran|Desk Booking.{0,300}Marcus Oyelaran.{0,200}Approved/, { what: 'the Desk Booking exception, approved by Marcus Oyelaran' });
    }
  });

  step('S-16.6 The owners of the data', async ({ demo }) => {
    await demo.as('owen');
    await openTeam(demo, 'Data owners', 'role-group');
    await demo.see('As the named data owner of their own data domains, not through the team');
    // Its role card says the same (fix round 2: it said "Answers no decision" beside "Decisions it answers 4").
    await demo.see('Answers as named owners');
    await demo.notSee('Answers no decision');
    // Kai answered his data-use card in S-07, so the requests asked of the members by name are Greta Lund's by now.
    await demo.see(/asked of its members by name/i, { what: 'the requests asked of its members by name are listed' });
    await demo.see(/Asked of (Greta Lund|Kai Ostrowski|Wren Abernathy)/, { what: 'a request asked of a data owner by name' });
    await demo.see('What its members own');
    await demo.see('Field operations / Safety findings');
    await demo.open('people/00000000-0000-4000-8000-000000001119');
    await demo.see('Data domains');
    await demo.see(/Data it owns.{0,400}Field operations \/ Safety findings/, { what: "Kai's page lists the domain he owns" });
  });
});
