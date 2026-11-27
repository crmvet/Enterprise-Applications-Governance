// Reads the Changes and Records pages need beyond useTool:
//   useWhoAmI     the signed-in person (or the demo persona) and the tools the Tool Policy gives them, from whoami. The
//                 pages show an action only when its tool is in that list; the engine still checks every write.
//   usePagedRead  one list read, page after page: until a row is found (a record page whose entity has no read by ID),
//                 or for as many pages as the person asked for (Show more).
// Both read again in place after a write, as useTool does: the last answer stays on screen until the new one comes.
import { useEffect, useState } from 'react';
import type { Envelope, ToolName } from '../../data/contracts';
import { useToolClient } from '../../data/toolClient';

// ---------- whoami ----------

// The parts of whoami's data these pages use (contracts/tools/whoami.output.schema.json). The scaffold's contracts.ts
// does not type whoami yet, so the shape lives here.
export interface WhoAmI {
  objectId: string;
  displayLabel: string;
  roleKeys: string[];
  tools: string[];
}

export type WhoState = { status: 'loading' } | { status: 'known'; who: WhoAmI } | { status: 'unknown' };

interface WhoAmIData {
  principal: { objectId: string; displayLabel: string };
  productRoles: { roleKey: string }[];
  tools: string[];
}

export function useWhoAmI(): WhoState {
  const client = useToolClient();
  const [state, setState] = useState<WhoState>({ status: 'loading' });
  useEffect(() => {
    if (!client) return;
    let live = true;
    const call = client.call('whoami' as ToolName, {}) as unknown as Promise<Envelope<WhoAmIData>>;
    call
      .then(envelope => {
        if (!live) return;
        if (!envelope.ok || !envelope.data?.principal) {
          setState({ status: 'unknown' });
          return;
        }
        const d = envelope.data;
        setState({
          status: 'known',
          who: {
            objectId: d.principal.objectId,
            displayLabel: d.principal.displayLabel,
            roleKeys: (d.productRoles ?? []).map(r => r.roleKey),
            tools: Array.isArray(d.tools) ? d.tools : [],
          },
        });
      })
      .catch(() => { if (live) setState({ status: 'unknown' }); });
    return () => { live = false; };
  }, [client]);
  return state;
}

// True when the page shows the action for this tool: the person's roles give them the tool. While whoami is still being
// read, no action shows. If whoami cannot be read, the actions show and the engine decides, so a failed read never
// locks a person out of their work.
export function canUse(state: WhoState, tool: string): boolean {
  if (state.status === 'loading') return false;
  if (state.status === 'unknown') return true;
  return state.who.tools.includes(tool);
}

export function myObjectId(state: WhoState): string | null {
  return state.status === 'known' ? state.who.objectId : null;
}

// ---------- paged reads ----------

// Moved to data/reads.ts for every page; re-exported so the Changes and Records pages keep their imports.
export { usePagedRead, type PagedOptions, type PagedState } from '../../data/reads';
