// sandbox.mjs: "demo sandbox rebuild", the rebuild of a demo install on Azure (a "sandbox"), in seven steps:
//   files      the role binding patch and the rows the seed writes: the demo's own files, plus the install's extra
//              bindings and rows when it has any (--extra-binding, --extra-rows), written to --folder;
//   reset      delete the database, so the provision creates it empty;
//   provision  azd provision --no-state with the persona switch on (after a reset the templates are unchanged, so a
//              plain provision would skip the deployment that creates the database);
//   deploy     azd deploy (its prepackage hook builds the web app), then health and config.json;
//   seed       the demo estate, through the seed command in the Demo environment, over the install's database;
//   director   the demo director in dev mode, through a local server on loopback whose store is the install's database:
//              the development personas carry the same fictional object IDs and groups as the demo personas, so the
//              rows are the ones the persona switch would write, and no person's token is handled;
//   check      the install's health and config.json, then "demo check" through the same local server.
// Every value comes from the azd environment (azd env get-values, in src/adapters/azure), or from --env-file. Every
// command is printed before it runs, with $NAME in place of each value, so no value of the install is printed, and
// the output of az, azd and dotnet goes to a log in --folder, with IDs and URLs left out of what is shown. Local paths
// are printed relative to src/adapters/azure, or as "<folder>/<file>" outside the source tree (shownPath).
// --dry-run prints the commands and runs nothing: no az, azd or dotnet command, no file written. A real run deletes a
// database and writes through the persona switch, so it needs --yes, whatever --steps says, and it stops before its
// first step unless PRODUCT_DEMO is true in the azd environment (security review: the demo check used to sit
// inside the provision step, after the reset had deleted the database).
import { spawn, spawnSync } from 'node:child_process';
import { closeSync, existsSync, mkdirSync, openSync, readFileSync, writeFileSync } from 'node:fs';
import { isAbsolute, join, relative, resolve } from 'node:path';
import { UsageError } from './args.mjs';
import { readPersonas } from './client.mjs';
import { AZURE, DIRECTOR, KIT, SEED, SERVER_DLL, SRC } from './paths.mjs';

export const STEPS = ['files', 'reset', 'provision', 'deploy', 'seed', 'director', 'check'];

// The azd environment values the rebuild reads. PRODUCT_DEMO must be true: the rebuild is for a demo install only.
export const VALUES = ['AZURE_RESOURCE_GROUP', 'AZURE_SQL_SERVER_NAME', 'AZURE_SQL_SERVER_FQDN', 'AZURE_SQL_DATABASE_NAME', 'PRODUCT_PUBLIC_BASE_URL', 'PRODUCT_DEMO'];

const SQL_ENV = {
  Product__Store__Provider: 'sqlserver',
  ConnectionStrings__Product: 'Server=tcp:${AZURE_SQL_SERVER_FQDN},1433;Database=${AZURE_SQL_DATABASE_NAME};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connect Timeout=60;',
  // The local commands sign in to Azure SQL as the person signed in to the Azure CLI.
  AZURE_TOKEN_CREDENTIALS: 'AzureCliCredential',
};

// "KEY=value" or KEY="value" lines, as azd env get-values prints them.
export function parseEnvValues(text) {
  const values = {};
  for (const line of String(text).split(/\r?\n/)) {
    const m = /^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)\s*$/.exec(line);
    if (!m) continue;
    let v = m[2];
    if ((v.startsWith('"') && v.endsWith('"')) || (v.startsWith('\'') && v.endsWith('\''))) v = v.slice(1, -1);
    values[m[1]] = v;
  }
  return values;
}

// ${NAME} in a template, as printed ("$NAME") and as run (the value). A command argument may also be a local path,
// { path, shown }: printed as shown, run as the path.
const shown = t => (t && typeof t === 'object' ? t.shown : String(t).replace(/\$\{([A-Z0-9_]+)\}/g, '$$$1'));
const filled = (t, values) => (t && typeof t === 'object' ? t.path : String(t).replace(/\$\{([A-Z0-9_]+)\}/g, (_, n) => {
  if (values[n] === undefined || values[n] === '') throw new UsageError('The azd environment has no value for ' + n + '.');
  return values[n];
}));

// A local path as printed (QA, wave 4b): relative to src/adapters/azure, where every command runs, when the path is
// inside the source tree; otherwise the fallback, such as "<folder>/sandbox-world.json". So no checkout path, drive
// letter or home folder reaches a shared screen or a recording.
export function shownPath(path, fallback) {
  const fromSrc = relative(SRC, path);
  if (fromSrc && !fromSrc.startsWith('..') && !isAbsolute(fromSrc)) return relative(AZURE, path).replace(/\\/g, '/') || '.';
  return fallback;
}
const quote = a => (/[\s;"'$]/.test(a) ? '"' + a.replace(/"/g, '\\"') + '"' : a);

// What a log line may show: no ID, no URL, no address.
export function redact(line) {
  return String(line)
    .replace(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi, '<id>')
    .replace(/https?:\/\/[^\s"')]+/gi, '<url>')
    .replace(/\b[a-z0-9-]+\.(database\.windows\.net|azurewebsites\.net|vault\.azure\.net)\b/gi, '<host>')
    .replace(/\b\d{1,3}(\.\d{1,3}){3}\b/g, '<address>');
}

// The plan: for each step, its actions. An action is a command (printed and run) or a check the kit does itself.
export function plan(options) {
  const { folder, port, timeout, clientIp, binding, rows, extraBinding, extraRows } = options;
  const rel = p => relative(AZURE, p).replace(/\\/g, '/') || '.';
  const dll = rel(SERVER_DLL);
  const outFiles = { binding: join(folder, 'sandbox-binding.patch.json'), rows: join(folder, 'sandbox-world.json') };
  // A command argument for a local file: inside the source tree the relative path, printed and run alike (every command
  // runs in src/adapters/azure); outside it the full path, printed as the fallback.
  const fileArg = (p, fallback) => shownPath(p, null) ?? { path: p, shown: fallback };
  const shownArg = a => (typeof a === 'object' ? a.shown : a);
  const out = { binding: fileArg(outFiles.binding, '<folder>/sandbox-binding.patch.json'), rows: fileArg(outFiles.rows, '<folder>/sandbox-world.json') };
  const base = 'http://127.0.0.1:' + port;
  const cmd = (title, command, args, extra = {}) => ({ kind: 'command', title, command, args, cwd: AZURE, ...extra });
  const own = (title, run) => ({ kind: 'own', title, run });
  return {
    files: {
      why: 'the binding and the rows the seed writes',
      actions: [own('write ' + shownArg(out.binding) + ' and ' + shownArg(out.rows) + ' from ' + shownPath(binding, 'the --binding file') + ' and ' + shownPath(rows, 'the --rows file')
        + (extraBinding ? ', with the bindings of ' + shownPath(extraBinding, 'the --extra-binding file') : '')
        + (extraRows ? ', with the rows of ' + shownPath(extraRows, 'the --extra-rows file') : ''), 'files')],
    },
    reset: {
      why: 'delete the database, so the provision creates it empty',
      actions: [cmd('delete the database', 'az', ['sql', 'db', 'delete', '--resource-group', '${AZURE_RESOURCE_GROUP}', '--server', '${AZURE_SQL_SERVER_NAME}', '--name', '${AZURE_SQL_DATABASE_NAME}', '--yes'])],
    },
    provision: {
      why: 'create the database again and apply the migrations, with the persona switch on',
      actions: [
        cmd('turn the persona switch on', 'azd', ['env', 'set', 'PRODUCT_DEMO_PERSONAS', 'true']),
        ...(clientIp ? [cmd('let this machine through the SQL firewall for the migration', 'azd', ['env', 'set', 'SQL_CLIENT_IP_ADDRESS', clientIp])] : []),
        // --no-state: after a reset the templates are unchanged, and azd would skip the deployment that creates the database.
        cmd('provision, forcing the deployment', 'azd', ['provision', '--no-prompt', '--no-state']),
      ],
    },
    deploy: {
      why: 'deploy this build (the prepackage hook builds the web app)',
      actions: [
        cmd('deploy', 'azd', ['deploy', '--no-prompt']),
        own('wait for ${PRODUCT_PUBLIC_BASE_URL}/health/ready, then read /app/config.json: demo true and every persona offered', 'public-health'),
      ],
    },
    seed: {
      why: 'the fictional demo estate, in the Demo environment',
      actions: [
        cmd('build the Release server', 'dotnet', ['build', 'Product.Azure.Server/Product.Azure.Server.csproj', '-c', 'Release', '--nologo', '-v', 'minimal']),
        cmd('seed', 'dotnet', [dll, 'seed', '--environment', 'Demo', '--preset', 'balanced', '--binding', out.binding, '--rows', out.rows], { env: SQL_ENV }),
      ],
    },
    director: {
      why: 'the story\'s open work, made live through the engine as the personas',
      actions: [
        own('start a local server on ' + base + ' over the install\'s database: Development, development identity, demo flag, directory mode Demo', 'local-start'),
        cmd('run the demo director', 'node', [rel(DIRECTOR), '--base', base, '--timeout', String(timeout)]),
      ],
    },
    check: {
      why: 'the install answers, and every scenario is ready',
      actions: [
        own('read ${PRODUCT_PUBLIC_BASE_URL}/health/ready and /app/config.json', 'public-health'),
        own('start the local server over the install\'s database, unless the director step started it', 'local-start'),
        cmd('demo check through the local server', 'node', [rel(join(KIT, 'demo.mjs')), 'check', '--base', base, '--timeout', String(timeout)]),
      ],
    },
    localServer: { base, port, args: [dll, '--environment', 'Development', '--urls', base, '--Product:DevIdentity:Enabled=true', '--Product:Directory:Mode=Demo', '--Product:WebApp:Demo=true'], env: SQL_ENV },
    outFiles,
  };
}

function printCommand(out, action) {
  const env = Object.entries(action.env ?? {}).map(([k, v]) => k + '=' + quote(shown(v))).join(' ');
  out.log('    $ ' + (env ? env + ' ' : '') + [action.command, ...action.args.map(a => quote(shown(a)))].join(' ') + (action.cwd !== AZURE ? '   (in ' + relative(AZURE, action.cwd).replace(/\\/g, '/') + ')' : ''));
}

// The files step: the binding patch and the rows, with the extras added. Returns the counts; writes only when asked.
export function buildFiles({ binding, rows, extraBinding, extraRows }, write) {
  const read = (file, what) => {
    try {
      return JSON.parse(readFileSync(file, 'utf8'));
    } catch (e) {
      throw new UsageError('Cannot read ' + what + ' ' + file + ': ' + e.message);
    }
  };
  const patch = read(binding, 'the binding patch');
  const world = read(rows, 'the rows');
  if (extraBinding) {
    const extra = read(extraBinding, 'the extra bindings');
    patch.bindingOperations = [...(patch.bindingOperations ?? []), ...(extra.bindingOperations ?? [])];
    patch.description = 'Demo install binding: the demo binding with the install\'s own extra bindings. Local file only, never committed.';
  }
  if (extraRows) {
    const extra = read(extraRows, 'the extra rows');
    const ids = new Set((world.rows ?? []).map(r => String(r.id).toLowerCase()));
    for (const r of extra.rows ?? []) {
      if (ids.has(String(r.id).toLowerCase())) throw new UsageError('An extra row reuses the ID of a demo row: ' + r.entity + ' ' + r.id + '.');
    }
    world.rows = [...(world.rows ?? []), ...(extra.rows ?? [])];
  }
  const bindings = (patch.bindingOperations ?? []).filter(o => o.entity === 'role-group-binding');
  const counts = { roles: new Set(bindings.map(b => b.values?.roleKey)).size, bindings: bindings.length, rows: (world.rows ?? []).length };
  if (write) {
    writeFileSync(write.binding, JSON.stringify(patch, null, 2) + '\n');
    writeFileSync(write.rows, JSON.stringify(world, null, 2) + '\n');
  }
  return counts;
}

function readValues(args, out, dryRun, runner) {
  const envFile = args.text('--env-file');
  if (envFile) return parseEnvValues(readFileSync(envFile, 'utf8'));
  if (dryRun) return null;
  out.log('  $ azd env get-values');
  const r = runner.spawnSync('azd', ['env', 'get-values'], { cwd: AZURE, encoding: 'utf8', shell: process.platform === 'win32' });
  if (r.status !== 0) throw new Error('azd env get-values failed. Select the environment with "azd env select <name>" in src/adapters/azure, and sign in with "azd auth login".');
  return parseEnvValues(r.stdout);
}

const sleep = ms => new Promise(r => setTimeout(r, ms));

// The demo-only check (security review, wave 4): every real run, whatever its steps, stops before the first one unless the selected
// azd environment says PRODUCT_DEMO=true.
export const DEMO_ONLY = 'PRODUCT_DEMO is not true in the azd environment, so this is not a demo install. The rebuild stops before its first step, and nothing ran.';

// runner: the child process functions, so a test can prove which commands a run would start (none, when it stops).
export async function sandbox(args, out, runner = { spawnSync, spawn }) {
  if (args.words[1] !== 'rebuild') throw new UsageError('demo sandbox rebuild [--dry-run] [--steps ' + STEPS.join(',') + '] [--yes]');
  const dryRun = args.flag('--dry-run');
  const steps = args.text('--steps') ? args.text('--steps').split(',').map(s => s.trim()).filter(Boolean) : STEPS;
  for (const s of steps) if (!STEPS.includes(s)) throw new UsageError('Unknown step "' + s + '". The steps: ' + STEPS.join(', ') + '.');
  const ordered = STEPS.filter(s => steps.includes(s));
  if (!dryRun && !args.flag('--yes')) {
    throw new UsageError('A rebuild deletes the install\'s database and writes the story through the persona switch. Pass --yes to run it, or --dry-run to see the commands.');
  }
  const folder = resolve(args.text('--folder', join(AZURE, '.azure', 'demo-kit')));
  const options = {
    folder,
    port: args.number('--local-port', 5099, { min: 1024, max: 65535 }),
    timeout: args.number('--timeout', 240, { min: 5, max: 3600 }),
    clientIp: args.text('--client-ip'),
    binding: resolve(args.text('--binding', join(SEED, 'demo-binding.patch.json'))),
    rows: resolve(args.text('--rows', join(SEED, 'demo-estate.json'))),
    extraBinding: args.text('--extra-binding') ? resolve(args.text('--extra-binding')) : null,
    extraRows: args.text('--extra-rows') ? resolve(args.text('--extra-rows')) : null,
  };
  if (options.clientIp && !/^\d{1,3}(\.\d{1,3}){3}$/.test(options.clientIp)) throw new UsageError('--client-ip takes an IPv4 address.');
  const p = plan(options);

  out.log('Demo sandbox rebuild' + (dryRun ? ' (dry run: nothing runs, nothing is written)' : '') + '. Steps: ' + ordered.join(', ') + '.');
  out.log('Values from the azd environment of src/adapters/azure' + (args.text('--env-file') ? ' (here from --env-file)' : '') + ': ' + VALUES.join(', ') + '.');
  if (process.platform === 'win32') out.log('Note: on Windows with Smart App Control, the dotnet steps may be blocked; run the rebuild in WSL, Linux or macOS.');
  const values = readValues(args, out, dryRun, runner);
  if (dryRun && !args.text('--env-file')) out.log('    $ azd env get-values');
  out.log('\n0. demo only: stop unless PRODUCT_DEMO is true, before any step runs');
  if (values && values.PRODUCT_DEMO !== 'true') {
    if (dryRun) {
      out.log('    - PRODUCT_DEMO is not true: a real run would stop here, and run nothing');
    } else {
      out.error('\ndemo: ' + DEMO_ONLY);
      return 1;
    }
  } else if (values) {
    out.log('    - PRODUCT_DEMO is true');
  }
  if (!dryRun) mkdirSync(folder, { recursive: true });

  let server = null;
  const lastLocal = Math.max(ordered.lastIndexOf('director'), ordered.lastIndexOf('check'));
  let failed = null;
  try {
    for (const [index, step] of ordered.entries()) {
      out.log('\n' + (index + 1) + '. ' + step + ': ' + p[step].why);
      for (const action of p[step].actions) {
        if (action.kind === 'command') {
          printCommand(out, action);
          if (dryRun) continue;
          const code = runLogged(action, values, join(folder, step + '.log'), out, runner);
          if (code !== 0) throw new Error(step + ': "' + action.title + '" failed (exit ' + code + '). The log: ' + shownPath(join(folder, step + '.log'), '<folder>/' + step + '.log'));
          continue;
        }
        out.log('    - ' + shown(action.title));
        if (dryRun) {
          if (action.run === 'files') {
            const counts = buildFiles(options, null);
            out.log('      would write ' + counts.roles + ' roles in ' + counts.bindings + ' bindings, and ' + counts.rows + ' rows');
          }
          if (action.run === 'local-start' && !server) {
            out.log('    $ ' + Object.entries(p.localServer.env).map(([k, v]) => k + '=' + quote(shown(v))).join(' ') + ' dotnet ' + p.localServer.args.join(' ') + '   (in the background; stopped by its PID after the last step that uses it)');
            server = { dry: true };
          }
          continue;
        }
        if (action.run === 'files') {
          const counts = buildFiles(options, p.outFiles);
          out.log('      wrote ' + counts.roles + ' roles in ' + counts.bindings + ' bindings, and ' + counts.rows + ' rows');
        } else if (action.run === 'public-health') {
          await publicHealth(values, out);
        } else if (action.run === 'local-start' && !server) {
          server = await startLocal(p.localServer, values, folder, out, runner);
        }
      }
      if (index === lastLocal && server && !server.dry) {
        stopLocal(server, out);
        server = null;
      }
    }
    if (dryRun && server) out.log('\n    - stop the local server by its PID');
  } catch (e) {
    failed = e;
  } finally {
    if (server && !server.dry) stopLocal(server, out);
  }
  if (failed) {
    out.error('\ndemo: ' + (failed instanceof UsageError ? failed.message : redact(failed.message)));
    return 1;
  }
  out.log('\n' + (dryRun ? 'Dry run done: nothing ran.' : 'Rebuild done. Before a demo, run "demo refresh" when it is days away.'));
  return 0;
}

function runLogged(action, values, logFile, out, runner) {
  const env = { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_NOLOGO: '1' };
  for (const [k, v] of Object.entries(action.env ?? {})) env[k] = filled(v, values);
  const args = action.args.map(a => filled(a, values));
  const r = runner.spawnSync(action.command, args, {
    cwd: action.cwd, env, encoding: 'utf8', maxBuffer: 256 * 1024 * 1024,
    shell: process.platform === 'win32' && (action.command === 'az' || action.command === 'azd'),
  });
  const text = (r.stdout ?? '') + (r.stderr ?? '');
  writeFileSync(logFile, text, { flag: 'a' });
  for (const line of text.split(/\r?\n/).filter(Boolean).slice(-8)) out.log('      ' + redact(line));
  if (r.error) out.error('      cannot start ' + action.command + ': ' + r.error.message);
  return r.status ?? 1;
}

async function publicHealth(values, out) {
  const base = filled('${PRODUCT_PUBLIC_BASE_URL}', values).replace(/\/$/, '');
  let status = 0;
  for (let i = 0; i < 6 && status !== 200; i++) {
    try {
      status = (await fetch(base + '/health/ready', { signal: AbortSignal.timeout(60000) })).status;
    } catch {
      status = 0;
    }
  }
  if (status !== 200) throw new Error('The install did not answer /health/ready with 200 (last answer ' + status + ').');
  const config = await (await fetch(base + '/app/config.json', { signal: AbortSignal.timeout(60000) })).json();
  const expected = readPersonas().length;
  const offered = (config.demoPersonas ?? []).length;
  out.log('      ready 200; config.json: demo ' + config.demo + ', ' + offered + ' of ' + expected + ' personas');
  if (config.demo !== true || offered !== expected) throw new Error('config.json does not show a demo install with every persona.');
}

async function startLocal(local, values, folder, out, runner) {
  const env = { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_NOLOGO: '1' };
  for (const [k, v] of Object.entries(local.env)) env[k] = filled(v, values);
  out.log('    $ ' + Object.entries(local.env).map(([k, v]) => k + '=' + quote(shown(v))).join(' ') + ' dotnet ' + local.args.join(' ') + '   (in the background)');
  if (!existsSync(SERVER_DLL)) throw new Error('The Release server is not built. Run the seed step first, or dotnet build in src/adapters/azure.');
  const logFile = join(folder, 'local-server.log');
  const fd = openSync(logFile, 'w');
  const child = runner.spawn('dotnet', local.args, { cwd: AZURE, env, detached: true, stdio: ['ignore', fd, fd] });
  closeSync(fd);
  child.unref();
  for (let i = 0; i < 180; i++) {
    try {
      if ((await fetch(local.base + '/health/ready', { signal: AbortSignal.timeout(5000) })).status === 200) {
        out.log('      the local server is ready, PID ' + child.pid);
        return { pid: child.pid };
      }
    } catch {
      // not yet
    }
    await sleep(1000);
  }
  try {
    process.kill(child.pid, 'SIGTERM');
  } catch {
    // gone already
  }
  throw new Error('The local server did not get ready. Its log: ' + shownPath(logFile, '<folder>/local-server.log'));
}

function stopLocal(server, out) {
  try {
    process.kill(server.pid, 'SIGTERM');
    out.log('    - stopped the local server, PID ' + server.pid);
  } catch {
    out.log('    - the local server, PID ' + server.pid + ', had stopped already');
  }
}
