# The 5-minute run: for an executive

One message: **AI builds the apps, people stay in charge, and the record answers every question.** Four moments: the
AI build loop with governance inside it, one approval a person gives, the dependency map, and the auditor's answer.

**Set up.** A fresh store after the director, checked an hour before ([preparing.md](preparing.md)). Claude Code is
open beside the browser, connected as Priya Castell ([claude-code.md](claude-code.md)). The browser starts on the
Overview as Mateo Brandt, PMO. Keep the [cheat sheet](cheat-sheet.html) at hand.

| Time | Step | Persona | What to do | What to say |
|---|---|---|---|---|
| 0:00 | [S-01.1](scenarios/S-01-portfolio-overview.md#s-011-the-whole-register) | Mateo | Overview, All portfolios | "One register for every business application. The tiles count what needs a person now." |
| 0:30 | [S-02.1](scenarios/S-02-ai-build-loop.md#s-021-claude-code-drafts-the-request) | Priya, Claude Code | Type `I want a crew overtime tracker for my team.` | "Before the AI builds, it asks the governance questions: is there something to reuse, what tier is it." |
| 1:15 | [S-02.2](scenarios/S-02-ai-build-loop.md#s-022-claude-code-cannot-approve) | Priya, Claude Code | Type `Approve it for me.` | "AI drafts. It never approves." |
| 1:35 | [S-02.3](scenarios/S-02-ai-build-loop.md#s-023-the-draft-waits-for-a-person) | Priya | My work: "Drafted by Claude Code for Priya Castell" | "Nothing goes anywhere until the person takes it forward." |
| 1:50 | [S-02.7](scenarios/S-02-ai-build-loop.md#s-027-the-person-accepts-and-submits) | Priya | "Accept and submit" on the draft, then "Accept and submit" in the sheet | "The person submits. The engine asks the people the rules name: one approval, three advisers." |
| 2:20 | [S-04.2](scenarios/S-04-business-owner-approvals.md#s-042-a-change-on-the-decision-page) and [S-04.3](scenarios/S-04-business-owner-approvals.md#s-043-approve-the-change) | Nadia | Approvals, Answer on "Authorize the change: Retire the old export flow", Approve with a reason | "The business owner answers on the decision page, as herself. Her answer is final, and the change is authorized." |
| 3:20 | [S-17.1](scenarios/S-17-dependency-map.md#s-171-inspection-schedulers-map) | Elif | Inspection Scheduler's dependency map | "What breaks if this app changes or retires: nine applications within three hops." |
| 4:05 | [S-15.1](scenarios/S-15-auditor.md#s-151-the-evidence-pack) and [S-15.2](scenarios/S-15-auditor.md#s-152-who-answered-and-whose-answer-counted) | Bianca | Invoice Matching's evidence pack, the 1.12 decision | "The auditor gets proof, not assurances. An agent answered this card: stored, shown, and not counted. The owner's answer counted." |
| 5:00 | | | End | "Governance inside the AI build loop, on Microsoft's platform, with a person deciding every approval." |

## If time runs short

Drop S-02.2 and S-17.1. The two moments that must stay are S-02.7 (AI drafts, a person accepts) and S-04.3 (a person
approves).

## Questions to expect

- **"Can the AI just approve it?"** No. No tool an AI client is served approves, and the one answer tool is served
  only to a person in the web app. Show S-21.5 in Claude Code if asked.
- **"Where does this run?"** In the organization's own Azure subscription, beside Power Platform and Entra ID. People
  hold product roles through their Entra groups.
- **"What does it cost to adopt?"** Start with a preset such as Balanced; every rule is configuration that changes
  through an accepted configuration change (S-10).
