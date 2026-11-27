// play.mjs: "demo play <scenario> --base <url>". It plays one scenario in a visible browser at human pace, with a caption
// bar naming the step and the persona, using the same steps as its end-to-end test. The browser part is the Playwright
// suite in src/tests/e2e (its README.md): the kit finds the scenario's spec in scenarios.json and starts the suite
// headed, on one worker, with no retries, in play mode.
//
// The contract with the suite (src/tests/e2e/lib/env.ts reads these variables):
//   E2E_BASE_URL     the server or the web app
//   DEMO_PLAY=1      play mode: a run folder named <time>-play, a longer step timeout and the list reporter
//   DEMO_SLOW_MO_MS  the pause between browser actions, in milliseconds
//   DEMO_HOLD_MS     how long the end of each step stays on screen (--hold, in seconds; default 4)
//   E2E_RUN_ID       the run folder, <time>-play-<scenario>, so the kit can say where the video is
//   DEMO_CAPTIONS=1  the caption bar: the step ID and title, and the persona with their role
//   DEMO_RECORD=1    --record: a video of the scenario in the run folder's videos/
//   E2E_READ_ONLY=1  --read-only: skip every write, for a deployed demo
// The projects are desktop-1280 and phone-390 (--phone). The suite uses the installed Chrome, never a download.
import { spawnSync } from 'node:child_process';
import { existsSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { UsageError } from './args.mjs';
import { readScenarios } from './check.mjs';
import { E2E } from './paths.mjs';

export const PROJECTS = { desktop: 'desktop-1280', phone: 'phone-390' };

// The run folder's name: the start time, "play" and the scenario.
export function playRunId(id, now = new Date()) {
  const two = n => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${two(now.getMonth() + 1)}-${two(now.getDate())}-${two(now.getHours())}${two(now.getMinutes())}${two(now.getSeconds())}-play-${id}`;
}

export function playPlan(scenario, { base, record = false, phone = false, slowMoMs = 600, holdMs = 4000, readOnly = false, runId = playRunId(scenario.id) }) {
  const config = join(E2E, 'playwright.config.ts');
  const cli = join(E2E, 'node_modules', '@playwright', 'test', 'cli.js');
  return {
    command: process.execPath,
    args: [cli, 'test', '--config', config, `--project=${phone ? PROJECTS.phone : PROJECTS.desktop}`, '--headed', '--workers', '1', '--retries', '0', scenario.spec],
    cwd: E2E,
    runDir: join(E2E, 'runs', runId),
    env: {
      E2E_BASE_URL: base,
      E2E_RUN_ID: runId,
      DEMO_PLAY: '1',
      DEMO_SLOW_MO_MS: String(slowMoMs),
      DEMO_HOLD_MS: String(holdMs),
      DEMO_CAPTIONS: '1',
      ...(record ? { DEMO_RECORD: '1' } : {}),
      ...(readOnly ? { E2E_READ_ONLY: '1' } : {}),
      PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD: '1',
    },
    ready: existsSync(config) && existsSync(cli),
  };
}

// The step IDs of a run or a track (scenarios.json "runs"), grouped by scenario in the run's order: one Playwright run
// per group, its spec and a grep of its step IDs, so the steps play in the run's order with captions (fix round 1,
// product review: rehearse a whole run, not one scenario at a time).
export function runGroups(run, scenarios) {
  const groups = [];
  for (const step of run.steps) {
    const id = step.split('.')[0];
    const last = groups[groups.length - 1];
    if (last && last.id === id) last.steps.push(step);
    else {
      const scenario = scenarios.find(s => s.id === id);
      if (!scenario?.spec) throw new UsageError('The run names ' + step + ', and scenarios.json has no spec for ' + id + '.');
      groups.push({ id, spec: scenario.spec, steps: [step] });
    }
  }
  return groups;
}

// The grep that selects exactly these steps: a test title starts with its step ID and a space.
export function stepGrep(steps) {
  return '(^|\\s)(' + steps.map(s => s.replace(/[.-]/g, m => '\\' + m)).join('|') + ')\\s';
}

export async function play(args, out) {
  const id = args.words[1];
  if (!id) throw new UsageError('demo play <scenario ID, run or track> --base <url>, such as demo play S-04 or demo play run-15 --base http://127.0.0.1:5080');
  const base = args.text('--base');
  if (!base) throw new UsageError('--base <url> names the server, such as http://127.0.0.1:5080.');
  const file = readScenarios();
  const run = (file.runs ?? {})[id.toLowerCase()];
  if (run) return playRun(id.toLowerCase(), run, file.scenarios ?? [], base, args, out);
  const scenario = (file.scenarios ?? []).find(s => s.id.toLowerCase() === id.toLowerCase());
  if (!scenario) throw new UsageError('No scenario, run or track ' + id + ' in scenarios.json. The runs and tracks: ' + Object.keys(file.runs ?? {}).join(', ') + '. "demo check --base <url>" lists the scenarios.');
  const plan = playPlan(scenario, {
    base, record: args.flag('--record'), phone: args.flag('--phone'), readOnly: args.flag('--read-only'),
    slowMoMs: args.number('--slow-mo', 600, { min: 0, max: 10000 }),
    holdMs: args.number('--hold', 4, { min: 0, max: 120 }) * 1000,
  });
  out.log('Demo play: ' + scenario.id + ' ' + scenario.title + (scenario.personas?.length ? ' (' + scenario.personas.join(', ') + ')' : '') + '.');
  if (!scenario.spec) {
    out.error('demo: scenarios.json names no spec for ' + scenario.id + '. Play it by hand from its presenter script.');
    return 2;
  }
  if (!plan.ready) {
    out.error('demo: the end-to-end suite is not installed. Run "npm ci --ignore-scripts" in src/tests/e2e first, with PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 '
      + '(the suite uses the installed Chrome). Until then, play the scenario by hand from its presenter script, or run "demo check --base ' + base + ' --scenario ' + scenario.id + '" to see that it is ready.');
    return 2;
  }
  out.log('  $ node ' + plan.args.join(' '));
  if (args.flag('--read-only')) out.log('  Read only: every write is skipped, so the store does not change.');
  else if (writes(scenario)) out.log('  ' + scenario.id + ' writes: play it on a fresh store ("demo local reset"), or add --read-only.');
  else out.log('  ' + scenario.id + ' only reads: the store does not change.');
  const r = spawnSync(plan.command, plan.args, { cwd: plan.cwd, env: { ...process.env, ...plan.env }, stdio: 'inherit' });
  if (r.error) {
    out.error('demo: cannot start Playwright: ' + r.error.message);
    return 1;
  }
  out.log('Run folder: ' + plan.runDir);
  if (args.flag('--record')) {
    const videos = join(plan.runDir, 'videos');
    const files = existsSync(videos) ? readdirSync(videos).filter(f => f.endsWith('.avi')) : [];
    if (!files.length) out.log('No video was saved.');
    for (const f of files) out.log('Video: ' + join(videos, f) + ' (' + Math.round(statSync(join(videos, f)).size / 1e6) + ' MB, Motion JPEG AVI; VLC and Windows Media Player play it)');
  }
  return r.status ?? 1;
}

async function playRun(id, run, scenarios, base, args, out) {
  const groups = runGroups(run, scenarios);
  const runId = playRunId(id);
  out.log('Demo play: ' + run.title + ', ' + run.steps.length + ' steps in ' + groups.length + ' parts (' + run.doc + ').');
  if (run.before?.length) out.log('  Before it, off-screen: ' + run.before.join(', ') + '. Play those first (demo play <scenario>), or the steps that stand on them fail.');
  if (!args.flag('--read-only')) out.log('  A run writes: play it on a fresh store ("demo local reset"), or add --read-only.');
  let failed = 0;
  for (const g of groups) {
    const plan = playPlan({ id: g.id, spec: g.spec }, {
      base, record: args.flag('--record'), phone: args.flag('--phone'), readOnly: args.flag('--read-only'),
      slowMoMs: args.number('--slow-mo', 600, { min: 0, max: 10000 }),
      holdMs: args.number('--hold', 4, { min: 0, max: 120 }) * 1000,
      runId,
    });
    if (!plan.ready) {
      out.error('demo: the end-to-end suite is not installed. Run "npm ci --ignore-scripts" in src/tests/e2e first, with PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1.');
      return 2;
    }
    out.log('  ' + g.steps.join(', '));
    const r = spawnSync(plan.command, [...plan.args, '--grep', stepGrep(g.steps)], { cwd: plan.cwd, env: { ...process.env, ...plan.env }, stdio: 'inherit' });
    if (r.error) {
      out.error('demo: cannot start Playwright: ' + r.error.message);
      return 1;
    }
    if (r.status !== 0) failed++;
  }
  const runDir = join(E2E, 'runs', runId);
  out.log('Run folder: ' + runDir + (failed ? '. ' + failed + ' of ' + groups.length + ' parts had a failed step.' : '.'));
  if (args.flag('--record')) {
    const videos = join(runDir, 'videos');
    for (const f of existsSync(videos) ? readdirSync(videos).filter(x => x.endsWith('.avi')) : []) out.log('Video: ' + join(videos, f));
  }
  return failed ? 1 : 0;
}

// A scenario writes unless its reset says there is nothing to reset (scenarios.json "reset").
export function writes(scenario) {
  return !/^nothing\b/i.test(String(scenario.reset ?? '').trim());
}
