// Names the scenarios share: the records the presenter scripts point at, by the words the app shows. Every one is
// fictional (src/adapters/azure/seed/demo-estate.json and the demo director).
import { apiAll, apiData, intakeByTitle } from './api';

export const CREW = 'Crew overtime tracker';
export const WEATHER = 'Weather alerts for outdoor jobs';
export const EXPORT = 'Retire the old export flow';
export const TOOLBOX = 'Toolbox talk sign-off';
export const FIRST_AID = 'Depot first-aid kit checks';

export const STATEMENT = {
  exportChange: `Authorize the change: ${EXPORT}`,
  weatherIntake: `Accept the intake request: ${WEATHER}`,
  photosRisk: 'Accept that inspection photos stay on devices for up to 7 days while offline.',
  claimsDataUse: 'Allow Claims Triage Agent to send damage claims to the approved AI model to route them.',
  ownerChange: 'End Wren Abernathy as business owner, make Nadia Ferreira business owner and end Nadia Ferreira as deputy business owner of Claims Triage Agent.',
  visitDataUse: 'Allow Visit Report Agent to send the open safety findings of a site to the approved AI model.',
  design: 'Route claims with a Copilot Studio agent and keep claim photos in the claims system.',
  autonomy: 'Agree the autonomy level Assist for Supplier Price Agent',
  weatherPriority: 'Fund weather alerts for outdoor jobs this quarter.',
  retired: 'Legacy Timesheet App is retired: nobody uses it, and nothing of it is left running.',
  configChange: 'Accept the configuration change: the AI use rule of Workplace / Visitor log changes from not set to never, version 1 to 2.',
  deskBooking: 'Grant an exception: the Desk Booking calendar connector update goes ahead without a rollback rehearsal in test.',
  hotfix: 'Grant an exception for the 1.12.1 hotfix, whose code was approved only by an AI reviewer.',
  firstAidIntake: `Accept the intake request: ${FIRST_AID}`,
};

// True when the crew overtime tracker was submitted (S-02.7 played on this store).
export async function crewSubmitted(): Promise<boolean> {
  const crew = await intakeByTitle('priya', CREW, 'mine');
  return !!crew && crew.status?.meaning !== 'draft';
}

// The approval requests open for a persona, by their decision's statement.
export async function waitingFor(persona: Parameters<typeof apiAll>[0]): Promise<string[]> {
  const asked = await apiAll<any>(persona, 'list_approvals', { view: 'mine' });
  return asked.map(a => String(a.decision?.statement ?? ''));
}

// The status meaning of the decision with this statement, read by a persona who may see it, or null.
export async function decisionStatus(persona: Parameters<typeof apiAll>[0], statement: string): Promise<string | null> {
  const log = await apiAll<any>(persona, 'get_decision_log', {});
  const found = log.find(d => d.statement === statement) ?? log.find(d => String(d.statement ?? '').startsWith(statement));
  if (!found) return null;
  const d = await apiData<any>(persona, 'get_decision', { decisionId: found.decisionId });
  return d.decision?.status?.meaning ?? null;
}
