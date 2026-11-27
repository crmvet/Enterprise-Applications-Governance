// rehearse.mjs: "demo local age-request" and "demo local fail-card", two rehearsal and test aids for a local run only
// (wave 4b QA). On a fresh local store every story request is days from its expiry and no approval card has failed,
// so the web app rightly hides "Ask again" and "Send again". These commands edit the folder's SQLite store directly, past
// the engine, so a presenter can rehearse those two actions and the end-to-end suite can perform them for real:
//   age-request --decision <number> [--hours 40] [--role <key>]
//       one pending request of that decision now expires in that many hours (1 to 47): inside the last 48 hours, in
//       which the web app offers "Ask again" and the engine asks the request again;
//   fail-card --decision <number> [--role <key>]
//       the approval outbox row of one pending request of that decision is failed, after its attempts, so the Admin
//       page's approval outbox offers "Send again" to a platform admin;
//   fail-card --decision <number> --settled [--role <key>]
//       the closed outbox row of one request of that decision that is answered, canceled or expired is failed, so the
//       Admin page offers "Close the card" for a card whose request needs no card any more. Live, the engine closes a card in the
//       same commit as the answer, so only this aid reaches that state (wave 4b, S-23).
// Both refuse anything but a local run: the folder's state.json must name the SQLite store "demo local up" created,
// the base must be a loopback http URL, and a server that answers must offer the development sign-in, not a demo
// install's persona switch. They never reach Azure SQL. The audit trail does not show these edits, and the changed row's
// Modified On is later than its Engine Checked On, as for any write outside the engine. Not for a real install.
import { existsSync } from 'node:fs';
import { UsageError } from './args.mjs';
import { isLoopback } from './client.mjs';

// The engine's window for asking a request again early (EngineRuntime.RenewWindow), and the approval runner's default
// attempts before a card fails (Product:ApprovalRunner:MaxAttempts).
export const RENEW_WINDOW_HOURS = 48;
export const MAX_ATTEMPTS = 3;
export const DEFAULT_HOURS = 40;

const REFUSED = 'This command edits the store of a local run only, never a demo install or any other install';

// "DEC-0206", "dec-0206" or "206" give "DEC-0206".
export function decisionNumber(text) {
  const t = String(text ?? '').trim();
  if (/^\d{1,6}$/.test(t)) return 'DEC-' + t.padStart(4, '0');
  if (/^DEC-\d{1,6}$/i.test(t)) return t.toUpperCase();
  throw new UsageError('--decision takes a decision number, such as DEC-0206.');
}

// A time as the engine and the outbox write it: UTC with seven fractional digits.
export function engineTime(date) {
  return date.toISOString().replace(/\.(\d{3})Z$/, '.$10000Z');
}

// node:sqlite, without its experimental warning on the presenter's screen. Node.js 22.13 or later has it unflagged.
async function loadSqlite() {
  const emit = process.emitWarning;
  process.emitWarning = (warning, ...rest) => (/SQLite/i.test(String(warning?.message ?? warning)) ? undefined : emit.call(process, warning, ...rest));
  try {
    return await import('node:sqlite');
  } catch {
    throw new UsageError('This command needs Node.js 22.13 or later, for its built-in node:sqlite module.');
  } finally {
    process.emitWarning = emit;
  }
}

async function readConfig(base, fetchImpl) {
  try {
    const r = await fetchImpl(new URL('/app/config.json', base), { signal: AbortSignal.timeout(5000) });
    if (r.status !== 200) return null;
    return await r.json();
  } catch {
    return null;
  }
}

// The checks before any edit. Returns the store file and whether a server answered.
export async function localTarget(args, { folder, state }, { fetchImpl = fetch } = {}) {
  if (args.text('--mode') === 'demo') throw new UsageError(REFUSED + ': --mode demo names a demo install.');
  if (!state) throw new UsageError(folder + ' has no state.json: no local server was started from it. Run "demo local up" first.');
  const given = args.text('--base');
  let base;
  try {
    base = new URL(given ?? state.base);
  } catch {
    throw new UsageError('--base is not a URL.');
  }
  if (!isLoopback(base) || base.protocol !== 'http:') throw new UsageError(REFUSED + ': ' + base.origin + ' is not a local server on a loopback http URL.');
  if (given && state.base && base.origin !== new URL(state.base).origin) throw new UsageError('--base ' + base.origin + ' is not the server this folder started (' + new URL(state.base).origin + ').');
  if (!state.db || !/\.db$/i.test(state.db) || !existsSync(state.db)) throw new UsageError(REFUSED + ': state.json names no SQLite store of a local run.');
  const config = await readConfig(base, fetchImpl);
  if (config && (config.auth?.mode !== 'development' || (config.demoPersonas ?? []).length > 0)) {
    throw new UsageError(REFUSED + ': the server at ' + base.origin + ' is a demo install (it offers the persona switch, not the development sign-in).');
  }
  return { db: state.db, base, serverAnswered: !!config };
}

async function openStore(file) {
  const { DatabaseSync } = await loadSqlite();
  const db = new DatabaseSync(file);
  db.exec('PRAGMA busy_timeout = 10000');
  const tables = new Set(db.prepare('SELECT "name" FROM "sqlite_master" WHERE "type" = \'table\'').all().map(r => r.name));
  if (!tables.has('Row') || !tables.has('ApprovalOutbox')) {
    db.close();
    throw new UsageError(REFUSED + ': the file state.json names is not the product\'s SQLite store.');
  }
  return db;
}

const parsed = row => ({ ...row, columns: JSON.parse(row.Columns) });

// Who a request asks, in words: a role, an owner role or a named person.
export function askedText(c) {
  if (c.requiredRole) return 'the role ' + c.requiredRole;
  if (c.requiredOwnerRole) return 'the owner role ' + c.requiredOwnerRole;
  return 'a named person';
}

// The decision by its number, and its pending requests, oldest first; --role narrows them to one role or owner role.
function decisionAndRequests(db, number, role) {
  const decision = db.prepare('SELECT "Id", "Columns" FROM "Row" WHERE "Entity" = \'decision\'').all().map(parsed)
    .find(d => String(d.columns.recordNumber ?? '').toUpperCase() === number);
  if (!decision) throw new UsageError('The store has no decision ' + number + '.');
  const meaning = decision.columns.statusMeaning;
  if (meaning !== 'proposed') throw new UsageError(number + ' is ' + (meaning ?? 'not proposed') + ', so none of its requests waits for an answer. Pick a proposed decision.');
  const id = String(decision.Id).toLowerCase();
  const all = db.prepare('SELECT "Id", "Columns", "Version" FROM "Row" WHERE "Entity" = \'approval-record\'').all().map(parsed)
    .filter(r => String(r.columns.decision ?? '').toLowerCase() === id && r.columns.outcomeMeaning === 'pending')
    .sort((a, b) => String(a.columns.requestedOn ?? '').localeCompare(String(b.columns.requestedOn ?? '')) || String(a.Id).localeCompare(String(b.Id)));
  const requests = role ? all.filter(r => r.columns.requiredRole === role || r.columns.requiredOwnerRole === role) : all;
  if (!all.length) throw new UsageError(number + ' has no pending request.');
  if (!requests.length) throw new UsageError(number + ' has no pending request that asks ' + role + '. It asks ' + [...new Set(all.map(r => askedText(r.columns)))].join(', ') + '.');
  return { decision, requests, pendingCount: all.length };
}

const when = iso => String(iso ?? '').replace('T', ' ').replace(/:\d{2}(\.\d+)?Z$/, ' UTC');

// "demo local age-request": one pending request now expires within the 48 hours in which "Ask again" shows.
export async function ageRequest(args, out, target, { now = Date.now() } = {}) {
  const number = decisionNumber(args.text('--decision'));
  const hours = args.number('--hours', DEFAULT_HOURS, { min: 1, max: RENEW_WINDOW_HOURS - 1 });
  const db = await openStore(target.db);
  try {
    const { decision, requests, pendingCount } = decisionAndRequests(db, number, args.text('--role'));
    const timed = requests.filter(r => r.columns.expiresOn);
    if (!timed.length) throw new UsageError(number + '\'s pending requests have no expiry, so "Ask again" never shows for them. Pick a decision whose rule sets a time limit.');
    const request = timed[0];
    const expiresOn = engineTime(new Date(now + hours * 3600 * 1000));
    const columns = { ...request.columns, expiresOn };
    const r = db.prepare('UPDATE "Row" SET "Columns" = ?, "Version" = "Version" + 1, "ModifiedOn" = ? WHERE "Entity" = \'approval-record\' AND "Id" = ? AND "Version" = ?')
      .run(JSON.stringify(columns), new Date(now).toISOString(), request.Id, request.Version);
    if (r.changes !== 1) throw new Error('Another write changed the request first. Run the command again.');
    const result = {
      command: 'age-request', decision: { recordNumber: number, statement: decision.columns.statement ?? null }, approvalRecordId: request.Id,
      asked: askedText(request.columns), previousExpiresOn: request.columns.expiresOn, expiresOn, hoursLeft: hours, pendingRequests: pendingCount, serverAnswered: target.serverAnswered,
    };
    if (args.flag('--json')) {
      out.log(JSON.stringify(result, null, 2));
    } else {
      out.log('Rehearsal aid, local run only: ' + number + ' "' + (decision.columns.statement ?? '') + '".');
      out.log('  The request that asks ' + result.asked + ' now expires ' + when(expiresOn) + ', in ' + hours + ' hours (before: ' + when(result.previousExpiresOn) + ').');
      if (pendingCount > 1) out.log('  ' + number + ' has ' + pendingCount + ' pending requests; this is the oldest' + (args.text('--role') ? ' that asks ' + args.text('--role') : '') + '. --role <key> picks another.');
      out.log('  Its proposer and a platform admin now see "Ask again" on the decision page; the proposer also under Waiting on others in My work.');
      out.log('  The store was edited directly, past the engine: the audit trail does not show this change.');
    }
    return 0;
  } finally {
    db.close();
  }
}

// The requests of a decision that are settled (answered, canceled or expired), oldest first, for fail-card --settled.
function settledRequests(db, number, role) {
  const decision = db.prepare('SELECT "Id", "Columns" FROM "Row" WHERE "Entity" = \'decision\'').all().map(parsed)
    .find(d => String(d.columns.recordNumber ?? '').toUpperCase() === number);
  if (!decision) throw new UsageError('The store has no decision ' + number + '.');
  const id = String(decision.Id).toLowerCase();
  const all = db.prepare('SELECT "Id", "Columns", "Version" FROM "Row" WHERE "Entity" = \'approval-record\'').all().map(parsed)
    .filter(r => String(r.columns.decision ?? '').toLowerCase() === id && r.columns.outcomeMeaning &&r.columns.outcomeMeaning !== 'pending')
    .sort((a, b) => String(a.columns.requestedOn ?? '').localeCompare(String(b.columns.requestedOn ?? '')) || String(a.Id).localeCompare(String(b.Id)));
  const requests = role ? all.filter(r => r.columns.requiredRole === role || r.columns.requiredOwnerRole === role) : all;
  if (!requests.length) throw new UsageError(number + ' has no answered, canceled or expired request' + (role ? ' that asks ' + role : '') + '.');
  return { decision, requests };
}

// "demo local fail-card": the outbox row of one pending request fails, so the Admin page offers "Send again".
export async function failCard(args, out, target, { now = Date.now() } = {}) {
  const number = decisionNumber(args.text('--decision'));
  if (args.flag('--settled')) return failSettledCard(args, out, target, number, now);
  const db = await openStore(target.db);
  try {
    const { decision, requests } = decisionAndRequests(db, number, args.text('--role'));
    const outbox = new Map(db.prepare('SELECT "ApprovalRecordId", "State", "Attempts", "RowVersion" FROM "ApprovalOutbox"').all().map(o => [String(o.ApprovalRecordId).toLowerCase(), o]));
    const open = ['pending', 'claimed', 'sent', 'failed'];
    const request = requests.find(r => open.includes(outbox.get(String(r.Id).toLowerCase())?.State));
    if (!request) throw new UsageError(number + ' has no pending request with an open card in the approval outbox.');
    const row = outbox.get(String(request.Id).toLowerCase());
    const attempts = Math.max(Number(row.Attempts) || 0, MAX_ATTEMPTS);
    if (row.State !== 'failed') {
      const r = db.prepare('UPDATE "ApprovalOutbox" SET "State" = \'failed\', "ClaimId" = NULL, "ClaimedUntil" = NULL, "Attempts" = ?, "UpdatedOn" = ?, "RowVersion" = "RowVersion" + 1 WHERE "ApprovalRecordId" = ? AND "RowVersion" = ?')
        .run(attempts, engineTime(new Date(now)), row.ApprovalRecordId, row.RowVersion);
      if (r.changes !== 1) throw new Error('Another write changed the card first. Run the command again.');
    }
    const result = {
      command: 'fail-card', decision: { recordNumber: number, statement: decision.columns.statement ?? null }, approvalRecordId: request.Id,
      asked: askedText(request.columns), previousState: row.State, state: 'failed', attempts: row.State === 'failed' ? Number(row.Attempts) : attempts, serverAnswered: target.serverAnswered,
    };
    if (args.flag('--json')) {
      out.log(JSON.stringify(result, null, 2));
    } else {
      out.log('Rehearsal aid, local run only: ' + number + ' "' + (decision.columns.statement ?? '') + '".');
      out.log(row.State === 'failed'
        ? '  The card of the request that asks ' + result.asked + ' had failed already. Nothing changed.'
        : '  The card of the request that asks ' + result.asked + ' is failed now, after ' + attempts + ' attempts (it was ' + row.State + ').');
      out.log('  A platform admin now sees "Send again" for it in the Admin page\'s approval outbox, under "Requests that need a look".');
      if (row.State !== 'failed') out.log('  The store was edited directly, past the engine: the audit trail does not show this change.');
    }
    return 0;
  } finally {
    db.close();
  }
}

// "fail-card --settled": a closed card of a settled request turns failed, so "Close the card" shows for it on the Admin page.
async function failSettledCard(args, out, target, number, now) {
  const db = await openStore(target.db);
  try {
    const { decision, requests } = settledRequests(db, number, args.text('--role'));
    const outbox = new Map(db.prepare('SELECT "ApprovalRecordId", "State", "Attempts", "RowVersion" FROM "ApprovalOutbox"').all().map(o => [String(o.ApprovalRecordId).toLowerCase(), o]));
    const request = requests.find(r => ['answered', 'cancelled', 'expired', 'failed'].includes(outbox.get(String(r.Id).toLowerCase())?.State));
    if (!request) throw new UsageError(number + ' has no settled request with a card in the approval outbox.');
    const row = outbox.get(String(request.Id).toLowerCase());
    const attempts = Math.max(Number(row.Attempts) || 0, MAX_ATTEMPTS);
    if (row.State !== 'failed') {
      const r = db.prepare('UPDATE "ApprovalOutbox" SET "State" = \'failed\', "ClaimId" = NULL, "ClaimedUntil" = NULL, "Attempts" = ?, "UpdatedOn" = ?, "RowVersion" = "RowVersion" + 1 WHERE "ApprovalRecordId" = ? AND "RowVersion" = ?')
        .run(attempts, engineTime(new Date(now)), row.ApprovalRecordId, row.RowVersion);
      if (r.changes !== 1) throw new Error('Another write changed the card first. Run the command again.');
    }
    const result = {
      command: 'fail-card', decision: { recordNumber: number, statement: decision.columns.statement ?? null }, approvalRecordId: request.Id,
      asked: askedText(request.columns), outcome: request.columns.outcomeMeaning, previousState: row.State, state: 'failed',
      attempts: row.State === 'failed' ? Number(row.Attempts) : attempts, serverAnswered: target.serverAnswered,
    };
    if (args.flag('--json')) {
      out.log(JSON.stringify(result, null, 2));
    } else {
      out.log('Rehearsal aid, local run only: ' + number + ' "' + (decision.columns.statement ?? '') + '".');
      out.log(row.State === 'failed'
        ? '  The card of the settled request that asks ' + result.asked + ' had failed already. Nothing changed.'
        : '  The card of the request that asks ' + result.asked + ', which is ' + result.outcome + ', is failed now (it was ' + row.State + ').');
      out.log('  A platform admin now sees "Close the card" for it in the Admin page\'s approval outbox: the request needs no card any more.');
      if (row.State !== 'failed') out.log('  The store was edited directly, past the engine: the audit trail does not show this change.');
    }
    return 0;
  } finally {
    db.close();
  }
}

export async function rehearse(verb, args, out, local, options = {}) {
  const target = await localTarget(args, local, options);
  return verb === 'age-request' ? ageRequest(args, out, target, options) : failCard(args, out, target, options);
}
