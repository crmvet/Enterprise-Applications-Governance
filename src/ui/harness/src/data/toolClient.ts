// The one door between a page and the product's tools.
//
// A page asks for a tool by its document 24 name with the tool's input, and gets the response envelope back.
// Binding A (Dataverse): a generative page would call the custom API <prefix>_<ToolInPascalCase> and parse
// resultJson. Binding C (Azure): the web app calls POST /api/<tool> with the same contract (apiClient.ts), for the reads
// and, on the person entry, for the writes (D-36). The harness answers from fixture JSON (fixtureClient.ts). Whether
// these components run inside a generative page unchanged is UNVERIFIED; on binding A they carry over at least as specs.
//
// Page calls never throw for expected failures. A failure comes back as an envelope with ok false,
// and each panel shows its own failure while the other panels keep working.
import { createContext, useContext, useEffect, useRef, useState } from 'react';
import type { Envelope, ToolData, ToolName } from './contracts';

export interface ToolClient {
  call<N extends ToolName>(tool: N, input: Record<string, unknown>): Promise<Envelope<ToolData[N]>>;
}

export const ToolClientContext = createContext<ToolClient | null>(null);

// The tool client of the page, or null outside a host.
export function useToolClient(): ToolClient | null {
  return useContext(ToolClientContext);
}

// Reads the page's data again after a write, in place: every useTool on the page asks again and keeps showing what it
// had until the new answer comes. The web app's shell provides it; elsewhere it does nothing.
export const RefreshContext = createContext<() => void>(() => {});

export function useRefresh(): () => void {
  return useContext(RefreshContext);
}

export type ToolState<T> =
  | { status: 'loading' }
  | { status: 'done'; envelope: Envelope<T> };

// True when the read answered with an error envelope (refused, limited or failed), for a figure that must not stay on
// "loading" for good.
export function readFailed(...states: (ToolState<unknown> | null | undefined)[]): boolean {
  return states.some(state => !!state && state.status === 'done' && !state.envelope.ok);
}

// Calls one tool and keeps its envelope. The input object is compared by value, so callers may build it inline.
// A new tool or input shows the loading state. The same tool and input asked again (a refresh after a write) keep the
// last answer on screen until the new one arrives, so a panel does not flash.
export function useTool<N extends ToolName>(tool: N, input: Record<string, unknown> | null): ToolState<ToolData[N]> {
  const client = useContext(ToolClientContext);
  const inputKey = input === null ? null : JSON.stringify(input);
  const [state, setState] = useState<ToolState<ToolData[N]>>({ status: 'loading' });
  const asked = useRef<string | null>(null);

  useEffect(() => {
    if (!client || inputKey === null) return;
    let live = true;
    const question = `${tool}\n${inputKey}`;
    if (asked.current !== question) setState({ status: 'loading' });
    asked.current = question;
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

// A client that shares and keeps reads for a short time (product review, fix round 1): a page asked the same read many times
// (Demand sent get_configuration 39 times on one load) and met the per-person request limit. The same read tool with
// the same input, asked while the first is on its way or within ttlMs after it answered, gets the same answer from one
// call. A failed answer is never kept. Any write empties it, and the shell makes a new client (an empty cache) after a
// write, on the refresh button and for a new persona, so a page never shows rows a write changed.
export function cachingClient(inner: ToolClient, isWrite: (tool: ToolName) => boolean, { ttlMs = 15000, now = () => Date.now() }: { ttlMs?: number; now?: () => number } = {}): ToolClient {
  const kept = new Map<string, { at: number; answer: Promise<unknown> }>();
  return {
    call<N extends ToolName>(tool: N, input: Record<string, unknown>): Promise<Envelope<ToolData[N]>> {
      if (isWrite(tool)) {
        kept.clear();
        return inner.call(tool, input);
      }
      const key = `${tool}|${JSON.stringify(input)}`;
      const hit = kept.get(key);
      if (hit && now() - hit.at <= ttlMs) return hit.answer as Promise<Envelope<ToolData[N]>>;
      const answer = inner.call(tool, input);
      const entry = { at: now(), answer: answer as Promise<unknown> };
      kept.set(key, entry);
      answer.then(
        envelope => { if (!envelope.ok && kept.get(key) === entry) kept.delete(key); },
        () => { if (kept.get(key) === entry) kept.delete(key); },
      );
      return answer;
    },
  };
}

// The data of a state when it succeeded, otherwise null.
export function dataOf<T>(state: ToolState<T>): T | null {
  return state.status === 'done' && state.envelope.ok ? state.envelope.data : null;
}
