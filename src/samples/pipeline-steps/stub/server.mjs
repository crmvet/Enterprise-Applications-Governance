#!/usr/bin/env node
// Local stub of the record endpoint, for the pipeline record step samples. A test double, never a service.
//
// - It validates every request body against the record endpoint contract (mcp/contracts/record-endpoint/)
//   with the pinned ajv in src/tools/node_modules. No new package.
// - It keeps idempotency keys in memory: the same key and body gives 200 with created false; the same key with
//   another body gives 409 conflict (mcp/contracts/record-endpoint.json, statuses).
// - It answers with the response envelope and validates its own answer against the response schema, so a
//   contract drift fails the test instead of passing silently.
// - It serves both platform branches: POST /api/deployments (and /corrections) as on the Azure binding, and
//   POST /api/data/v9.2/<prefix>_RecordDeployment (and ..._RecordDeploymentCorrection) as on the Dataverse
//   binding, which answers 200 with resultJson, recordId and statusMeaning (document 24, section 9.1).
// - It fakes the two token hops of the GitHub sample (GitHub's OIDC request URL and the Microsoft Entra token
//   endpoint), and refuses any token request that carries a client secret. So the test never leaves this
//   machine and proves that no secret is sent.
// - It runs no engine: route, flags and gate level in its answers are fixed stub values.
// - On request it answers with a redirect (redirectNext), so the test can prove that no redirect is followed.
//
// It listens on 127.0.0.1 only.
// Usage: node samples/pipeline-steps/stub/server.mjs [--port 7071]
// Needs "npm ci" in src/tools first (tools/ci.mjs does it).
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
export const SRC = path.resolve(HERE, '..', '..', '..');
const CONTRACTS = path.join(SRC, 'mcp', 'contracts');

// Fixed stub values. None of them is a real token.
export const STUB = Object.freeze({
  accessToken: 'stub-access-token-not-real',
  oidcRequestToken: 'stub-oidc-request-token-not-real',
  federatedAssertion: 'stub-federated-assertion-not-real',
  tokenExchangeAudience: 'api://AzureADTokenExchange',
  principalObjectId: 'e1000000-0000-4000-8000-000000000001',
});

const SCHEMAS = {
  deployment: {
    request: 'record-endpoint/deployment.request.schema.json',
    response: 'record-endpoint/deployment.response.schema.json',
  },
  correction: {
    request: 'record-endpoint/deployment-correction.request.schema.json',
    response: 'record-endpoint/deployment-correction.response.schema.json',
  },
};

const ROUTES = [
  { re: /^\/api\/deployments$/, operation: 'deployment', binding: 'azure' },
  { re: /^\/api\/deployments\/corrections$/, operation: 'correction', binding: 'azure' },
  { re: /^\/api\/data\/v9\.\d+\/[^/]+_RecordDeployment$/, operation: 'deployment', binding: 'dataverse' },
  { re: /^\/api\/data\/v9\.\d+\/[^/]+_RecordDeploymentCorrection$/, operation: 'correction', binding: 'dataverse' },
];

const HTTP_FOR_CODE = {
  validation: 400,
  not_signed_in: 401,
  not_authorized: 403,
  not_found: 404,
  conflict: 409,
  rule_violation: 422,
  rate_limited: 429,
  upstream_unavailable: 503,
  configuration_unavailable: 503,
};

const RESULT_LABELS = { succeeded: 'Succeeded', 'partly-succeeded': 'Partly succeeded', failed: 'Failed', 'not-deployed': 'Not deployed' };

// ---- contract validators ----

function loadAjv() {
  const requireFromTools = createRequire(path.join(SRC, 'tools', 'package.json'));
  try {
    const ajvModule = requireFromTools('ajv/dist/2020');
    const formatsModule = requireFromTools('ajv-formats');
    return { Ajv2020: ajvModule.default || ajvModule, addFormats: formatsModule.default || formatsModule };
  } catch {
    throw new Error('ajv is not installed. Run "npm ci" in src/tools first (tools/ci.mjs does it).');
  }
}

function schemaFiles(dir) {
  const out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...schemaFiles(full));
    else if (entry.name.endsWith('.schema.json')) out.push(full);
  }
  return out.sort();
}

let cachedValidators = null;

// Compiles the record endpoint schemas the same way tools/json-check.mjs does: every contract schema under its
// file URL, so relative $ref values resolve, and x- keys allowed as annotations.
export function contractValidators() {
  if (cachedValidators) return cachedValidators;
  const { Ajv2020, addFormats } = loadAjv();
  const ajv = new Ajv2020({ allErrors: true, strict: true, strictTypes: false, strictTuples: false, strictRequired: false, logger: false });
  addFormats(ajv);
  const docs = schemaFiles(CONTRACTS).map(file => ({ file, schema: JSON.parse(fs.readFileSync(file, 'utf8')) }));
  const annotations = new Set();
  const collect = node => {
    if (Array.isArray(node)) node.forEach(collect);
    else if (node && typeof node === 'object') {
      for (const [k, v] of Object.entries(node)) {
        if (k.startsWith('x-')) annotations.add(k);
        collect(v);
      }
    }
  };
  docs.forEach(d => collect(d.schema));
  for (const k of annotations) ajv.addKeyword(k);
  for (const { file, schema } of docs) ajv.addSchema(schema, pathToFileURL(file).href);
  const get = (rel, pointer = '') => {
    const v = ajv.getSchema(pathToFileURL(path.join(CONTRACTS, rel)).href + pointer);
    if (!v) throw new Error(`stub: cannot compile ${rel}${pointer}`);
    return v;
  };
  cachedValidators = {};
  for (const [operation, files] of Object.entries(SCHEMAS)) {
    cachedValidators[operation] = {
      request: get(files.request),
      response: get(files.response),
      data: get(files.response, '#/$defs/data'),
    };
  }
  return cachedValidators;
}

function describeErrors(errors) {
  return (errors || []).slice(0, 5).map(e => `${e.instancePath || '(root)'} ${e.message}`).join('; ');
}

// The body field an ajv error points at, for error.field.
function fieldOf(errors) {
  for (const e of errors || []) {
    const fromPath = (e.instancePath || '').split('/').filter(Boolean)[0];
    if (fromPath) return fromPath;
    if (e.params?.missingProperty) return e.params.missingProperty;
    if (e.params?.additionalProperty) return e.params.additionalProperty;
  }
  return undefined;
}

// ---- helpers ----

function canonical(value) {
  if (Array.isArray(value)) return `[${value.map(canonical).join(',')}]`;
  if (value && typeof value === 'object') {
    return `{${Object.keys(value).sort().map(k => `${JSON.stringify(k)}:${canonical(value[k])}`).join(',')}}`;
  }
  return JSON.stringify(value);
}

function nowUtc() {
  return new Date().toISOString().replace(/\.\d{3}Z$/, 'Z');
}

function readBody(req, limit = 1024 * 1024) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    req.on('data', chunk => {
      size += chunk.length;
      if (size > limit) {
        reject(new Error('body too large'));
        req.destroy();
      } else chunks.push(chunk);
    });
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    req.on('error', reject);
  });
}

function sendJson(res, status, value, extraHeaders = {}) {
  const text = JSON.stringify(value);
  res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Content-Length': Buffer.byteLength(text), ...extraHeaders });
  res.end(text);
}

function idFor(prefix, n) {
  return `${prefix}-0000-4000-8000-${n.toString(16).padStart(12, '0')}`;
}

function performedBy() {
  return {
    actorKind: 'pipeline',
    principal: { objectId: STUB.principalObjectId, displayLabel: 'Pipeline identity (stub)', principalKind: 'service-principal' },
    onBehalfOf: null,
    clientApp: null,
  };
}

// ---- the stub ----

export async function startStub({ port = 0, host = '127.0.0.1', log = () => {} } = {}) {
  const validators = contractValidators();
  const state = {
    records: new Map(), // idempotency key -> { operation, canonicalBody, data }
    byRecordId: new Map(), // deployment record ID -> data
    received: [], // every record endpoint call: { operation, binding, path, headers, body, status }
    oidcRequests: [],
    tokenRequests: [],
    forced: [], // forced answers for the next record endpoint calls: { status, code, retryAfterSeconds }
    faults: [], // answers the stub built that broke the response schema; the test expects none
    seq: 0,
  };

  function reset() {
    state.records.clear();
    state.byRecordId.clear();
    state.received.length = 0;
    state.oidcRequests.length = 0;
    state.tokenRequests.length = 0;
    state.forced.length = 0;
    state.faults.length = 0;
  }

  // The next `times` record endpoint calls answer with this error, before any other check.
  function failNext(status, { code, retryAfterSeconds, times = 1 } = {}) {
    for (let i = 0; i < times; i++) state.forced.push({ status, code, retryAfterSeconds });
  }

  // The next `times` record endpoint calls answer with a redirect to `location`, before any other check. The test
  // uses it to prove that the record step follows no redirect, so the token never reaches another host.
  function redirectNext(location, { status = 307, times = 1 } = {}) {
    for (let i = 0; i < times; i++) state.forced.push({ status, location });
  }

  function failure(route, status, code, message, extra = {}) {
    if (route.binding === 'dataverse') {
      // The Web API's OData error shape. The real Dataverse error codes are hex numbers; the stub uses the
      // contract's code so the test can read it.
      return { status, body: { error: { code, message } } };
    }
    const body = { ok: false, summary: message, error: { code, message, ...extra }, asOf: nowUtc(), correlationId: `stub-${++state.seq}` };
    const v = validators[route.operation].response;
    if (!v(body)) state.faults.push(`failure answer breaks the response schema: ${describeErrors(v.errors)}`);
    return { status, body };
  }

  function success(route, status, data, summary) {
    const operation = validators[route.operation];
    if (route.binding === 'dataverse') {
      if (!operation.data(data)) state.faults.push(`data breaks the response schema: ${describeErrors(operation.data.errors)}`);
      return {
        status: 200,
        body: {
          '@odata.context': 'stub',
          resultJson: JSON.stringify(data),
          recordId: data.recordId,
          statusMeaning: data.status?.meaning ?? null,
        },
      };
    }
    const envelope = {
      ok: true,
      summary,
      data,
      asOf: nowUtc(),
      configurationSetVersion: 1,
      couldNotRead: [],
      warnings: [],
      untrustedFields: [],
      nextSteps: [],
      correlationId: `stub-${state.seq}`,
    };
    if (!operation.response(envelope)) state.faults.push(`envelope breaks the response schema: ${describeErrors(operation.response.errors)}`);
    return { status, body: envelope };
  }

  function recordDeployment(body) {
    const n = ++state.seq;
    const recordId = idFor('d2000000', n);
    return {
      recordId,
      status: { label: RESULT_LABELS[body.resultMeaning], meaning: body.resultMeaning },
      performedBy: performedBy(),
      isAiCreated: false,
      engineStamp: { engineVersion: '0.0.0-stub', engineCheckedOn: nowUtc() },
      created: true,
      deploymentRecordId: recordId,
      artifactVersionId: idFor('d3000000', n),
      deploymentTargetId: body.deploymentTargetId ?? null,
      changeRequestId: body.changeRequestId ?? null,
      route: { key: 'pipeline-step', meaning: 'pipeline-step' },
      flags: { outsideWindow: false, otherPath: false, missingApproval: false, aiOnlyCodeApproval: false },
      gateLevelApplied: 'record',
    };
  }

  function recordCorrection(body, corrected) {
    const n = ++state.seq;
    const recordId = idFor('d2000000', n);
    const meaning = body.resultMeaning ?? corrected.status.meaning;
    return {
      recordId,
      status: { label: RESULT_LABELS[meaning], meaning },
      performedBy: performedBy(),
      isAiCreated: false,
      engineStamp: { engineVersion: '0.0.0-stub', engineCheckedOn: nowUtc() },
      created: true,
      deploymentRecordId: recordId,
      correctsRecordId: body.correctsDeploymentRecordId,
      flags: { outsideWindow: false, otherPath: false, missingApproval: false, aiOnlyCodeApproval: false },
      gateLevelApplied: 'record',
    };
  }

  async function handleRecord(req, route, text) {
    const headers = {
      authorization: req.headers.authorization ? req.headers.authorization.replace(/^(\S+)\s+.*$/, '$1 ***') : undefined,
      'content-type': req.headers['content-type'],
      'idempotency-key': req.headers['idempotency-key'],
      'odata-version': req.headers['odata-version'],
    };
    const entry = { operation: route.operation, binding: route.binding, path: decodeURIComponent(new URL(req.url, 'http://stub').pathname), headers, body: null, status: 0 };
    state.received.push(entry);

    if (req.method !== 'POST') return failure(route, 400, 'validation', 'The record endpoint takes POST only.');
    if (state.forced.length) {
      const f = state.forced.shift();
      if (f.location) return { status: f.status, body: { message: 'Stub: moved (forced by the test).' }, headers: { Location: f.location } };
      const code =f.code || Object.keys(HTTP_FOR_CODE).find(k => HTTP_FOR_CODE[k] === f.status) || 'upstream_unavailable';
      const extra = code === 'rate_limited' ? { retryAfterSeconds: f.retryAfterSeconds || 1 } : {};
      const answer = failure(route, f.status, code, `Forced by the test: ${code}.`, extra);
      if (f.status === 429) answer.headers = { 'Retry-After': String(f.retryAfterSeconds || 1) };
      return answer;
    }
    const auth = req.headers.authorization || '';
    const match = /^Bearer\s+(.+)$/i.exec(auth);
    if (!match || match[1] !== STUB.accessToken) return failure(route, 401, 'not_signed_in', 'No valid app-only token.');
    if (!/^application\/json\b/i.test(req.headers['content-type'] || '')) {
      return failure(route, 400, 'validation', 'The body must be application/json.');
    }
    let body;
    try {
      body = JSON.parse(text);
    } catch {
      return failure(route, 400, 'validation', 'The body is not JSON.');
    }
    entry.body = body;
    const v = validators[route.operation].request;
    if (!v(body)) {
      const field = fieldOf(v.errors);
      return failure(route, 400, 'validation', `The body does not match the record endpoint contract: ${describeErrors(v.errors)}`, field ? { field } : {});
    }
    const headerKey = req.headers['idempotency-key'];
    if (headerKey !== undefined && headerKey !== body.idempotencyKey) {
      return failure(route, 400, 'validation', 'The Idempotency-Key header must equal idempotencyKey in the body.', { field: 'idempotencyKey' });
    }
    const key = `${route.operation}:${body.idempotencyKey}`;
    const known = state.records.get(key);
    if (known) {
      if (known.canonicalBody !== canonical(body)) {
        return failure(route, 409, 'conflict', 'The idempotency key was reused with a different body.', { field: 'idempotencyKey' });
      }
      ++state.seq;
      return success(route, 200, { ...known.data, created: false }, 'A retry matched the idempotency key. Nothing new was written.');
    }
    let data;
    if (route.operation === 'deployment') {
      data = recordDeployment(body);
    } else {
      const corrected = state.byRecordId.get(body.correctsDeploymentRecordId);
      if (!corrected) return failure(route, 404, 'not_found', 'The deployment record to correct does not exist.', { field: 'correctsDeploymentRecordId' });
      data = recordCorrection(body, corrected);
    }
    state.records.set(key, { operation: route.operation, canonicalBody: canonical(body), data });
    state.byRecordId.set(data.deploymentRecordId, data);
    return success(route, 201, data, 'The deployment is recorded. Local stub: no engine ran, so the flags and the gate level are fixed values.');
  }

  // GitHub's OIDC token request URL, faked.
  function handleOidc(req, url) {
    const audience = url.searchParams.get('audience');
    const auth = req.headers.authorization || '';
    state.oidcRequests.push({ audience, authorized: auth === `bearer ${STUB.oidcRequestToken}` || auth === `Bearer ${STUB.oidcRequestToken}` });
    if (!state.oidcRequests.at(-1).authorized) return { status: 401, body: { message: 'Stub: wrong OIDC request token.' } };
    if (audience !== STUB.tokenExchangeAudience) return { status: 400, body: { message: `Stub: audience must be ${STUB.tokenExchangeAudience}.` } };
    return { status: 200, body: { value: STUB.federatedAssertion } };
  }

  // The Microsoft Entra token endpoint, faked: client credentials with a federated assertion only.
  function handleToken(text) {
    const form = new URLSearchParams(text);
    const seen = {
      grantType: form.get('grant_type'),
      clientId: form.get('client_id'),
      scope: form.get('scope'),
      assertionType: form.get('client_assertion_type'),
      assertionMatches: form.get('client_assertion') === STUB.federatedAssertion,
      secretSent: form.has('client_secret'),
    };
    state.tokenRequests.push(seen);
    if (seen.secretSent) return { status: 400, body: { error: 'invalid_request', error_description: 'Stub: a client secret was sent. The samples never send one.' } };
    if (seen.grantType !== 'client_credentials') return { status: 400, body: { error: 'unsupported_grant_type', error_description: 'Stub: client_credentials only.' } };
    if (seen.assertionType !== 'urn:ietf:params:oauth:client-assertion-type:jwt-bearer' || !seen.assertionMatches) {
      return { status: 401, body: { error: 'invalid_client', error_description: 'Stub: no valid federated assertion.' } };
    }
    if (!/^[0-9a-f-]{36}$/i.test(seen.clientId || '')) return { status: 400, body: { error: 'invalid_request', error_description: 'Stub: client_id must be a GUID.' } };
    if (!/\/\.default$/.test(seen.scope || '')) return { status: 400, body: { error: 'invalid_scope', error_description: 'Stub: the scope must end in /.default.' } };
    return { status: 200, body: { token_type: 'Bearer', expires_in: 3599, ext_expires_in: 3599, access_token: STUB.accessToken } };
  }

  const server = http.createServer(async (req, res) => {
    let answer;
    try {
      const url = new URL(req.url, 'http://stub');
      const pathName = decodeURIComponent(url.pathname);
      const text = await readBody(req);
      const route = ROUTES.find(r => r.re.test(pathName));
      if (route) answer = await handleRecord(req, route, text);
      else if (req.method === 'GET' && pathName === '/github-oidc') answer = handleOidc(req, url);
      else if (req.method === 'POST' && /^\/[^/]+\/oauth2\/v2\.0\/token$/.test(pathName)) answer = handleToken(text);
      else answer = { status: 404, body: { message: 'Stub: no such route.' } };
    } catch (e) {
      answer = { status: 500, body: { message: `Stub fault: ${e.message}` } };
      state.faults.push(e.message);
    }
    const last = state.received.at(-1);
    if (last && last.status === 0) last.status = answer.status;
    log(`${req.method} ${req.url} -> ${answer.status}`);
    sendJson(res, answer.status, answer.body, answer.headers);
  });

  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, host, resolve);
  });
  const url = `http://${host}:${server.address().port}`;
  return {
    url,
    state,
    reset,
    failNext,
    redirectNext,
    close: () => new Promise(resolve => server.close(() => resolve())),
  };
}

// ---- run alone ----

function invokedDirectly() {
  if (!process.argv[1]) return false;
  try {
    const self = fs.realpathSync(fileURLToPath(import.meta.url));
    const started = fs.realpathSync(process.argv[1]);
    return process.platform === 'win32' ? self.toLowerCase() === started.toLowerCase() : self === started;
  } catch {
    return false;
  }
}

if (invokedDirectly()) {
  const args = process.argv.slice(2);
  const i = args.indexOf('--port');
  const port = i >= 0 ? Number.parseInt(args[i + 1], 10) : 7071;
  const stub = await startStub({ port, log: line => console.log(`  ${line}`) });
  console.log(`Record endpoint stub on ${stub.url} (Ctrl+C stops it)`);
  console.log(`  Azure binding:     POST ${stub.url}/api/deployments and ${stub.url}/api/deployments/corrections`);
  console.log(`  Dataverse binding: POST ${stub.url}/api/data/v9.2/<prefix>_RecordDeployment and ..._RecordDeploymentCorrection`);
  console.log(`  Accepted token:    ${STUB.accessToken} (set RECORD_AUTH=token and RECORD_ACCESS_TOKEN to it)`);
}
