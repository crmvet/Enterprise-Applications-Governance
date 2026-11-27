# Role tracks: one route per audience

Each track picks the scenarios an audience cares about, in the order to play them, and what to stress. Times are for
the steps listed; the scripts are in [scenarios/](scenarios/). Play a track on a fresh store, in the order given; the
dependencies between scenarios are in [scenarios.md](scenarios.md#order-between-scenarios). A track whose steps stand on
an earlier scenario's writes says so in its "Before you start (off-screen)" line: play those steps first, before the
audience joins. `demo check --base <url> --track <track>` checks that the store is ready for the scenarios a track plays, and
`demo play <track> --base <url>` plays a track's steps in order with captions, for a rehearsal.

## CIO and CTO (10 minutes)

The question: can AI build the apps without losing control?

| Order | Steps | Stress |
|---|---|---|
| 1 | S-01.1, S-01.2 | One register for everything, whatever it is built on |
| 2 | S-02.1, S-02.2, S-02.7 | Governance inside the AI build loop; AI never approves |
| 3 | S-04.2, S-04.3 | A person answers every approval, on the decision page |
| 4 | S-17.1, S-17.4 | What breaks before a change or a retirement |
| 5 | S-13.1, S-13.3 | Measure flow and coverage; the slide-back signals |
| 6 | S-15.1, S-15.2 | The auditor's answer comes from the record |

## IT and platform (20 minutes)

The question: how does IT run it day to day?

| Order | Steps | Stress |
|---|---|---|
| 1 | S-03.1 to S-03.9 | Triage, the gap check against the estate, places and pipelines, leavers |
| 2 | S-05.1 to S-05.6 | Changes with their dependants; flags on deployments |
| 3 | S-17.1 to S-17.5 | Impact from an application, a component and a data location |
| 4 | S-11.1 to S-11.4 | Roles only through Entra groups; the binding check |
| 5 | S-14.1 to S-14.5 | The daily check, clients, the approval outbox, the Tool Policy |
| 6 | S-16.1 to S-16.3 | Support teams and the owner gaps on their applications |
| 7 | S-21.1, S-21.3, S-21.4 | Claude Code reads the same governance over MCP |

## Security (15 minutes)

The question: what can an AI client or an agent do on its own?

| Order | Steps | Stress |
|---|---|---|
| 1 | S-21.1, S-21.5, S-21.6, S-21.7, S-21.8 | The AI client holds the person's rights and no more; every refusal is recorded |
| 2 | S-07.2, S-07.3, S-07.4 | Restricted data to AI needs the data owner and a security reviewer |
| 3 | S-07.5, S-07.6 | A never rule stops a person and an AI client alike |
| 4 | S-08.1, S-08.2, S-08.5, S-08.6 | Agent autonomy agreed by the security reviewer; security reviews |
| 5 | S-05.6 | A code approval from an AI never counts |
| 6 | S-11.2, S-11.4 | What each role may call, and how far an AI client may go |
| 7 | S-15.4 | The audit trail of AI calls |

## Data governance (15 minutes)

The question: who owns the data, and what may each application do with it?

| Order | Steps | Stress |
|---|---|---|
| 1 | S-07.1 | The domain tree, owners, deputies and inherited rules |
| 2 | S-07.2 to S-07.4 | A data-use card, two answers |
| 3 | S-07.5, S-07.6 | The never rule |
| 4 | S-07.8, S-07.9, S-07.10 | Copies outside production, locations, every gap with its fix |
| 5 | S-07.11 | Declaring what an application reads |
| 6 | S-17.5 | What depends on a data location |
| 7 | S-10.1 to S-10.5 | A taxonomy change, with the data owner consulted by name |
| 8 | S-09.10 | Retention when an application retires |

Before you start (off-screen): S-09.8 and S-09.9 as Owen and Nadia, so the retiring application has its open retention
gap (DC-13) for S-09.10. S-10.1 to S-10.5 change version 1 into version 2 on the store.

## Makers building with AI (12 minutes)

The question: what does governance feel like from the build seat?

| Order | Steps | Stress |
|---|---|---|
| 1 | S-02.1 to S-02.8 | The AI client asks the governance questions first; the maker stays in charge |
| 2 | S-21.2, S-21.3 | My queue and the dependants, from Claude Code |
| 3 | S-07.6, S-07.7 | The rules say before saving whether a data use is allowed; AI drafts wait |
| 4 | S-12.1 to S-12.3 | Every refusal links to the guideline behind it |
| 5 | S-20.1 to S-20.4 | The same front door from the web app, for a tier 1 app |

## PMO (12 minutes)

The question: what is in the portfolio, and what gets funded or retired?

| Order | Steps | Stress |
|---|---|---|
| 1 | S-01.1 to S-01.5 | The portfolio at a glance |
| 2 | S-09.1 to S-09.5 | Score, priority, the Commit gate |

Before you start (off-screen): S-03.3 as Owen and S-04.5 as Nadia, so Weather alerts for outdoor jobs is on the table
and registered (S-09.1's counts and S-09.5 stand on them), and S-02's request is submitted. With no time for that,
replace step 2 with S-09.1 alone and go on with step 3.
| 3 | S-09.6, S-09.7 | The review pack; applications on their way out |
| 4 | S-04.7, S-04.8 | The sponsor's answer on an owner change |
| 5 | S-17.4 | The impact of retiring Finance Ledger |
| 6 | S-13.1 to S-13.3 | Measures with targets; the slide-back signals |
| 7 | S-16.1, S-16.2 | The committees and what waits on them |

## Auditor (10 minutes)

The question: can every answer be traced to a person, a rule and a time?

| Order | Steps | Stress |
|---|---|---|
| 1 | S-15.1 to S-15.4 | The evidence pack and the audit trail |
| 2 | S-19.1 to S-19.4 | The decision log, AI drafts marked, exceptions with review dates, documents |
| 3 | S-15.3 | Each record names the rules version it was checked under ("Checked under version 1") |
| 4 | S-14.2 | What the daily check sent, to whom and how |
| 5 | S-18.5 | A read-only role sees and changes nothing it should not |
