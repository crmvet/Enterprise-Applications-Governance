#!/usr/bin/env node
// Record step: sends one deployment record to the record endpoint. A copy-paste sample, not a package.
// The product builds, hosts and runs no pipeline. Copy this file into your repository, for example as
// pipelines/record-step.mjs, and call it from the Azure DevOps or GitHub Actions step next to it.
//
// Contract: mcp/contracts/record-endpoint.json, with the body of record-endpoint/deployment.request.schema.json.
// Node 20 or later. No packages. Azure Pipelines hosted agents and GitHub-hosted runners have Node already.
//
// What it promises:
// - It fails soft. On any error, timeout or answer other than 200 or 201 it writes a warning and exits 0.
//   A failed record never fails the deployment. Stopping a deployment is a check step's job, and check steps
//   wait for the gate decision; this script never asks for one.
// - It is idempotent. The body carries idempotencyKey. Every retry sends the same bytes with the same key, so the
//   endpoint returns the first record and writes nothing new.
// - It holds no secret. The token comes from workload identity federation: the Azure CLI that the AzureCLI@2
//   task signed in with a workload identity federation service connection, or GitHub's OIDC token exchanged with
//   Microsoft Entra ID. It never takes a client secret, a certificate or a personal access token.
// - The transport is one variable, RECORD_ENDPOINT_URL. The token scope follows from it unless
//   RECORD_ENDPOINT_SCOPE says otherwise.
// - It sends the bearer token only to the record endpoint. The allowlist is one origin (scheme, host and port)
//   taken from RECORD_ENDPOINT_URL, HTTPS only; the one exception, as for every URL here, is plain http to this
//   machine for the local stub. Every send is checked against it, and no redirect is followed, so a redirect to
//   another host never receives the token or the body. This matters most for RECORD_AUTH=token, whose token
//   comes from outside the script. Token mode is for a short-lived token another step obtained without a secret,
//   and for the local test; never store a long-lived token for it.
//
// WORKING DEFAULT (open design question 12). The pipeline's workload identity holds the deployment recorder
// role directly: the Deployment.Record app role on its service principal, or the role on a Dataverse application
// user. Each such identity is recorded as a workload grant with a reason, the deciding decision and an expiry.
// WORKING DEFAULT (open design question 7). RECORD_CODE_APPROVAL_KIND and RECORD_REPORTED_APPROVALS_JSON are
// evidence only. An AI-only code approval never counts as authorization, and the shipped gate default flags it.
//
// Usage: node record-step.mjs        (every input comes from environment variables, see the README)
// Exit code: always 0.
import fs from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

// Body fields, in contract order: environment variable -> request field.
export const FIELD_VARIABLES = Object.freeze({
  RECORD_IDEMPOTENCY_KEY: 'idempotencyKey',
  RECORD_BUSINESS_APPLICATION_ID: 'businessApplicationId',
  RECORD_ARTIFACT_NAME: 'artifactName',
  RECORD_ARTIFACT_VERSION: 'artifactVersion',
  RECORD_ARTIFACT_KIND_KEY: 'artifactKindKey',
  RECORD_DIGEST: 'digest',
  RECORD_SOURCE_COMMIT: 'sourceCommit',
  RECORD_SOURCE_REPOSITORY_URL: 'sourceRepositoryUrl',
  RECORD_DEPLOYMENT_TARGET_ID: 'deploymentTargetId',
  RECORD_TARGET_EXTERNAL_ID: 'targetExternalId',
  RECORD_CHANGE_REQUEST_ID: 'changeRequestId',
  RECORD_PIPELINE_REFERENCE_ID: 'pipelineReferenceId',
  RECORD_RUN_URL: 'runUrl',
  RECORD_STARTED_ON: 'startedOn',
  RECORD_FINISHED_ON: 'finishedOn',
  RECORD_RESULT: 'resultMeaning',
  RECORD_VERSION_BEFORE: 'versionBefore',
  RECORD_VERSION_AFTER: 'versionAfter',
  RECORD_DEPLOYED_BY_OBJECT_ID: 'deployedByObjectId',
  RECORD_MERGE_ACTOR_OBJECT_ID: 'mergeActorObjectId',
  RECORD_MERGE_ACTOR_KIND: 'mergeActorKind',
  RECORD_STAGE_ORDER_RUN: 'stageOrderRun',
  RECORD_CODE_APPROVAL_KIND: 'codeApprovalKind',
  RECORD_REPORTED_APPROVALS_JSON: 'reportedApprovals',
  RECORD_PIPELINE_EXTERNAL_ID: 'pipelineExternalId',
  RECORD_STARTED_BY_AI_CLIENT: 'startedByAiClient',
});

// Settings that are not body fields.
export const SETTING_VARIABLES = Object.freeze([
  'RECORD_ENDPOINT_URL', // the transport: the one variable that differs between the two platform branches
  'RECORD_ENDPOINT_SCOPE', // optional: the token scope; default is the endpoint's origin plus /.default
  'RECORD_AUTH', // azure-cli, github-oidc or token; default from the platform
  'RECORD_TENANT_ID', // github-oidc only: the Microsoft Entra tenant ID (not a secret)
  'RECORD_CLIENT_ID', // github-oidc only: the pipeline identity's application (client) ID (not a secret)
  'RECORD_ACCESS_TOKEN', // token only: a short-lived token another step obtained without a secret; sent only to RECORD_ENDPOINT_URL's origin
  'RECORD_AUTHORITY_HOST', // optional: default https://login.microsoftonline.com (sovereign clouds differ)
  'RECORD_BODY_FILE', // optional: a JSON file with the whole body; the field variables are then ignored
  'RECORD_TIMEOUT_SECONDS', // optional: per call, 1 to 120, default 20
  'RECORD_MAX_ATTEMPTS', // optional: 1 to 5, default 3
]);

export const KNOWN_VARIABLES = Object.freeze(new Set([...Object.keys(FIELD_VARIABLES), ...SETTING_VARIABLES]));

// Platform result words -> the contract's deployment result meaning. The contract's own words pass through.
// Judgment calls: "SucceededWithIssues" (Azure Pipelines) is partly-succeeded; a cancelled or skipped deploy
// is not-deployed ("An abandoned run is not-deployed"). Set RECORD_RESULT from your deploy step to be exact.
export const RESULT_WORDS = Object.freeze(new Map([
  ['succeeded', 'succeeded'],
  ['success', 'succeeded'],
  ['partly-succeeded', 'partly-succeeded'],
  ['succeededwithissues', 'partly-succeeded'],
  ['failed', 'failed'],
  ['failure', 'failed'],
  ['not-deployed', 'not-deployed'],
  ['canceled', 'not-deployed'],
  ['cancelled', 'not-deployed'],
  ['skipped', 'not-deployed'],
]));

const REQUIRED_FIELDS = ['idempotencyKey', 'artifactName', 'artifactVersion', 'finishedOn', 'resultMeaning'];
const DEFAULT_AUTHORITY = 'https://login.microsoftonline.com';
const TOKEN_EXCHANGE_AUDIENCE = 'api://AzureADTokenExchange';
const SCOPE_RE = /^[A-Za-z][A-Za-z0-9+.-]*:\/\/[A-Za-z0-9.:\-[\]]+(\/[A-Za-z0-9._~-]+)*\/\.default$/;
const GUID_RE = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
const TENANT_RE = /^[A-Za-z0-9.-]{1,100}$/;

// A variable's value, or undefined when it is empty or is an Azure Pipelines macro left as its own text,
// which is what $(NAME) becomes when NAME is not defined.
export function readVariable(env, name) {
  const raw = env[name];
  if (raw === undefined || raw === null) return undefined;
  const text = String(raw).trim();
  if (!text) return undefined;
  if (/^\$\([^)]*\)$/.test(text)) return undefined;
  return text;
}

function nowUtc() {
  return new Date().toISOString().replace(/\.\d{3}Z$/, 'Z');
}

// Builds the request body from the environment. Returns { body, problems, notes }.
// A problem means the step must not send anything; a note is only reported.
export function buildBody(env) {
  const problems = [];
  const notes = [];
  const file = readVariable(env, 'RECORD_BODY_FILE');
  let body = {};
  if (file) {
    try {
      const text = fs.readFileSync(file, 'utf8');
      body = JSON.parse(text.charCodeAt(0) === 0xfeff ? text.slice(1) : text); // a byte order mark is allowed
    } catch (e) {
      problems.push(`RECORD_BODY_FILE cannot be read as JSON: ${e.message}`);
      return { body: {}, problems, notes };
    }
    if (!body || typeof body !== 'object' || Array.isArray(body)) {
      problems.push('RECORD_BODY_FILE must hold one JSON object');
      return { body: {}, problems, notes };
    }
  } else {
    for (const [name, field] of Object.entries(FIELD_VARIABLES)) {
      const text = readVariable(env, name);
      if (text === undefined) continue;
      if (text.includes('$(')) notes.push(`${name} still holds an Azure Pipelines macro: ${text}`);
      if (field === 'resultMeaning') {
        const meaning = RESULT_WORDS.get(text.toLowerCase());
        if (meaning) body[field] = meaning;
        else problems.push(`RECORD_RESULT "${text}" is not a known result; use succeeded, partly-succeeded, failed or not-deployed`);
      } else if (field === 'stageOrderRun') {
        const stages = text.split(',').map(s => s.trim()).filter(Boolean);
        if (stages.length) body[field] = stages;
      } else if (field === 'reportedApprovals') {
        try {
          const list = JSON.parse(text);
          if (!Array.isArray(list)) problems.push('RECORD_REPORTED_APPROVALS_JSON must be a JSON array');
          else if (list.length) body[field] = list;
        } catch (e) {
          problems.push(`RECORD_REPORTED_APPROVALS_JSON is not JSON: ${e.message}`);
        }
      } else {
        body[field] = text;
      }
    }
    if (!body.finishedOn) body.finishedOn = nowUtc();
  }
  for (const field of REQUIRED_FIELDS) {
    if (body[field] === undefined) problems.push(`${field} is missing (${variableFor(field)})`);
  }
  const targets = ['deploymentTargetId', 'targetExternalId'].filter(f => body[f] !== undefined);
  if (targets.length !== 1) problems.push('give exactly one of RECORD_TARGET_EXTERNAL_ID and RECORD_DEPLOYMENT_TARGET_ID');
  if (body.pipelineReferenceId !== undefined && body.pipelineExternalId !== undefined) {
    problems.push('give at most one of RECORD_PIPELINE_EXTERNAL_ID and RECORD_PIPELINE_REFERENCE_ID');
  }
  return { body, problems, notes };
}

function variableFor(field) {
  return Object.keys(FIELD_VARIABLES).find(k => FIELD_VARIABLES[k] === field) || field;
}

// Checks a URL the script will send a token or a body to. Returns an error text, or null when it is fine.
export function checkUrl(raw, name) {
  let url;
  try {
    url = new URL(raw);
  } catch {
    return `${name} is not a URL`;
  }
  if (url.username || url.password) return `${name} must not carry a user name or password`;
  for (const key of url.searchParams.keys()) {
    if (['code', 'sig', 'key', 'apikey', 'api-key', 'access_token'].includes(key.toLowerCase())) {
      return `${name} carries a key in its query (${key}); use workload identity federation, never a key or secret`;
    }
  }
  if (isSecureScheme(url)) return null;
  return `${name} must use https (plain http is allowed only for a local stub on this machine)`;
}

// https, or plain http to this machine (the local stub). Never plain http to another host.
const LOOPBACK_HOSTS = Object.freeze(['127.0.0.1', 'localhost', '[::1]']);
function isSecureScheme(url) {
  return url.protocol === 'https:' || (url.protocol === 'http:' && LOOPBACK_HOSTS.includes(url.hostname));
}

// The allowlist for the bearer token: exactly one origin (scheme, host and port), taken from RECORD_ENDPOINT_URL.
// Empty when that URL is not https (or http to this machine) or carries a user name, so every send is refused.
export function tokenAllowlist(endpointUrl) {
  let url;
  try {
    url = new URL(endpointUrl);
  } catch {
    return Object.freeze([]);
  }
  if (url.username || url.password || !isSecureScheme(url)) return Object.freeze([]);
  return Object.freeze([url.origin]);
}

// Checks one URL the script is about to send the bearer token to. Returns an error text, or null when its scheme
// is https (or http to this machine) and its origin is on the allowlist. Another host, another port, another
// scheme, a look-alike host name and a user name in the URL are all refused.
export function checkTokenTarget(raw, allowlist) {
  let url;
  try {
    url = new URL(raw);
  } catch {
    return 'the token target is not a URL';
  }
  if (url.username || url.password) return 'the token target must not carry a user name or password';
  if (!isSecureScheme(url)) return `the token goes over https only, never ${url.protocol.replace(/:$/, '')} to ${url.host}`;
  const allowed = Array.isArray(allowlist) ? allowlist : [];
  if (!allowed.includes(url.origin)) {
    return `the token goes only to ${allowed.length ? allowed.join(', ') : 'the origin of RECORD_ENDPOINT_URL'}, never to ${url.origin}`;
  }
  return null;
}

class TokenTargetRefused extends Error {}

// The only call that carries the bearer token. It refuses a URL off the allowlist before any byte is sent, and
// it never follows a redirect: a 3xx answer comes back to the caller as it is.
async function fetchWithToken(url, init, allowlist) {
  const refused = checkTokenTarget(url, allowlist);
  if (refused) throw new TokenTargetRefused(refused);
  return fetch(url, { ...init, redirect: 'manual' });
}

// Says where a redirect pointed, by origin only, and that it was not followed.
function describeRedirect(location, endpointUrl) {
  let target = null;
  try {
    if (location) target = new URL(location, endpointUrl);
  } catch {
    target = null;
  }
  if (!target) return 'the endpoint answered with a redirect; the script follows no redirect';
  if (target.origin === new URL(endpointUrl).origin) {
    return 'the endpoint answered with a redirect to another path on the same host; the script follows no redirect';
  }
  return `the endpoint answered with a redirect to another host (${oneLine(target.origin, 200)}); the script follows no redirect, so the token and the body were not sent there`;
}

// The endpoint shape follows from the URL: a Dataverse Web API action path means the Dataverse binding.
export function isDataverseUrl(raw) {
  try {
    return /\/api\/data\/v\d+(\.\d+)*\//i.test(new URL(raw).pathname);
  } catch {
    return false;
  }
}

export function defaultScope(endpointUrl) {
  return `${new URL(endpointUrl).origin}/.default`;
}

function oneLine(text, max = 400) {
  return String(text ?? '').replace(/[\r\n\t]+/g, ' ').replace(/\s{2,}/g, ' ').trim().slice(0, max);
}

// Log lines in the platform's own format. Server text is flattened to one line, so it cannot start a
// logging command of its own.
export function makeLog(env, write = line => console.log(line)) {
  const ado = Boolean(readVariable(env, 'TF_BUILD'));
  const gh = String(env.GITHUB_ACTIONS || '').toLowerCase() === 'true';
  const ghEscape = s => oneLine(s, 2000).replace(/%/g, '%25');
  return {
    platform: ado ? 'azure-devops' : gh ? 'github' : 'plain',
    info: m => write(`Record step: ${oneLine(m, 2000)}`),
    warn: m => {
      if (ado) write(`##vso[task.logissue type=warning]Record step: ${oneLine(m, 2000)}`);
      else if (gh) write(`::warning title=Record step::${ghEscape(m)}`);
      else write(`WARNING: Record step: ${oneLine(m, 2000)}`);
    },
    mask: token => {
      if (ado) write(`##vso[task.setsecret]${token}`);
      else if (gh) write(`::add-mask::${token}`);
    },
    output: (value) => {
      if (!value) return;
      if (ado) write(`##vso[task.setvariable variable=deploymentRecordId;isOutput=true]${oneLine(value, 100)}`);
      else if (gh && env.GITHUB_OUTPUT) {
        try {
          fs.appendFileSync(env.GITHUB_OUTPUT, `deployment-record-id=${oneLine(value, 100)}\n`);
        } catch {
          // An output is a convenience. Never fail for it.
        }
      }
    },
  };
}

function boundedInt(env, name, fallback, min, max) {
  const text = readVariable(env, name);
  if (text === undefined) return fallback;
  const n = Number.parseInt(text, 10);
  return Number.isFinite(n) ? Math.min(max, Math.max(min, n)) : fallback;
}

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

// azure-cli: the AzureCLI@2 task signed in with the service connection's workload identity federation.
function tokenFromAzureCli(scope, env) {
  const args = ['account', 'get-access-token', '--scope', scope, '--query', 'accessToken', '--output', 'tsv'];
  const windows = process.platform === 'win32';
  // On Windows the CLI is az.cmd, which needs a shell. The scope is checked against SCOPE_RE first, so it
  // holds no shell character.
  const r = windows
    ? spawnSync(`az ${args.join(' ')}`, { env, encoding: 'utf8', shell: true, timeout: 60000, windowsHide: true })
    : spawnSync('az', args, { env, encoding: 'utf8', timeout: 60000 });
  if (r.error) throw new Error(`cannot start the Azure CLI: ${r.error.message}`);
  if (r.status !== 0) {
    throw new Error(`the Azure CLI gave no token (exit ${r.status}): ${oneLine(r.stderr, 300)}. Run this step in an AzureCLI@2 task with a workload identity federation service connection`);
  }
  const token = String(r.stdout || '').trim();
  if (!token) throw new Error('the Azure CLI returned an empty token');
  return token;
}

// github-oidc: GitHub's OIDC token for the job, exchanged with Microsoft Entra ID (client credentials with a
// federated assertion). Needs "permissions: id-token: write". No secret is stored anywhere.
async function tokenFromGitHubOidc(scope, env, timeoutMs) {
  const requestUrl = readVariable(env, 'ACTIONS_ID_TOKEN_REQUEST_URL');
  const requestToken = readVariable(env, 'ACTIONS_ID_TOKEN_REQUEST_TOKEN');
  if (!requestUrl || !requestToken) {
    throw new Error('GitHub gave this job no OIDC token request; add "permissions: id-token: write" to the workflow or the job');
  }
  const urlProblem = checkUrl(requestUrl, 'ACTIONS_ID_TOKEN_REQUEST_URL');
  if (urlProblem) throw new Error(urlProblem);
  const tenant = readVariable(env, 'RECORD_TENANT_ID');
  const clientId = readVariable(env, 'RECORD_CLIENT_ID');
  if (!tenant || !TENANT_RE.test(tenant)) throw new Error('RECORD_TENANT_ID is missing or not a tenant ID');
  if (!clientId || !GUID_RE.test(clientId)) throw new Error('RECORD_CLIENT_ID is missing or not an application (client) ID');
  const authority = (readVariable(env, 'RECORD_AUTHORITY_HOST') || DEFAULT_AUTHORITY).replace(/\/+$/, '');
  const authorityProblem = checkUrl(authority, 'RECORD_AUTHORITY_HOST');
  if (authorityProblem) throw new Error(authorityProblem);

  const oidcUrl = `${requestUrl}${requestUrl.includes('?') ? '&' : '?'}audience=${encodeURIComponent(TOKEN_EXCHANGE_AUDIENCE)}`;
  const oidc = await fetch(oidcUrl, {
    headers: { Authorization: `bearer ${requestToken}`, Accept: 'application/json' },
    signal: AbortSignal.timeout(timeoutMs),
    redirect: 'manual',
  });
  if (oidc.status !== 200) throw new Error(`GitHub gave no OIDC token (HTTP ${oidc.status})`);
  const assertion = (await oidc.json().catch(() => null))?.value;
  if (!assertion) throw new Error('GitHub answered without an OIDC token');

  const form = new URLSearchParams({
    client_id: clientId,
    scope,
    grant_type: 'client_credentials',
    client_assertion_type: 'urn:ietf:params:oauth:client-assertion-type:jwt-bearer',
    client_assertion: assertion,
  });
  const entra = await fetch(`${authority}/${encodeURIComponent(tenant)}/oauth2/v2.0/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded', Accept: 'application/json' },
    body: form,
    signal: AbortSignal.timeout(timeoutMs),
    redirect: 'manual',
  });
  const answer = await entra.json().catch(() => null);
  if (entra.status !== 200 || !answer?.access_token) {
    const why = answer?.error ? `${answer.error}: ${oneLine(answer.error_description, 300)}` : 'no token in the answer';
    throw new Error(`Microsoft Entra ID gave no token (HTTP ${entra.status}, ${why}). Check the federated credential's issuer, subject and audience`);
  }
  return answer.access_token;
}

async function getToken(mode, scope, env, timeoutMs) {
  if (mode === 'azure-cli') return tokenFromAzureCli(scope, env);
  if (mode === 'github-oidc') return tokenFromGitHubOidc(scope, env, timeoutMs);
  if (mode === 'token') {
    const token = readVariable(env, 'RECORD_ACCESS_TOKEN');
    if (!token) throw new Error('RECORD_AUTH is token, but RECORD_ACCESS_TOKEN is empty');
    return token;
  }
  throw new Error(`RECORD_AUTH "${mode}" is not known; use azure-cli, github-oidc or token`);
}

// One POST, retried only where errors.json says a retry may help (rate_limited, upstream_unavailable) and on
// transport faults. configuration_unavailable is not retried: an admin must fix the configuration first.
// A redirect is not followed and not retried. A URL off the token allowlist is refused before anything is sent.
export async function postWithRetries(endpointUrl, body, token, { timeoutMs, maxAttempts, log, allowlist }) {
  const headers = {
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json',
    Accept: 'application/json',
    'Idempotency-Key': body.idempotencyKey,
  };
  if (isDataverseUrl(endpointUrl)) {
    headers['OData-MaxVersion'] = '4.0';
    headers['OData-Version'] = '4.0';
  }
  const payload = JSON.stringify(body); // built once: every attempt sends the same bytes
  let last = null;
  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    let res;
    let text = '';
    try {
      res = await fetchWithToken(endpointUrl, { method: 'POST', headers, body: payload, signal: AbortSignal.timeout(timeoutMs) }, allowlist);
      text = await res.text();
    } catch (e) {
      if (e instanceof TokenTargetRefused) return { ok: false, status: 0, code: 'token_target', message: e.message, attempt: 0 };
      const why = e?.name === 'TimeoutError' ? `no answer within ${timeoutMs / 1000} s` : oneLine(e?.cause?.message || e?.message, 200).replace(/[.\s]+$/, '');
      last = { ok: false, status: 0, code: 'transport', message: why, attempt };
      if (attempt < maxAttempts) {
        const wait = Math.min(10, 2 ** (attempt - 1));
        log.info(`attempt ${attempt} reached no endpoint (${why}); trying again in ${wait} s with the same idempotency key`);
        await sleep(wait * 1000);
        continue;
      }
      return last;
    }
    let json = null;
    try {
      json = text ? JSON.parse(text) : null;
    } catch {
      json = null;
    }
    if (res.status === 200 || res.status === 201) return { ok: true, status: res.status, json, attempt };
    if (res.status >= 300 && res.status < 400) {
      return { ok: false, status: res.status, code: 'redirect', message: describeRedirect(res.headers.get('location'), endpointUrl), attempt };
    }
    const code = json?.error?.code ? String(json.error.code) : '';
    const message = oneLine(json?.error?.message || json?.summary || text || res.statusText, 400).replace(/[.\s]+$/, '');
    last = { ok: false, status: res.status, code, message, attempt };
    const retryable = res.status === 429 || res.status === 502 || res.status === 504
      || (res.status === 503 && code !== 'configuration_unavailable');
    if (!retryable || attempt >= maxAttempts) return last;
    const header = Number.parseInt(res.headers.get('retry-after') || '', 10);
    const fromBody = Number.parseInt(json?.error?.retryAfterSeconds ?? '', 10);
    const wait = Math.min(30, Math.max(1, Number.isFinite(fromBody) ? fromBody : Number.isFinite(header) ? header : 2 ** attempt));
    log.info(`attempt ${attempt} got HTTP ${res.status}${code ? ` ${code}` : ''}; trying again in ${wait} s with the same idempotency key`);
    await sleep(wait * 1000);
  }
  return last;
}

const HINTS = {
  0: 'The endpoint could not be reached. Check RECORD_ENDPOINT_URL and the agent or runner network.',
  400: 'The body does not match the record endpoint contract. Fix the field the message names.',
  401: 'The token was refused. Check RECORD_ENDPOINT_SCOPE and the federated credential.',
  403: 'The identity lacks the Deployment.Record app role (or the deployment recorder role on Dataverse), or its workload grant is missing or expired.',
  404: 'An ID in the body does not exist, such as RECORD_CHANGE_REQUEST_ID.',
  409: 'The idempotency key was used before with a different body. Make the key unique per deployment: run, stage, job and attempt.',
  422: 'A configuration rule refused the record.',
  429: 'Too many calls. A later run records normally.',
  503: 'The product has no valid active configuration, or a service it needs is down. Tell the platform admin.',
};
const REDIRECT_HINT = 'Set RECORD_ENDPOINT_URL to the record endpoint\'s own URL. The token goes only to that URL\'s origin, and no redirect is followed.';

function readResult(json) {
  if (json?.ok === true && json.data && typeof json.data === 'object') return json.data; // the envelope
  if (typeof json?.resultJson === 'string') {
    try {
      return JSON.parse(json.resultJson); // the Dataverse custom API answer: data as a JSON string
    } catch {
      return null;
    }
  }
  return null;
}

export async function main(env = process.env, write = line => console.log(line)) {
  const log = makeLog(env, write);
  const notRecorded = why => log.warn(`The deployment was not recorded: ${why} The deployment itself is not affected.`);

  const endpointUrl = readVariable(env, 'RECORD_ENDPOINT_URL');
  if (!endpointUrl) {
    notRecorded('RECORD_ENDPOINT_URL is not set.');
    return { recorded: false };
  }
  const urlProblem = checkUrl(endpointUrl, 'RECORD_ENDPOINT_URL');
  if (urlProblem) {
    notRecorded(`${urlProblem}.`);
    return { recorded: false };
  }
  const scope = readVariable(env, 'RECORD_ENDPOINT_SCOPE') || defaultScope(endpointUrl);
  if (!SCOPE_RE.test(scope)) {
    notRecorded(`RECORD_ENDPOINT_SCOPE "${oneLine(scope, 200)}" is not an app ID URI ending in /.default.`);
    return { recorded: false };
  }

  const { body, problems, notes } = buildBody(env);
  for (const note of notes) log.warn(note);
  if (problems.length) {
    notRecorded(`${problems.join('; ')}.`);
    return { recorded: false };
  }

  const mode = readVariable(env, 'RECORD_AUTH') || (log.platform === 'azure-devops' ? 'azure-cli' : log.platform === 'github' ? 'github-oidc' : 'token');
  const timeoutMs = boundedInt(env, 'RECORD_TIMEOUT_SECONDS', 20, 1, 120) * 1000;
  const maxAttempts = boundedInt(env, 'RECORD_MAX_ATTEMPTS', 3, 1, 5);
  const target = body.targetExternalId || body.deploymentTargetId;
  log.info(`recording ${body.artifactName} ${body.artifactVersion} on ${target} (${body.resultMeaning}), idempotency key ${body.idempotencyKey}, endpoint ${endpointUrl}${isDataverseUrl(endpointUrl) ? ' (Dataverse Web API)' : ''}`);

  let token;
  try {
    token = await getToken(mode, scope, env, timeoutMs);
  } catch (e) {
    notRecorded(`no token (${mode}): ${e.message}.`);
    return { recorded: false };
  }
  log.mask(token);

  const result = await postWithRetries(endpointUrl, body, token, { timeoutMs, maxAttempts, log, allowlist: tokenAllowlist(endpointUrl) });
  if (!result.ok) {
    if (result.code === 'token_target') {
      notRecorded(`nothing was sent: ${result.message}.`);
      return { recorded: false, status: 0, code: result.code };
    }
    const what = result.status ? `HTTP ${result.status}${result.code ? ` ${result.code}` : ''}` : 'no answer';
    const hint = result.code === 'redirect' ? REDIRECT_HINT : HINTS[result.status] || '';
    notRecorded(`${what} after ${result.attempt} attempt(s): ${result.message}. ${hint} A retry with the same idempotency key is safe.`);
    return { recorded: false, status: result.status, code: result.code };
  }

  const data = readResult(result.json);
  const id = data?.deploymentRecordId || data?.recordId || result.json?.recordId || '';
  const created = data?.created;
  const how = created === false ? 'an earlier call with the same idempotency key already wrote it, nothing new was written' : 'a new record was written';
  log.info(`recorded${id ? ` as ${id}` : ''}: ${how} (HTTP ${result.status}, attempt ${result.attempt})${data?.gateLevelApplied ? `, gate level ${data.gateLevelApplied}` : ''}.`);
  if (!data) log.warn('The record was accepted, but the answer could not be read.');
  const flags = Object.entries(data?.flags || {}).filter(([, on]) => on === true).map(([name]) => name);
  if (flags.length) log.warn(`The record is flagged: ${flags.join(', ')}. This never fails the deployment; the owner sees the flag in the product.`);
  log.output(id);
  return { recorded: true, status: result.status, id, created };
}

// Run as a script: never exit with an error.
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
  const soft = e => {
    makeLog(process.env).warn(`The deployment was not recorded: unexpected error: ${e?.message || e}. The deployment itself is not affected.`);
    process.exitCode = 0;
  };
  process.on('uncaughtException', soft);
  process.on('unhandledRejection', soft);
  main().then(() => { process.exitCode = 0; }, soft);
}
