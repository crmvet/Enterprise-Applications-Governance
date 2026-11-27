// local.mjs: "demo local up", "demo local down" and "demo local status".
//
// Natively (Linux, macOS, WSL): the kit builds and runs the product from the source tree it sits in. The work folder
// (--folder) holds the store, the logs and state.json, which names the port and the PID of the server it started.
//
// From Windows: .NET builds there but Smart App Control may stop what it built from running, so the kit drives WSL. It
// builds the web app on Windows (its packages are installed there), copies src/ into <folder>/src inside WSL (build
// output and packages left out, the built web app kept), and runs this same kit natively there with the same options.
// --native runs it on Windows itself.
//
// "local up" again stops the server it started from the same folder and starts over on a fresh store. Once the director
// has run, "local up" saves the store as data/demo.after-director.db, and "demo local reset" puts that copy back and
// restarts the server in seconds, for the next rehearsal or audience (product review, fix round 1).
//
// "demo local age-request" and "demo local fail-card" (rehearse.mjs) edit that folder's store for a rehearsal or a
// test of "Ask again" and "Send again", on a local run only (wave 4b QA).
import { spawn, spawnSync } from 'node:child_process';
import { closeSync, copyFileSync, createWriteStream, existsSync, mkdirSync, openSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join, resolve } from 'node:path';
import { UsageError, port as portOption } from './args.mjs';
import { readPersonas } from './client.mjs';
import { AZURE, DIRECTOR, HARNESS, PERSONA_FILE, SEED, SERVER_DLL, SERVER_PROJECT, SRC, WEB_APP, fileTime, newestTime, toWslPath } from './paths.mjs';
import { rehearse } from './rehearse.mjs';

// The verbs of "demo local", and the two rehearsal aids among them.
export const LOCAL_VERBS = ['up', 'down', 'status', 'reset', 'age-request', 'fail-card'];
const REHEARSAL_VERBS = ['age-request', 'fail-card'];

export const DEFAULT_PORT = 5080;
const DEFAULT_WSL_FOLDER = '/root/eag-demo-local';
const STATE = 'state.json';

const quiet = {
  ...process.env,
  DOTNET_CLI_TELEMETRY_OPTOUT: '1',
  DOTNET_NOLOGO: '1',
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: '1',
};

// WSL exists on Windows only; --native runs the server on Windows itself.
function useWsl(args) {
  return process.platform === 'win32' && !args.flag('--native');
}

function folderOf(args) {
  return resolve(args.text('--folder', join(homedir(), '.eag-demo-local')));
}

// ---------------------------------------------------------------------------------------------------------------
// From Windows: drive WSL
// ---------------------------------------------------------------------------------------------------------------

function wslTarget(args) {
  const distro = args.text('--wsl-distro', 'Ubuntu');
  const user = args.text('--wsl-user', 'root');
  const folder = args.text('--folder', DEFAULT_WSL_FOLDER);
  if (!folder.startsWith('/')) {
    // Git Bash turns /root/x into C:/Program Files/Git/root/x before node sees it.
    const gitBash = /\/Git\//i.test(folder) ? ' Git Bash changed the path: run the command with MSYS_NO_PATHCONV=1 in front, or from PowerShell.' : '';
    throw new UsageError('From Windows, --folder names a folder inside WSL, such as ' + DEFAULT_WSL_FOLDER + '.' + gitBash);
  }
  if (/^\/mnt\//.test(folder)) throw new UsageError('--folder must be on WSL\'s own disk, not under /mnt: .NET builds and SQLite run slowly and unreliably on the Windows drives.');
  return { distro, user, folder };
}

function wsl(target, command, { capture = false } = {}) {
  const r = spawnSync('wsl', ['-d', target.distro, '-u', target.user, '--', ...command], { stdio: capture ? 'pipe' : 'inherit', encoding: 'utf8' });
  if (r.error) throw new Error('Cannot start WSL (' + r.error.message + '). Install WSL with the ' + target.distro + ' distribution, or pass --native.');
  return r;
}

// The rsync rules: the source without build output, packages, local stores or azd state, but with the built web app.
export function mirrorRules() {
  return [
    '--include=/ui/harness/dist/', '--include=/ui/harness/dist/app/***', '--exclude=/ui/harness/dist/*',
    '--exclude=bin/', '--exclude=obj/', '--exclude=node_modules/', '--exclude=dist/', '--exclude=TestResults/', '--exclude=.vs/',
    '--exclude=*.db', '--exclude=*.db-shm', '--exclude=*.db-wal', '--exclude=.azure/',
    // The end-to-end runs' screenshots, traces and videos: hundreds of MB the server never reads.
    '--exclude=/tests/e2e/runs/',
  ];
}

async function upFromWindows(args, out) {
  const target = wslTarget(args);
  if (!args.flag('--no-web-build')) {
    const built = buildWebApp(args, out);
    if (!built) return 1;
  }
  out.log('Copying src/ into ' + target.folder + '/src in WSL (' + target.distro + ').');
  const mkdir = wsl(target, ['mkdir', '-p', target.folder + '/src']);
  if (mkdir.status !== 0) return 1;
  const sync = wsl(target, ['rsync', '-a', '--delete', ...mirrorRules(), toWslPath(SRC) + '/', target.folder + '/src/']);
  if (sync.status !== 0) {
    out.error('demo: rsync into WSL failed. Is rsync installed in ' + target.distro + '? (apt-get install rsync)');
    return 1;
  }
  const kit = target.folder + '/src/demo/kit/demo.mjs';
  const r = wsl(target, ['node', kit, 'local', 'up', '--native', '--no-web-build', ...args.passOn(['--native', '--wsl-distro', '--wsl-user', '--no-web-build', '--folder']), '--folder=' + target.folder]);
  return r.status ?? 1;
}

// The rehearsal aids run this checkout's kit (through /mnt), not the copy "local up" made, so they work on a folder that
// an older kit started; the other verbs run the copy that started the server.
function relayToWsl(args, verb) {
  const target = wslTarget(args);
  const rehearsal = REHEARSAL_VERBS.includes(verb);
  const kit = rehearsal ? toWslPath(join(SRC, 'demo', 'kit', 'demo.mjs')) : target.folder + '/src/demo/kit/demo.mjs';
  const has = wsl(target, ['test', '-f', rehearsal ? target.folder + '/' + STATE : kit], { capture: true });
  if (has.status !== 0) {
    const what = verb === 'down' ? 'stop' : verb === 'reset' ? 'reset' : rehearsal ? 'change' : 'show';
    console.log('Nothing to ' + what + ': ' + target.folder + ' in WSL holds no ' + (rehearsal ? 'store of a local run' : 'demo kit') + '. Run "demo local up" first.');
    return verb === 'down' ? 0 : 1;
  }
  const r = wsl(target, ['node', kit, 'local', verb, '--native', ...args.passOn(['--native', '--wsl-distro', '--wsl-user', '--folder']), '--folder=' + target.folder]);
  return r.status ?? 1;
}

// ---------------------------------------------------------------------------------------------------------------
// Builds
// ---------------------------------------------------------------------------------------------------------------

function run(command, commandArgs, cwd, out, { env = quiet, shell = false } = {}) {
  out.log('  $ ' + [command, ...commandArgs].join(' ') + '   (in ' + cwd + ')');
  const r = spawnSync(command, commandArgs, { cwd, env, stdio: 'inherit', shell });
  if (r.error) out.error('  cannot start ' + command + ': ' + r.error.message);
  return r.status ?? 1;
}

// The server's sources: the engine, the MCP core and its contracts, the presets and the Azure hosts and stores.
const SERVER_SOURCES = () => [
  join(SRC, 'engine'), join(SRC, 'mcp'), join(SRC, 'config', 'presets'),
  join(AZURE, 'Product.Azure.Host'), join(AZURE, 'Product.Azure.Server'), join(AZURE, 'Product.Azure.Stores'),
  join(AZURE, 'Product.Azure.Stores.Sqlite'), join(AZURE, 'Product.Azure.Stores.SqlServer'),
  join(SRC, 'Directory.Build.props'), join(SRC, 'Directory.Build.targets'), join(SRC, 'Directory.Packages.props'), join(SRC, 'global.json'),
  join(AZURE, 'Directory.Packages.props'),
];
const SERVER_FILES = p => /\.(cs|csproj|props|targets|json|sql|md)$/i.test(p);

export function serverIsStale() {
  const built = fileTime(SERVER_DLL);
  return built === 0 ? 'missing' : newestTime(SERVER_SOURCES(), { match: SERVER_FILES }) > built ? 'stale' : null;
}

const WEB_SOURCES = () => ['src', 'public', 'index.html', 'vite.config.ts', 'tsconfig.json', 'package.json', 'package-lock.json'].map(p => join(HARNESS, p));

export function webAppIsStale() {
  const built = fileTime(join(WEB_APP, 'index.html'));
  return built === 0 ? 'missing' : newestTime(WEB_SOURCES()) > built ? 'stale' : null;
}

function buildServer(args, out) {
  const why = args.flag('--rebuild') ? 'asked with --rebuild' : serverIsStale();
  if (!why) {
    out.log('Server: the Release build is up to date.');
    return true;
  }
  out.log('Server: building the Release server (' + why + ').');
  return run('dotnet', ['build', join('Product.Azure.Server', 'Product.Azure.Server.csproj'), '-c', 'Release', '--nologo', '-v', 'minimal'], AZURE, out) === 0;
}

export function buildWebApp(args, out) {
  const why = args.flag('--rebuild') ? 'asked with --rebuild' : webAppIsStale();
  if (!why) {
    out.log('Web app: the build in ui/harness/dist/app is up to date.');
    return true;
  }
  out.log('Web app: building it (' + why + ').');
  const npm = process.platform === 'win32';
  if (!existsSync(join(HARNESS, 'node_modules'))) {
    if (run('npm', ['ci', '--ignore-scripts', '--no-audit', '--no-fund'], HARNESS, out, { env: process.env, shell: npm }) !== 0) return false;
  }
  return run('npm', ['run', 'build:app'], HARNESS, out, { env: process.env, shell: npm }) === 0;
}

// ---------------------------------------------------------------------------------------------------------------
// The server process
// ---------------------------------------------------------------------------------------------------------------

function readState(folder) {
  try {
    return JSON.parse(readFileSync(join(folder, STATE), 'utf8'));
  } catch {
    return null;
  }
}

function alive(pid) {
  if (!Number.isInteger(pid) || pid <= 0) return false;
  try {
    process.kill(pid, 0);
    return true;
  } catch (e) {
    return e.code === 'EPERM';
  }
}

// True when the process is the server this kit started on that port: its command line names the server and the URL.
// On a system without /proc the PID file is trusted.
function isOurServer(pid, port) {
  const file = '/proc/' + pid + '/cmdline';
  if (!existsSync('/proc')) return alive(pid);
  try {
    const cmd = readFileSync(file, 'utf8').replace(/\0/g, ' ');
    return cmd.includes('Product.Azure.Server') && cmd.includes('http://127.0.0.1:' + port);
  } catch {
    return false;
  }
}

async function answers(url, timeoutMs = 2000) {
  try {
    const r = await fetch(url, { signal: AbortSignal.timeout(timeoutMs) });
    return r.status;
  } catch {
    return 0;
  }
}

function tail(file, lines = 30) {
  try {
    return readFileSync(file, 'utf8').split(/\r?\n/).slice(-lines).join('\n');
  } catch {
    return '(no log)';
  }
}

const sleep = ms => new Promise(r => setTimeout(r, ms));

// The server's command line, for the local demo store.
export function serverArgs({ port, db, webApp }) {
  return [
    '--environment', 'Development',
    '--urls', 'http://127.0.0.1:' + port,
    '--Product:DevIdentity:Enabled=true',
    '--Product:WebApp:Demo=true',
    '--Product:Directory:Mode=Demo',
    '--Product:Store:Provider=sqlite',
    '--Product:Store:Sqlite:ConnectionString=Data Source=' + db,
    '--Product:WebApp:RootPath=' + webApp,
  ];
}

async function upNative(args, out) {
  const port = portOption(args, DEFAULT_PORT);
  const folder = folderOf(args);
  const base = 'http://127.0.0.1:' + port;
  mkdirSync(folder, { recursive: true });

  const state = readState(folder);
  if (state && alive(state.pid) && isOurServer(state.pid, state.port)) {
    out.log('The demo server of this folder runs (port ' + state.port + ', PID ' + state.pid + '): stopping it, for a fresh store.');
    await stopServer(state.pid);
    rmSync(join(folder, STATE), { force: true });
  }
  if (await answers(base + '/health/live')) {
    out.error('demo: something already answers on port ' + port + '. Pick another with --port.');
    return 1;
  }

  out.log('Demo local up: port ' + port + ', folder ' + folder + '.');
  if (!args.flag('--no-build') && !buildServer(args, out)) return 1;
  if (!existsSync(SERVER_DLL)) {
    out.error('demo: the server is not built (' + SERVER_DLL + '). Run without --no-build.');
    return 1;
  }
  if (!args.flag('--no-web-build') && !buildWebApp(args, out)) return 1;
  if (!existsSync(join(WEB_APP, 'index.html'))) out.log('Web app: not built, so /app/ answers that its files are missing. The API and MCP still work.');

  // A fresh store, seeded with the demo estate.
  const dataFolder = join(folder, 'data');
  mkdirSync(dataFolder, { recursive: true });
  const db = join(dataFolder, 'demo.db');
  if (!args.flag('--keep-store')) {
    for (const f of [db, db + '-shm', db + '-wal']) rmSync(f, { force: true });
    // The copy an earlier director run saved belongs to the old store: "demo local reset" must not put it back over this
    // one (fix round 2: after "local up --no-director" a reset restored an earlier run's store). A finished director
    // run below saves a new one.
    const stale = snapshotOf(db);
    if (existsSync(stale)) out.log('Snapshot: removing the one an earlier run saved (' + stale + ').');
    for (const f of [stale, stale + '-shm', stale + '-wal']) rmSync(f, { force: true });
    out.log('Store: a fresh SQLite file, ' + db + '.');
    const seedLog = join(folder, 'seed.log');
    const seed = spawnSync('dotnet', [SERVER_DLL, 'seed', '--environment', 'Development', '--preset', 'balanced',
      '--binding', join(SEED, 'demo-binding.patch.json'), '--rows', join(SEED, 'demo-estate.json'),
      '--Product:Store:Provider=sqlite', '--Product:Store:Sqlite:ConnectionString=Data Source=' + db], { cwd: SERVER_PROJECT, env: quiet, encoding: 'utf8' });
    writeFileSync(seedLog, (seed.stdout ?? '') + (seed.stderr ?? ''));
    if (seed.status !== 0) {
      out.error('demo: the seed failed. Its log, ' + seedLog + ':\n' + tail(seedLog));
      return 1;
    }
    out.log('Seed: ' + String(seed.stdout ?? '').trim().split(/\r?\n/).pop().replace(/[0-9a-f]{8}-[0-9a-f-]{27}/gi, '<id>'));
  } else {
    out.log('Store: kept as it is (--keep-store), ' + db + '.');
  }

  // The server, on loopback, detached so it outlives this command.
  const readyWithin = args.number('--ready-timeout', 180, { min: 5, max: 1800 });
  let pid = await startServer({ port, folder, db, base, readyWithin }, out);
  if (!pid) return 1;

  // The director makes the story's open work live, as the personas.
  let directorExit = 'skipped (--no-director)';
  if (!args.flag('--no-director')) {
    out.log('Director: making the story\'s open work live (log ' + join(folder, 'director.log') + ').');
    const code = await runDirector(['--base', base, '--timeout', String(args.number('--timeout', 180, { min: 5, max: 3600 }))], join(folder, 'director.log'), out);
    directorExit = code === 0 ? 'every step done' : 'exit ' + code + ', see the log';
    if (code !== 0) out.error('demo: the director reported a failed step (exit ' + code + '). The server stays up; see ' + join(folder, 'director.log') + '.');
    if (code === 0) {
      // The copy "demo local reset" puts back: the server stops first, so SQLite has written everything to the file.
      out.log('Snapshot: saving the store as it is after the director, for "demo local reset".');
      await stopServer(pid);
      copyStore(db, snapshotOf(db));
      pid = await startServer({ port, folder, db, base, readyWithin }, out);
      if (!pid) return 1;
    }
  }

  printUp(out, { base, pid, folder, db, directorExit });
  return 0;
}

// The store saved right after the director, beside the store.
export function snapshotOf(db) {
  return db.replace(/\.db$/, '') + '.after-director.db';
}

// Copies a stopped SQLite store, leaving no journal of the target behind.
function copyStore(from, to) {
  for (const f of [to, to + '-shm', to + '-wal']) rmSync(f, { force: true });
  copyFileSync(from, to);
}

// Starts the server on the store, detached so it outlives this command, writes state.json and waits until it is ready.
// Returns its PID, or null (with the end of its log) when it did not get ready.
async function startServer({ port, folder, db, base, readyWithin }, out) {
  const serverLog = join(folder, 'server.log');
  const fd = openSync(serverLog, 'w');
  const child = spawn('dotnet', [SERVER_DLL, ...serverArgs({ port, db, webApp: WEB_APP })], { cwd: SERVER_PROJECT, env: quiet, detached: true, stdio: ['ignore', fd, fd] });
  closeSync(fd);
  child.unref();
  const pid = child.pid;
  writeFileSync(join(folder, STATE), JSON.stringify({ port, pid, base, db, readyWithin, startedOn: new Date().toISOString() }, null, 2) + '\n');
  out.log('Server: started, PID ' + pid + ', log ' + serverLog + '. Waiting for it to be ready.');
  for (let waited = 0; waited < readyWithin * 1000; waited += 1000) {
    if (!alive(pid)) break;
    if ((await answers(base + '/health/ready')) === 200) return pid;
    await sleep(1000);
  }
  out.error('demo: the server did not get ready within ' + readyWithin + ' seconds. The end of its log:\n' + tail(serverLog));
  if (alive(pid)) process.kill(pid, 'SIGTERM');
  rmSync(join(folder, STATE), { force: true });
  return null;
}

// Stops a server by its PID: SIGTERM, then SIGKILL after 15 seconds.
async function stopServer(pid) {
  if (!alive(pid)) return;
  process.kill(pid, 'SIGTERM');
  for (let i = 0; i < 30 && alive(pid); i++) await sleep(500);
  if (alive(pid)) {
    process.kill(pid, 'SIGKILL');
    await sleep(500);
  }
}

// "demo local reset": the store saved after the director goes back, and the server starts again on it.
async function resetNative(args, out) {
  const folder = folderOf(args);
  const state = readState(folder);
  if (!state) {
    out.error('demo: ' + folder + ' has no ' + STATE + ': no server was started from it. Run "demo local up" first.');
    return 1;
  }
  const snapshot = snapshotOf(state.db);
  if (!existsSync(snapshot)) {
    out.error('demo: there is no store saved after the director (' + snapshot + '). Run "demo local up" without --no-director; it saves one when the director finishes.');
    return 1;
  }
  if (alive(state.pid)) {
    if (!isOurServer(state.pid, state.port)) {
      out.error('demo: PID ' + state.pid + ' is not the demo server on port ' + state.port + ' any more, so nothing was reset. Run "demo local up".');
      return 1;
    }
    out.log('Stopping the demo server, PID ' + state.pid + '.');
    await stopServer(state.pid);
  }
  for (const f of [state.db + '-shm', state.db + '-wal']) rmSync(f, { force: true });
  copyFileSync(snapshot, state.db);
  out.log('Store: put back as it was after the director (' + snapshot + ').');
  const pid = await startServer({ port: state.port, folder, db: state.db, base: state.base, readyWithin: state.readyWithin ?? 180 }, out);
  if (!pid) return 1;
  out.log('The demo is reset: ' + state.base + '/app/ (PID ' + pid + '). Run "demo check --base ' + state.base + '" before the audience.');
  return 0;
}

// Runs the demo director with the arguments, writing its output to the console and the log file.
export function runDirector(directorArgs, logFile, out, { env = process.env } = {}) {
  return new Promise(resolvePromise => {
    const log = logFile ? createWriteStream(logFile) : null;
    const child = spawn(process.execPath, [DIRECTOR, ...directorArgs], { cwd: AZURE, env, stdio: ['ignore', 'pipe', 'pipe'] });
    const relay = (chunk, toErr) => {
      log?.write(chunk);
      (toErr ? out.errStream : out.outStream).write(chunk);
    };
    child.stdout.on('data', c => relay(c, false));
    child.stderr.on('data', c => relay(c, true));
    child.on('error', e => {
      out.error('demo: cannot start the director: ' + e.message);
      log?.end();
      resolvePromise(1);
    });
    child.on('close', code => {
      log?.end();
      resolvePromise(code ?? 1);
    });
  });
}

function printUp(out, { base, pid, folder, db, directorExit }) {
  const personas = readPersonas(PERSONA_FILE);
  out.log('');
  out.log('The demo is up.');
  out.log('  Web app:   ' + base + '/app/');
  out.log('  MCP:       ' + base + '/mcp  (Claude Code: claude mcp add --transport http demo-local ' + base + '/mcp --header "X-Product-Dev-Identity: priya")');
  out.log('  Server:    PID ' + pid + ', log ' + join(folder, 'server.log'));
  out.log('  Store:     ' + db);
  out.log('  Director:  ' + directorExit);
  out.log('');
  out.log('Personas (the sign-in page offers each by its name and role, such as "Lena Okoro, requester"; the API and the kit use demo-<name>):');
  for (const p of personas) out.log('  ' + p.name.padEnd(8) + p.label);
  out.log('');
  out.log('Next: demo check --base ' + base + '    Stop: demo local down --folder ' + folder);
}

async function downNative(args, out) {
  const folder = folderOf(args);
  const state = readState(folder);
  if (!state) {
    out.log('Nothing to stop: ' + folder + ' has no ' + STATE + ' (no server started from it).');
    return 0;
  }
  const { pid, port } = state;
  if (!alive(pid)) {
    rmSync(join(folder, STATE), { force: true });
    out.log('The server of ' + folder + ' (PID ' + pid + ', port ' + port + ') has already stopped.');
    return 0;
  }
  if (!isOurServer(pid, port)) {
    out.error('demo: PID ' + pid + ' is not the demo server on port ' + port + ' any more, so nothing was stopped. Remove ' + join(folder, STATE) + ' if the server is gone.');
    return 1;
  }
  await stopServer(pid);
  rmSync(join(folder, STATE), { force: true });
  const free = (await answers('http://127.0.0.1:' + port + '/health/live')) === 0;
  out.log('Stopped the demo server, PID ' + pid + '. Port ' + port + (free ? ' is free.' : ' still answers: another program uses it.'));
  return 0;
}

async function statusNative(args, out) {
  const folder = folderOf(args);
  const state = readState(folder);
  if (!state || !alive(state.pid) || !isOurServer(state.pid, state.port)) {
    out.log('No demo server runs from ' + folder + '.');
    return 1;
  }
  const ready = await answers(state.base + '/health/ready');
  out.log('The demo server runs: ' + state.base + '/app/, PID ' + state.pid + ', started ' + state.startedOn + ', ready ' + (ready === 200 ? 'yes' : 'no (' + ready + ')') + '.');
  return ready === 200 ? 0 : 1;
}

// options: { fetchImpl, now } for the kit's tests of the rehearsal aids.
export async function local(args, out, options = {}) {
  const verb = args.words[1];
  if (!LOCAL_VERBS.includes(verb)) throw new UsageError('demo local ' + LOCAL_VERBS.join('|'));
  if (useWsl(args)) {
    return verb === 'up' ? upFromWindows(args, out) : relayToWsl(args, verb);
  }
  if (REHEARSAL_VERBS.includes(verb)) {
    const folder = folderOf(args);
    return rehearse(verb, args, out, { folder, state: readState(folder) }, options);
  }
  return verb === 'up' ? upNative(args, out) : verb === 'down' ? downNative(args, out) : verb === 'reset' ? resetNative(args, out) : statusNative(args, out);
}
