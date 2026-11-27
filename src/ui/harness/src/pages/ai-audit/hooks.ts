// Reads for the AI activity, Agents and Audit pages beyond data/reads.ts:
//   useEachRead(tool, inputs)   one read of the same tool per input, in parallel, such as one application card per agent.
//                               Each answer arrives on its own; a read asked again after a write keeps its last answer
//                               on screen until the new one comes, as useTool does.
import { useEffect, useRef, useState } from 'react';
import type { Envelope, ToolData, ToolInput, ToolName } from '../../data/contracts';
import { useToolClient, type ToolState } from '../../data/toolClient';

const LOADING = { status: 'loading' } as const;

export function useEachRead<N extends ToolName>(tool: N, inputs: ToolInput<N>[] | null): ToolState<ToolData[N]>[] {
  const client = useToolClient();
  const key = inputs === null ? null : JSON.stringify(inputs);
  const [answer, setAnswer] = useState<{ question: string; states: ToolState<ToolData[N]>[] } | null>(null);
  const asked = useRef<string | null>(null);

  useEffect(() => {
    if (!client || key === null) return;
    let live = true;
    const list = JSON.parse(key) as Record<string, unknown>[];
    const question = `${tool}\n${key}`;
    if (asked.current !== question) setAnswer({ question, states: list.map(() => LOADING) });
    asked.current = question;
    // At most four calls in flight (product review 2026-11-24): a page of many agents read every agent at once and
    // reached the per-caller request limit, which every demo persona shares with the operator.
    let next = 0;
    const work = () => {
      if (!live || next >= list.length) return;
      const i = next++;
      client
        .call(tool, list[i])
        .catch((e: unknown): Envelope<ToolData[N]> => ({
          ok: false,
          error: { code: 'upstream_unavailable', message: e instanceof Error ? e.message : 'The call did not complete.' },
          correlationId: 'client-side',
        }))
        .then(envelope => {
          if (!live) return;
          setAnswer(prev => {
            const states = prev && prev.question === question ? [...prev.states] : list.map((): ToolState<ToolData[N]> => LOADING);
            states[i] = { status: 'done', envelope };
            return { question, states };
          });
          work();
        });
    };
    for (let k = 0; k < Math.min(4, list.length); k++) work();
    return () => { live = false; };
  }, [client, tool, key]);

  if (key === null || !inputs) return [];
  const question = `${tool}\n${key}`;
  return answer && answer.question === question ? answer.states : inputs.map((): ToolState<ToolData[N]> => LOADING);
}
