// What every Application 360 panel in the web app shares, read once per page: who the person is to the application
// (session, the owner roles they hold on it), the owners, the decisions about the application, its documents with
// their owners, and which decision types the person may propose here. Application360.tsx provides it in the web app
// only; outside the provider (the harness) the row actions read null and render nothing.
import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import type { ApplicationCard, ApplicationOwners, DecisionLog, DocumentationStatus } from '../../data/contracts';
import { useConfigRows, useRead } from '../../data/reads';
import { useSession, type Session } from '../../data/session';
import { dataOf, ToolClientContext, useToolClient, type ToolState } from '../../data/toolClient';
import { useHost } from '../../harness';
import { dedupeReads } from './dedupe';
import { aboutApplication, gateDecisionsOf, myOwnerRoles, proposableTypes } from './logic';

export interface App360Context {
  applicationId: string;
  applicationName: string;
  card: ApplicationCard | null;
  owners: ApplicationOwners | null;
  session: Session;
  today: string;
  // The owner roles the person holds on this application now.
  ownerRoles: string[];
  // The decisions about the application and its records (get_decision_log, newest first), and those about the
  // application itself.
  decisionLog: ToolState<DecisionLog>;
  appDecisions: DecisionLog['items'];
  // Every decision of the application that a gate may take (gateDecisionsOf), whatever its subject kind.
  gateDecisions: DecisionLog['items'];
  // The application's documents with their owners and link kinds (get_documentation_status), for the document actions.
  docs: DocumentationStatus['applications'][number] | null;
  // The decision types about the application the person's decision rights let them propose, in configured order.
  proposable: string[];
  // True when the person may call the write tool here: the web app's own entry, and the Tool Policy gives it.
  can(tool: string): boolean;
  // The decision the person just proposed on this page: nobody is asked until it is submitted, so the page offers the
  // submit at once (ProposalBanner).
  proposal: { decisionId: string; subject: string } | null;
  onProposed(decisionId: string, subject: string): void;
  dismissProposal(): void;
}

const Context = createContext<App360Context | null>(null);

export function useApp360(): App360Context | null {
  return useContext(Context);
}

export function App360Scope(props: { applicationId: string; card: ApplicationCard | null; owners: ApplicationOwners | null; children: ReactNode }) {
  const host = useHost();
  const session = useSession();
  const decisionLog = useRead('get_decision_log', { businessApplicationId: props.applicationId, top: 100 });
  const docState = useRead('get_documentation_status', { businessApplicationId: props.applicationId, top: 10 });
  const types = useConfigRows('decision-type');
  const rights = useConfigRows('decision-right');
  const app = props.card?.application ?? null;
  const ownerRoles = useMemo(() => myOwnerRoles(props.owners, session.objectId, host.today), [props.owners, session.objectId, host.today]);
  const log = dataOf(decisionLog);
  const [proposal, setProposal] = useState<{ decisionId: string; subject: string } | null>(null);
  const onProposed = useCallback((decisionId: string, subject: string) => setProposal({ decisionId, subject }), []);
  const dismissProposal = useCallback(() => setProposal(null), []);
  const value = useMemo<App360Context>(() => ({
    applicationId: props.applicationId,
    applicationName: app?.name ?? 'this application',
    card: props.card,
    owners: props.owners,
    session,
    today: host.today,
    ownerRoles,
    decisionLog,
    appDecisions: aboutApplication(log?.items ?? [], props.applicationId),
    gateDecisions: gateDecisionsOf(log?.items ?? []),
    docs: dataOf(docState)?.applications.find(a => a.businessApplicationId.toLowerCase() === props.applicationId.toLowerCase()) ?? null,
    proposable: session.can('save_decision_proposal')
      ? proposableTypes(types.rows, rights.rows, { roleKeys: session.roleKeys, ownerRoleKeys: ownerRoles, tierKey: app?.tier?.key ?? null, kindKey: app?.kind.key ?? null })
      : [],
    can: tool => session.isPerson && session.can(tool as Parameters<Session['can']>[0]),
    proposal,
    onProposed,
    dismissProposal,
  }), [props.applicationId, props.card, props.owners, app, session, host.today, ownerRoles, decisionLog, log, docState, types.rows, rights.rows, proposal, onProposed, dismissProposal]);
  return <Context.Provider value={value}>{props.children}</Context.Provider>;
}

// Identical reads below it share one request until the shell's next refresh (dedupe.ts). Application 360 and the
// dependency map wrap themselves in it, so a page of many small parts stays well under the per-person call limit.
export function ReadDedupe({ children }: { children: ReactNode }) {
  const parent = useToolClient();
  const client = useMemo(() => (parent ? dedupeReads(parent) : null), [parent]);
  return <ToolClientContext.Provider value={client}>{children}</ToolClientContext.Provider>;
}

// The person holds an owner role on the application, or a role that acts on every application (the engine's owner
// relation check for owner writes: ai-lead and platform-admin act on any application).
export function ownsOrAdmin(ctx: App360Context): boolean {
  return ctx.ownerRoles.length > 0 || ctx.session.hasRole('platform-admin', 'ai-lead');
}
