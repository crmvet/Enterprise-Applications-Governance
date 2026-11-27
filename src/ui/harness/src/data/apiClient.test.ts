// The API client against a fake fetch: what it sends, where its headers go, and how every kind of answer reaches a
// panel as an envelope.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { SignInRequired, SignInUnavailable } from '../auth/types';
import { createApiClient, isEnvelope, LOCAL_DAY_HEADER, type ApiClientOptions } from './apiClient';
import { todayInZone } from './format';

const BASE = 'https://product.example.org/api';

interface Sent {
  url: string;
  init: RequestInit;
}

function json(status: number, body: unknown, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } });
}

function setup(
  answer: (sent: Sent) => Response | Promise<Response>,
  auth: Partial<ApiClientOptions['auth']> = {},
  timeoutMs?: number,
  extraHeaders?: ApiClientOptions['extraHeaders'],
) {
  const sent: Sent[] = [];
  let unauthorized = 0;
  const fetchFake = (async (url: RequestInfo | URL, init?: RequestInit) => {
    const call = { url: String(url), init: init ?? {} };
    sent.push(call);
    return answer(call);
  }) as typeof fetch;
  const client = createApiClient({
    apiBase: BASE,
    fetch: fetchFake,
    timeoutMs,
    extraHeaders,
    auth: {
      headers: auth.headers ?? (async () => ({ Authorization: 'Bearer token-for-the-api' })),
      onUnauthorized: auth.onUnauthorized ?? (() => { unauthorized += 1; }),
    },
  });
  return { client, sent, unauthorized: () => unauthorized };
}

const success = {
  ok: true,
  summary: 'Two buckets.',
  data: { buckets: [], ownerGaps: 0, reviewsOverdue: 0, documentsMissingOrStale: 0, draftsOlderThanDays: 0 },
  asOf: '2026-11-23T09:00:00Z',
  configurationSetVersion: 1,
  couldNotRead: [],
  warnings: [],
  untrustedFields: [],
  nextSteps: [],
  correlationId: 'corr-1',
};

const refusal = (code: string, message: string) => ({ ok: false, summary: message, error: { code, message }, correlationId: 'corr-2' });

test('a tool call posts the input as JSON to <apiBase>/<tool> with the sign-in headers and no cookies', async () => {
  const { client, sent } = setup(() => json(200, success));
  const envelope = await client.call('get_portfolio_summary', { groupBy: 'tier' });
  assert.equal(envelope.ok, true);
  assert.equal(sent.length, 1);
  assert.equal(sent[0].url, `${BASE}/get_portfolio_summary`);
  assert.equal(sent[0].init.method, 'POST');
  assert.equal(sent[0].init.body, JSON.stringify({ groupBy: 'tier' }));
  assert.equal(sent[0].init.credentials, 'omit');
  const headers = sent[0].init.headers as Record<string, string>;
  assert.equal(headers.Authorization, 'Bearer token-for-the-api');
  assert.equal(headers['Content-Type'], 'application/json');
});

test('every call carries the local day of the person, so a date the server stamps is their day (B24-21)', async () => {
  const { client, sent } = setup(() => json(200, success));
  await client.call('confirm_document_checked', { documentLinkId: '00000000-0000-4000-8000-000000000541', stillTrue: true });
  await client.me();
  for (const s of sent) {
    const headers = s.init.headers as Record<string, string>;
    assert.equal(headers[LOCAL_DAY_HEADER], todayInZone());
    assert.match(headers[LOCAL_DAY_HEADER], /^\d{4}-\d{2}-\d{2}$/);
  }
});

test('the bearer token only ever goes to the API base', async () => {
  const { client, sent } = setup(() => json(200, { objectId: '00000000-0000-4000-8000-000000000212', roles: [] }));
  await client.call('get_deployments', {});
  await client.me();
  await client.info();
  assert.equal(sent.length, 3);
  for (const s of sent) assert.ok(s.url.startsWith(`${BASE}/`), s.url);
});

test('a refusal envelope passes through on its HTTP status', async () => {
  const { client } = setup(() => json(403, refusal('not_authorized', 'You lack the role.')));
  const envelope = await client.call('get_application_card', { businessApplicationId: 'x' });
  assert.equal(envelope.ok, false);
  if (!envelope.ok) {
    assert.equal(envelope.error.code, 'not_authorized');
    assert.equal(envelope.correlationId, 'corr-2');
  }
});

test('401 answers the panel with the envelope and starts sign-in again', async () => {
  const { client, unauthorized } = setup(() => json(401, refusal('not_signed_in', 'Sign in with a bearer token.'), { 'WWW-Authenticate': 'Bearer' }));
  const envelope = await client.call('get_change_schedule', {});
  assert.equal(envelope.ok, false);
  if (!envelope.ok) assert.equal(envelope.error.code, 'not_signed_in');
  assert.equal(unauthorized(), 1);
});

test('an answer that is not an envelope becomes a failure from its status, with the correlation header', async () => {
  const { client } = setup(() => new Response('<html>Bad gateway</html>', { status: 502, headers: { 'X-Correlation-Id': 'corr-proxy' } }));
  const envelope = await client.call('get_deployments', {});
  assert.equal(envelope.ok, false);
  if (!envelope.ok) {
    assert.equal(envelope.error.code, 'upstream_unavailable');
    assert.equal(envelope.correlationId, 'corr-proxy');
  }
});

test('429 without an envelope keeps Retry-After', async () => {
  const { client } = setup(() => new Response('', { status: 429, headers: { 'Retry-After': '30' } }));
  const envelope = await client.call('get_deployments', {});
  assert.equal(envelope.ok, false);
  if (!envelope.ok) {
    assert.equal(envelope.error.code, 'rate_limited');
    assert.equal(envelope.error.retryAfterSeconds, 30);
  }
});

test('a network fault becomes upstream_unavailable inside the panel', async () => {
  const { client } = setup(() => { throw new TypeError('Failed to fetch'); });
  const envelope = await client.call('get_dependency_impact', {});
  assert.equal(envelope.ok, false);
  if (!envelope.ok) {
    assert.equal(envelope.error.code, 'upstream_unavailable');
    assert.equal(envelope.correlationId, 'client-side');
  }
});

test('a timeout aborts the request and becomes upstream_unavailable', async () => {
  const { client } = setup(sent => new Promise<Response>((_, reject) => {
    sent.init.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')));
  }), {}, 20);
  const envelope = await client.call('get_configuration', { entity: 'owner-role' });
  assert.equal(envelope.ok, false);
  if (!envelope.ok) {
    assert.equal(envelope.error.code, 'upstream_unavailable');
    assert.match(envelope.error.message, /did not answer/);
  }
});

test('no sign-in sends nothing and answers not_signed_in', async () => {
  const { client, sent } = setup(() => json(200, success), { headers: async () => { throw new SignInRequired('Choose a development persona to see this section.'); } });
  const envelope = await client.call('get_portfolio_summary', {});
  assert.equal(sent.length, 0);
  assert.equal(envelope.ok, false);
  if (!envelope.ok) assert.equal(envelope.error.code, 'not_signed_in');
});

test('an unreachable sign-in service answers upstream_unavailable', async () => {
  const { client, sent } = setup(() => json(200, success), { headers: async () => { throw new SignInUnavailable(); } });
  const envelope = await client.call('get_portfolio_summary', {});
  assert.equal(sent.length, 0);
  assert.equal(envelope.ok, false);
  if (!envelope.ok) assert.equal(envelope.error.code, 'upstream_unavailable');
});

test('a tool name the product does not use is refused without a request', async () => {
  const { client, sent } = setup(() => json(200, success));
  const envelope = await client.call('../me' as 'get_deployments', {});
  assert.equal(sent.length, 0);
  assert.equal(envelope.ok, false);
});

test('me reads the roles, and the name and actor kind when the host sends them', async () => {
  const { client, sent } = setup(() => json(200, {
    objectId: '00000000-0000-4000-8000-000000000212',
    name: 'Nadia Ferreira',
    actorKind: 'person',
    roles: [{ roleKey: 'application-owner', label: 'Application owner', viaGroup: { objectId: '00000000-0000-4000-8000-000000000103', displayLabel: 'Application owners' } }],
    configurationSetKey: 'balanced',
    configurationSetVersion: 1,
    asOf: '2026-11-23T09:00:00Z',
    correlationId: 'corr-me',
  }));
  const answer = await client.me();
  assert.equal(sent[0].url, `${BASE}/me`);
  assert.equal(sent[0].init.method, 'GET');
  assert.equal(answer.ok, true);
  if (answer.ok) {
    assert.equal(answer.value.name, 'Nadia Ferreira');
    assert.equal(answer.value.actorKind, 'person');
    assert.equal(answer.value.roles.length, 1);
    assert.equal(answer.value.roles[0].viaGroup?.displayLabel, 'Application owners');
  }
});

test('me without a name or actor kind still reads', async () => {
  const { client } = setup(() => json(200, { objectId: '00000000-0000-4000-8000-000000000211', roles: [] }));
  const answer = await client.me();
  assert.equal(answer.ok, true);
  if (answer.ok) {
    assert.equal(answer.value.name, null);
    assert.equal(answer.value.actorKind, null);
  }
});

test('a refused me answers its envelope', async () => {
  const { client } = setup(() => json(403, refusal('not_authorized', 'No product role.')));
  const answer = await client.me();
  assert.equal(answer.ok, false);
  if (!answer.ok) assert.equal(answer.failure.error.code, 'not_authorized');
});

test('info reads the configuration in force', async () => {
  const { client } = setup(() => json(200, {
    serverName: 'product-mcp',
    engineVersion: '0.9.0',
    mcpEndpoint: 'https://product.example.org/mcp',
    mcpTransport: 'core',
    mcpProtocolVersion: '2025-11-25',
    configuration: { configurationSetKey: 'balanced', versionNumber: 1, contentHash: 'abc' },
    asOf: '2026-11-23T09:00:00Z',
    correlationId: 'corr-info',
  }));
  const answer = await client.info();
  assert.equal(answer.ok, true);
  if (answer.ok) assert.deepEqual(answer.value.configuration, { configurationSetKey: 'balanced', versionNumber: 1 });
});

test('isEnvelope tells envelopes from other JSON', () => {
  assert.equal(isEnvelope(success), true);
  assert.equal(isEnvelope(refusal('not_found', 'Nothing.')), true);
  assert.equal(isEnvelope({ ok: true }), false);
  assert.equal(isEnvelope({ error: 'x' }), false);
  assert.equal(isEnvelope(null), false);
});

test('a write posts its input to <apiBase>/<tool> the same way, and a refusal comes back with its rule and guideline', async () => {
  const input = { approvalRecordId: '00000000-0000-4000-8000-000000000a01', answer: 'reject', reason: 'The data use goes beyond the notice.', clientRequestId: 'web-1' };
  const { client, sent } = setup(() => json(422, {
    ok: false,
    summary: 'Refused.',
    error: { code: 'rule_violation', message: 'The separation rule keeps your answer from counting.', ruleKey: 'self-approval-tier-3', guideline: 'G17' },
    correlationId: 'corr-write',
  }));
  const envelope = await client.call('answer_approval', input);
  assert.equal(sent[0].url, `${BASE}/answer_approval`);
  assert.equal(sent[0].init.method, 'POST');
  assert.equal(sent[0].init.body, JSON.stringify(input));
  assert.equal(envelope.ok, false);
  if (!envelope.ok) {
    assert.equal(envelope.error.ruleKey, 'self-approval-tier-3');
    assert.equal(envelope.error.guideline, 'G17');
    assert.equal(envelope.correlationId, 'corr-write');
  }
});

test('the demo persona header goes to the API base on every call and never replaces a sign-in header', async () => {
  let persona: string | null = 'nadia';
  const { client, sent } = setup(
    () => json(200, success),
    {},
    undefined,
    (): Record<string, string> => (persona ? { 'X-Product-Demo-Persona': persona, Authorization: 'Bearer forged' } : {}),
  );
  await client.call('get_my_queue', {});
  await client.me();
  persona = null;
  await client.info();
  const headers = sent.map(s => s.init.headers as Record<string, string>);
  assert.equal(headers[0]['X-Product-Demo-Persona'], 'nadia');
  assert.equal(headers[1]['X-Product-Demo-Persona'], 'nadia');
  assert.equal(headers[2]['X-Product-Demo-Persona'], undefined);
  for (const h of headers) assert.equal(h.Authorization, 'Bearer token-for-the-api');
  for (const s of sent) assert.ok(s.url.startsWith(`${BASE}/`), s.url);
});

test('me names the demo persona the request ran as, and null without one', async () => {
  const { client } = setup(() => json(200, {
    objectId: '00000000-0000-4000-8000-000000000212',
    name: 'Nadia Ferreira',
    actorKind: 'person',
    demoPersona: { name: 'nadia', label: 'Nadia Ferreira, application owner' },
    roles: [],
  }));
  const answer = await client.me();
  assert.equal(answer.ok && answer.value.demoPersona?.name, 'nadia');
  assert.equal(answer.ok && answer.value.demoPersona?.label, 'Nadia Ferreira, application owner');
  const plain = setup(() => json(200, { objectId: '00000000-0000-4000-8000-000000000212', roles: [], demoPersona: null }));
  const none = await plain.client.me();
  assert.equal(none.ok && none.value.demoPersona, null);
});
