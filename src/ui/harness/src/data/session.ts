// Who the signed-in person is to the product, for every page: whoami (tool 1) gives the principal, the product roles
// and the tools the Tool Policy lets them call. A page shows an action only when its tool is in that list, and a menu
// entry only when the person may call the page's tools; the engine still checks every write.
//
// The web app's shell reads whoami once per data version and shares it (SessionContext), so the pages and the menu
// agree and no page reads it again. Outside the shell (a test host, the harness) useSession reads it itself.
//
//   const session = useSession();
//   session.can('save_portfolio')          the Tool Policy gives the person this tool
//   session.hasRole('pmo', 'platform-admin')
//   session.objectId                       the person's object ID (the demo persona's in a demo install)
//   session.isPerson                       the web app's own entry: the engine stamps the person (D-36)
import { createContext, useContext } from 'react';
import type { WhoAmI, WriteToolName, ToolName } from './contracts';
import { useTool, type ToolState } from './toolClient';

export const SessionContext = createContext<ToolState<WhoAmI> | null>(null);

export interface Session {
  who: WhoAmI | null;
  // True once whoami answered, either way.
  known: boolean;
  // True when whoami answered with a failure: the pages hide their actions and may say why.
  failed: boolean;
  // True on the web app's own entry, where the engine stamps the person (D-36). False for an AI client's token.
  isPerson: boolean;
  objectId: string | null;
  roleKeys: string[];
  can(tool: WriteToolName | ToolName): boolean;
  hasRole(...roleKeys: string[]): boolean;
}

// The session in the form the pages ask it. While whoami is loading or after it failed, every answer is no, so no
// action shows that the person may not have.
export function sessionOf(state: ToolState<WhoAmI>): Session {
  const who = state.status === 'done' && state.envelope.ok ? state.envelope.data : null;
  const tools = new Set(who?.tools ?? []);
  const roleKeys = (who?.productRoles ?? []).map(r => r.roleKey);
  const roles = new Set(roleKeys);
  return {
    who,
    known: state.status === 'done',
    failed: state.status === 'done' && !state.envelope.ok,
    isPerson: who?.actorKind === 'person',
    objectId: who?.principal.objectId.toLowerCase() ?? null,
    roleKeys,
    can: tool => tools.has(tool),
    hasRole: (...keys) => keys.some(k => roles.has(k)),
  };
}

// The tools of the session, or null while whoami is loading or after it failed (the menu then shows every page and the
// engine decides).
export function toolsOf(state: ToolState<WhoAmI> | null): ReadonlySet<string> | null {
  if (!state || state.status !== 'done' || !state.envelope.ok) return null;
  return new Set(state.envelope.data.tools);
}

// The whoami answer the shell shares, or this component's own read when no shell provides one.
export function useWhoAmIState(): ToolState<WhoAmI> {
  const shared = useContext(SessionContext);
  const own = useTool('whoami', shared ? null : {});
  return shared ?? own;
}

export function useSession(): Session {
  return sessionOf(useWhoAmIState());
}
