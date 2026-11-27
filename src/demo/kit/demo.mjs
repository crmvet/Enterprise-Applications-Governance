#!/usr/bin/env node
// demo.mjs: the demo kit, one command line for the fictional demo of the product (docs in README.md next to it).
//
//   demo local up        build if needed, a fresh store with the demo estate, the local server, the director
//   demo local down      stop the server "local up" started (by its PID)
//   demo local status    say whether that server runs
//   demo local reset     put back the store saved after the director, and restart the server
//   demo local age-request, demo local fail-card
//                        rehearsal and test aids on a local run: a request in its last 48 hours, a failed card
//   demo refresh         ask the story's expired or soon-expiring approval requests again, then rerun the director
//   demo check           go or no-go for every scenario of scenarios.json
//   demo play <id>       play one scenario in a visible browser (the Playwright suite in src/tests/e2e)
//   demo sandbox rebuild the demo install's rebuild on Azure, step by step; --dry-run prints the commands only
//
// Node.js 22 or later. No packages: the browser part (demo play) is the end-to-end suite's own package.
// Exit codes: 0 done (or ready), 1 a step failed (or a scenario is not ready), 2 bad usage.
import { UsageError, parseArgs } from './lib/args.mjs';
import { check } from './lib/check.mjs';
import { local } from './lib/local.mjs';
import { play } from './lib/play.mjs';
import { refresh } from './lib/refresh.mjs';
import { sandbox } from './lib/sandbox.mjs';

export const HELP = `Usage: demo <command> [options]   (node src/demo/kit/demo.mjs, or demo.cmd on Windows)

Commands:
  local up       Build the Release server and the web app if missing or stale, seed a fresh SQLite store with the
                 demo estate, start the server on loopback (Development, development identity, demo flag, directory
                 mode Demo), run the demo director, and print the URL and the personas. From Windows it drives WSL.
                   --port <n>            default 5080
                   --folder <dir>        the work folder (store, logs, state.json); from Windows a folder inside WSL,
                                         default /root/eag-demo-local; elsewhere default ~/.eag-demo-local
                   --rebuild             build the server and the web app even when they look up to date
                   --no-build            use the server as built       --no-web-build   use the web app as built
                   --no-director         start with the estate only    --keep-store     keep the store as it is
                   --timeout <s>         the director's wait for one answer (default 180)
                   --ready-timeout <s>   how long to wait for the server to be ready (default 180)
                   --native              on Windows: run here instead of driving WSL
                   --wsl-distro <name>   default Ubuntu                --wsl-user <name>  default root
  local down     Stop the server that "local up" started from --folder (by the PID in state.json).
  local status   Say whether that server runs and is ready.
  local reset    Put back the store "local up" saved right after the director, and restart the server on it: a fresh
                 demo in seconds, for the next rehearsal or audience. "local up" again starts over from the seed.
  local age-request --decision <number> [--hours <n>] [--role <key>] [--json]
                 Rehearsal and test aid, local run only. One pending request of that decision (such as DEC-0206) now
                 expires in --hours (default 40, at most 47), so "Ask again" shows for it. --role picks the request
                 that asks that role or owner role; otherwise the oldest.
  local fail-card --decision <number> [--role <key>] [--settled] [--json]
                 Rehearsal and test aid, local run only. The approval card of one pending request of that decision
                 fails, so the Admin page offers "Send again" for it. With --settled, the closed card of a request
                 that is answered, canceled or expired fails instead, so the Admin page offers "Close the card" for it.
                 Both edit the SQLite store of --folder directly, past the engine, and refuse a demo install, a
                 non-loopback --base or --mode demo. The audit trail does not show the edit. "local reset" undoes it.

  refresh        --base <url> [--dry-run] [--no-director] [--within-hours <n>] [--as <persona>]
                 Asks again ("Ask again", renew_approval_request) every story approval request that expired or expires
                 within 48 hours, as the platform admin persona owen, then reruns the director, which adds only what
                 is missing. Prints what it renewed.
  check          --base <url> [--scenario <id> | --track <run or track>] [--json]
                 Go or no-go: health, config.json, the persona count, every persona's sign-in and pending counts,
                 the story's approval requests, and each scenario's preconditions (scenarios.json). Exit 1 when any
                 scenario is not ready. --track checks only the scenarios a run or track plays, such as run-15,
                 with the scenarios of its "before" steps.
  play <id>      --base <url> [--record] [--phone] [--slow-mo <ms>] [--hold <s>] [--read-only]
                 Plays one scenario (S-04), a run (run-5, run-15) or a role track (cio-cto, it-platform, security,
                 data-governance, makers, pmo, auditor) in a visible browser at human pace, with captions, through the
                 Playwright suite. --hold keeps the end of each step on screen (default 4 seconds). --record saves a
                 video and prints where.
  sandbox rebuild [--dry-run] [--steps a,b] [--yes] [--env-file <file>] [--folder <dir>] [--client-ip <ipv4>]
                  [--extra-binding <file>] [--extra-rows <file>] [--local-port <n>]
                 The demo install's rebuild on Azure: files, reset, provision, deploy, seed, director, check. Every
                 value comes from the azd environment. Prints every command; --dry-run runs nothing. A real run needs
                 --yes, whatever the steps, and stops before its first step unless PRODUCT_DEMO is true.

Options for every command that calls a server:
  --mode dev|demo     dev: the development identity on a loopback URL (the default there); demo: a demo install
  --token-env <name>  demo mode: the variable with the operator's web app token (default PRODUCT_DEMO_TOKEN)
  --timeout <s>       the wait for one answer (default 180)
`;

const out = {
  log: line => console.log(line),
  error: line => console.error(line),
  outStream: process.stdout,
  errStream: process.stderr,
};

async function main(argv) {
  const args = parseArgs(argv);
  const [command] = args.words;
  if (!command || args.flag('--help') || command === 'help') {
    console.log(HELP);
    return command ? 0 : 2;
  }
  switch (command) {
    case 'local':
      return local(args, out);
    case 'refresh':
      return refresh(args, out);
    case 'check':
      return check(args, out);
    case 'play':
      return play(args, out);
    case 'sandbox':
      return sandbox(args, out);
    default:
      throw new UsageError('Unknown command "' + command + '". Run "demo help".');
  }
}

// The exit code is set, not forced: process.exit() while a child's pipes or an HTTP connection are still closing stops
// Node on Windows with a libuv assertion (exit 127). The process ends on its own once they have closed.
main(process.argv.slice(2)).then(
  code => {
    process.exitCode = code ?? 0;
  },
  e => {
    if (e instanceof UsageError) {
      console.error('demo: ' + e.message);
      process.exitCode = 2;
      return;
    }
    console.error('demo: ' + (e?.stack ?? e));
    process.exitCode = 1;
  },
);
