// refresh.mjs: "demo refresh --base <url>". Before a demo, no story approval request may have expired or be about to.
//   1. get_demo_freshness lists the story's requests that expired, or expire within --within-hours (default 48).
//   2. renew_approval_request ("Ask again", tool 100) asks each one again, as the platform admin persona owen (--as
//      names another; the proposer of a decision may also ask again). The engine closes the old request and opens the
//      new one with the expiry its rule gives. A request asked again already is left as it is.
//   3. The demo director runs again. It checks before it creates, so it adds only what is missing.
//   4. get_demo_freshness again: the demo is fresh, or the kit names what is left.
// --dry-run sends each "Ask again" with validateOnly and skips the director, so nothing is written.
import { UsageError } from './args.mjs';
import { Refused, connection, createClient, plain } from './client.mjs';
import { runDirector } from './local.mjs';
import { count } from './print.mjs';

// A role's label inside a sentence: "the business owner", but "the AI lead" and "the PMO".
const inSentence = label => (/^[A-Z]{2}/.test(label) ? label : label.charAt(0).toLowerCase() + label.slice(1));

function asked(request) {
  if (!request) return 'nobody';
  const person = request.askedPerson?.displayLabel;
  const role = request.askedOwnerRole?.label ?? request.askedRole?.label;
  return (person ? person + ' by name' : role ? 'the ' + inSentence(String(role)) : 'the same role') + (request.isConsultation ? ' (consulted)' : '');
}

function describe(item) {
  const d = item.decision ?? {};
  return (d.recordNumber ? d.recordNumber + ' ' : '') + '"' + plain(d.statement, 90) + '"' + (item.application?.name ? ' on ' + item.application.name : '');
}

export async function freshness(client, persona, withinHours) {
  try {
    return (await client.call(persona, 'get_demo_freshness', { withinHours })).data;
  } catch (e) {
    if (e instanceof Refused && e.code === 'not_found') {
      throw new UsageError('The server answers get_demo_freshness with not_found: it is not a demo install. Run the server with --Product:WebApp:Demo=true (a local run in Development) or in the Demo environment.');
    }
    throw e;
  }
}

export async function refresh(args, out, { env = process.env, fetchImpl } = {}) {
  const c = connection(args, { env });
  const client = createClient({ ...c, ...(fetchImpl ? { fetchImpl } : {}) });
  const persona = args.text('--as', 'owen');
  // "Ask again" takes a request in its last 48 hours only (document 25, B25-24), so the kit looks no further ahead.
  const withinHours = args.number('--within-hours', 48, { min: 1, max: 48 });
  const dryRun = args.flag('--dry-run');
  const json = args.flag('--json');
  const log = line => { if (!json) out.log(line); };

  log('Demo refresh: ' + (c.mode === 'dev' ? 'local server, development identity' : 'demo install, persona switch') + ', as ' + persona + (dryRun ? ', dry run' : '') + '.');
  const before = await freshness(client, persona, withinHours);
  log('Before: ' + count(before.counts.expired, 'story request has expired', 'story requests have expired') + ', ' + count(before.counts.expiresSoon, 'expires', 'expire') + ' within ' + withinHours + ' hours.');

  const renewed = [];
  for (const item of before.items) {
    const line = { request: item.approvalRecordId, decision: item.decision?.recordNumber ?? null, statement: item.decision?.statement ?? '', application: item.application?.name ?? null, state: item.state, asked: asked(item.request) };
    try {
      const env2 = await client.call(persona, 'renew_approval_request', {
        approvalRecordId: item.approvalRecordId,
        ...(dryRun ? { validateOnly: true } : { clientRequestId: 'demo-refresh:' + item.approvalRecordId }),
      });
      const data = env2.data ?? {};
      const until = data.request?.expiresOn ? String(data.request.expiresOn).slice(0, 10) : null;
      line.outcome = dryRun ? 'would ask again' : data.renewed ? 'asked again' : 'already asked again';
      line.until = until;
      line.asksNow = data.request ? asked(data.request) : line.asked;
      log('  ' + line.outcome.padEnd(20) + describe(item) + ': asks ' + line.asksNow + (until ? ' until ' + until : '') + ' (was ' + (item.state === 'expired' ? 'expired' : item.hoursLeft + ' hours left') + ')');
    } catch (e) {
      line.outcome = 'not asked again';
      line.note = e instanceof Refused ? e.message : plain(e.message);
      log('  ' + line.outcome.padEnd(20) + describe(item) + ': ' + plain(line.note, 240));
    }
    renewed.push(line);
  }
  if (!before.items.length) log('  Nothing to ask again.');

  let directorCode = null;
  if (!dryRun && !args.flag('--no-director')) {
    log('\nDirector: rerunning it, so anything missing is made again (it changes nothing that is there).');
    const directorArgs = ['--base', c.base.toString().replace(/\/$/, ''), '--mode', c.mode, '--timeout', String(c.timeoutSeconds), ...(c.mode === 'demo' ? ['--token-env', c.tokenVariable] : [])];
    directorCode = await runDirector(directorArgs, null, json ? { ...out, outStream: process.stderr } : out, { env });
  }

  const after = dryRun ? before : await freshness(client, persona, withinHours);
  const failed = renewed.filter(r => r.outcome === 'not asked again');
  const fresh = !after.needsRefresh;
  if (json) {
    out.log(JSON.stringify({ mode: c.mode, dryRun, withinHours, before: before.counts, renewed, directorExit: directorCode, after: after.counts, fresh }, null, 2));
  } else {
    out.log('');
    out.log('Renewed: ' + renewed.filter(r => r.outcome === 'asked again').length + ' asked again, ' + renewed.filter(r => r.outcome === 'already asked again').length
      + ' already asked again, ' + (dryRun ? renewed.filter(r => r.outcome === 'would ask again').length + ' would be asked again, ' : '') + failed.length + ' not asked again.');
    if (directorCode !== null) out.log('Director: ' + (directorCode === 0 ? 'every step done' : 'exit ' + directorCode));
    out.log(dryRun ? 'Dry run: nothing was written.' : fresh ? 'After: every story approval request has more than ' + withinHours + ' hours left. The demo is fresh.'
      : 'After: ' + count(after.counts.expired, 'request is still expired', 'requests are still expired') + ' and ' + count(after.counts.expiresSoon, 'expires', 'expire') + ' within ' + withinHours + ' hours.');
  }
  return failed.length || (directorCode !== null && directorCode !== 0) || (!dryRun && !fresh) ? 1 : 0;
}
