// Before any step: the run's settings make sense, the server answers, and a writing run plays one project per store.
import fs from 'node:fs';
import path from 'node:path';
import type { FullConfig } from '@playwright/test';
import { env, setupProblems } from './env';

// The projects this invocation runs: the --project values, or every project of the config.
function selectedProjects(config: FullConfig): string[] {
  const names: string[] = [];
  const argv = process.argv;
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--project' && argv[i + 1]) names.push(argv[i + 1]);
    else if (argv[i].startsWith('--project=')) names.push(argv[i].slice('--project='.length));
  }
  return names.length ? names : config.projects.map(p => p.name);
}

export default async function globalSetup(config: FullConfig) {
  const problems = setupProblems();
  const projects = selectedProjects(config);
  if (!env.readOnly && projects.length > 1) {
    problems.push(
      `A writing run plays one project per fresh store, and this one selects ${projects.join(' and ')}. `
      + 'Run "node run.mjs", which resets the store between projects (E2E_RESET_COMMAND), pass --project, or set E2E_READ_ONLY=1.',
    );
  }
  if (!problems.length) {
    try {
      const ready = await fetch(`${env.origin}/health/ready`, { signal: AbortSignal.timeout(10000) });
      if (ready.status !== 200) problems.push(`${env.origin}/health/ready answers ${ready.status}, not 200: is the server ready?`);
      const cfg = (await (await fetch(new URL('config.json', env.base), { signal: AbortSignal.timeout(10000) })).json()) as { auth?: { mode?: string }; demo?: boolean };
      if (env.mode === 'dev' && cfg.auth?.mode !== 'development') problems.push(`config.json says auth mode ${cfg.auth?.mode}: a local run needs the development sign-in (Product:DevIdentity:Enabled=true).`);
      if (!cfg.demo) problems.push('config.json does not say demo: start the server with --Product:WebApp:Demo=true, as "demo local up" does.');
    } catch (e) {
      problems.push(`The server at ${env.origin} does not answer (${(e as Error).message}). Start it first (src/demo/kit: demo local up).`);
    }
  }
  if (problems.length) throw new Error(`The end-to-end suite cannot start:\n- ${problems.join('\n- ')}`);
  fs.mkdirSync(env.runDir, { recursive: true });
  const runFile = path.join(env.runDir, 'run.json');
  const before = fs.existsSync(runFile) ? JSON.parse(fs.readFileSync(runFile, 'utf8')) : null;
  const run = {
    run: env.runId,
    app: env.base.toString(),
    mode: env.mode,
    readOnly: env.readOnly,
    play: env.play,
    startedOn: before?.startedOn ?? new Date().toISOString(),
    projects: [...new Set([...(before?.projects ?? []), ...projects])],
  };
  fs.writeFileSync(runFile, `${JSON.stringify(run, null, 2)}\n`);
}
