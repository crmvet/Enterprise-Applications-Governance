// Who the signed-in person is to the product, for My work, Approvals and the decision page: whoami (tool 1) gives the
// principal, the product roles and the tools the Tool Policy lets them call. The pages show an action only when the
// tool is in that list; the engine still checks every write.
//
// PAGE-LOCAL: whoami is not in data/contracts.ts (ReadToolData), so useTool cannot type it. This hook calls it through
// the same tool client and types the answer from tools/whoami.output.schema.json. Reported to the lead: add whoami to
// ReadToolData and this file shrinks to useTool('whoami', {}).
import { useEffect, useRef, useState } from 'react';
import type { ClientApp, Envelope, Guid, IsoDateTime, Key, PrincipalKind, WriteToolName } from '../../data/contracts';
import { useToolClient, type ToolState } from '../../data/toolClient';

export interface Whoami {
  principal: { objectId: Guid; displayLabel: string; principalKind: PrincipalKind };
  productRoles: { roleKey: Key; label: string; viaGroup: { objectId: Guid; displayLabel: string } | null }[];
  actorKind: 'ai-client-for-person' | 'person';
  clientApp: ClientApp | null;
  configurationSet: { key: Key; version: number; publishedOn: IsoDateTime };
  toolsets: Key[];
  tools: string[];
}

type LooseCall = (tool: string, input: Record<string, unknown>) => Promise<Envelope<Whoami>>;

// Reads whoami once per tool client. A write gives the page a new client, and the answer read again replaces the last
// one without a loading flash.
export function useWhoami(): ToolState<Whoami> {
  const client = useToolClient();
  const [state, setState] = useState<ToolState<Whoami>>({ status: 'loading' });
  const asked = useRef(false);
  useEffect(() => {
    if (!client) return;
    let live = true;
    if (!asked.current) setState({ status: 'loading' });
    asked.current = true;
    const call = client.call.bind(client) as unknown as LooseCall;
    call('whoami', {})
      .then(envelope => { if (live) setState({ status: 'done', envelope }); })
      .catch((e: unknown) => {
        if (!live) return;
        setState({
          status: 'done',
          envelope: {
            ok: false,
            error: { code: 'upstream_unavailable', message: e instanceof Error ? e.message : 'The call did not complete.' },
            correlationId: 'client-side',
          },
        });
      });
    return () => { live = false; };
  }, [client]);
  return state;
}

export interface Session {
  who: Whoami | null;
  // True once whoami answered, either way.
  known: boolean;
  can(tool: WriteToolName): boolean;
  hasRole(...roleKeys: string[]): boolean;
  objectId: string | null;
}

// The session in the form the pages ask it: may this person call a tool, do they hold a role. While whoami is loading
// or after it failed, every answer is no, so no action shows that the person may not have.
export function sessionOf(state: ToolState<Whoami>): Session {
  const who = state.status === 'done' && state.envelope.ok ? state.envelope.data : null;
  const tools = new Set(who?.tools ?? []);
  const roles = new Set((who?.productRoles ?? []).map(r => r.roleKey));
  return {
    who,
    known: state.status === 'done',
    can: tool => tools.has(tool),
    hasRole: (...keys) => keys.some(k => roles.has(k)),
    objectId: who?.principal.objectId ?? null,
  };
}
