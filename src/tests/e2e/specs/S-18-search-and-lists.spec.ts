// S-18 Reader: search, filters and what a reader may do (src/docs/demo/scenarios/S-18-search-and-lists.md). Reads only.
import { apiAll, appId } from '../lib/api';
import { scenario, type Demo } from '../lib/demo';
import { STATEMENT } from '../lib/story';

const SEARCH_LABEL = 'Search applications, people and record numbers';

// The top bar's search box on a wide screen; on a phone the top bar's search link opens the search page, whose box
// takes the text.
async function search(demo: Demo, text: string) {
  if (demo.phone) {
    await demo.click({ text: SEARCH_LABEL, role: 'link' });
    await demo.at(/\/search/, 'the search link opens the search page');
  }
  await demo.fill(SEARCH_LABEL, text);
  await demo.key('Enter');
  await demo.settle();
}

scenario('S-18', 'Reader: search, filters and what a reader may do', step => {
  step('S-18.1 Search by name', async ({ demo }) => {
    await demo.open('overview', 'theo');
    await search(demo, 'invoice');
    await demo.see('Results for "invoice"');
    await demo.count('Applications', 1);
    await demo.see('Invoice Matching');
    await demo.see('AI-built app. Tier 2. Operate');
    await demo.see('Open in the register');
    await demo.count('Intake requests', 0);
    await demo.count('People', 0);
  });

  step('S-18.2 Search for a person', async ({ demo }) => {
    await demo.open('overview', 'theo');
    await search(demo, 'Wren');
    await demo.count('People', 1);
    await demo.click({ text: 'Wren Abernathy', role: 'link', scope: 'main' });
    await demo.count('Owner roles now', 7);
    await demo.see('On 7 applications');
    await demo.count('Deputy roles', 4);
    await demo.see('Owns now');
    await demo.see(/Claims Triage Agent.{0,300}Business owner/, { what: 'she owns Claims Triage Agent as business owner' });
    await demo.see(/Claims Triage Agent.{0,400}Data owner/, { what: 'and as data owner' });
    await demo.see(/Claims Triage Agent.{0,500}Nadia Ferreira/, { what: 'with Nadia Ferreira as deputy' });
    await demo.see('Member of');
  });

  step('S-18.3 Search by record number', async ({ demo }) => {
    await demo.as('theo');
    const hotfix = (await apiAll<any>('theo', 'get_decision_log', {})).find(d => d.statement === STATEMENT.hotfix);
    const number = hotfix?.recordNumber ?? 'DEC-0221';
    await demo.open('overview');
    await search(demo, number);
    if (!/\/records\/decision\//.test(demo.url())) await demo.click({ text: number, prefix: true, role: 'link', scope: 'main' });
    await demo.at(/\/records\/decision\//, `${number} opens its decision page`);
    await demo.see(STATEMENT.hotfix);
    await demo.see('Exception');
    await demo.see('Proposed');
    await demo.see('This decision does not ask you. The approvals below show who is asked.');
  });

  step('S-18.4 Filter the register', async ({ demo }) => {
    await demo.open('applications?ai=yes', 'theo');
    await demo.see('Uses AI');
    await demo.see('Clear all');
    await demo.see('14 applications match these filters.');
    for (const name of ['Claims Triage Agent', 'Expense Assistant', 'Field Knowledge MCP Server', 'Invoice Matching', 'Supplier Price Agent', 'Visit Report Agent']) await demo.see(name, { what: `${name} uses AI` });
    // The Map link sits in the table's own column, on a wide screen.
    if (!demo.phone) await demo.see('Map');
    for (const filter of ['Stage', 'Tier', 'Kind', 'Portfolio', 'Owner', 'Build path']) await demo.see(filter, { what: `the filter "${filter}"` });
  });

  step('S-18.5 A reader changes nothing', async ({ demo }) => {
    await demo.open(`records/business-application/${await appId('Inspection Scheduler')}`, 'theo');
    for (const act of ['Hand over', 'Propose', 'Confirm it is still true']) {
      demo.check(!(await demo.has({ text: act, prefix: true, scope: 'main' })), `the reader gets no "${act}" button`);
    }
    await demo.open(`records/business-application/${await appId('Inspection Scheduler')}?tab=components`);
    await demo.see('What depends on it?');
    await demo.open('work');
    await demo.count('Approvals', 0);
    await demo.see('approval records are hidden by your access');
  });

  step('S-18.6 Search inside a list', async ({ demo }) => {
    await demo.open('teams', 'theo');
    await demo.fill('Search the teams by team or role name', 'change');
    await demo.at(/[?&]q=change/, 'the search goes into the address as q=change');
    await demo.count('All', 2);
    await demo.count('Committees', 2);
    await demo.count('Support teams', 0);
    await demo.count('Role groups', 0);
    await demo.see('Change board');
    await demo.see('Change managers');
    await demo.open('owners');
    await demo.see(/Search|Application, owner role or portfolio/, { what: 'Owners has its own search' });
    // Approvals and Retirement search the same way, the words in the address (product review, fix round 1).
    await demo.open('approvals?view=open');
    await demo.fill('Search the requests shown', 'exception');
    await demo.at(/[?&]q=exception/, 'the Approvals search goes into the address as q=exception');
    await demo.open('retirement');
    await demo.fill('Search the applications on their way out', 'Legacy');
    await demo.at(/[?&]q=Legacy/, 'the Retirement search goes into the address as q=Legacy');
    await demo.see('Legacy Timesheet App');
  });
});
