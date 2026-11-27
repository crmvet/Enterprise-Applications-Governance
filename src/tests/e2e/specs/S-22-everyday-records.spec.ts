// S-22 Everyday records: the work between the decisions (src/docs/demo/scenarios/S-22-everyday-records.md).
// It plays last: every step writes a record that earlier scenarios would count, so it comes after S-21 on the same
// store and changes no count an earlier step proves (product review, fix round 2). In read-only mode each step opens its page
// and checks the control a person uses; the write is skipped.
import { apiAll, appId, day, decisionByStatement, intakeByTitle, portfolioId } from '../lib/api';
import { scenario, type Demo } from '../lib/demo';

// The names this scenario writes. All fictional.
const REGISTERED = 'Depot key register';
const LOCATION = 'Depot safety binders';
const TARGET = 'Field Ops Training';
const PORTFOLIO = 'Depot services';
const SPARE_PARTS = 'Spare parts returns from field vans';
const ROTATE = 'Rotate the reminder flow connection';
const ROTATE_ID = '00000000-0000-4000-8000-000000004205';
const RISK = 'Accept that contractor accounts stay active for 30 days after a contract ends.';
const FUEL_LOG = 'Depot fuel log';
const NOTICES_FLOW = 'Send planned work notices';
const PURCHASE_FLOW = 'Route purchase approvals';
const CANTEEN = 'Canteen orders';
const TEST_FLOW = 'Test reminder flow (personal)';
const CHECKLISTS = 'Depot safety checklists';
const EXPORT_CHANGE = 'Retire the old export flow';
const CREW_CHANGE = 'Crew Roster 5.4: overtime column';
const REMINDER_CHANGE = 'Inspection Scheduler 3.5: reminder wording';
const PEOPLE_TEST = 'People Test';

// The deployment Hugo recorded by hand in S-22.5 (Inspection Scheduler 3.4.3 to Field Ops Prod), or its correction
// when corrected is true: the record of that version that corrects nothing, or the one that corrects it.
async function handDeployment(corrected = false) {
  const items = await apiAll<any>('hugo', 'get_deployments', { businessApplicationId: await appId('Inspection Scheduler') });
  const of = items.filter(d => d.artifactVersion === '3.4.3' && d.targetLabel === 'Field Ops Prod');
  return (corrected ? of.find(d => d.correctsRecordId) : of.find(d => !d.correctsRecordId)) ?? null;
}

// A change by its title, in the weeks around today.
async function changeByTitle(title: string) {
  return (await apiAll<any>('hugo', 'get_change_schedule', { from: day(-60), to: day(60) })).find(c => c.title === title) ?? null;
}

// A deployment target by its name.
async function targetByName(name: string) {
  return (await apiAll<any>('owen', 'list_deployment_targets', {})).find(t => t.labelAsSeen === name || t.label === name) ?? null;
}

// A data location by its name.
async function locationByName(name: string) {
  return (await apiAll<any>('hana', 'find_data_locations', { text: name })).find(l => l.label === name) ?? null;
}

// A moment as a datetime-local field takes it, in the browser's time zone (the suite runs on the same machine).
function localTime(iso: string) {
  const d = new Date(iso);
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
}

const escaped = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

// Picks an option in the select a field label names, in the open side sheet. A sheet can hold several selects that
// read "Choose", so the field is found by its label, as a person reads it.
async function pickIn(demo: Demo, label: string, option: string) {
  const sheet = demo.page.getByRole('dialog').last();
  await sheet.getByRole('combobox', { name: new RegExp(`^${escaped(label)}( Required)?$`) }).first().click();
  await demo.page.getByRole('option', { name: new RegExp(`^${escaped(option)}`) }).first().click();
  await demo.settle();
}

// Answers each tier question of the open sheet that has no answer yet with its first option (as in S-23).
async function answerEveryQuestion(demo: Demo) {
  await demo.page.evaluate(() => {
    const dialogs = Array.from(document.querySelectorAll('[role="dialog"]')).filter(d => d.getBoundingClientRect().width > 0);
    const sheet = dialogs[dialogs.length - 1];
    if (!sheet) return;
    for (const group of Array.from(sheet.querySelectorAll('[role="radiogroup"], fieldset'))) {
      if (group.querySelector('[role="radio"][aria-checked="true"], input[type="radio"]:checked')) continue;
      const first = group.querySelector('[role="radio"], input[type="radio"]') as HTMLElement | null;
      first?.click();
    }
  });
  await demo.settle();
}

scenario('S-22', 'Everyday records: the work between the decisions', step => {
  step('S-22.1 Register an app already built', async ({ demo }) => {
    demo.writes('save_application_draft@pages/register-extras/RegisterSheet#RegisterBuiltAppSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
    await demo.open('applications', 'priya');
    await demo.see('Register an app already built');
    await demo.act(`Priya registers ${REGISTERED}, an app built before the product came, and sends its ownership for confirmation`, async () => {
      await demo.proposeAndSubmit({
        open: { text: 'Register an app already built', scope: 'main' },
        fill: async () => {
          await demo.fill('Name', REGISTERED, 'dialog');
          await demo.fill('What it is for', 'Lists which depot keys each crew member holds, and when each key comes back.', 'dialog');
          await pickIn(demo, 'Kind', 'Power Platform app');
        },
        preview: 'Check first',
        submit: 'Save the draft',
        what: `Register ${REGISTERED}`,
        sendShows: ['Submitted. 2 approvals are asked. Only people answer, on the decision page.'],
      });
    });
  });

  step('S-22.2 Add a dependency', async ({ demo }) => {
    demo.writes('save_dependency@pages/app360/sheets#NewDependencySheet');
    await demo.open(`records/business-application/${await appId('Visit Report Agent')}/map`, 'elif');
    await demo.see('Add a dependency');
    await demo.act('Elif records that Visit Report Agent calls the API of Field Knowledge MCP Server', async () => {
      await demo.sheet({
        open: { text: 'Add a dependency', scope: 'main' },
        fill: async () => {
          await demo.see('Visit Report Agent needs the other application', { scope: 'dialog' });
          await demo.fill('The other application', 'Field Knowledge', 'dialog');
          await demo.click({ text: 'Field Knowledge MCP Server', prefix: true, scope: 'dialog' });
          await pickIn(demo, 'Kind of dependency', 'Calls API of');
          await pickIn(demo, 'Criticality', 'Medium');
        },
        preview: 'Check first',
        submit: 'Record it',
        what: 'Record the dependency',
        answerShows: ['The dependency is recorded, as reported: Visit Report Agent calls API of Field Knowledge MCP Server.'],
      });
      await demo.open(`records/business-application/${await appId('Visit Report Agent')}/map`);
      await demo.see('Field Knowledge MCP Server');
    });
  });

  step('S-22.3 Request an architecture review', async ({ demo }) => {
    demo.writes('request_architecture_review@pages/app360/sheets#ArchitectureReviewSheet');
    await demo.open(`records/business-application/${await appId('Contractor Portal')}?tab=work`, 'nadia');
    await demo.see('Request an architecture review');
    await demo.act('Nadia asks the architect to review the sign-in page design of Contractor Portal', async () => {
      await demo.sheet({
        open: { text: 'Request an architecture review', scope: 'main' },
        fill: async () => {
          await demo.fill('What to review', 'The new sign-in page sends contractors to their own company sign-in. Please check the design before the change goes ahead.', 'dialog');
          await demo.setValue('Due on', day(14), 'dialog');
        },
        preview: 'Check first',
        submit: 'Request the review',
        what: 'Request the architecture review',
        answerShows: ['The architecture review is requested. It is a request, never a decision. A review was opened'],
      });
    });
  });

  step('S-22.4 Record a leave', async ({ demo }) => {
    demo.writes('record_owner_leave@pages/owners-reviews/OwnerSheets#RecordLeaveSheet');
    await demo.open('owners', 'owen');
    await demo.see('Record a leave');
    await demo.act('Owen records that Ines Varga leaves in 30 days', async () => {
      await demo.sheet({
        open: { text: 'Record a leave', scope: 'main' },
        fill: async () => {
          await demo.click({ text: 'Choose the person', role: 'combobox', prefix: true, scope: 'dialog' });
          await demo.click({ text: 'Ines Varga', role: 'option', prefix: true });
          await demo.setValue('Leave date', day(30), 'dialog');
          await demo.fill('Note', 'Ines moves to the head office. Her teams know.', 'dialog');
        },
        preview: 'Check first',
        submit: 'Record the leave',
        confirm: 'Record the leave',
        what: 'Record the leave',
        answerShows: [
          '7 owner roles are affected, and 1 owner role on data (Field operations); a successor needs an accepted ownership decision.',
          '12 owner check reviews cover this leave (9 new, 3 already open).',
        ],
      });
    });
  });

  step('S-22.5 Record a deployment by hand', async ({ demo }) => {
    demo.writes('record_deployment@pages/deployments-places/sheets#RecordDeploymentSheet');
    await demo.open(`deployments/new?app=${await appId('Inspection Scheduler')}&change=${ROTATE_ID}`, 'hugo');
    await demo.see('Record a deployment');
    await demo.act(`Hugo records the deployment of the standard change "${ROTATE}"`, async () => {
      await demo.sheet({
        open: { text: 'Record the deployment', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'Target', 'Field Ops Prod');
          await demo.fill('Artifact name', 'InspectionScheduler', 'dialog');
          await demo.fill('Version', '3.4.3', 'dialog');
          // Inside the change's window (07:00 to 08:00 UTC on 25 Nov 2026), in the browser's own time zone.
          await demo.setValue('Finished', localTime('2026-11-25T07:40:00Z'), 'dialog');
        },
        preview: 'Check first',
        submit: 'Record it',
        what: 'Record the deployment',
        answerShows: ['The deployment is recorded as reported. The engine set the route and the flags. The engine flagged it: came another way.'],
      });
    });
  });

  step('S-22.6 Record the result and close the change', async ({ demo }) => {
    demo.writes('complete_change@pages/changes-records/ChangeActions#CompleteChangeSheet', 'record_change_result@pages/changes-records/ChangeActions#RecordResultSheet');
    await demo.open(`records/change-request/${ROTATE_ID}`, 'hugo');
    await demo.see(ROTATE);
    await demo.see('Record the result');
    await demo.act(`Hugo records how "${ROTATE}" ended and closes it`, async () => {
      await demo.sheet({
        open: { text: 'Record the result', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'Result', 'Succeeded');
          await demo.fill('Note', 'The new connection works; the reminders went out at 08:00.', 'dialog');
        },
        preview: 'Check first',
        submit: 'Record the result',
        what: 'Record the result',
        answerShows: ['The change result is recorded as reported.'],
      });
      await demo.sheet({
        open: { text: 'Close the change', scope: 'main' },
        preview: 'Check first',
        submit: 'Close the change',
        what: 'Close the change',
        answerShows: [`The change ${ROTATE} is closed with the result succeeded.`],
      });
    });
  });

  step('S-22.7 Link a document', async ({ demo }) => {
    demo.writes('record_document_link@pages/guide-log-docs/DocumentSheets#LinkDocumentSheet');
    // Supplier Onboarding's runbook is missing on a fresh store, and its requirement applies (tier 2, Change and release),
    // so a second link for it is refused. Dmitri Halloran is its deputy technical owner (wave 4b verify round).
    await demo.open('documents', 'dmitri');
    await demo.see('Link a document');
    const link = async (what: string, expect: 'done' | 'refused', answerShows: (string | RegExp)[]) => demo.sheet({
      open: { text: 'Link a document', scope: 'main' },
      fill: async () => {
        await demo.fill('Application', 'Supplier Onb', 'dialog');
        await demo.click({ text: 'Supplier Onboarding', prefix: true, scope: 'dialog' });
        await pickIn(demo, 'Document type', 'Runbook');
        await demo.fill('Link', 'https://wiki.example.org/finance/supplier-onboarding/runbook', 'dialog');
        await demo.setValue('Last updated', day(-3), 'dialog');
      },
      preview: expect === 'done' ? 'Check first' : undefined,
      submit: 'Link it',
      what,
      expect,
      answerShows,
    });
    await demo.act('Dmitri links the runbook of Supplier Onboarding and confirms it current, then tries to link it a second time', async () => {
      await link('Link the document', 'done', ['The document link is recorded with status current, as reported. The product never writes the document.']);
      await link('Link the same runbook again', 'refused', ['Supplier Onboarding already has its runbook linked. Update that link instead: one link meets the requirement, and its history stays on one record.', 'Open that record']);
    });
  });

  step('S-22.8 Compare a list from your own tool', async ({ demo }) => {
    demo.writes('compare_inventory_list@pages/deployments-places/sheets#CompareListSheet');
    await demo.open('gap-check', 'owen');
    await demo.see('Compare a list');
    await demo.act("Owen compares two items from the CoE kit's export with the register", async () => {
      await demo.sheet({
        open: { text: 'Compare a list', scope: 'main' },
        fill: async () => {
          await demo.fill('Where the list came from', 'CoE kit export, this morning', 'dialog');
          await demo.fill('The items', 'flow-depot-keys | Depot keys reminder | Cloud flow | Field Ops Prod\napp-canteen-orders | Canteen orders | Canvas app | Field Ops Prod', 'dialog');
          await demo.see('2 items read', { scope: 'dialog' });
        },
        preview: 'Check first',
        submit: 'Compare it',
        what: 'Compare the list',
        answerShows: ['2 items seen, 2 new claims, 0 vanished. The list came from your own tool, so the run is marked as reported.'],
      });
    });
  });

  step('S-22.9 Register a target and map a pipeline', async ({ demo }) => {
    demo.writes('map_pipeline@pages/deployments-places/sheets#MapPipelineSheet', 'save_deployment_target@pages/deployments-places/sheets#TargetSheet');
    await demo.open('places', 'owen');
    await demo.see('Register a target');
    await demo.see('Map a pipeline');
    await demo.act(`Owen registers ${TARGET} and maps a pipeline to it`, async () => {
      await demo.sheet({
        open: { text: 'Register a target', scope: 'main' },
        fill: async () => {
          await demo.fill('Name', TARGET, 'dialog');
          await pickIn(demo, 'Kind', 'Power Platform environment');
          await pickIn(demo, 'Stage', 'Test');
          await pickIn(demo, 'Change path', 'Pipeline only');
          await pickIn(demo, 'Link kind', 'Power Platform resource');
          await demo.fill('ID in its tool', 'field-ops-training', 'dialog');
        },
        preview: 'Check first',
        submit: 'Register it',
        what: 'Register the target',
        answerShows: [`The deployment target ${TARGET} is registered. Deployments to it can now be recorded.`],
      });
      await demo.sheet({
        open: { text: 'Map a pipeline', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'Tool', 'Azure Pipelines pipeline');
          await demo.fill('ID in its tool', 'inspection-scheduler-training', 'dialog');
          await demo.fill('Link', 'https://dev.example.org/field-ops/_build?definitionId=118', 'dialog');
          await demo.fill('Name', 'Inspection Scheduler to training', 'dialog');
          await demo.fill('Application', 'Inspection', 'dialog');
          await demo.click({ text: 'Inspection Scheduler', prefix: true, scope: 'dialog' });
          await pickIn(demo, 'Step 1: target', TARGET);
        },
        preview: 'Check first',
        submit: 'Save the map',
        what: 'Map the pipeline',
        answerShows: ['The pipeline is mapped to 1 application and target pair. The product never runs the pipeline.'],
      });
    });
  });

  step('S-22.10 Add a data location and map data to it', async ({ demo }) => {
    demo.writes('save_data_location@pages/data/sheets#DataLocationSheet', 'save_data_mapping@pages/data/sheets#DataMappingSheet');
    await demo.open('data/locations', 'hana');
    await demo.see('Add a location');
    await demo.act(`Hana adds ${LOCATION} and maps the safety findings there`, async () => {
      await demo.sheet({
        open: { text: 'Add a location', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'Platform', 'SharePoint');
          await pickIn(demo, 'Stage', 'Production');
          await demo.fill('Name', LOCATION, 'dialog');
          await demo.fill('Link', 'https://fieldops.example.org/sites/depot-safety', 'dialog');
        },
        submit: 'Add the location',
        what: 'Add the location',
        answerShows: ['1 location is registered.'],
      });
      const found = (await apiAll<any>('hana', 'find_data_locations', { text: LOCATION })).find(l => l.label === LOCATION);
      if (!found) throw new Error(`no location ${LOCATION} after the save`);
      await demo.open(`records/data-location/${String(found.dataLocationId).toLowerCase()}`);
      await demo.sheet({
        open: { text: 'Map data here', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'The data', 'Field operations / Safety findings');
          await demo.choose('System of record', 'dialog');
        },
        preview: 'Check first',
        submit: 'Map the data',
        what: 'Map the data',
        answerShows: ['1 mapping is saved.'],
      });
    });
  });

  step('S-22.11 Withdraw a request', async ({ demo }) => {
    demo.writes('withdraw_record@pages/intake/sheets#WithdrawSheet');
    const request = await intakeByTitle('lena', SPARE_PARTS, 'mine');
    if (!request) throw new Error(`no "${SPARE_PARTS}" request for Lena Okoro`);
    await demo.open(`records/intake-request/${String(request.intakeRequestId).toLowerCase()}`, 'lena');
    await demo.see(SPARE_PARTS);
    await demo.see('Withdraw');
    await demo.act(`Lena withdraws "${SPARE_PARTS}"`, async () => {
      await demo.sheet({
        open: { text: 'Withdraw', scope: 'main' },
        fill: async () => {
          await demo.fill('Reason', 'The vans now bring spare parts back through the depot store, so the need went away.', 'dialog');
        },
        submit: 'Withdraw',
        confirm: 'Withdraw',
        what: 'Withdraw the request',
        answerShows: ['Withdrawn. 5 waiting approvals are canceled.'],
      });
    });
  });

  step('S-22.12 A new portfolio', async ({ demo }) => {
    demo.writes('save_portfolio@pages/pmo-portfolio/sheets#PortfolioSheet');
    await demo.open(`portfolios/${await portfolioId('Field operations')}`, 'owen');
    await demo.see('New portfolio inside');
    await demo.act(`Owen creates ${PORTFOLIO} inside Field operations`, async () => {
      await demo.sheet({
        open: { text: 'New portfolio inside', scope: 'main' },
        fill: async () => {
          await demo.fill('Name', PORTFOLIO, 'dialog');
          await pickIn(demo, 'Level', 'Application group');
          await demo.fill('Description', 'The depot apps: keys, stores and the canteen.', 'dialog');
        },
        preview: 'Check first',
        submit: 'Create',
        what: 'Create the portfolio',
        closes: true,
      });
      // The page moves on to the new portfolio.
      await demo.at(/\/portfolios\/[0-9a-f-]{36}$/, 'the new portfolio opens');
      await demo.see(PORTFOLIO);
      await demo.api(`${PORTFOLIO} sits inside Field operations`, async () => {
        const all = await apiAll<any>('owen', 'list_portfolios', {});
        const made = all.find(p => p.name === PORTFOLIO);
        demo.check(!!made && String(made.parentPortfolioId ?? '').toLowerCase() === await portfolioId('Field operations'), `${PORTFOLIO} is inside Field operations`, JSON.stringify(made ?? null));
      });
    });
  });

  step('S-22.13 A separation rule refuses a self-approval at tier 3', async ({ demo }) => {
    demo.writes('save_decision_proposal@pages/app360/sheets#ProposeDecisionSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
    await demo.open(`records/business-application/${await appId('Contractor Portal')}?tab=decisions`, 'nadia');
    await demo.see('Propose a decision');
    await demo.act('Nadia proposes a risk acceptance on Contractor Portal, a tier 3 application, and sends it', async () => {
      await demo.proposeAndSubmit({
        open: { text: 'Propose a decision', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'Kind of decision', 'Risk acceptance');
          await pickIn(demo, 'Proposed outcome', 'Accept');
          await demo.fill('Decision', RISK, 'dialog');
          await demo.fill('Context', 'Contractors often come back within a month, and a new account takes two days.', 'dialog');
          await demo.fill('Reason', 'The accounts are disabled for sign-in at once; only the account stays. The risk is small and known.', 'dialog');
        },
        submit: 'Propose',
        what: 'Propose the risk acceptance',
        sendShows: ['Submitted. 1 approval is asked.'],
      });
      await demo.open('approvals?view=mine');
      // Waiting for me counts only the requests she may answer now (wave 4b verify round): her own sits apart, with the rule.
      await demo.see('Asked of you, but another person answers', { what: 'the card sits under "Asked of you, but another person answers"' });
      await demo.see(/Asked of you, but another person answers[\s\S]{0,600}Accept that contractor accounts stay active for 30 days after a contract ends\./, { what: 'her risk acceptance is listed apart' });
      await demo.see('The separation rule No self-approval at tier 3 keeps you from answering.');
      await demo.see(/1 more asks? you, but a separation rule keeps you from answering\./, { what: 'the header says one more asks her, but a separation rule keeps her from answering' });
      // Nobody else of the demo is a business owner of Contractor Portal, so the decision stays proposed.
      const risk = (await apiAll<any>('nadia', 'get_decision_log', {})).find(d => d.statement === RISK);
      demo.check(risk?.status?.meaning === 'proposed', 'the risk acceptance stays proposed', JSON.stringify(risk?.status ?? null));
    });
  });

  step("S-22.14 Move a domain's rows", async ({ demo }) => {
    await demo.open('records/data-domain/client-cases', 'hana');
    await demo.see('Move its rows');
    if (!demo.readOnly) {
      await demo.count('Where its data sits', 2);
      await demo.count('Applications using it', 2);
    }
    await demo.act('Hana moves the mappings and the uses of Customers / Client cases and feedback to Customers / Client accounts and contacts', async () => {
      demo.writes('move_data_references@pages/data/sheets#MoveDataReferencesSheet');
      await demo.sheet({
        open: { text: 'Move its rows', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'Move to', 'Customers / Client accounts and contacts');
          await demo.fill('Reason', "The service team now logs each case on the client's account, so Client cases and feedback is to be retired.", 'dialog');
        },
        preview: 'Check first: what would change',
        previewShows: [/2 mappings and 2 (data )?uses/, 'Both domains have the same data owner, so each row would keep its identity and its decisions.'],
        submit: 'Move',
        confirm: 'Move',
        what: 'Move the rows',
        answerShows: [/2 mappings and 2 (data )?uses/],
      });
      await demo.open('records/data-domain/client-cases');
      await demo.count('Where its data sits', 0);
      await demo.count('Applications using it', 0);
    });
  });

  step('S-22.15 Run the gap check', async ({ demo }) => {
    await demo.open('gap-check', 'owen');
    await demo.see('Run the gap check');
    await demo.act("Owen runs the gap check over the demo's fictional inventory and continues the run to its last page", async () => {
      demo.writes('run_gap_check@pages/deployments-places/sheets#RunGapCheckSheet');
      await demo.sheet({
        open: { text: 'Run the gap check', scope: 'main' },
        fill: async () => {
          await demo.see("On this demo install it reads the demo's fictional inventory, not Microsoft's", { scope: 'dialog' });
        },
        preview: 'Check first',
        previewShows: [/25 items seen/],
        submit: 'Run it',
        what: 'Run the gap check',
        answerShows: [/25 items seen, 0 new claims, 0 vanished/],
      });
      await demo.see('The run has more pages.');
      await demo.sheet({
        open: { text: 'Continue the run', scope: 'main' },
        submit: 'Continue',
        what: 'Continue the run',
        answerShows: ['9 items seen, 1 new claim, 2 vanished. 2 registered items have vanished from the estate.'],
      });
      await demo.notSee('The run has more pages.');
      await demo.open('gap-check?view=open');
      await demo.see(FUEL_LOG, { what: `Not registered lists ${FUEL_LOG}` });
      await demo.open('gap-check?view=vanished');
      await demo.see(NOTICES_FLOW, { what: `Vanished lists ${NOTICES_FLOW}` });
      await demo.see(PURCHASE_FLOW, { what: `Vanished lists ${PURCHASE_FLOW}` });
    });
  });

  step('S-22.16 Resolve the vanished items', async ({ demo }) => {
    await demo.open('gap-check?view=vanished', 'owen');
    await demo.act(`Owen links the new place of ${NOTICES_FLOW} and says ${PURCHASE_FLOW} is still there`, async () => {
      demo.writes('dismiss_claim@pages/Records#DismissSheet', 'link_component@pages/deployments-places/sheets#ResolveVanishedSheet');
      await demo.sheet({
        open: { text: 'Resolve', near: NOTICES_FLOW, scope: 'main' },
        fill: async () => {
          await demo.choose('It moved: link where it lives now', 'dialog');
          await demo.fill('Application that holds it', 'Customer Notice', 'dialog');
          await demo.click({ text: 'Customer Notice Sender', prefix: true, scope: 'dialog' });
          await demo.see(`Chosen by its name, ${NOTICES_FLOW}.`, { scope: 'dialog' });
          await demo.fill('ID in its tool', 'flow-a41c07', 'dialog');
          await demo.fill('Link', 'https://flows.example.org/customer-services-prod/flow-a41c07', 'dialog');
          await demo.fill('Note', 'Its maker rebuilt the flow in the new notices solution, with a new ID.', 'dialog');
        },
        preview: 'Check first',
        submit: 'Link its new place',
        what: 'Link the new place',
        answerShows: ['The component is linked to Customer Notice Sender. The component is recorded as moved.'],
      });
      await demo.sheet({
        open: { text: 'Still there', near: PURCHASE_FLOW, scope: 'main' },
        fill: () => demo.fill('Reason', 'It runs in the Finance Prod environment, which the admin role of this check does not read.', 'dialog'),
        submit: 'Dismiss the finding',
        what: 'Dismiss the finding',
        answerShows: ['The claim is dismissed. Reopen brings it back.'],
      });
      await demo.open('gap-check?view=vanished');
      await demo.notSee(NOTICES_FLOW, { scope: 'main', what: `${NOTICES_FLOW} leaves Vanished` });
      await demo.notSee(PURCHASE_FLOW, { scope: 'main', what: `${PURCHASE_FLOW} leaves Vanished` });
    });
  });

  step('S-22.17 Reopen a claim and claim an item for a new application', async ({ demo }) => {
    await demo.open('gap-check?view=dismissed', 'owen');
    await demo.see(TEST_FLOW);
    await demo.act(`Owen reopens "${TEST_FLOW}" and claims "${CANTEEN}" into a new draft application`, async () => {
      demo.writes('claim_resource@pages/Records#ClaimSheet', 'dismiss_claim@pages/Records#DismissSheet');
      await demo.sheet({
        open: { text: 'Reopen', near: TEST_FLOW, scope: 'main' },
        fill: () => demo.fill('Reason', 'Its maker kept the flow: it now sends the real reminders, so it needs an owner.', 'dialog'),
        submit: 'Reopen',
        what: 'Reopen the claim',
        answerShows: ['The claim is open again.'],
      });
      await demo.open('gap-check?view=open');
      await demo.see(TEST_FLOW, { what: `${TEST_FLOW} is open again` });
      await demo.sheet({
        open: { text: 'Claim', near: CANTEEN, scope: 'main' },
        fill: async () => {
          await demo.choose('A new application: start an intake request', 'dialog');
          await demo.fill('Name', CANTEEN, 'dialog');
          await demo.fill('What it is for', 'Crews order lunch from the depot canteen before their shift.', 'dialog');
          await pickIn(demo, 'Kind', 'Power Platform app');
        },
        preview: 'Check first',
        submit: 'Claim it',
        confirm: 'Claim it',
        what: 'Claim the item for a new application',
        answerShows: ['The item is claimed for a new draft application, and its intake request is started as a draft. Answer the tier questions and submit it for triage.'],
      });
    });
  });

  step('S-22.18 Check the owners', async ({ demo }) => {
    await demo.open('gap-check', 'owen');
    await demo.see('Check the owners');
    await demo.act('Owen checks every owner in the demo directory', async () => {
      demo.writes('run_owner_check@pages/deployments-places/sheets#OwnerCheckSheet');
      await demo.sheet({
        open: { text: 'Check the owners', scope: 'main' },
        submit: 'Check the owners',
        what: 'Check the owners',
        answerShows: ["25 owners checked in the demo's fictional directory, 2 findings."],
      });
    });
  });

  step('S-22.19 Withdraw the draft application', async ({ demo }) => {
    const draft = (await apiAll<any>('priya', 'find_applications', { text: REGISTERED, registrationMeanings: ['draft'] })).find(a => a.name === REGISTERED);
    if (!draft) throw new Error(`no draft application ${REGISTERED}: S-22.1 did not run on this store`);
    await demo.open(`records/business-application/${String(draft.businessApplicationId).toLowerCase()}`, 'priya');
    await demo.see('A draft application');
    await demo.act(`Priya withdraws the draft ${REGISTERED}`, async () => {
      demo.writes('withdraw_record@pages/work-approvals/parts#WithdrawSheet');
      await demo.sheet({
        open: { text: 'Withdraw', scope: 'main', near: 'A draft application' },
        fill: () => demo.fill('Reason', 'The depot keys move into Asset Register, so the app is not needed.', 'dialog'),
        preview: 'Check first',
        submit: 'Withdraw',
        confirm: 'Withdraw',
        what: 'Withdraw the draft application',
        answerShows: ['Withdrawn. 2 waiting approvals are canceled.'],
      });
    });
  });

  step('S-22.20 Change a leave date', async ({ demo }) => {
    await demo.open(`records/business-application/${await appId('Crew Roster')}`, 'owen');
    // She has a leave on record since S-22.4, so her owner row offers to change it, never a second leave (QA walk).
    demo.check(await demo.has({ text: 'Change the leave date of Ines Varga', scope: 'main' }), 'the owner row of Ines Varga on Crew Roster offers "Change the leave date"');
    demo.check(!(await demo.has({ text: 'Record a leave for Ines Varga', scope: 'main' })), 'the owner row of Ines Varga on Crew Roster no longer offers "Record a leave"');
    await demo.open('owners?view=leavers');
    await demo.see('Ines Varga');
    await demo.act('Owen moves the leave date of Ines Varga to 45 days from today', async () => {
      demo.writes('record_owner_leave@pages/owners-reviews/OwnerSheets#RecordLeaveSheet');
      await demo.sheet({
        open: { text: 'Change the leave date of Ines Varga', scope: 'main' },
        fill: async () => {
          await demo.see(/A leave on .{0,40} is recorded now\. A new date replaces it\./, { scope: 'dialog' });
          await demo.setValue('Leave date', day(45), 'dialog');
        },
        preview: 'Check first',
        submit: 'Record the leave',
        confirm: 'Record the leave',
        what: 'Change the leave date',
        answerShows: ['7 owner roles are affected, and 1 owner role on data (Field operations); a successor needs an accepted ownership decision.', '12 owner check reviews cover this leave'],
      });
    });
  });

  step('S-22.21 Propose a successor for a leaver', async ({ demo }) => {
    await demo.open('owners?view=leavers', 'owen');
    await demo.see('Ines Varga');
    await demo.act('Owen proposes a successor to Ines Varga as business owner of Crew Roster and sends it for approval', async () => {
      demo.writes('propose_owner_change@pages/owners-reviews/OwnerSheets#ProposeOwnerChangeSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
      await demo.proposeAndSubmit({
        open: { text: 'Propose a successor for Business owner of Crew Roster', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'Successor', 'Ravi Holm');
          await demo.fill('Reason', 'Ines moves to the head office; Ravi, her deputy, runs the crew roster day to day.', 'dialog');
        },
        preview: 'Check first',
        submit: 'Propose',
        what: 'Propose the successor',
        sendShows: ['Submitted. 2 approvals are asked.', /Ravi Holm, asked by name about the business owner role/],
      });
    });
  });

  step('S-22.22 End an owner assignment', async ({ demo }) => {
    await demo.open(`records/business-application/${await appId('Asset Register')}`, 'owen');
    demo.check(await demo.has({ text: "End Ines Varga's assignment", prefix: true, scope: 'main' }), 'the owner rows of Ines Varga on Asset Register offer "End"');
    await demo.act('Owen proposes to end Ines Varga as deputy data owner of Asset Register and sends it', async () => {
      demo.writes('propose_owner_change@pages/owners-reviews/OwnerSheets#ProposeOwnerChangeSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
      await demo.proposeAndSubmit({
        // Each End names the role it ends (QA walk): Ines is deputy business owner and deputy data owner here.
        open: { text: "End Ines Varga's assignment as data owner deputy", scope: 'main' },
        fill: () => demo.fill('Reason', 'Ines leaves the field operations team; the data owner keeps the asset data alone until a new deputy is named.', 'dialog'),
        preview: 'Check first',
        submit: 'Propose',
        what: 'Propose to end the assignment',
        sendShows: ['Submitted. 1 approval is asked.'],
      });
      await demo.open(`records/business-application/${await appId('Asset Register')}`);
      await demo.see('End Ines Varga as deputy data owner of Asset Register.');
    });
  });

  step('S-22.23 Add a data link on Application 360', async ({ demo }) => {
    await demo.open(`records/business-application/${await appId('Visit Report Agent')}?tab=data`, 'elif');
    await demo.see('Add a data link');
    await demo.act('Elif records that Visit Report Agent reads data from Field Data Lake', async () => {
      demo.writes('save_dependency@pages/app360/sheets#NewDependencySheet');
      await demo.sheet({
        open: { text: 'Add a data link', scope: 'main' },
        fill: async () => {
          await demo.fill('The other application', 'Field Data', 'dialog');
          await demo.click({ text: 'Field Data Lake', prefix: true, scope: 'dialog' });
          await pickIn(demo, 'Criticality', 'Low');
        },
        preview: 'Check first',
        submit: 'Record it',
        what: 'Record the data link',
        answerShows: ['The dependency is recorded, as reported: Visit Report Agent reads data from Field Data Lake.'],
      });
    });
  });

  step('S-22.24 Change and end a dependency', async ({ demo }) => {
    await demo.open(`records/business-application/${await appId('Visit Report Agent')}/map`, 'elif');
    await demo.see('Field Knowledge MCP Server');
    if (!demo.phone) await demo.click({ text: 'List', role: 'tab', scope: 'main' });
    await demo.act('Elif raises the criticality of the Field Knowledge MCP Server link, then ends the Field Data Lake link', async () => {
      demo.writes('save_dependency@pages/app360/sheets#DependencyChangeSheet');
      await demo.sheet({
        open: { text: 'Change the link: Field Knowledge MCP Server', prefix: true, scope: 'main' },
        fill: () => pickIn(demo, 'Criticality', 'High'),
        preview: 'Check first',
        submit: 'Save',
        what: 'Change the dependency',
        answerShows: ['The dependency is changed, as reported: Visit Report Agent calls API of Field Knowledge MCP Server.'],
      });
      await demo.open(`records/business-application/${await appId('Visit Report Agent')}?tab=data`);
      await demo.sheet({
        open: { text: 'End the link with Field Data Lake', scope: 'main' },
        preview: 'Check first',
        submit: 'End it',
        confirm: 'End it',
        what: 'End the data link',
        answerShows: ['The dependency is ended: Visit Report Agent reads data from Field Data Lake.'],
      });
      await demo.notSee('End the link with Field Data Lake', { what: 'the Field Data Lake link leaves the list' });
    });
  });

  step('S-22.25 Record a correction', async ({ demo }) => {
    const record = await handDeployment();
    if (!record) throw new Error('no hand deployment of Inspection Scheduler 3.4.3: S-22.5 did not run on this store');
    await demo.open(`records/deployment-record/${String(record.deploymentRecordId).toLowerCase()}`, 'hugo');
    await demo.see('Record a correction');
    await demo.see('Not this change?');
    await demo.act('Hugo corrects the finish time of the hand deployment', async () => {
      demo.writes('record_deployment_correction@pages/deployments-places/sheets#CorrectionSheet');
      await demo.sheet({
        open: { text: 'Record a correction', scope: 'main' },
        fill: async () => {
          await demo.fill('What was wrong', 'The deployment finished five minutes later than recorded: the import took five more minutes.', 'dialog');
          await demo.setValue('Finished', localTime('2026-11-25T07:45:00Z'), 'dialog');
        },
        preview: 'Check first',
        submit: 'Record the correction',
        confirm: 'Record it',
        what: 'Record the correction',
        answerShows: ['The correction is recorded as a new deployment record; the old record stays. The engine flagged it: came another way.'],
      });
    });
  });

  step('S-22.26 Propose an exception for the flagged deployment', async ({ demo }) => {
    const record = (await handDeployment(true)) ?? (await handDeployment());
    if (!record) throw new Error('no hand deployment of Inspection Scheduler 3.4.3: S-22.5 did not run on this store');
    await demo.open(`records/deployment-record/${String(record.deploymentRecordId).toLowerCase()}`, 'elif');
    await demo.see('Came another way');
    await demo.act('Elif proposes an exception for the hand deployment and sends it for approval', async () => {
      demo.writes('save_decision_proposal@pages/Records#ProposeExceptionSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
      await demo.proposeAndSubmit({
        open: { text: 'Propose an exception', scope: 'main' },
        fill: async () => {
          await demo.fill('What the exception accepts', 'Accept the hand deployment of Inspection Scheduler 3.4.3 to Field Ops Prod.', 'dialog');
          await demo.fill('Context', 'The reminder flow connection had to be rotated inside its window, and the pipeline was paused for maintenance.', 'dialog');
          await demo.fill('Reason', 'The change was a standard one, inside its window, and the result was checked the same morning.', 'dialog');
        },
        submit: 'Save the proposal',
        what: 'Propose the exception',
        sendShows: ['Submitted. 1 approval is asked.', /Architect \(the first answer counts/],
      });
      await demo.open('decisions?view=exceptions');
      await demo.see('Accept the hand deployment of Inspection Scheduler 3.4.3 to Field Ops Prod.');
    });
  });

  step('S-22.27 Add evidence on the deployment', async ({ demo }) => {
    const record = (await handDeployment(true)) ?? (await handDeployment());
    if (!record) throw new Error('no hand deployment of Inspection Scheduler 3.4.3: S-22.5 did not run on this store');
    await demo.open(`records/deployment-record/${String(record.deploymentRecordId).toLowerCase()}`, 'hugo');
    await demo.see('Add evidence');
    await demo.act('Hugo links the deployment log as evidence', async () => {
      demo.writes('save_link@pages/changes-records/parts#SaveLinkSheet');
      await demo.sheet({
        open: { text: 'Add evidence', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'Kind', 'Evidence link');
          await demo.fill('Link', 'https://dev.example.org/field-ops/_wiki/wikis/ops/deploy-log-3-4-3', 'dialog');
          await demo.fill('Label', 'Deployment log, Inspection Scheduler 3.4.3', 'dialog');
        },
        submit: 'Add evidence',
        what: 'Add the evidence link',
        answerShows: ['The evidence link is saved. It stores an ID, a URL and a label, never a copy.'],
      });
    });
  });

  step('S-22.28 Change the result of an authorized change', async ({ demo }) => {
    const change = await changeByTitle(EXPORT_CHANGE);
    if (!change) throw new Error(`no change "${EXPORT_CHANGE}"`);
    await demo.open(`records/change-request/${String(change.changeRequestId).toLowerCase()}`, 'hugo');
    await demo.see('Record the result');
    await demo.act(`Hugo records how "${EXPORT_CHANGE}" ended, then changes the result`, async () => {
      demo.writes('record_change_result@pages/changes-records/ChangeActions#RecordResultSheet');
      await demo.sheet({
        open: { text: 'Record the result', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'Result', 'Partly succeeded');
          await demo.fill('Note', 'The flow is off, but one scheduled export still ran overnight.', 'dialog');
        },
        preview: 'Check first',
        submit: 'Record the result',
        what: 'Record the result',
        answerShows: ['The change result is recorded as reported.'],
      });
      await demo.see('Change the result');
      await demo.sheet({
        open: { text: 'Change the result', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'Result', 'Succeeded');
          await demo.fill('Note', 'The overnight export was the last one: nothing ran since.', 'dialog');
        },
        preview: 'Check first',
        submit: 'Record the result',
        what: 'Change the result',
        answerShows: ['The change result is recorded as reported.'],
      });
    });
  });

  step('S-22.29 Edit a change draft and link its work item', async ({ demo }) => {
    await demo.open(`changes/new?app=${await appId('Inspection Scheduler')}`, 'hugo');
    await demo.see('Plan the change');
    await demo.act('Hugo plans a change, edits the draft twice, and links its work item and evidence', async () => {
      demo.writes('save_change_request_draft@pages/changes-records/ChangeDraftSheet#ChangeDraftSheet', 'save_link@pages/changes-records/parts#SaveLinkSheet');
      const start = day(7);
      await demo.sheet({
        open: { text: 'Plan the change', scope: 'main' },
        fill: async () => {
          await demo.fill('Title', REMINDER_CHANGE, 'dialog');
          await demo.select('Choose the type', 'Normal');
          await demo.fill('What changes, and why', 'Clearer wording in the inspection reminders, so inspectors know which site is due.', 'dialog');
          await demo.setValue('Window starts', `${start}T10:00`, 'dialog');
          await demo.setValue('Window ends', `${start}T11:00`, 'dialog');
        },
        submit: 'Save draft',
        what: 'Save the change draft',
        answerShows: ['The change request is saved as a draft (built-in mode).'],
      });
      await demo.sheet({
        open: { text: 'Edit the draft', scope: 'main' },
        fill: async () => {
          await demo.setValue('Window starts', `${day(8)}T10:00`, 'dialog');
          await demo.setValue('Window ends', `${day(8)}T11:00`, 'dialog');
        },
        submit: 'Save draft',
        what: 'Edit the draft on the New change page',
        answerShows: ['The change request is saved as a draft (built-in mode).'],
      });
      const change = await changeByTitle(REMINDER_CHANGE);
      if (!change) throw new Error(`no change "${REMINDER_CHANGE}" after the save`);
      await demo.open(`records/change-request/${String(change.changeRequestId).toLowerCase()}`);
      await demo.sheet({
        open: { text: 'Edit the draft', scope: 'main' },
        fill: () => demo.fill('What changes, and why', 'Clearer wording in the inspection reminders, so inspectors know which site is due and by when.', 'dialog'),
        submit: 'Save draft',
        what: 'Edit the draft on the change record',
        answerShows: ['The change request is saved as a draft (built-in mode).'],
      });
      await demo.sheet({
        open: { text: 'Link a work item', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'Kind', 'Azure DevOps work item');
          await demo.fill('Link', 'https://dev.example.org/field-ops/_workitems/edit/4230', 'dialog');
          await demo.fill('Label', 'Reminder wording, work item 4230', 'dialog');
          await demo.fill('ID in its own tool', '4230', 'dialog');
        },
        submit: 'Link it',
        what: 'Link the work item',
        answerShows: ['The delivery link is saved. It stores an ID, a URL and a label, never a copy.'],
      });
      await demo.sheet({
        open: { text: 'Add evidence', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'Kind', 'Evidence link');
          await demo.fill('Link', 'https://dev.example.org/field-ops/_wiki/wikis/ops/reminder-wording', 'dialog');
          await demo.fill('Label', 'The new reminder wording, agreed with the inspectors', 'dialog');
        },
        submit: 'Add evidence',
        what: 'Add evidence on the change',
        answerShows: ['The evidence link is saved. It stores an ID, a URL and a label, never a copy.'],
      });
    });
  });

  step('S-22.30 Withdraw a change', async ({ demo }) => {
    const change = await changeByTitle(CREW_CHANGE);
    if (!change) throw new Error(`no change "${CREW_CHANGE}": S-05.3 did not run on this store`);
    await demo.open(`records/change-request/${String(change.changeRequestId).toLowerCase()}`, 'hugo');
    await demo.see('Withdraw');
    await demo.act(`Hugo withdraws "${CREW_CHANGE}" and the draft of S-22.29`, async () => {
      demo.writes('withdraw_record@pages/changes-records/ChangeActions#WithdrawChangeSheet');
      await demo.sheet({
        open: { text: 'Withdraw', scope: 'main' },
        fill: () => demo.fill('Reason', 'The overtime column comes with the next Crew Roster release instead.', 'dialog'),
        submit: 'Withdraw',
        confirm: 'Withdraw',
        what: 'Withdraw the submitted change',
        answerShows: ['Withdrawn. 1 waiting approval is canceled.'],
      });
      const draft = await changeByTitle(REMINDER_CHANGE);
      if (!draft) throw new Error(`no draft change "${REMINDER_CHANGE}" to withdraw (S-22.29)`);
      {
        await demo.open(`records/change-request/${String(draft.changeRequestId).toLowerCase()}`);
        await demo.sheet({
          open: { text: 'Withdraw', scope: 'main' },
          fill: () => demo.fill('Reason', 'The wording goes into the 3.5 release instead.', 'dialog'),
          submit: 'Withdraw',
          confirm: 'Withdraw',
          what: 'Withdraw the draft change',
          answerShows: ['Withdrawn. No approval was waiting.'],
        });
      }
    });
  });

  step('S-22.31 Change a target, retire one and return it', async ({ demo }) => {
    const training = await targetByName(TARGET);
    const people = await targetByName(PEOPLE_TEST);
    if (!training || !people) throw new Error(`no target ${TARGET} or ${PEOPLE_TEST}`);
    await demo.open(`places/${String(training.deploymentTargetId).toLowerCase()}`, 'owen');
    await demo.see('Change the target');
    await demo.act(`Owen changes the change path of ${TARGET}, then retires ${PEOPLE_TEST} and returns it to service`, async () => {
      demo.writes('save_deployment_target@pages/deployments-places/sheets#RetireTargetSheet', 'save_deployment_target@pages/deployments-places/sheets#TargetSheet');
      await demo.sheet({
        open: { text: 'Change the target', scope: 'main' },
        fill: () => pickIn(demo, 'Change path', 'Pipeline or AI tool'),
        preview: 'Check first',
        submit: 'Save the change',
        what: 'Change the target',
        answerShows: [`The deployment target ${TARGET} is changed.`],
      });
      await demo.open(`places/${String(people.deploymentTargetId).toLowerCase()}`);
      await demo.sheet({
        open: { text: 'Retire the target', scope: 'main' },
        fill: () => demo.fill('Why it is retired', 'People Hub testing moves to the shared HR test environment.', 'dialog'),
        preview: 'Check first',
        submit: 'Retire it',
        confirm: 'Retire it',
        what: 'Retire the target',
        answerShows: [`The deployment target ${PEOPLE_TEST} is retired.`],
      });
      await demo.sheet({
        open: { text: 'Return to service', scope: 'main' },
        preview: 'Check first',
        submit: 'Return it to service',
        what: 'Return the target to service',
        answerShows: [`The deployment target ${PEOPLE_TEST} is back in service.`],
      });
    });
  });

  step('S-22.32 Change a pipeline map', async ({ demo }) => {
    await demo.open('places?view=pipelines', 'owen');
    await demo.see('Inspection Scheduler to training');
    await demo.act('Owen adds Route Planner to the training pipeline', async () => {
      demo.writes('map_pipeline@pages/deployments-places/sheets#MapPipelineSheet');
      await demo.sheet({
        open: { text: 'Change the map', near: 'Inspection Scheduler to training', scope: 'main' },
        fill: async () => {
          await demo.click({ text: 'Choose an application other than Inspection Scheduler', scope: 'dialog' });
          await demo.fill('Application', 'Route Pla', 'dialog');
          await demo.click({ text: 'Route Planner', prefix: true, scope: 'dialog' });
          await pickIn(demo, 'Step 1: target', TARGET);
        },
        preview: 'Check first',
        submit: 'Save the map',
        what: 'Change the map',
        answerShows: ['The pipeline is mapped to 1 application and target pair. The product never runs the pipeline.'],
      });
      await demo.open('places?view=pipelines');
      await demo.see(/Inspection Scheduler to training[\s\S]{0,400}Route Planner/, { what: 'the training pipeline now deploys Route Planner too' });
    });
  });

  step('S-22.33 Confirm, change and add inside the new location', async ({ demo }) => {
    const location = await locationByName(LOCATION);
    if (!location) throw new Error(`no location ${LOCATION}: S-22.10 did not run on this store`);
    await demo.open(`records/data-location/${String(location.dataLocationId).toLowerCase()}`, 'hana');
    await demo.see('Confirm it is still there');
    await demo.act(`Hana confirms ${LOCATION} is still there, changes its link and adds a list inside it`, async () => {
      demo.writes('save_data_location@pages/data/sheets#DataLocationSheet');
      await demo.sheet({
        open: { text: 'Confirm it is still there', scope: 'main' },
        submit: 'Confirm seen',
        what: 'Confirm the location seen',
        answerShows: ['1 location is changed.'],
      });
      await demo.sheet({
        open: { text: 'Change', scope: 'main' },
        fill: () => demo.fill('Link', 'https://fieldops.example.org/sites/depot-safety/binders', 'dialog'),
        preview: 'Check first',
        submit: 'Save the change',
        what: 'Change the location',
        answerShows: ['1 location is changed.'],
      });
      await demo.sheet({
        open: { text: 'Add one', scope: 'main' },
        fill: async () => {
          await pickIn(demo, 'Platform', 'SharePoint');
          await demo.fill('Name', CHECKLISTS, 'dialog');
          await demo.fill('Link', 'https://fieldops.example.org/sites/depot-safety/checklists', 'dialog');
        },
        preview: 'Check first',
        submit: 'Add the location',
        what: 'Add a location inside it',
        answerShows: ['1 location is registered.'],
      });
      await demo.see(CHECKLISTS, { what: `${CHECKLISTS} is listed inside ${LOCATION}` });
    });
  });

  step('S-22.34 Propose a technical owner for a location', async ({ demo }) => {
    await demo.open('data/locations', 'hana');
    await demo.see('PAYR01');
    await demo.act('Hana proposes Felix Arendt as technical owner of PAYR01 and sends it for approval', async () => {
      demo.writes('propose_owner_change@pages/data/sheets#ProposeDataOwnerSheet', 'submit_record@pages/data/sheetParts#SubmitFollowUp');
      await demo.proposeAndSubmit({
        open: { text: 'Propose an owner', near: 'PAYR01', scope: 'main' },
        fill: async () => {
          await demo.person('New holder', 'Felix Arendt', 'dialog');
          await demo.fill('Reason', 'Felix runs the payroll database servers for the platform team.', 'dialog');
        },
        submit: 'Propose',
        what: 'Propose the technical owner',
        sendShows: ['Submitted. 2 approvals are asked.', /Felix Arendt, asked by name about the technical owner role/],
      });
    });
  });

  step('S-22.35 Change and end the mapping, then retire the location', async ({ demo }) => {
    const location = await locationByName(LOCATION);
    if (!location) throw new Error(`no location ${LOCATION}: S-22.10 did not run on this store`);
    await demo.open(`records/data-location/${String(location.dataLocationId).toLowerCase()}`, 'hana');
    await demo.see('Field operations / Safety findings');
    await demo.act(`Hana changes the mapping at ${LOCATION}, ends it, and retires ${CHECKLISTS}`, async () => {
      demo.writes('save_data_location@pages/data/sheets#DataLocationSheet', 'save_data_mapping@pages/data/sheets#DataMappingSheet');
      await demo.click({ text: 'The mapping', role: 'link', scope: 'main', near: 'Safety findings' });
      await demo.at(/\/records\/data-mapping\//, 'the mapping record opens');
      const mappingId = /\/records\/data-mapping\/([0-9a-f-]{36})/.exec(demo.url())?.[1] ?? null;
      await demo.sheet({
        open: { text: 'Change', scope: 'main' },
        fill: () => demo.choose('Replica', 'dialog'),
        submit: 'Save the change',
        what: 'Change the mapping',
        answerShows: ['1 mapping is saved.'],
      });
      await demo.see(/Origin ?Replica/, { scope: 'main', what: 'the record shows the Replica origin' });
      await demo.sheet({
        open: { text: 'End the mapping', scope: 'main' },
        submit: 'End the mapping',
        confirm: 'End the mapping',
        what: 'End the mapping',
        closes: true,
      });
      await demo.api('the ended mapping keeps the Replica origin', async () => {
        const ended = mappingId ? (await apiAll<any>('hana', 'list_data_mappings', { dataMappingIds: [mappingId] }))[0] ?? null : null;
        demo.check(ended?.origin === 'replica' && !!ended?.endedOn, 'the ended mapping keeps the origin saved a moment before', JSON.stringify({ origin: ended?.origin, endedOn: ended?.endedOn }));
      });
      // The record lists open mappings only, so once the mapping ends the page says so in place of it.
      await demo.see('The product lists open mappings only, and this one has ended or was withdrawn.');
      const inside = await locationByName(CHECKLISTS);
      if (!inside) throw new Error(`no location ${CHECKLISTS}: S-22.33 did not run on this store`);
      await demo.open(`records/data-location/${String(inside.dataLocationId).toLowerCase()}`);
      await demo.sheet({
        open: { text: 'Retire', scope: 'main' },
        submit: 'Retire',
        confirm: 'Retire',
        what: 'Retire the location',
        answerShows: ['1 location is changed.'],
      });
      await demo.see('Retired');
    });
  });

  step('S-22.36 Withdraw the risk acceptance', async ({ demo }) => {
    const risk = await decisionByStatement('nadia', RISK);
    if (!risk) throw new Error('no risk acceptance on Contractor Portal: S-22.13 did not run on this store');
    await demo.open(`records/decision/${String(risk.decision.decisionId).toLowerCase()}`, 'nadia');
    await demo.see('Withdraw');
    await demo.act('Nadia withdraws her risk acceptance on Contractor Portal', async () => {
      demo.writes('withdraw_record@pages/work-approvals/parts#WithdrawSheet');
      await demo.sheet({
        open: { text: 'Withdraw', scope: 'main' },
        fill: () => demo.fill('Reason', "Contract ends are now handled by the vendor's offboarding.", 'dialog'),
        preview: 'Check first',
        submit: 'Withdraw',
        confirm: 'Withdraw',
        what: 'Withdraw the decision',
        answerShows: ['Withdrawn. 1 waiting approval is canceled.'],
      });
    });
  });

  step('S-22.37 Edit, retire and bring back the new portfolio', async ({ demo }) => {
    const all = await apiAll<any>('owen', 'list_portfolios', {});
    const depot = all.find(p => p.name === PORTFOLIO);
    if (!depot) throw new Error(`no portfolio ${PORTFOLIO}: S-22.12 did not run on this store`);
    const id = String(depot.portfolioId).toLowerCase();
    await demo.open(`portfolios/${id}`, 'mateo');
    await demo.see(PORTFOLIO);
    await demo.act(`Mateo edits ${PORTFOLIO}; Owen retires it and brings it back`, async () => {
      demo.writes('save_portfolio@pages/pmo-portfolio/sheets#PortfolioActiveSheet', 'save_portfolio@pages/pmo-portfolio/sheets#PortfolioSheet');
      await demo.sheet({
        open: { text: 'Edit', scope: 'main' },
        fill: () => demo.fill('Description', 'The depot apps: keys, stores, the canteen and the safety binders.', 'dialog'),
        preview: 'Check first',
        submit: 'Save',
        what: 'Edit the portfolio',
        answerShows: [`The portfolio ${PORTFOLIO} is saved.`],
      });
      await demo.open(`portfolios/${id}`, 'owen');
      await demo.sheet({
        open: { text: 'Retire', scope: 'main' },
        submit: 'Retire',
        confirm: 'Retire',
        what: 'Retire the portfolio',
        answerShows: [`The portfolio ${PORTFOLIO} is retired.`],
      });
      await demo.sheet({
        open: { text: 'Bring back', scope: 'main' },
        submit: 'Bring back',
        confirm: 'Bring back',
        what: 'Bring the portfolio back',
        answerShows: [`The portfolio ${PORTFOLIO} is back in service.`],
      });
    });
  });

  step('S-22.38 Answer the tier questions on the new request', async ({ demo }) => {
    const request = await intakeByTitle('owen', CANTEEN);
    if (!request) throw new Error(`no request "${CANTEEN}": S-22.17 did not run on this store`);
    await demo.open(`records/intake-request/${String(request.intakeRequestId).toLowerCase()}`, 'owen');
    await demo.see('Answer the tier questions', { scope: 'main', what: 'the draft request offers "Answer the tier questions"' });
    await demo.act(`Owen answers the tier questions of "${CANTEEN}"`, async () => {
      demo.writes('answer_tier_questions@pages/intake/sheets#RecordTierSheet');
      const answer = await demo.sheet({
        open: { text: 'Answer the tier questions', scope: 'main' },
        fill: () => answerEveryQuestion(demo),
        preview: 'Check with the engine',
        submit: 'Save answers',
        what: 'Answer the tier questions on the request',
        answerShows: [/The answers suggest Tier \d\./],
      });
      const tier = /The answers suggest (Tier \d)/.exec(answer.text)?.[1];
      // The label is set in capitals by its style, and the page's text reads it so.
      if (tier) await demo.see(new RegExp(`Suggested tier ?${tier}`, 'i'), { scope: 'main', what: `the request shows the suggested ${tier}` });
    });
  });
});
