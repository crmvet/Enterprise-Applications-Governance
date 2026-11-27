# Preparing a demo

The demo kit (`src/demo/kit/`, Node.js 22 or later, no packages) starts a local demo, refreshes a demo before a
meeting, checks every scenario, plays one in a browser, and rebuilds a demo install on Azure. Its full reference is
`src/demo/kit/README.md`; this page is the presenter's checklist. Run the commands from the repository root.

```
node src/demo/kit/demo.mjs <command> [options]     # anywhere
src\demo\kit\demo.cmd <command> [options]          # Windows
sh src/demo/kit/demo <command> [options]           # Linux, macOS, WSL
```

## A local demo

```
node src/demo/kit/demo.mjs local up --port 5080
```

It builds the server and the web app when they are missing or stale, seeds a fresh store with the demo estate, starts
the server on `http://127.0.0.1:5080` (Development, the development sign-in, the demo flag, the directory mode Demo),
runs the demo director (64 steps) and prints the web app's URL, the MCP endpoint with the Claude Code command, and the
seventeen personas. On Windows it runs the server inside WSL, because Smart App Control can stop freshly built .NET
assemblies; the browser on Windows reaches it at the same address. In Git Bash, put `MSYS_NO_PATHCONV=1` in front of a
command that passes a Linux path, such as `--folder /root/eag-demo-local`.

| Command | What it does |
|---|---|
| `demo local up [--port <n>] [--folder <dir>]` | A fresh store and a running server. Run again from the same folder, it stops the server it started and starts over from the seed (a few minutes) |
| `demo local reset [--folder <dir>]` | Puts back the store `local up` saved right after the director, and restarts the server on it: a fresh demo in seconds, for the next rehearsal or audience |
| `demo local status [--folder <dir>]` | Whether that server runs and is ready |
| `demo local down [--folder <dir>]` | Stops the server it started, by the PID it recorded, never by name |

Open `http://127.0.0.1:5080/app/`, choose a persona on the development sign-in page and choose "Continue as this
persona". The page lists the seventeen demo personas by the names and roles of the persona file, such as "Mateo Brandt,
PMO"; the test harness logins show only with `?all=1` in the address. The account menu (the name at the top right) switches persona later; the Demo guide (`/app/demo-guide`)
switches it with each link.

## A demo install

A demo install runs on Azure in the environment named `Demo`, with the "Demo, fictional data" badge and the "Acting
as" persona switch. The operator signs in as themselves and acts as any persona.

```
node src/demo/kit/demo.mjs sandbox rebuild --dry-run
node src/demo/kit/demo.mjs sandbox rebuild --yes
```

The rebuild runs seven steps (files, reset, provision, deploy, seed, director, check) and reads every value from the
azd environment of `src/adapters/azure`. Every command is printed with `$NAME` in place of each value, so no value of the
install reaches the screen, and the dry run prints paths from the repository root. `--dry-run` prints the steps and runs
nothing. A real run needs `--yes`, whatever `--steps` says, and stops before its first step unless the azd environment
says `PRODUCT_DEMO=true`: the rebuild deletes the database and writes the story through the persona switch, so it runs
on a demo install only.

Where to run it: where .NET runs and where the install's azd environment is. On Windows with Smart App Control, that is
WSL: run the kit from a clone of the repository inside WSL (not the copy `demo local up` makes, which leaves out
`.azure/`), select the environment there with `azd env select <name>` in `src/adapters/azure`, and sign in inside WSL
with `azd auth login` and `az login`; the seed and director steps sign in to Azure SQL as the person signed in to the
Azure CLI.

The kit's director step runs the director in dev mode through a local server on loopback whose store is the install's
database, with the development identity on. So after `demo sandbox rebuild`, Claude Code's seeded work is there: the
director does Claude Code's part as `priya` over MCP, and its rows say "Claude Code". The seeded rows of the personas
say "Web app"; only live persona switches on the install stamp "Web app, demo operator". A director run against the
install itself (`--mode demo`, or `demo refresh` against the install's URL) does Claude Code's part only with the
operator's Claude Code token (`PRODUCT_DEMO_MCP_TOKEN`).

## Fresh before a meeting

Approval requests expire by the rules of the preset: five days for a change authorization, ten for a risk
acceptance or a data use. A demo held days after the director ran meets expired cards.

```
node src/demo/kit/demo.mjs refresh --base http://127.0.0.1:5080
```

On a demo install, `refresh` acts as the operator, with the operator's bearer token for the web app in
`PRODUCT_DEMO_TOKEN`: copy it from the Authorization header of a request the web app sends to `/api` (the browser's
developer tools, Network). Read it without echoing it, so it stays out of the shell history, and unset it afterwards:

```
# bash
read -rs PRODUCT_DEMO_TOKEN && export PRODUCT_DEMO_TOKEN
node src/demo/kit/demo.mjs refresh --base https://<host>
unset PRODUCT_DEMO_TOKEN

# PowerShell
$env:PRODUCT_DEMO_TOKEN = Read-Host -MaskInput
node src/demo/kit/demo.mjs refresh --base https://<host>
Remove-Item Env:PRODUCT_DEMO_TOKEN
```

`refresh` lists the story's requests that expired or expire within 48 hours (`get_demo_freshness`), asks each one again
as the platform admin ("Ask again", `renew_approval_request`), reruns the director, which adds only what is missing,
and prints what it renewed. `--dry-run` checks and writes nothing. Running it twice is safe.

The Demo guide's panel "Is the demo fresh?" reads the same list, links each request to its decision page, and shows
the exact refresh command for the install. One request at a time works too: its proposer or a platform admin opens it
and chooses "Ask again".

## Go or no-go

```
node src/demo/kit/demo.mjs check --base http://127.0.0.1:5080
node src/demo/kit/demo.mjs check --base http://127.0.0.1:5080 --scenario S-04
node src/demo/kit/demo.mjs check --base http://127.0.0.1:5080 --track run-15
```

`--track` checks only the scenarios a run or a role track plays (the names in `src/demo/kit/scenarios.json`, `runs`).

`check` writes nothing. It proves the health and the config, that each persona signs in, each persona's pending work,
that no story request has expired, and every scenario's preconditions (`src/demo/kit/scenarios.json`). Each story
request that expired or expires within 48 hours is named on its own line, and a scenario whose precondition stands on
an expired request says NO GO with the request ("its request DEC-0206 expired: run demo refresh"); one about to expire
says GO, WARN. It ends with "GO: every check passed, and all 23 scenarios are ready." (or "GO, with a warning: ...")
or names what is missing; the exit code is 1 when any scenario is not ready. On a demo install the preconditions about Claude Code's seeded work are checked only when the
operator's Claude Code token is set.

## Play a scenario

```
node src/demo/kit/demo.mjs play S-04 --base http://127.0.0.1:5080 [--record] [--phone] [--slow-mo 600] [--hold 4] [--read-only]
node src/demo/kit/demo.mjs play run-15 --base http://127.0.0.1:5080
node src/demo/kit/demo.mjs play pmo --base http://127.0.0.1:5080
```

It plays one scenario, a whole run (`run-5`, `run-15`) or a role track (`cio-cto`, `it-platform`, `security`,
`data-governance`, `makers`, `pmo`, `auditor`) in a visible browser at human pace, with a caption bar naming the persona
and the step ID, using the scenarios' end-to-end tests (`src/tests/e2e/specs/<ID>-<slug>.spec.ts`). The caption runs
along the foot of the page and moves beside an open side sheet, so it never covers the sheet's buttons. `--hold` keeps
the end of each step on screen for that many seconds (4 by default), so a step with nothing to click can still be read.
`--record` saves a video of each scenario as a Motion JPEG AVI (about 35 MB for 2 minutes) and prints where it is:
`src/tests/e2e/runs/<time>-play-<id>/videos/`. `--phone` uses the phone screen, `--read-only` skips every write for a
demo install. A scenario that writes says so before it plays; play it on a fresh store (`demo local reset`). Until the
end-to-end suite is installed, the command says so and exits 2; play the scenario by hand from its script instead.

## The day before

1. On a local run: `demo local up`. On a demo install: the sandbox rebuild, or `demo refresh` if it was rebuilt
   within the last few days.
2. `demo check`: every scenario GO.
3. Connect Claude Code as Priya Castell and type `Call whoami.` ([claude-code.md](claude-code.md)).
4. Walk the chosen run once with its scripts, or `demo play run-15` with captions. The run writes approvals,
   proposals and claims the next audience should not see, so finish with a fresh store (`demo local reset`).

## One hour before

1. `demo local status`, or open the install's `/health/ready`: the server answers.
2. `demo check --base <url>`: GO. If it names expired or soon-expiring requests, run `demo refresh`, then check again.
3. Open the Demo guide: "Fresh: every story approval request has more than 48 hours left."
4. The top bar shows "Demo, fictional data".
5. Claude Code answers `whoami` for Priya Castell, and its tool list is there (`/mcp` in Claude Code).
6. The browser zoom is 100 percent, notifications are off, and only the demo's windows are open.
7. The [cheat sheet](cheat-sheet.html) is printed or open on a phone.
8. No crew overtime tracker exists yet (Intake, Drafts, as Priya): S-02 starts clean.

## After the demo

- A local run: `demo local reset` for a fresh store in seconds, `demo local up` again to start over from the seed, or
  `demo local down`.
- A demo install: the sandbox rebuild for a fresh store. A full rebuild resets everything the audience saw.

## When something goes wrong on stage

| What happens | What to do |
|---|---|
| A card says the request expired | Ask again on the decision page (its proposer or a platform admin), or `demo refresh` in a break |
| A persona sees nothing to answer | Check the persona in use (the account menu names it), and that `demo check` was GO |
| A count differs from the script | Something before it ran out of order; say what the count is for, and go on |
| Claude Code answers in other words | Judge by the tool calls and the product's answers ([claude-code.md](claude-code.md)) |
| Claude Code is refused on every tool | The persona header is missing or wrong; reconnect it |
| The page shows "Page not found" for the Demo guide | The install is not a demo install, or the demo flag is off |
