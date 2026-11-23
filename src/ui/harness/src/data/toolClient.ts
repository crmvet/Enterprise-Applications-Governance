// The one door between a page and the product's tools.
//
// A page asks for a tool by its document 24 name with the tool's input, and gets the response envelope back.
// Binding A (Dataverse): a generative page would call the custom API <prefix>_<ToolInPascalCase> and parse
// resultJson. Binding C (Azure): the web app calls POST /api/<tool> with the same contract (apiClient.ts).
// The harness answers from fixture JSON (fixtureClient.ts). Whether these components run inside a generative
// page unchanged is UNVERIFIED; on binding A they carry over at least as specs.
//
// Page calls never throw for expected failures. A failure comes back as an envelope with ok false,
// and each panel shows its own failure while the other panels keep working.
import { createContext, useContext, useEffect, useState } from 'react';
import type { Envelope, ToolData, ToolName } from './contracts';

export interface ToolClient {
  call<N extends ToolName>(tool: N, input: Record<string, unknown>): Promise<Envelope<ToolData[N]>>;
}

export const ToolClientContext = createContext<ToolClient | null>(null);

export type ToolState<T> =
  | { status: 'loading' }
  | { status: 'done'; envelope: Envelope<T> };

// Calls one tool and keeps its envelope. The input object is compared by value, so callers may build it inline.
export function useTool<N extends ToolName>(tool: N, input: Record<string, unknown> | null): ToolState<ToolData[N]> {
  const client = useContext(ToolClientContext);
  const inputKey = input === null ? null : JSON.stringify(input);
  const [state, setState] = useState<ToolState<ToolData[N]>>({ status: 'loading' });

  useEffect(() => {
    if (!client || inputKey === null) return;
    let live = true;
    setState({ status: 'loading' });
    client
      .call(tool, JSON.parse(inputKey) as Record<string, unknown>)
      .then(envelope => { if (live) setState({ status: 'done', envelope }); })
      .catch((e: unknown) => {
        if (!live) return;
        // A transport fault is shown the same way as an upstream failure.
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
  }, [client, tool, inputKey]);

  return state;
}

// The data of a state when it succeeded, otherwise null.
export function dataOf<T>(state: ToolState<T>): T | null {
  return state.status === 'done' && state.envelope.ok ? state.envelope.data : null;
}
