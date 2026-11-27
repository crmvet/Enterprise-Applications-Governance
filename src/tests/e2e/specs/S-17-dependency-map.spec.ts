// S-17 Architect: the dependency map and what breaks (src/docs/demo/scenarios/S-17-dependency-map.md). Reads only.
import { appId } from '../lib/api';
import { scenario } from '../lib/demo';

// PAYLIB, a fictional data location of the demo estate (seed/demo-estate.json).
const PAYLIB = '00000000-0000-4000-8000-000000006017';

scenario('S-17', 'Architect: the dependency map and what breaks', step => {
  step("S-17.1 Inspection Scheduler's map", async ({ demo }) => {
    await demo.open(`records/business-application/${await appId('Inspection Scheduler')}/map`, 'elif');
    await demo.see('What breaks if Inspection Scheduler changes or retires, and what it needs to run. Up to 3 hops.');
    // A phone shows the walk as a list; the graph's columns are a wide screen's.
    if (!demo.phone) {
      await demo.see(/IT NEEDS, 3 HOPS/i);
      await demo.see(/DEPENDS ON IT, 3 HOPS/i);
    }
    await demo.see('What breaks, in numbers: 9 applications depend on it within 3 hops, and more beyond');
    await demo.see('What it needs, in numbers: 7 applications');
    await demo.see('Customer Prod');
    await demo.see('Field Ops Prod');
    for (const part of ['canvas app', 'cloud flow', 'solution', 'repository']) await demo.see(new RegExp(part, 'i'), { what: `its components include a ${part}` });
  });

  step('S-17.2 Only what breaks, as a list', async ({ demo }) => {
    await demo.open(`records/business-application/${await appId('Inspection Scheduler')}/map`, 'elif');
    await demo.click({ text: 'What breaks', scope: 'main' });
    await demo.click({ text: 'List', scope: 'main' });
    await demo.count('What breaks if Inspection Scheduler changes or retires', 9);
    await demo.see(/1 HOP.{0,600}Route Planner/i, { what: '1 hop: Route Planner' });
    await demo.see(/Visit Report Agent.{0,200}Reads data from Inspection Scheduler.{0,80}High/, { what: 'Visit Report Agent reads data from it, high criticality' });
    await demo.see(/Crew Roster.{0,200}Calls API of.{0,120}Medium/, { what: 'Crew Roster calls its API, medium criticality' });
    await demo.see(/Safety Findings Tracker.{0,200}No business owner/, { what: 'Safety Findings Tracker has no business owner' });
    await demo.see('Customer Notice Sender');
    await demo.see(/2 HOPS.{0,600}Crew Shift Mobile/i, { what: '2 hops: Crew Shift Mobile' });
    await demo.see('Contractor Portal');
    await demo.see('Shift Swap Requests');
    await demo.see(/3 HOPS.{0,300}Customer Self-Service Hub/i, { what: '3 hops: Customer Self-Service Hub' });
    await demo.see('Change');
    await demo.see('End');
  });

  step('S-17.3 What depends on one component', async ({ demo }) => {
    await demo.open(`records/business-application/${await appId('Inspection Scheduler')}?tab=components`, 'elif');
    await demo.click({ text: 'What depends on it?', near: 'Inspection Scheduler solution', scope: 'main' });
    await demo.waitSheet();
    await demo.see('What depends on Inspection Scheduler solution?', { scope: 'dialog' });
    await demo.see(/DIRECTLY.{0,200}Inspection Scheduler.{0,100}Built from Inspection Scheduler solution/i, { scope: 'dialog', what: 'directly: Inspection Scheduler, built from the solution' });
    await demo.see(/2 HOPS AWAY/i, { scope: 'dialog' });
    for (const name of ['Crew Roster', 'Customer Notice Sender', 'Route Planner', 'Safety Findings Tracker', 'Visit Report Agent']) await demo.see(name, { scope: 'dialog', what: `2 hops away: ${name}` });
    await demo.see('Reads data from Inspection Scheduler', { scope: 'dialog' });
    await demo.see(/3 HOPS AWAY/i, { scope: 'dialog' });
    for (const name of ['Contractor Portal', 'Crew Shift Mobile', 'Shift Swap Requests']) await demo.see(name, { scope: 'dialog', what: `3 hops away: ${name}` });
    await demo.closeSheet();
  });

  step('S-17.4 The impact of retiring Finance Ledger', async ({ demo }) => {
    await demo.open(`records/business-application/${await appId('Finance Ledger')}?tab=lifecycle`, 'elif');
    await demo.see('No retirement is decided. The application stays in use. 5 applications depend on it.');
    await demo.click({ text: 'See what breaks', prefix: true, scope: 'main' });
    await demo.see('14 applications depend on it within 3 hops, and more beyond');
    await demo.count('Tier 2', 10);
    await demo.see(/BY CRITICALITY.{0,20}High ?6/i, { what: 'High criticality 6' });
    await demo.see(/Invoice Matching.{0,200}Reads data from/, { what: 'Invoice Matching reads data from it' });
    await demo.see(/Supplier Onboarding.{0,200}Writes data to/, { what: 'Supplier Onboarding writes data to it' });
    await demo.see(/Purchase Approvals.{0,200}Writes data to/, { what: 'Purchase Approvals writes data to it' });
  });

  step('S-17.5 What depends on a data location', async ({ demo }) => {
    await demo.open(`records/data-location/${PAYLIB}`, 'elif');
    await demo.see('PAYLIB');
    await demo.click({ text: 'What depends on it?', scope: 'main' });
    await demo.waitSheet();
    await demo.see('PAYLIB, IBM i.', { scope: 'dialog' });
    await demo.see(demo.aiSeeded ? '2 open data uses name it.' : '1 open data use names it.', { scope: 'dialog' });
    await demo.see('Holds People / Payroll / Bank details (SoR), People / Payroll / Paychecks (SoR).', { scope: 'dialog' });
    await demo.see(/DIRECTLY.{0,300}Finance Ledger/i, { scope: 'dialog', what: 'directly: Finance Ledger' });
    if (demo.aiSeeded) await demo.see('Mileage Claims', { scope: 'dialog', what: 'directly: Mileage Claims' });
    await demo.see('Has a data use at PAYLIB', { scope: 'dialog' });
    await demo.see(/2 HOPS AWAY/i, { scope: 'dialog' });
    for (const name of ['Expense Assistant', 'Invoice Matching', 'Parts Catalog API', 'Purchase Approvals', 'Supplier Onboarding']) await demo.see(name, { scope: 'dialog', what: `2 hops away: ${name}` });
    await demo.see(/3 HOPS AWAY/i, { scope: 'dialog' });
    for (const name of ['Inspection Scheduler', 'Quote Builder', 'Supplier Price Agent', 'Warehouse Stock Count']) await demo.see(name, { scope: 'dialog', what: `3 hops away: ${name}` });
    await demo.closeSheet();
  });
});
