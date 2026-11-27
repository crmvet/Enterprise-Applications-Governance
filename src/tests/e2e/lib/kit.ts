// The demo kit's rehearsal aids, for the steps that need a state a live demo cannot reach in its order: a failed
// approval card ("demo local fail-card"). A local run only, with the kit's folder of the server (E2E_KIT_FOLDER, or
// run.mjs --kit-folder). The kit edits that folder's SQLite store directly and refuses any other install
// (src/demo/kit/lib/rehearse.mjs). From Windows the kit runs itself in WSL, where the store is.
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { env, SRC } from './env';

const KIT = path.join(SRC, 'demo', 'kit', 'demo.mjs');

// True when the run can use the kit's rehearsal aids.
export const kitReady = () => env.mode === 'dev' && !!env.kitFolder;

// Runs "demo local <verb> ... --json" against the local run's folder and answers its JSON.
export function kitLocal(verb: string, args: string[]): any {
  if (!kitReady()) throw new Error('the kit folder of the local run is not known: pass --kit-folder to run.mjs (E2E_KIT_FOLDER)');
  const r = spawnSync(process.execPath, [KIT, 'local', verb, ...args, '--folder', env.kitFolder, '--base', env.origin, '--json'], { encoding: 'utf8', timeout: 120000 });
  if (r.status !== 0) throw new Error(`demo local ${verb} failed (exit ${r.status}): ${(r.stderr || r.stdout || '').trim().slice(0, 600)}`);
  const text = r.stdout.trim();
  const start = text.indexOf('{');
  return JSON.parse(start >= 0 ? text.slice(start) : text);
}
