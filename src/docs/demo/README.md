# The product demo

Scenarios and presenter scripts for the product demo, on fictional data. Every person, group, application, host and ID
here is fictional.

| File | For |
|---|---|
| [scenarios.md](scenarios.md) | The catalog: every scenario, its personas, use cases, screens, end-to-end spec and story section |
| [scenarios/](scenarios/) | One presenter script per scenario, S-01 to S-23, with numbered steps |
| [run-5-minutes.md](run-5-minutes.md) | An executive: the AI build loop, one approval, the dependency map, the audit answer |
| [run-15-minutes.md](run-15-minutes.md) | The guided story |
| [run-45-minutes.md](run-45-minutes.md) | The deep dive: every scenario and every screen group |
| [tracks.md](tracks.md) | One route per audience: CIO and CTO, IT and platform, security, data governance, makers, PMO, auditor |
| [claude-code.md](claude-code.md) | The prompts to type in Claude Code, the answers and the refusals to expect |
| [preparing.md](preparing.md) | The demo kit, the sandbox rebuild, and the checks before a demo |
| [cheat-sheet.html](cheat-sheet.html) | One printable page for the presenter, readable on a phone |

The demo kit is in `src/demo/kit/`; its scenario file, `scenarios.json`, uses the same IDs and checks each scenario's
preconditions. The in-app Demo guide (`/app/demo-guide`) lists the same scenarios and steps.
