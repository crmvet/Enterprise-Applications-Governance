// The web app's tool client: the same ToolClient the pages use in the harness, over the product's REST API.
//
//   POST <apiBase>/<tool>   one read tool, the JSON body is the tool's input. The body of every answer is the tool's
//                           envelope, on every status (200, 401, 403, 404, 429, 5xx).
//   GET  <apiBase>/me       the signed-in caller: object ID, product roles and the group that grants each, and, once
//                           the host has the web app's person entry, the name and the actor kind.
//   GET  <apiBase>/info     the service: engine version, MCP endpoint and protocol, configuration in force.
//
// Rules:
// - Headers from the sign-in (the bearer token, or the development persona) go only to apiBase. No cookies are sent.
// - 401 starts sign-in again (AuthSession.onUnauthorized) and still answers the panel with the envelope.
// - A timeout, a network fault or an answer that is not an envelope becomes a failure envelope, so the panel shows it
//   like any other failure and the rest of the page keeps working. Nothing here throws to a page.
import { SignInUnavailable, type AuthSession } from '../auth/types';
import type { Envelope, ErrorCode, Failure, IsoDateTime, ToolData, ToolName } from './contracts';
import type { ToolClient } from './toolClient';

export interface MeRole {
  roleKey: string;
  label: string;
  viaGroup: { objectId: string; displayLabel: string } | null;
}

export interface Me {
  objectId: string;
  name: string | null;
  // "person" through the web app's own entry; "ai-client-for-person" before the host has it.
  actorKind: string | null;
  roles: MeRole[];
  configurationSetKey: string | null;
  configurationSetVersion: number | null;
  asOf: IsoDateTime | null;
  correlationId: string | null;
}

export interface ServiceInfo {
  serverName: string | null;
  engineVersion: string | null;
  mcpEndpoint: string | null;
  mcpProtocolVersion: string | null;
  configuration: { configurationSetKey: string; versionNumber: number } | null;
  asOf: IsoDateTime | null;
}

export type Answer<T> = { ok: true; value: T } | { ok: false; failure: Failure };

export interface ApiClient extends ToolClient {
  me(): Promise<Answer<Me>>;
  info(): Promise<Answer<ServiceInfo>>;
}

export interface ApiClientOptions {
  // An absolute URL with no trailing slash, from a validated config.json.
  apiBase: string;
  auth: Pick<AuthSession, 'headers' | 'onUnauthorized'>;
  fetch: typeof fetch;
  timeoutMs?: number;
}

export const CORRELATION_HEADER = 'X-Correlation-Id';
const CLIENT_SIDE = 'client-side';
const TOOL = /^[a-z][a-z0-9_]{0,63}$/;

const STATUS_CODES: Record<number, ErrorCode> = {
  400: 'validation',
  401: 'not_signed_in',
  403: 'not_authorized',
  404: 'not_found',
  409: 'conflict',
  422: 'rule_violation',
  429: 'rate_limited',
};

const STATUS_TEXT: Partial<Record<ErrorCode, string>> = {
  validation: 'The product could not read this request.',
  not_signed_in: 'Sign in to see this section.',
  not_authorized: 'You cannot see this section.',
  not_found: 'The product does not serve this request.',
  conflict: 'The record changed. Reload the page.',
  rule_violation: 'A rule refused this request.',
  rate_limited: 'Too many requests. Wait a minute and try again.',
};

export function failureOf(code: ErrorCode, message: string, correlationId = CLIENT_SIDE, retryAfterSeconds?: number): Failure {
  return {
    ok: false,
    summary: message,
    error: retryAfterSeconds === undefined ? { code, message } : { code, message, retryAfterSeconds },
    correlationId,
  };
}

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const str = (v: unknown): string | null => (typeof v === 'string' ? v : null);
const num = (v: unknown): number | null => (typeof v === 'number' && Number.isFinite(v) ? v : null);

// The envelope shape of envelope.schema.json, checked loosely: the pages read the rest by contract.
export function isEnvelope(value: unknown): value is Envelope<unknown> {
  if (!isObject(value) || typeof value.ok !== 'boolean' || typeof value.correlationId !== 'string') return false;
  return value.ok ? 'data' in value : isObject(value.error) && typeof value.error.code === 'string' && typeof value.error.message === 'string';
}

interface Raw {
  status: number;
  body: unknown;
  correlationId: string;
  retryAfter?: number;
}

function retryAfterOf(response: Response): number | undefined {
  const value = Number(response.headers.get('Retry-After'));
  return Number.isInteger(value) && value >= 0 ? value : undefined;
}

// A failure for an answer whose body is not an envelope (a proxy page, an empty body).
function statusFailure(raw: Raw): Failure {
  const code = STATUS_CODES[raw.status] ?? 'upstream_unavailable';
  const message = STATUS_TEXT[code] ?? `The product could not answer (HTTP ${raw.status}). Try again in a minute.`;
  return failureOf(code, message, raw.correlationId, raw.retryAfter);
}

export function createApiClient(options: ApiClientOptions): ApiClient {
  const timeoutMs = options.timeoutMs ?? 30_000;
  const base = options.apiBase.replace(/\/+$/, '');

  async function send(method: 'GET' | 'POST', path: string, body?: unknown): Promise<Raw | Failure> {
    let auth: Record<string, string>;
    try {
      auth = await options.auth.headers();
    } catch (e) {
      if (e instanceof SignInUnavailable) return failureOf('upstream_unavailable', e.message);
      return failureOf('not_signed_in', e instanceof Error && e.message ? e.message : STATUS_TEXT.not_signed_in!);
    }

    const controller = new AbortController();
    let timedOut = false;
    const timer = setTimeout(() => {
      timedOut = true;
      controller.abort();
    }, timeoutMs);
    try {
      const headers: Record<string, string> = { Accept: 'application/json', ...auth };
      if (body !== undefined) headers['Content-Type'] = 'application/json';
      const response = await options.fetch(`${base}/${path}`, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body),
        signal: controller.signal,
        credentials: 'omit',
        cache: 'no-store',
        redirect: 'error',
      });
      const text = await response.text();
      let parsed: unknown = null;
      if (text) {
        try {
          parsed = JSON.parse(text);
        } catch {
          parsed = null;
        }
      }
      const raw: Raw = {
        status: response.status,
        body: parsed,
        correlationId: response.headers.get(CORRELATION_HEADER) ?? (isObject(parsed) ? str(parsed.correlationId) : null) ?? CLIENT_SIDE,
        retryAfter: retryAfterOf(response),
      };
      if (response.status === 401) options.auth.onUnauthorized();
      return raw;
    } catch {
      return timedOut
        ? failureOf('upstream_unavailable', `The product did not answer within ${Math.round(timeoutMs / 1000)} seconds. Try again in a minute.`)
        : failureOf('upstream_unavailable', 'The product could not be reached. Check the connection and try again.');
    } finally {
      clearTimeout(timer);
    }
  }

  const isFailure = (r: Raw | Failure): r is Failure => 'ok' in r && r.ok === false;

  async function read<T>(path: string, parse: (body: Record<string, unknown>) => T | null): Promise<Answer<T>> {
    const r = await send('GET', path);
    if (isFailure(r)) return { ok: false, failure: r };
    if (r.status === 200 && isObject(r.body)) {
      const value = parse(r.body);
      if (value !== null) return { ok: true, value };
    }
    if (isEnvelope(r.body) && !r.body.ok) return { ok: false, failure: r.body };
    return { ok: false, failure: statusFailure(r) };
  }

  return {
    async call<N extends ToolName>(tool: N, input: Record<string, unknown>): Promise<Envelope<ToolData[N]>> {
      if (!TOOL.test(tool)) return failureOf('validation', 'The page asked for a tool name the product does not use.');
      const r = await send('POST', encodeURIComponent(tool), input);
      if (isFailure(r)) return r;
      if (isEnvelope(r.body)) return r.body as Envelope<ToolData[N]>;
      return statusFailure(r);
    },

    me: () => read<Me>('me', body => {
      const objectId = str(body.objectId);
      if (!objectId) return null;
      const roles: MeRole[] = Array.isArray(body.roles)
        ? body.roles.filter(isObject).map(r => ({
          roleKey: str(r.roleKey) ?? '',
          label: str(r.label) ?? str(r.roleKey) ?? '',
          viaGroup: isObject(r.viaGroup) && str(r.viaGroup.objectId)
            ? { objectId: str(r.viaGroup.objectId)!, displayLabel: str(r.viaGroup.displayLabel) ?? '' }
            : null,
        })).filter(r => r.roleKey)
        : [];
      return {
        objectId,
        name: str(body.name),
        actorKind: str(body.actorKind),
        roles,
        configurationSetKey: str(body.configurationSetKey),
        configurationSetVersion: num(body.configurationSetVersion),
        asOf: str(body.asOf),
        correlationId: str(body.correlationId),
      };
    }),

    info: () => read<ServiceInfo>('info', body => {
      const c = isObject(body.configuration) ? body.configuration : null;
      return {
        serverName: str(body.serverName),
        engineVersion: str(body.engineVersion),
        mcpEndpoint: str(body.mcpEndpoint),
        mcpProtocolVersion: str(body.mcpProtocolVersion),
        configuration: c && str(c.configurationSetKey) && num(c.versionNumber) !== null
          ? { configurationSetKey: str(c.configurationSetKey)!, versionNumber: num(c.versionNumber)! }
          : null,
        asOf: str(body.asOf),
      };
    }),
  };
}
