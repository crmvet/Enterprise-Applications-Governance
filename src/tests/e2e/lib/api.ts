// The checks behind the pages: tool calls on the REST API as a persona, and Claude Code's calls on the MCP endpoint.
//
// REST (POST /api/{tool}): on a local run as the development identity demo-<name>; on a Demo install with the
// operator's bearer token and the persona header. MCP (POST /mcp): only on a local run, as a development identity the
// endpoint knows (priya for Claude Code acting for Priya Castell, config-admin for S-10.6), as claude-code.md says.
import { env } from './env';
import type { Persona } from './personas';

export class ToolRefused extends Error {
  code: string | null;
  status: number;
  constructor(tool: string, status: number, error: { code?: string; message?: string } | null | undefined) {
    super(`${tool} answered ${status} ${error?.code ?? 'no-envelope'}: ${error?.message ?? ''}`.trim());
    this.code = error?.code ?? null;
    this.status = status;
  }
}

export interface Envelope<T = any> {
  ok: boolean;
  data?: T;
  error?: { code?: string; message?: string; [k: string]: unknown } | null;
  warnings?: { code?: string; message?: string }[];
  summary?: string;
  [k: string]: unknown;
}

const sleep = (ms: number) => new Promise(r => setTimeout(r, ms));

// When each persona's REST calls were sent, the suite's and the page's alike: the server counts both against the
// person's request limit (Product:Limits:ApiPerMinute), so the suite paces itself by it (Demo.pace).
export const callLog = new Map<string, number[]>();
export function logCall(persona: string) {
  const list = callLog.get(persona) ?? [];
  const now = Date.now();
  list.push(now);
  while (list.length && list[0] < now - 60000) list.shift();
  callLog.set(persona, list);
}

// One tool call on the REST API as a persona: the HTTP status and the envelope.
export async function apiCall(persona: Persona, tool: string, input: object = {}): Promise<{ status: number; body: Envelope | null }> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' };
  if (env.mode === 'dev') headers['X-Product-Dev-Identity'] = `demo-${persona}`;
  else {
    headers.Authorization = `Bearer ${env.token}`;
    headers['X-Product-Demo-Persona'] = persona;
  }
  for (let attempt = 0; ; attempt++) {
    logCall(persona);
    const r = await fetch(`${env.origin}/api/${tool}`, { method: 'POST', headers, body: JSON.stringify(input), signal: AbortSignal.timeout(60000) });
    if (r.status === 429 && attempt < 3) {
      await sleep(Math.min(30, Number(r.headers.get('retry-after')) || 3) * 1000);
      continue;
    }
    const body = (await r.json().catch(() => null)) as Envelope | null;
    return { status: r.status, body };
  }
}

// The data of a tool's answer, or a ToolRefused.
export async function apiData<T = any>(persona: Persona, tool: string, input: object = {}): Promise<T> {
  const r = await apiCall(persona, tool, input);
  if (!r.body?.ok) throw new ToolRefused(tool, r.status, r.body?.error);
  return r.body.data as T;
}

// Every item of a paged list.
export async function apiAll<T = any>(persona: Persona, tool: string, input: object = {}): Promise<T[]> {
  const items: T[] = [];
  let skipToken: string | undefined;
  do {
    const data = await apiData<any>(persona, tool, { ...input, top: 100, ...(skipToken ? { skipToken } : {}) });
    items.push(...(data?.items ?? []));
    skipToken = data?.skipToken ?? undefined;
  } while (skipToken && items.length < 2000);
  return items;
}

// One tool call on the MCP endpoint as a development identity (a local run only). Answers the envelope the tool's
// structured content carries, or the error's data.
let rpcId = 0;
export async function mcpCall(tool: string, input: object = {}, identity = 'priya'): Promise<Envelope> {
  const r = await fetch(`${env.origin}/mcp`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json, text/event-stream', 'X-Product-Dev-Identity': identity },
    body: JSON.stringify({ jsonrpc: '2.0', id: ++rpcId, method: 'tools/call', params: { name: tool, arguments: input } }),
    signal: AbortSignal.timeout(60000),
  });
  const body = (await r.json().catch(() => null)) as any;
  return body?.result?.structuredContent ?? body?.error?.data ?? { ok: false, error: { code: 'no-envelope', message: `HTTP ${r.status}` } };
}

// tools/list on the MCP endpoint: the names of the tools the identity may call.
export async function mcpTools(identity = 'priya'): Promise<string[]> {
  const r = await fetch(`${env.origin}/mcp`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json, text/event-stream', 'X-Product-Dev-Identity': identity },
    body: JSON.stringify({ jsonrpc: '2.0', id: ++rpcId, method: 'tools/list', params: {} }),
    signal: AbortSignal.timeout(60000),
  });
  const body = (await r.json().catch(() => null)) as any;
  return (body?.result?.tools ?? []).map((t: { name: string }) => t.name);
}

// Names to IDs, from the install's own register, as the Demo guide finds them. A cache per worker.
const ids = new Map<string, string>();
export async function appId(name: string): Promise<string> {
  const key = `app:${name}`;
  if (!ids.has(key)) {
    const found = (await apiData<any>('owen', 'find_applications', { text: name, top: 25 })).items.find((a: any) => a.name === name);
    if (!found) throw new Error(`The register has no application named ${name}.`);
    ids.set(key, String(found.businessApplicationId).toLowerCase());
  }
  return ids.get(key)!;
}

export async function hasApp(name: string): Promise<boolean> {
  const items = (await apiData<any>('owen', 'find_applications', { text: name, top: 25 })).items ?? [];
  return items.some((a: any) => a.name === name);
}

export async function portfolioId(name: string): Promise<string> {
  const key = `portfolio:${name}`;
  if (!ids.has(key)) {
    const found = (await apiData<any>('owen', 'list_portfolios', {})).items.find((p: any) => p.name === name);
    if (!found) throw new Error(`The install has no portfolio named ${name}.`);
    ids.set(key, String(found.portfolioId).toLowerCase());
  }
  return ids.get(key)!;
}

// The decision with this statement (or the first whose statement starts with it), through the decision log, and its
// detail (get_decision). Null when there is none.
export async function decisionByStatement(persona: Persona, statement: string): Promise<any | null> {
  const log = await apiAll<any>(persona, 'get_decision_log', {});
  const found = log.find(d => d.statement === statement) ?? log.find(d => String(d.statement ?? '').startsWith(statement));
  if (!found) return null;
  return apiData(persona, 'get_decision', { decisionId: found.decisionId });
}

// An intake request by its title, through the persona's intake list (any view the persona may read).
export async function intakeByTitle(persona: Persona, title: string, view = 'all'): Promise<any | null> {
  const items = await apiAll<any>(persona, 'list_intake_requests', { view }).catch(() => [] as any[]);
  return items.find(i => i.title === title) ?? null;
}

// A day counted from today (UTC), as YYYY-MM-DD.
export const day = (offset: number) => new Date(Date.now() + offset * 86400000).toISOString().slice(0, 10);
