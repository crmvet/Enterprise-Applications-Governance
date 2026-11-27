# The 15-minute run: the guided story

"Govern AI development of your apps", told through the people who do the work. Sixteen scenarios, the key steps of
each. The Demo guide in the web app (`/app/demo-guide`) lists the same scenarios by ID (there S-16 comes after S-15;
here it plays at 12:00, after S-10) and opens each page as its persona.

**Set up.** A fresh store after the director, `demo check` all GO and `demo refresh` run if the store is older than a
day ([preparing.md](preparing.md)). Claude Code is open beside the browser, connected as Priya Castell
([claude-code.md](claude-code.md)). The browser starts on the Demo guide, or on the Overview as Mateo Brandt.

**Rhythm.** One sentence of why, then the screen proves it. Switch persona with the Demo guide's links, the "Acting
as" menu (demo install) or the account menu (local run).

## The run

| Time | Scenario | Steps | Persona | The point |
|---|---|---|---|---|
| 0:00 | [S-01](scenarios/S-01-portfolio-overview.md) The portfolio | S-01.2, S-01.3, S-01.4 | Mateo | One register; the owner gaps and the overdue reviews of Field operations |
| 0:45 | [S-02](scenarios/S-02-ai-build-loop.md) The AI build loop | S-02.1, S-02.2, S-02.3, S-02.5, S-02.7, S-02.8 | Priya | Claude Code drafts and cannot approve; the person checks the tier and submits; AI activity shows every draft and refusal |
| 3:00 | [S-03](scenarios/S-03-triage-gap-check-places-leavers.md) Triage and the gap check | S-03.2, S-03.3, S-03.6, S-03.9 | Owen | Who decides and who only advises; a claim from the estate; a leaver |
| 4:15 | [S-04](scenarios/S-04-business-owner-approvals.md) The business owner | S-04.2, S-04.3, S-04.5, S-04.7, S-04.8, S-04.9 | Nadia, then Mateo, then Nadia | Answers on the decision page; the owner role never empty; the stale runbook on the Go-live gate |
| 6:00 | [S-05](scenarios/S-05-changes-and-deployments.md) Changes and deployments | S-05.2, S-05.3, S-05.6 | Hugo | A change planned with its dependants; deployments only an AI approved |
| 7:00 | [S-06](scenarios/S-06-reviews-and-exceptions.md) Reviews and exceptions | S-06.3, S-06.4, S-06.6 | Elif, Marcus answers S-06.3 | A design decision, the review done, the Design gate met; the exceptions register |
| 7:45 | [S-07](scenarios/S-07-data-taxonomy.md) Data | S-07.1, S-07.2, S-07.3, S-07.4, S-07.6, S-07.8 | Hana, Kai, Noor, Priya, Hana | The domain tree; two answers for restricted data to AI; the never rule refused for a person too; copies outside production |
| 9:15 | [S-08](scenarios/S-08-agents.md) Agents | S-08.1, S-08.2, S-08.4 | Dmitri | Autonomy is agreed by people; an agent's answer never counts |
| 10:00 | [S-09](scenarios/S-09-demand-portfolio-retirement.md) Demand and retirement | S-09.2, S-09.3, S-09.4, S-09.8, S-09.9, S-09.11 | Mateo, Nadia, Owen, Nadia | Score, priority, the Commit gate; a retirement on purpose (S-09.9 proposes the retirement complete that S-09.11 answers; say S-09.2 without words) |
| 11:15 | [S-10](scenarios/S-10-configuration.md) Configuration | S-10.3, S-10.4, S-10.5 | Yusuf, then Jonah | One admin proposes; here another accepts the exact change; version 2 in force |
| 12:00 | [S-16](scenarios/S-16-teams-and-committees.md) Teams and committees | S-16.1, S-16.2, S-16.4 | Owen, then Marcus | Committees, support teams and what waits on each |
| 12:45 | [S-11](scenarios/S-11-roles-and-access.md) Roles and access | S-11.1, S-11.4 | Owen | Roles only through Entra groups; the binding check |
| 13:15 | [S-12](scenarios/S-12-guidelines.md) Guidelines | S-12.1 | Priya | Every refusal opens its guideline |
| 13:30 | [S-13](scenarios/S-13-measures.md) Measures | S-13.1 | Mateo | Measures in pairs, and the slide-back signals |
| 13:45 | [S-14](scenarios/S-14-admin.md) Admin | S-14.2 | Owen | What the daily check did, and to whom it sent |
| 14:00 | [S-15](scenarios/S-15-auditor.md) The auditor | S-15.1, S-15.2, S-15.4 | Bianca | The evidence pack; an AI answer stored and not counted; the audit trail of AI calls |
| 15:00 | | | | End |

## What to say at each turn

- **0:00** "Every application, whatever it is built on, in one register, with an owner and a deputy."
- **0:45** "The maker builds with AI. The governance questions are asked inside the build loop, before anything is
  built, and the AI never approves."
- **3:00** "One front door for every request, and a gap check that finds what runs but is not registered."
- **4:15** "People answer every approval, on the decision page, signed in as themselves."
- **6:00** "Every change knows what it touches, and the engine flags what came another way."
- **7:00** "Architecture decisions record the options weighed, and the gate they feed."
- **7:45** "Business data in a tree, each domain with an owner. A never rule stops a person and an AI client alike."
- **9:15** "Agents have an agreed level of autonomy."
- **10:00** "From a scored request to a funded priority, and an application retired on purpose."
- **11:15** "The rules are configuration. One admin proposes; here another accepts the exact change." (Balanced also
  lets the instructing admin answer, and the page says so; the Regulated preset refuses it.)
- **12:00** "Committees and teams are the Entra groups the product already knows."
- **12:45** "Roles come only through groups, and the binding check proves they exist."
- **13:15** "A refusal teaches: it opens the guideline behind it."
- **13:30** "Is AI development working? Measure flow and coverage, not apps shipped."
- **13:45** "The daily check runs the cadences and tells the people they name."
- **14:00** "The auditor's answer comes from the record."

## If it runs long

Drop, in this order: S-14, S-13, S-12, S-11, then S-08.4 and S-07.8. Keep S-02, S-04 and S-15: they carry the story.

## After the run

Reset with a fresh store before the next audience: most steps above write. See [preparing.md](preparing.md).
