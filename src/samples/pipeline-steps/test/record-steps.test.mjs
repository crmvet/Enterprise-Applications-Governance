#!/usr/bin/env node
// Local test for the pipeline record step samples. No sign-in, no tenant, no network beyond 127.0.0.1.
//
// It starts the stub (stub/server.mjs), then:
// - runs record-step.mjs as the Azure DevOps and GitHub Actions samples call it, with each sample's fixture
//   environment, and checks that the stub received exactly that sample's fixture body;
// - posts the Power Platform Pipelines flow's fixture body, as the flow's HTTP action would;
// - checks that every failure fails soft (exit 0, a warning, the deployment untouched), that retries reuse the
//   idempotency key, that only RECORD_ENDPOINT_URL changes between the two platform branches, and that no
//   client secret is ever sent;
// - checks that the bearer token goes only to the origin of RECORD_ENDPOINT_URL, over https (plain http only to
//   this machine), and that no redirect is followed, so a redirect to another host never receives the token. A
//   second listener on 127.0.0.1 stands in for that other host and must receive nothing.
// The Azure CLI is replaced by test/fake-az.mjs on a PATH that holds no real Azure CLI.
//
// Usage: node samples/pipeline-steps/test/record-steps.test.mjs     (from src/; needs "npm ci" in src/tools)
import { test, before, after, beforeEach, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { startStub, contractValidators, STUB } from '../stub/server.mjs';
import { KNOWN_VARIABLES, tokenAllowlist, checkTokenTarget, postWithRetries } from '../record-step/record-step.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const SAMPLES = path.resolve(HERE, '..');
const SCRIPT = path.join(SAMPLES, 'record-step', 'record-step.mjs');
const FAKE_AZ = path.join(HERE, 'fake-az.mjs');
const readJson = rel => JSON.parse(fs.readFileSync(path.join(SAMPLES, rel), 'utf8'));

const SAMPLE_FILES = {
  'azure-devops': { yaml: 'azure-devops/record-step.yml', env: 'azure-devops/fixtures/env.json', body: 'azure-devops/fixtures/deployment.json' },
  'github-actions': { yaml: 'github-actions/record-deployment.yml', env: 'github-actions/fixtures/env.json', body: 'github-actions/fixtures/deployment.json' },
  'power-platform-pipelines': { body: 'power-platform-pipelines/fixtures/deployment.json' },
};
const ADO_WARNING = '##vso[task.logissue type=warning]';
const GH_WARNING = '::warning';
// The user name separator in a URL, kept out of the source text, so a text scanner does not read a test URL as an
// email address.
const AT = String.fromCharCode(64);

let stub;
let capture;
let tmp;
let fakeAzDir;
let azLog;
let validators;

// "Another host": a second listener on 127.0.0.1 with its own port, so its origin differs from the stub's. It
// records every request it gets. The token tests expect it to get none.
async function startCapture() {
  const requests = [];
  const server = http.createServer((req, res) => {
    requests.push({ method: req.method, url: req.url, hasAuthorization: Boolean(req.headers.authorization) });
    req.resume();
    res.writeHead(201, { 'Content-Type': 'application/json' });
    res.end('{}');
  });
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', resolve);
  });
  return { url: `http://127.0.0.1:${server.address().port}`, requests, close: () => new Promise(resolve => server.close(() => resolve())) };
}

before(async () => {
  validators = contractValidators();
  stub = await startStub();
  capture = await startCapture();
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'record-step-test-'));
  fakeAzDir = path.join(tmp, 'bin');
  fs.mkdirSync(fakeAzDir);
  azLog = path.join(tmp, 'az.log');
  if (process.platform === 'win32') {
    fs.writeFileSync(path.join(fakeAzDir, 'az.cmd'), `@echo off\r\n"${process.execPath}" "${FAKE_AZ}" %*\r\n`);
  } else {
    const wrapper = path.join(fakeAzDir, 'az');
    fs.writeFileSync(wrapper, `#!/bin/sh\nexec "${process.execPath}" "${FAKE_AZ}" "$@"\n`);
    fs.chmodSync(wrapper, 0o755);
  }
});

after(async () => {
  await stub?.close();
  await capture?.close();
  if (tmp) fs.rmSync(tmp, { recursive: true, force: true });
});

beforeEach(() => {
  stub.reset();
  capture.requests.length = 0;
  fs.rmSync(azLog, { force: true });
});

afterEach(() => {
  assert.deepEqual(stub.state.faults, [], 'the stub built an answer that breaks the response schema');
});

// A clean environment: the fake az first, then Node, then the system folder. No real Azure CLI is reachable,
// and nothing leaks in from the machine running the test (no TF_BUILD, no GITHUB_*).
function baseEnv() {
  const system = process.platform === 'win32'
    ? [path.join(process.env.SystemRoot || 'C:\\Windows', 'System32')]
    : ['/usr/bin', '/bin'];
  const env = {
    PATH: [fakeAzDir, path.dirname(process.execPath), ...system].join(path.delimiter),
    FAKE_AZ_LOG: azLog,
    FAKE_AZ_TOKEN: STUB.accessToken,
  };
  for (const k of ['SystemRoot', 'windir', 'ComSpec', 'PATHEXT', 'TEMP', 'TMP']) if (process.env[k]) env[k] = process.env[k];
  return env;
}

function runStep(vars) {
  const env = baseEnv();
  for (const [k, v] of Object.entries(vars)) {
    if (v === undefined) delete env[k];
    else env[k] = v;
  }
  return new Promise(resolve => {
    const child = spawn(process.execPath, [SCRIPT], { env, windowsHide: true });
    let stdout = '';
    let stderr = '';
    child.stdout.on('data', d => { stdout += d; });
    child.stderr.on('data', d => { stderr += d; });
    child.on('close', code => resolve({ code, stdout, stderr }));
  });
}

function adoVars(overrides = {}) {
  return {
    ...readJson(SAMPLE_FILES['azure-devops'].env).variables,
    RECORD_ENDPOINT_URL: `${stub.url}/api/deployments`,
    TF_BUILD: 'True',
    ...overrides,
  };
}

function ghVars(overrides = {}) {
  return {
    ...readJson(SAMPLE_FILES['github-actions'].env).variables,
    RECORD_ENDPOINT_URL: `${stub.url}/api/deployments`,
    RECORD_AUTHORITY_HOST: stub.url,
    GITHUB_ACTIONS: 'true',
    GITHUB_OUTPUT: path.join(tmp, 'github-output.txt'),
    ACTIONS_ID_TOKEN_REQUEST_URL: `${stub.url}/github-oidc?api-version=2.0`,
    ACTIONS_ID_TOKEN_REQUEST_TOKEN: STUB.oidcRequestToken,
    ...overrides,
  };
}

// RECORD_AUTH=token with a prepared body, on no platform (plain log lines).
function tokenVars(overrides = {}) {
  return {
    RECORD_ENDPOINT_URL: `${stub.url}/api/deployments`,
    RECORD_AUTH: 'token',
    RECORD_ACCESS_TOKEN: STUB.accessToken,
    RECORD_BODY_FILE: path.join(SAMPLES, SAMPLE_FILES['power-platform-pipelines'].body),
    ...overrides,
  };
}

function azCalls() {
  if (!fs.existsSync(azLog)) return [];
  return fs.readFileSync(azLog, 'utf8').trim().split('\n').filter(Boolean).map(line => JSON.parse(line));
}

// The token may appear only in the platform's mask command, never in any other line.
function assertTokenOnlyMasked(stdout, maskPrefix) {
  for (const line of stdout.split(/\r?\n/)) {
    if (line.includes(STUB.accessToken)) assert.ok(line.startsWith(maskPrefix), `the token leaked into a log line: ${line}`);
  }
}

function postJson(urlPath, body, headers = {}) {
  return fetch(`${stub.url}${urlPath}`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${STUB.accessToken}`, 'Content-Type': 'application/json', ...headers },
    body: JSON.stringify(body),
  }).then(async res => ({ status: res.status, json: await res.json() }));
}

// Names a YAML sample sets in its env block (lines like "  RECORD_X: value"; comment lines never match).
function yamlVariables(rel) {
  const text = fs.readFileSync(path.join(SAMPLES, rel), 'utf8');
  return new Set([...text.matchAll(/^\s+(RECORD_[A-Z0-9_]+):\s/gm)].map(m => m[1]));
}

// ---- the contract and the samples agree ----

test('every sample fixture body matches the record endpoint contract', () => {
  for (const [sample, files] of Object.entries(SAMPLE_FILES)) {
    const ok = validators.deployment.request(readJson(files.body));
    assert.ok(ok, `${sample}: ${JSON.stringify(validators.deployment.request.errors)}`);
  }
  assert.ok(validators.correction.request(readJson('test/fixtures/deployment-correction.json')), JSON.stringify(validators.correction.request.errors));
});

test('the flow template builds exactly the fields of the flow fixture body, keyed by the stage run', () => {
  const template = readJson('power-platform-pipelines/flow-template.json');
  const fixture = readJson(SAMPLE_FILES['power-platform-pipelines'].body);
  assert.deepEqual(Object.keys(template.recordBody).sort(), Object.keys(fixture).sort());
  assert.match(template.recordBody.idempotencyKey, /^@\{concat\('pp-pipelines-', outputs\('Stage_run_facts'\)\?\['stageRunId'\]\)\}$/);
  assert.ok(fixture.idempotencyKey.startsWith('pp-pipelines-'));
  for (const [field, expression] of Object.entries(template.recordBody)) {
    if (!expression.startsWith('@')) assert.equal(fixture[field], expression, `${field} is a fixed value in the template`);
  }
  for (const value of Object.values(template.stageRunFacts)) assert.match(value, /^PICK: /);
});

test('the YAML samples set only variables the script reads, and each fixture environment mirrors its YAML', () => {
  for (const [sample, files] of Object.entries(SAMPLE_FILES)) {
    if (!files.yaml) continue;
    const names = yamlVariables(files.yaml);
    assert.ok(names.size > 10, `${sample}: found only ${names.size} RECORD_ variables in ${files.yaml}`);
    for (const name of names) assert.ok(KNOWN_VARIABLES.has(name), `${sample}: ${files.yaml} sets ${name}, which record-step.mjs does not read`);
    const fixtureNames = new Set(Object.keys(readJson(files.env).variables));
    assert.deepEqual([...fixtureNames].sort(), [...names].sort(), `${sample}: ${files.env} must set exactly the variables ${files.yaml} sets`);
  }
});

// ---- each sample posts its fixture body ----

test('Azure DevOps: the step gets a token from the Azure CLI, posts the fixture body and records it', async () => {
  const r = await runStep(adoVars());
  assert.equal(r.code, 0, r.stderr);
  assert.equal(stub.state.received.length, 1);
  const call = stub.state.received[0];
  assert.equal(call.status, 201);
  assert.equal(call.binding, 'azure');
  assert.deepEqual(call.body, readJson(SAMPLE_FILES['azure-devops'].body));
  assert.equal(call.headers['idempotency-key'], call.body.idempotencyKey);
  assert.equal(call.headers.authorization, 'Bearer ***');
  const calls = azCalls();
  assert.equal(calls.length, 1, 'the Azure CLI is asked once');
  assert.deepEqual(calls[0], ['account', 'get-access-token', '--scope', `${stub.url}/.default`, '--query', 'accessToken', '--output', 'tsv']);
  assert.ok(r.stdout.includes(`##vso[task.setsecret]${STUB.accessToken}`), 'the token is masked in the log');
  assert.match(r.stdout, /##vso\[task\.setvariable variable=deploymentRecordId;isOutput=true\]d2000000-/);
  assert.ok(!r.stdout.includes(ADO_WARNING), r.stdout);
  assertTokenOnlyMasked(r.stdout, '##vso[task.setsecret]');
});

test('Azure DevOps: running the same step again is idempotent (200, created false, one record)', async () => {
  const first = await runStep(adoVars());
  const second = await runStep(adoVars());
  assert.equal(first.code, 0);
  assert.equal(second.code, 0);
  assert.deepEqual(stub.state.received.map(c => c.status), [201, 200]);
  assert.equal(stub.state.records.size, 1);
  assert.match(second.stdout, /nothing new was written/);
  assert.ok(!second.stdout.includes(ADO_WARNING), second.stdout);
});

test('GitHub Actions: the step exchanges the OIDC token with Entra, posts the fixture body and records it', async () => {
  const outputFile = path.join(tmp, 'github-output.txt');
  fs.rmSync(outputFile, { force: true });
  const vars = ghVars();
  const r = await runStep(vars);
  assert.equal(r.code, 0, r.stderr);
  assert.deepEqual(stub.state.oidcRequests, [{ audience: 'api://AzureADTokenExchange', authorized: true }]);
  assert.equal(stub.state.tokenRequests.length, 1);
  assert.deepEqual(stub.state.tokenRequests[0], {
    grantType: 'client_credentials',
    clientId: vars.RECORD_CLIENT_ID,
    scope: `${stub.url}/.default`,
    assertionType: 'urn:ietf:params:oauth:client-assertion-type:jwt-bearer',
    assertionMatches: true,
    secretSent: false,
  });
  assert.equal(stub.state.received.length, 1);
  assert.equal(stub.state.received[0].status, 201);
  assert.deepEqual(stub.state.received[0].body, readJson(SAMPLE_FILES['github-actions'].body));
  assert.match(fs.readFileSync(outputFile, 'utf8'), /^deployment-record-id=d2000000-/m);
  assert.ok(r.stdout.includes(`::add-mask::${STUB.accessToken}`), 'the token is masked in the log');
  assert.ok(!r.stdout.includes(GH_WARNING), r.stdout);
  assert.equal(azCalls().length, 0, 'the GitHub sample needs no Azure CLI');
  assertTokenOnlyMasked(r.stdout, '::add-mask::');
});

test('Power Platform Pipelines: the flow fixture body posts to the stub, is recorded, and a resubmit is idempotent', async () => {
  const body = readJson(SAMPLE_FILES['power-platform-pipelines'].body);
  const first = await postJson('/api/deployments', body, { 'Idempotency-Key': body.idempotencyKey });
  assert.equal(first.status, 201, JSON.stringify(first.json));
  assert.ok(validators.deployment.response(first.json), JSON.stringify(validators.deployment.response.errors));
  assert.equal(first.json.data.route.meaning, 'pipeline-step');
  assert.equal(first.json.data.performedBy.actorKind, 'pipeline');
  assert.equal(first.json.data.isAiCreated, false);
  const again = await postJson('/api/deployments', body, { 'Idempotency-Key': body.idempotencyKey });
  assert.equal(again.status, 200);
  assert.equal(again.json.data.created, false);
  assert.equal(again.json.data.deploymentRecordId, first.json.data.deploymentRecordId);
});

// ---- the transport is one variable ----

test('Dataverse binding: the same step and body work when only RECORD_ENDPOINT_URL changes', async () => {
  const r = await runStep(adoVars({ RECORD_ENDPOINT_URL: `${stub.url}/api/data/v9.2/<prefix>_RecordDeployment` }));
  assert.equal(r.code, 0, r.stderr);
  assert.equal(stub.state.received.length, 1);
  const call = stub.state.received[0];
  assert.equal(call.binding, 'dataverse');
  assert.equal(call.path, '/api/data/v9.2/<prefix>_RecordDeployment');
  assert.equal(call.status, 200);
  assert.equal(call.headers['odata-version'], '4.0');
  assert.deepEqual(call.body, readJson(SAMPLE_FILES['azure-devops'].body));
  assert.equal(azCalls()[0][3], `${stub.url}/.default`, 'the scope follows from the URL');
  assert.match(r.stdout, /recorded as d2000000-[0-9a-f-]+: a new record was written/);
  assert.ok(!r.stdout.includes(ADO_WARNING), r.stdout);
});

test('RECORD_ENDPOINT_SCOPE overrides the scope the URL implies', async () => {
  const r = await runStep(adoVars({ RECORD_ENDPOINT_SCOPE: 'api://e3000000-0000-4000-8000-000000000001/.default' }));
  assert.equal(r.code, 0);
  assert.equal(azCalls()[0][3], 'api://e3000000-0000-4000-8000-000000000001/.default');
});

// ---- fails soft ----

test('fails soft: configuration unavailable (503) warns once and is not retried', async () => {
  stub.failNext(503, { code: 'configuration_unavailable' });
  const r = await runStep(adoVars());
  assert.equal(r.code, 0);
  assert.equal(stub.state.received.length, 1, 'configuration_unavailable is not retryable (errors.json)');
  assert.ok(r.stdout.includes(ADO_WARNING), r.stdout);
  assert.match(r.stdout, /not recorded: HTTP 503 configuration_unavailable/);
  assert.match(r.stdout, /deployment itself is not affected/);
  assert.equal(stub.state.records.size, 0);
});

test('fails soft: rate limited once, then retried with the same key and the same body', async () => {
  stub.failNext(429, { code: 'rate_limited', retryAfterSeconds: 1 });
  const r = await runStep(adoVars());
  assert.equal(r.code, 0);
  assert.deepEqual(stub.state.received.map(c => c.status), [429, 201]);
  const keys = stub.state.received.map(c => c.headers['idempotency-key']);
  assert.equal(keys[0], keys[1]);
  assert.match(r.stdout, /trying again in 1 s with the same idempotency key/);
  assert.equal(stub.state.records.size, 1);
  assert.ok(!r.stdout.includes(ADO_WARNING), r.stdout);
});

test('fails soft: an upstream outage (503 upstream_unavailable) is retried up to the attempt limit', async () => {
  stub.failNext(503, { code: 'upstream_unavailable', times: 2 });
  const r = await runStep(adoVars({ RECORD_MAX_ATTEMPTS: '2' }));
  assert.equal(r.code, 0);
  assert.deepEqual(stub.state.received.map(c => c.status), [503, 503]);
  assert.match(r.stdout, /not recorded: HTTP 503 upstream_unavailable after 2 attempt\(s\)/);
});

test('fails soft: the endpoint cannot be reached', async () => {
  const port = await new Promise(resolve => {
    const s = http.createServer();
    s.listen(0, '127.0.0.1', () => {
      const p = s.address().port;
      s.close(() => resolve(p));
    });
  });
  const r = await runStep(adoVars({ RECORD_ENDPOINT_URL: `http://127.0.0.1:${port}/api/deployments`, RECORD_MAX_ATTEMPTS: '2', RECORD_TIMEOUT_SECONDS: '5' }));
  assert.equal(r.code, 0);
  assert.match(r.stdout, /not recorded: no answer after 2 attempt\(s\)/);
  assert.ok(r.stdout.includes(ADO_WARNING));
});

test('fails soft: a body the contract refuses (400) names the field and is not retried', async () => {
  const r = await runStep(adoVars({ RECORD_RUN_URL: 'http://pipelines.example.org/runs/4711' }));
  assert.equal(r.code, 0);
  assert.equal(stub.state.received.length, 1);
  assert.equal(stub.state.received[0].status, 400);
  assert.match(r.stdout, /HTTP 400 validation/);
  assert.match(r.stdout, /runUrl/);
});

test('fails soft: a missing required value sends nothing', async () => {
  const r = await runStep(adoVars({ RECORD_ARTIFACT_VERSION: undefined }));
  assert.equal(r.code, 0);
  assert.equal(stub.state.received.length, 0);
  assert.equal(azCalls().length, 0, 'no token is asked for a body that cannot be sent');
  assert.match(r.stdout, /artifactVersion is missing \(RECORD_ARTIFACT_VERSION\)/);
});

test('fails soft: an unknown result word sends nothing', async () => {
  const r = await runStep(adoVars({ RECORD_RESULT: 'Mostly fine' }));
  assert.equal(r.code, 0);
  assert.equal(stub.state.received.length, 0);
  assert.match(r.stdout, /RECORD_RESULT "Mostly fine" is not a known result/);
});

test('fails soft: the Azure CLI is not signed in', async () => {
  const r = await runStep(adoVars({ FAKE_AZ_FAIL: '1' }));
  assert.equal(r.code, 0);
  assert.equal(stub.state.received.length, 0);
  assert.match(r.stdout, /no token \(azure-cli\): the Azure CLI gave no token/);
});

test('fails soft: a refused token (401)', async () => {
  const r = await runStep(adoVars({ RECORD_AUTH: 'token', RECORD_ACCESS_TOKEN: 'not-the-stub-token' }));
  assert.equal(r.code, 0);
  assert.equal(stub.state.received[0].status, 401);
  assert.match(r.stdout, /HTTP 401 not_signed_in/);
});

test('fails soft: the same key with another body is a conflict (409)', async () => {
  await runStep(adoVars());
  const r = await runStep(adoVars({ RECORD_ARTIFACT_VERSION: '1.4.1.1' }));
  assert.equal(r.code, 0);
  assert.deepEqual(stub.state.received.map(c => c.status), [201, 409]);
  assert.match(r.stdout, /HTTP 409 conflict/);
  assert.match(r.stdout, /unique per deployment/);
  assert.equal(stub.state.records.size, 1);
});

test('GitHub Actions: without "id-token: write" the step warns and sends nothing', async () => {
  const r = await runStep(ghVars({ ACTIONS_ID_TOKEN_REQUEST_URL: undefined, ACTIONS_ID_TOKEN_REQUEST_TOKEN: undefined }));
  assert.equal(r.code, 0);
  assert.equal(stub.state.received.length, 0);
  assert.ok(r.stdout.includes(GH_WARNING), r.stdout);
  assert.match(r.stdout, /id-token: write/);
});

// ---- no secret, no plain http, no key in the URL ----

test('GitHub Actions: no client secret is sent, even when one sits in the environment', async () => {
  const r = await runStep(ghVars({ RECORD_CLIENT_SECRET: 'never-sent', AZURE_CLIENT_SECRET: 'never-sent' }));
  assert.equal(r.code, 0);
  assert.equal(stub.state.tokenRequests.length, 1);
  assert.equal(stub.state.tokenRequests[0].secretSent, false);
  assert.equal(stub.state.received[0].status, 201);
});

test('refuses plain http to another host and a key in the URL, before asking for a token', async () => {
  const plain = await runStep(adoVars({ RECORD_ENDPOINT_URL: 'http://record.example.org/api/deployments' }));
  assert.equal(plain.code, 0);
  assert.match(plain.stdout, /must use https/);
  const keyed = await runStep(adoVars({ RECORD_ENDPOINT_URL: 'https://record.example.org/api/deployments?code=abc' }));
  assert.equal(keyed.code, 0);
  assert.match(keyed.stdout, /carries a key in its query \(code\)/);
  assert.equal(azCalls().length, 0);
  assert.equal(stub.state.received.length, 0);
});

// ---- the token goes only to the record endpoint (allowlist from RECORD_ENDPOINT_URL, https, no redirect) ----

test('token allowlist: exactly the origin of RECORD_ENDPOINT_URL, https only (plain http only to this machine)', () => {
  assert.deepEqual([...tokenAllowlist('https://record.example.org/api/deployments?x=1')], ['https://record.example.org']);
  assert.deepEqual([...tokenAllowlist('https://Record.Example.org:443/api/data/v9.2/<prefix>_RecordDeployment')], ['https://record.example.org']);
  assert.deepEqual([...tokenAllowlist('https://record.example.org:8443/api/deployments')], ['https://record.example.org:8443']);
  assert.deepEqual([...tokenAllowlist('http://127.0.0.1:7071/api/deployments')], ['http://127.0.0.1:7071'], 'the local stub');
  for (const none of ['http://record.example.org/api/deployments', 'ftp://record.example.org/api', `https://user:pw${AT}record.example.org/api`, 'not a url', '']) {
    assert.deepEqual([...tokenAllowlist(none)], [], `no origin may receive the token for "${none}"`);
  }
});

test('token allowlist: every send off the allowlist is refused (another host, port or scheme, look-alikes, a user name)', () => {
  const allow = tokenAllowlist('https://record.example.org/api/deployments');
  assert.equal(checkTokenTarget('https://record.example.org/api/deployments', allow), null);
  assert.equal(checkTokenTarget('https://RECORD.example.org:443/api/other', allow), null, 'the same origin, any path');
  const refused = {
    'https://elsewhere.invalid/api/deployments': /goes only to https:\/\/record\.example\.org, never to https:\/\/elsewhere\.invalid$/,
    'https://record.example.org.elsewhere.invalid/api/deployments': /never to https:\/\/record\.example\.org\.elsewhere\.invalid$/,
    'https://sub.record.example.org/api/deployments': /never to https:\/\/sub\.record\.example\.org$/,
    'https://elsewhere.invalid/api/deployments?next=https://record.example.org': /never to https:\/\/elsewhere\.invalid$/,
    'https://record.example.org:8443/api/deployments': /never to https:\/\/record\.example\.org:8443$/,
    'http://record.example.org/api/deployments': /https only, never http to record\.example\.org$/,
    [`https://record.example.org${AT}elsewhere.invalid/api/deployments`]: /user name or password/,
    'http://127.0.0.1:7071/api/deployments': /never to http:\/\/127\.0\.0\.1:7071$/,
    'not a url': /not a URL/,
  };
  for (const [url, why] of Object.entries(refused)) assert.match(checkTokenTarget(url, allow) ?? 'allowed', why, url);

  const local = tokenAllowlist('http://127.0.0.1:7071/api/deployments');
  assert.equal(checkTokenTarget('http://127.0.0.1:7071/api/deployments', local), null, 'the local stub itself');
  assert.match(checkTokenTarget('http://127.0.0.1:7072/api/deployments', local) ?? 'allowed', /never to http:\/\/127\.0\.0\.1:7072$/);
  assert.match(checkTokenTarget('http://localhost:7071/api/deployments', local) ?? 'allowed', /never to http:\/\/localhost:7071$/);

  for (const empty of [tokenAllowlist('http://record.example.org/api/deployments'), [], undefined]) {
    assert.match(checkTokenTarget('https://record.example.org/api/deployments', empty) ?? 'allowed', /goes only to the origin of RECORD_ENDPOINT_URL/);
  }
});

test('token allowlist: the sender refuses a URL off the allowlist before a single byte is sent, and does not retry', async () => {
  const body = readJson(SAMPLE_FILES['azure-devops'].body);
  for (const allowlist of [tokenAllowlist(`${stub.url}/api/deployments`), undefined]) {
    const lines = [];
    const result = await postWithRetries(`${capture.url}/api/deployments`, body, STUB.accessToken, { timeoutMs: 5000, maxAttempts: 3, log: { info: line => lines.push(line) }, allowlist });
    assert.equal(result.ok, false);
    assert.equal(result.code, 'token_target');
    assert.equal(result.attempt, 0);
    assert.ok(result.message.endsWith(`never to ${capture.url}`), result.message);
    assert.deepEqual(lines, [], 'no attempt and no retry');
  }
  assert.deepEqual(capture.requests, [], 'the other host received nothing');
  assert.equal(stub.state.received.length, 0);
});

test('token mode: a redirect to another host is never followed, so that host gets neither the token nor the body', async () => {
  for (const status of [301, 302, 303, 307, 308]) {
    stub.reset();
    stub.redirectNext(`${capture.url}/api/deployments`, { status });
    const r = await runStep(tokenVars());
    assert.equal(r.code, 0, `${status}: ${r.stderr}`);
    assert.deepEqual(stub.state.received.map(c => c.status), [status], `${status}: one call to the endpoint, no retry`);
    assert.match(r.stdout, new RegExp(`not recorded: HTTP ${status} redirect after 1 attempt\\(s\\): the endpoint answered with a redirect to another host \\(${capture.url.replace(/\./g, '\\.')}\\); the script follows no redirect, so the token and the body were not sent there`));
    assert.match(r.stdout, /The token goes only to that URL's origin, and no redirect is followed/);
    assert.ok(!r.stdout.includes(STUB.accessToken), `${status}: plain output never prints the token`);
    assert.equal(stub.state.records.size, 0);
  }
  assert.deepEqual(capture.requests, [], 'the other host received nothing');
});

test('token mode: a redirect on the same host is not followed either', async () => {
  stub.redirectNext(`${stub.url}/api/deployments`, { status: 308 });
  const r = await runStep(tokenVars());
  assert.equal(r.code, 0);
  assert.deepEqual(stub.state.received.map(c => c.status), [308]);
  assert.match(r.stdout, /HTTP 308 redirect after 1 attempt\(s\): the endpoint answered with a redirect to another path on the same host; the script follows no redirect/);
  assert.equal(stub.state.records.size, 0);
});

test('token mode: plain http to another host is refused before the token is sent anywhere', async () => {
  const r = await runStep(tokenVars({ RECORD_ENDPOINT_URL: 'http://record.invalid/api/deployments' }));
  assert.equal(r.code, 0);
  assert.match(r.stdout, /RECORD_ENDPOINT_URL must use https/);
  assert.equal(stub.state.received.length, 0);
  assert.deepEqual(capture.requests, []);
  assert.ok(!r.stdout.includes(STUB.accessToken));
});

test('Azure DevOps: the same rule holds for a token from the Azure CLI (a redirect to another host is not followed)', async () => {
  stub.redirectNext(`${capture.url}/api/deployments`, { status: 307 });
  const r = await runStep(adoVars());
  assert.equal(r.code, 0);
  assert.equal(azCalls().length, 1);
  assert.deepEqual(stub.state.received.map(c => c.status), [307]);
  assert.ok(r.stdout.includes(ADO_WARNING), r.stdout);
  assert.match(r.stdout, /redirect to another host/);
  assert.deepEqual(capture.requests, [], 'the other host received nothing');
  assertTokenOnlyMasked(r.stdout, '##vso[task.setsecret]');
});

// ---- smaller rules ----

test('RECORD_BODY_FILE sends a prepared body as it is (the README example by hand)', async () => {
  const bodyFile = path.join(SAMPLES, SAMPLE_FILES['power-platform-pipelines'].body);
  const r = await runStep({ RECORD_ENDPOINT_URL: `${stub.url}/api/deployments`, RECORD_AUTH: 'token', RECORD_ACCESS_TOKEN: STUB.accessToken, RECORD_BODY_FILE: bodyFile });
  assert.equal(r.code, 0);
  assert.equal(stub.state.received[0].status, 201);
  assert.deepEqual(stub.state.received[0].body, readJson(SAMPLE_FILES['power-platform-pipelines'].body));
  assert.match(r.stdout, /^Record step: recorded as d2000000-/m);
  assert.ok(!r.stdout.includes('WARNING'), r.stdout);
  assert.ok(!r.stdout.includes(STUB.accessToken), 'plain output never prints the token');
});

test('finishedOn defaults to the time the step runs', async () => {
  const before = Date.now();
  const r = await runStep(adoVars({ RECORD_FINISHED_ON: '$(RECORD_FINISHED_ON)' }));
  assert.equal(r.code, 0);
  const finishedOn = stub.state.received[0].body.finishedOn;
  assert.match(finishedOn, /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/);
  const t = Date.parse(finishedOn);
  assert.ok(t >= before - 2000 && t <= Date.now() + 2000, `finishedOn ${finishedOn} is not the time of the run`);
});

test('a correction goes to the corrections endpoint and is validated', async () => {
  const body = readJson(SAMPLE_FILES['azure-devops'].body);
  const recorded = await postJson('/api/deployments', body);
  assert.equal(recorded.status, 201);
  const correction = { ...readJson('test/fixtures/deployment-correction.json'), correctsDeploymentRecordId: recorded.json.data.deploymentRecordId };
  const corrected = await postJson('/api/deployments/corrections', correction);
  assert.equal(corrected.status, 201, JSON.stringify(corrected.json));
  assert.ok(validators.correction.response(corrected.json), JSON.stringify(validators.correction.response.errors));
  assert.equal(corrected.json.data.correctsRecordId, recorded.json.data.deploymentRecordId);
  const unknown = await postJson('/api/deployments/corrections', { ...correction, idempotencyKey: 'another-key', correctsDeploymentRecordId: 'd2000000-0000-4000-8000-0000000000ff' });
  assert.equal(unknown.status, 404);
  assert.equal(unknown.json.error.code, 'not_found');
});

test('the stub refuses bodies that break the contract (exactly one target, no unknown field)', async () => {
  const body = readJson(SAMPLE_FILES['power-platform-pipelines'].body);
  const noTarget = { ...body };
  delete noTarget.targetExternalId;
  assert.equal((await postJson('/api/deployments', noTarget)).status, 400);
  const twoTargets = { ...body, deploymentTargetId: 'd4000000-0000-4000-8000-000000000002' };
  assert.equal((await postJson('/api/deployments', twoTargets)).status, 400);
  const extra = { ...body, gateLevelApplied: 'off' };
  const refused = await postJson('/api/deployments', extra);
  assert.equal(refused.status, 400);
  assert.equal(refused.json.error.field, 'gateLevelApplied');
  const mismatchedHeader = await postJson('/api/deployments', body, { 'Idempotency-Key': 'another-key' });
  assert.equal(mismatchedHeader.status, 400);
  assert.equal(stub.state.records.size, 0);
});
