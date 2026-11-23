// The harness tool client: answers each call from the fixture JSON under harness/fixtures/<tool>/.
// A fixture is a pair: <case>.input.json (the tool input) and <case>.output.json (the envelope).
// tools/json-check.mjs validates both halves against the MCP tool schemas. All data is fictional.
//
// Harness switches in the page address, for design review only:
//   ?fail=get_change_schedule,get_deployments   answer those tools with an upstream_unavailable failure
//   ?slow=1                                       answer after 1.5 seconds, to show the loading state
import type { Envelope, ToolData, ToolName } from './contracts';
import type { ToolClient } from './toolClient';

interface FixturePair {
  name: string;
  input: unknown;
  output: unknown;
}

const files = import.meta.glob('../../fixtures/*/*.json', { eager: true, import: 'default' }) as Record<string, unknown>;

const byTool = new Map<string, FixturePair[]>();
for (const [file, json] of Object.entries(files)) {
  const m = /fixtures\/([a-z_]+)\/(.+)\.(input|output)\.json$/.exec(file);
  if (!m) continue;
  const [, tool, name, side] = m;
  const list = byTool.get(tool) ?? [];
  let pair = list.find(p => p.name === name);
  if (!pair) {
    pair = { name, input: undefined, output: undefined };
    list.push(pair);
    byTool.set(tool, list);
  }
  if (side === 'input') pair.input = json;
  else pair.output = json;
}

// Key order does not matter when matching an input.
function canonical(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(canonical).join(',')}]`;
  if (value && typeof value === 'object') {
    const entries = Object.entries(value as Record<string, unknown>)
      .filter(([, v]) => v !== undefined)
      .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0));
    return `{${entries.map(([k, v]) => `${JSON.stringify(k)}:${canonical(v)}`).join(',')}}`;
  }
  return JSON.stringify(value);
}

declare global {
  interface Window {
    __harnessPending?: number;
    __harnessCalls?: number;
  }
}

const params = new URLSearchParams(window.location.search);
const failing = new Set((params.get('fail') ?? '').split(',').map(s => s.trim()).filter(Boolean));
const delayMs = params.get('slow') === '1' ? 1500 : 80;
let calls = 0;

export const fixtureClient: ToolClient = {
  call<N extends ToolName>(tool: N, input: Record<string, unknown>): Promise<Envelope<ToolData[N]>> {
    calls += 1;
    const correlationId = `harness-call-${String(calls).padStart(4, '0')}`;
    window.__harnessCalls = (window.__harnessCalls ?? 0) + 1;
    window.__harnessPending = (window.__harnessPending ?? 0) + 1;

    let answer: Envelope<ToolData[N]>;
    if (failing.has(tool)) {
      answer = {
        ok: false,
        summary: 'The product could not reach its data source.',
        error: { code: 'upstream_unavailable', message: 'The data source did not answer in time. Try again in a minute.' },
        correlationId,
      };
    } else {
      const wanted = canonical(input);
      const pair = (byTool.get(tool) ?? []).find(p => canonical(p.input) === wanted);
      answer = pair
        ? (pair.output as Envelope<ToolData[N]>)
        : {
            ok: false,
            summary: 'Nothing was found for this request.',
            error: { code: 'not_found', message: 'No record matches this request, or you cannot see it.' },
            correlationId,
          };
    }

    return new Promise(resolve => {
      window.setTimeout(() => {
        window.__harnessPending = (window.__harnessPending ?? 1) - 1;
        resolve(answer);
      }, delayMs);
    });
  },
};
