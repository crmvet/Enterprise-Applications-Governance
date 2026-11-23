# Working defaults and open design questions

Step 2, document 28.

> **Platform (D-26, 2026-11-22).** The first release is designed to run in the client's own Azure subscription, on the Azure binding. The Dataverse binding is kept as a later option, not a plan. So Q19 (approval cards on the Azure binding) applies, as D-26 names it: it is the working default for approval cards. Q20 and Q21, the other Azure defaults, apply too, and issue 13's host (Azure Functions Flex Consumption) is the host.

- **Date:** 2026-11-22.
- **Status:** working defaults, so the step 2 design can move forward on one consistent reading. None of them is a decision, and the product owner can change any line. Sections 1 and 2 are settled for the design. Section 3 holds six open design questions. Each touches an accepted decision, so each stays open until the product owner answers it. Section 4 holds three design questions that the first code build left open.
- **How the defaults were chosen:** a review across documents 20 to 26 found 16 places where the documents disagreed (section 1) and 23 design questions (sections 2 and 3). Where the review recommended an option and no decision says otherwise, that option is the working default. Every document in `docs/design/step2/` is aligned to these defaults (alignment pass, 2026-11-22).
- **How other documents cite this file:** "28, issue n" for a row of section 1, and "28, Qn" for a question in section 2 or 3, and "open from the build, item n" for a row of section 4. A line that follows one of the six open questions in section 3 is marked as a working default with its question number, and never as decided.
- **Decisions:** D-numbers refer to the decision records in [docs/decisions/](../../decisions/).

---

## 1. Open issues settled for the design

These are points of internal consistency between the documents, not product owner decisions.

| # | Issue | Working default |
|---|---|---|
| 1 | Decisions with no approval rule | Every decision type goes on at least one card, to the holder of its accountable letter (C31). The engine may accept without a card only for a locked list of types (see question 8 in section 3). Minimum Count 0 is removed from OP-02, except for that list. |
| 2 | Two algorithms for which approval rules apply | Document 21's rule: the single most specific rule group wins, and broader groups drop out. Write it into document 20 (C32). Combined cases get their own group in the seed. |
| 3 | Configuration drafts, and who writes configuration rows | Document 21's model. A draft is a patch stored on a Configuration Draft entity on both bindings, and C02 (Configuration Set Version) is created only at activation (class A, append-only). Live configuration rows change only inside the activation operation. No person, role or tool writes configuration tables directly. |
| 4 | Who authorizes tier 3 and emergency changes | The change manager authorizes tier 3 changes and emergencies. The change board handles exceptions and windows that cover several applications. There is no separate emergency approver role. Question 11 in section 2 holds the same default. |
| 5 | Three shapes for separation of duties | Document 21's Separation Rule entity, C40. OP-03 reads C40. Document 26's four columns and document 25's single column are dropped. |
| 6 | Configuration accountability by area | One accountable per configuration decision type: the configuration admin. Roles for each area (the change manager for gate policy, the AI lead for AI rules, the platform owner for roles and bindings) are consulted, not accountable. Remove the per-area accountables from P11. |
| 7 | The default role list, and the approval flow's identity | Document 21 holds one seed list, agreed with document 26: 14 human roles including `reader` and `security-reviewer`. `deployment-recorder` and `approval-writer` are workload roles held through workload grants. The approval flows are called the "approval runner", and the workload role that writes outcomes is the "approval writer". Record both terms in document 20, and add the missing role defaults to document 24, section 2.2. |
| 8 | Two shapes for workload access | Workload Grant (C42, document 26), with a reason, the deciding decision and an expiry. C30 stays group-only (V35). This depends on question 12 in section 3. |
| 9 | Does the engine ever act with more rights than the caller? | No. It always runs as the caller, with an engine-nested marker, and approval outcomes are protected by column security (question 15). If spike S2 (document 25, section 14) fails, elevation is allowed only inside named engine effects. Align document 26, sections 3.4 and 6.3. |
| 10 | Read visibility defaults | Open read, with three settings. `intake-visibility` defaults to "own" (requester, PMO, platform admin, architect). `claim-visibility` defaults to "admins and claimant". `record-access-mode` defaults to open, with portfolio read available (question 13). |
| 11 | Process steps with no MCP tool | Add or extend tools in document 24: `request_architecture_review`, `record_deployment_correction` (it links a deployment to a change by adding a correcting record), `link_component` with a "retired" outcome, `record_owner_leave` and `run_owner_check`, an owner-gap list in `get_portfolio_summary`, `explain_rules` (returns the engine's rule trace), `withdraw_record` with a configuration subject kind, `get_configuration_health` (version history and engine info), the effect on live applications in `propose_configuration_change` validate-only, and reported code approvals as an input of `record_deployment`. |
| 12 | Rules the processes need that document 25 lacks | Add engine operations to document 25: evaluate a standard change model's conditions; compare a gap check result with the register and run the claim lifecycle; a separation-of-duties test in OP-03 that reads C40; a consultation outcome (Is Consultation on C32, recorded as a recommendation only); the full validator set V01 to V44; and the linked-mode authorization fact. |
| 13 | Where the server and engine run on the Azure binding | One service on Azure Functions Flex Consumption (.NET 10) that hosts the API, the MCP server and the engine. Container Apps is the fallback if the on-behalf-of or MCP spike fails on Functions. A spike confirms the host before the build. |
| 14 | A built-in fallback for a missing gate policy row | No fallback. The engine refuses governed writes and reports a broken configuration (document 21, rule 8). V23 makes the case impossible after validation. |
| 15 | Columns and settings missing from document 22 | Add to document 22 the columns the other documents need: on C01, C02, C12, C22, C32, O05, O21 and O24, and the common columns Engine Version and Engine Checked On. Merge `history-retention-days` into `audit-retention-days`. Keep one settings table in document 21 that lists every setting, with its default and owner. |
| 16 | Stored or computed "missing" documents | Computed. "Missing" is worked out from configuration, and a Document Link row exists only once a link has been recorded. |

## 2. Questions adopted as working defaults

Each row uses the recommended option, and the product owner can override any of them. The documents that use a default cite it as "28, Qn".

| Q | Question | Working default |
|---|---|---|
| 2 | How configuration differs per area | One working set, with an optional portfolio scope on Gate Policy, Approval Rule, Decision Right and the change management mode. The most specific row wins. |
| 3 | Mixing presets | One full preset plus declared overlays (small team, strict separation). The result is validated as one set. |
| 5 | A release changes the shipped defaults | The upgrade proposes a draft. A configuration admin accepts it, and a client's local changes are never lost. |
| 6 | Microsoft CoE baseline preset | Ships in the first release, extending Balanced, and is re-checked against Microsoft's guidance each release. |
| 10 | How strict separation of duties is | Separation Rule rows with preset defaults. Balanced refuses self-approval at tier 3. Regulated and strict separation refuse it at every tier. Starter and small team allow it only through a recorded exception decision. |
| 11 | Who authorizes tier 3 and emergencies | The change manager. The board handles exceptions (as in issue 4). |
| 13 | What every role can read | Open read, with three settings (as in issue 10). |
| 14 | Claude Code client path | A registered Entra client first, with CIMD (Client ID Metadata Documents, document 24, section 4) as an option for pilots. CIMD becomes the lead once it is generally available. |
| 15 | Engine rights | Always as the caller (as in issue 9). |
| 16 | Writes that bypass the engine | Detect, plus guidance. Every engine write gets an Engine Checked On stamp, a scheduled read-only check finds bypassed rows, and top admin roles are kept for break-glass accounts. |
| 17 | How outside staff who set up a client's configuration reach the client's tenant | A guest account in a group that is PIM-eligible for the configuration admin role. Activation needs the client's approval and is time-bound. For clients without Entra ID P2, the client's own staff run the tools while the outside staff advise. |
| 18 | Noticing that an owner left | An on-demand owner check, run delegated by the admin, plus an optional Entra Lifecycle Workflows sample for clients with ID Governance. |
| 19 | Approval cards on the Azure binding. **Applies: the Azure binding was chosen (D-26).** | Power Automate approval cards from a runner flow group carrying one Process license. The product's own approval page is offered where a security review asks for token-level evidence of the approver. |
| 20 | How the engine reaches the database on Azure | Managed identity, plus the person's ID in SESSION_CONTEXT for row-level security. |
| 21 | Ledger tables on Azure | Ledger tables for the frozen and append-only entities, a rule against personal data in free text, label-clearing erasure and a documented legal basis. |
| 22 | Linked mode authorization | The client's ITSM tool reports the authorization state through a client-built step. Where no step exists, the change manager attests on a confirmation card. A recorded number alone never counts. |
| 23 | The engine library as a NuGet package | Decide after the first release, with a package planned. |

## 3. Six open design questions

Each of these questions touches an accepted decision, so the product owner answers it. Until then, every document in this folder uses the recommended option as its working default and marks the line with the question number. None of them is stated as decided anywhere.

| Q | Question | Decisions it touches | Recommended (the working default) |
|---|---|---|---|
| 1 | Is a short list of fixed engine meanings acceptable under the rule that nothing is hardcoded? Labels stay configurable, but the engine must know that a status is final, an approver is a person, or a route is a pipeline. | D-06, D-10 | Yes. About 20 fixed meanings, all labels configurable. |
| 4 | When an admin tells an AI to change configuration, does the admin then accept the exact change on a card? | D-10 | Yes, one card showing the diff. The instructing admin may accept their own AI's draft, and strict separation needs a second admin. |
| 7 | Do AI approvals from client tools count? For example, a pull request approved only by GitHub Copilot. ("AI never approves" in the product itself is already decided and is not part of this question.) | D-02, D-10 | No. AI-only code approvals never count as authorization, and the shipped gate default flags them. |
| 8 | May the engine ever accept a decision without a person's card? | D-02 | Only for a locked list: lowest-tier intake and standard changes, whose model a person approved once. Everything else goes on a card. |
| 9 | Which act counts as a person's for AI drafts that are not decisions, such as submitting an intake or a change, registering an app, or marking a document current? | D-02, D-16 | Configuration per subject kind. An AI client may submit intakes, changes and decisions for the person. Registering an application, and marking a gate-relevant document current, need a confirmation card. Links and deployment records need nothing. |
| 12 | Pipelines are not people. May each pipeline's workload identity hold the deployment recorder role directly, as a recorded and expiring workload grant? Entra does not put a group's app roles into a service principal's token. | D-11 | Yes, a stated exception for non-human workloads only. People still get roles only through groups, and agent identities never get a grant. |

### 3.1 Question 1: Is a short list of fixed engine meanings acceptable under the rule that nothing is hardcoded?

- **Decisions it touches.** D-06 (risk tiers, statuses, kinds and types are configuration rows, and logic never branches on a fixed value) and D-10 (rules are configuration, and the engine enforces them on every write).
- **Background.** To enforce that AI never approves, that accepted decisions are append-only, and that flags and gates behave as configured, the engine must know what some labels mean: which decision status is final, which approver is a person, which deployment route is a pipeline step. Document 20, section 2.4 lists these meanings as the engine contract.
- **Example.** A client renames "Accepted" to "Approved by board" and adds "Deferred". The engine must still know that the first satisfies a gate and the second does not.
- **Options.**
  - **A. Fixed meanings, configurable labels (recommended).** About 20 small contracts, and every client label maps to one meaning. *Pros:* safety rules cannot be configured away, not even by an AI making configuration changes, and upgrades and tests stay predictable. *Cons:* a status with brand-new behavior needs a product release.
  - **B. Configurable meanings.** Each status's behavior is itself a rule. *Pros:* the most flexible. *Cons:* a configuration change could remove a safety rule, and every client becomes a different product to test and audit.
  - **C. Fixed option sets.** *Pros:* the simplest to build. *Cons:* contradicts D-06.
- **Recommendation.** A. The contract list is published in document 20, section 2.4, and every label stays configurable.
- **What the answer changes.** With A, Status, Actor Kind, Deployment Route and Link Kind keep their meaning columns, and V45 (proposed by the build, B21-1) can know the configuration admin role by its fixed key. With B, document 20, section 2.4 becomes a rule language, and the engine library in document 25 grows a lot.

### 3.2 Question 4: When an admin instructs an AI to change configuration, does the admin then accept the exact change?

- **Decisions it touches.** D-10 (configuration changes are made by an AI on an admin's instruction, and nobody edits configuration by hand).
- **Background.** D-10 does not say whether the admin's instruction is itself the approval, or whether the admin then accepts the exact change. An AI can misread an instruction, and a chat message is not a record of what changed. The engine already computes a diff and a plain change summary for every Configuration Draft (C46), so the exact change can be shown.
- **Example.** The admin says "make tier 2 stricter". The AI raises the tier 2 production gate to Enforce and also adds the data owner to tier 2 go-live. Did the admin approve both?
- **Options.**
  - **A. The admin accepts the engine's diff on an approval card (recommended).** In the default presets the instructing admin may accept their own AI's draft; the strict separation overlay requires a second admin. *Pros:* the record shows exactly what a person accepted, and AI never activates anything. *Cons:* one extra click per change.
  - **B. The instruction is the acceptance.** *Pros:* the fastest. *Cons:* nobody sees the actual diff, an AI mistake goes live, and it goes against the rule that AI never approves.
  - **C. Always two admins.** *Pros:* the strongest control. *Cons:* blocks small teams that have only one configuration admin.
- **Recommendation.** A: one card that shows the diff.
- **What the answer changes.** With B, the engine accepts on the admin's behalf and needs its own safety floor. With C, the two-person rule moves into the safety floors of document 21.

### 3.3 Question 7: Do AI approvals from client tools count?

- **Decisions it touches.** D-02 and D-10 (AI never approves, and the rule is a configuration record).
- **What is not asked.** Inside the product, "AI never approves" is decided and locked: there is no approve tool, and only an outcome from a person counts. This question is only about approvals that AI gives inside a client's own tools.
- **Background.** GitHub Copilot code review approvals are generally available and can count toward a repository's required approvals; that is off by default ([GitHub changelog, 2026-10-28](https://github.blog/changelog/2026-10-28-copilot-code-review-merge-with-confidence-with-max-and-approvals), document 20, source J1). A pipeline's record step can report such approvals, and the product stores them on the deployment record as evidence (Reported Approvals Json, document 22). The product must say what an AI-only code approval means for a gate.
- **Example.** A pull request is merged with only a Copilot approval and deployed to production. The change request in the product has its human authorization. Is the code review "approved"?
- **Options.**
  - **A. External AI approvals never count as authorization, and the shipped gate policy default is "flag" (recommended).** *Pros:* only people approve, in every tool the product reads, and the rule is simple for auditors. *Cons:* a client that wants AI code approvals to count must handle that in its own tool.
  - **B. They never count, but the gate policy default for AI-only code approvals ships at "ignore".** *Pros:* respects each client's toolchain. *Cons:* AI-only merges pass silently unless a client turns the flag on.
  - **C. A switch per client that lets them count.** *Pros:* the literal reading of "everything is configuration". *Cons:* the product could record an AI approval as a person's authorization, against D-02 and D-10.
- **Recommendation.** A. The setting `default-ai-only-code-approval-handling` ships at flag, and a gate policy row can set ignore, flag or stop. Whatever the handling, an AI-only code approval never counts as authorization.
- **What the answer changes.** The AI Only Code Approval Handling default in each preset and the setting's Is Locked value (documents 21 and 22).

### 3.4 Question 8: May the engine ever accept a decision without a person's card?

- **Decisions it touches.** D-02 (AI drafts and people decide; a person answers for every decision).
- **Background.** In the design, every decision is accepted on an approval card answered by a person. Standard changes already work without a card each time, because a person approved the standard change model once. Some decision types have no approval rule in the shipped presets, intake among them. Issue 1 in section 1 closes most of that gap: a decision type with no approval rule still goes on one card, to the holder of its accountable letter (C31). This question asks whether any exception remains.
- **Example.** A maker registers a tier 1 personal app. Who is the decider on record for the intake decision: the platform admin on a card, or the engine?
- **Options.**
  - **A. Only a locked list may be accepted by the engine (recommended).** The shipped list holds lowest-tier intake and change authorization for a standard change whose model is active. Every other decision goes on one card to its accountable holder. *Pros:* low friction where it is safe, and a person's card for every weighty decision. *Cons:* one more locked setting, and extending the list needs a product release.
  - **B. Never.** Every decision needs a person's card, standard changes included. *Pros:* the simplest rule. *Cons:* card fatigue, and it defeats the point of a standard change.
  - **C. Any decision type, by configuration.** The engine accepts when the rules ask for no approval, citing the configuration decision that set them. *Pros:* full flexibility. *Cons:* a client could configure away human decisions for go-live or retirement.
- **Recommendation.** A. The list is the locked setting `engine-acceptable-decision-types` (document 21). Minimum Count is never 0 (V28), so the list is the only way to accept with no card (document 25, OP-02 and G-17).
- **Two details that come with A.**
  - The shipped presets accept lowest-tier intake with no card, because intake has no approval rule. A client that wants a card there adds intake approval rules. That is a seed row change, not an engine change.
  - The bootstrap of configuration version 1 (document 21, section 4.8) is a second case with no card. No role is bound before version 1 exists, so the installing person accepts it directly, recorded as a configuration change decision. It is engine contract, not part of the list.
- **What the answer changes.** The locked setting and its allowed types; the meaning of Requires Approval and V28 in document 21; OP-02 in document 25; S1 and P01 in document 23.

### 3.5 Question 9: Which act counts as a person's for AI drafts that are not decisions?

- **Decisions it touches.** D-02 (AI drafts and people decide) and D-16 (people use MCP clients, the app and approval cards, with Claude Code as the reference client).
- **Background.** Decisions are settled: only approval cards accept them. For other AI drafts, such as submitting an intake or a change, registering an application, or marking a document current, no decision says what proves that a person acted. An MCP call cannot prove it, because the AI client holds the person's token. With MCP clients first (D-16), a rule that a person must press "submit" somewhere else needs another screen or a card.
- **Example.** An owner tells Claude Code: "Raise a normal change for the expense app, window Saturday 6 to 8, and send it for approval." Then: "Also register the code app I built." May Claude Code submit the change, and does the app enter the register on the owner's word?
- **Options.**
  - **A. A table per subject kind, held as configuration (recommended).** An AI client may submit for the person where Tool Policy allows it (shipped: yes for intake, change and decision submissions). Registering an application and marking a gate-relevant document current need a confirmation card. Plain facts (links, deployments) need nothing. *Pros:* one conversation from idea to approval cards, with proof where it matters. *Cons:* a rule set to explain, and a careless prompt could flood approvers (rate limits help).
  - **B. A person acts outside the AI client for every draft,** on a card or on the product's own page. *Pros:* the simplest rule and the strongest proof. *Cons:* card fatigue, and owners approve without reading.
  - **C. The person's word in the AI client is enough for everything except decisions.** *Pros:* the fastest. *Cons:* applications could enter the register, and documents be marked current, with no person in the loop.
  - **D. AI may submit only drafts it did not create.** *Pros:* splits drafting from sending. *Cons:* hard to explain and easy to work around.
- **Recommendation.** A, held in the setting `confirmation-channel` (document 21).
- **One more act of the same kind.** The code build found that marking a review done also meets a gate requirement (review done, OP-07). It is not a new question. The proposal is to enforce document 22's O21 rule for every writer: a review is done only with Done On, Reviewer and an accepted Outcome Decision. An AI client can then mark a review done only after people accepted its outcome decision, which leaves option A as it stands. With option B, a done review would need the person's own act, like every other draft.
- **What the answer changes.** The `confirmation-channel` setting and C22 Current Needs Person (documents 21 and 23), the Tool Policy AI May Submit defaults and the "Submitted" state (document 24), and claim confirmation in P06.

### 3.6 Question 12: May each pipeline's workload identity hold the deployment recorder role directly?

- **Decisions it touches.** D-11 (roles come only through Entra groups, never directly; on the Azure binding, D-26 makes these Entra app roles assigned to groups) and D-19 (the product records deployments from the client's pipelines and never builds pipelines).
- **Background.** The deployment record step (D-19) signs in with the pipeline's workload identity, not a person. A service principal added to a group does not receive the group's app roles in its token ([Microsoft Learn: Add app roles to your application](https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps), checked 2026-11-22). So on the Azure binding a pipeline cannot get the role through a group. On the later Dataverse binding, whether an application user can get a role through a group team is UNVERIFIED.
- **Example.** A client has 40 pipelines across Azure DevOps and GitHub, each with its own federated identity. Each must write deployment records and nothing else.
- **Options.**
  - **A. A stated exception for non-human workloads only, held as configuration rows (recommended).** Each workload identity gets the role directly (an app role assignment on Azure, a role on the application user on Dataverse), recorded as a Workload Grant with a reason, the deciding decision and an expiry, and listed by the direct assignment check. *Pros:* works on both platforms today, each pipeline is visible and revocable on its own, and it keeps least privilege. *Cons:* a stated exception to D-11, for non-people.
  - **B. Strict groups: the workload identity joins the recorder group.** *Pros:* one rule for everyone. *Cons:* does not work on the Azure binding today, and is UNVERIFIED on Dataverse.
  - **C. One shared relay identity.** *Pros:* one assignment. *Cons:* every pipeline shares one credential path, and records lose which pipeline wrote them.
  - **D. No workload role: people record deployments.** *Pros:* D-11 holds with no exception. *Cons:* weakens D-19's main route and the Flag level.
- **Recommendation.** A. The grant is C42 Workload Grant (document 26). Its Expires On is required and capped by the setting `workload-grant-max-days`, and CHK-09 (document 26, section 4.5) lists every grant, soonest expiry first. People still get roles only through groups, and agent identities and agent users never get a grant. A per-pipeline token issued by the product was also considered and rejected, because it is a shared secret.
- **What the answer changes.** The Workload Grant entity (C42), the direct assignment check, the record step setup guide, and one line in D-11's consequences.

## 4. Open from the build

The first code build in `src/` was checked against documents 20 to 26 in several review rounds. Its proposals sit at the end of each document as "Proposed by the build (2026-11-22)" items (B20-1 to B20-5, B21-1 to B21-8, B22-1 to B22-7, B23-1 to B23-5, B24-1 to B24-5, B25-1 to B25-7, B26-1 to B26-5). Each is a proposal and changes no decision. Three design questions from the build stay open, because the design does not say which way to go. Only item 1 touches an accepted decision, and it belongs to question 9 (section 3.5), so it is not a seventh question. Items 2 and 3 are design points, not product owner decisions.

| Item | Question | Recommended | Where it lands |
|---|---|---|---|
| 1 | May an AI client mark a review done? | Enforce document 22's O21 rule for every writer | OP-08 and OP-07 in document 25; question 9 |
| 2 | How does the guard read a final column that the facts leave out? | Require the column in the pre-image, null when empty | Document 25, section 5.10; document 22, B22-3 |
| 3 | Must every Estates Covered key be a value of the `estate` list? | Add `entra-owners` to the `estate` list of every preset | Document 21, section 12; document 22, B22-4 |

### 4.1 Item 1: may an AI client mark a review done?

- **Background.** A done review of the required type meets a gate requirement of kind review done (OP-07). G-08 names only accepted, registered, approved and completed, and the review status contract is due, done or cancelled. RA-03 names no review act. Document 26 (section 5.3) says "Reviewer named on the row; outcome through a decision". Document 22 (O21) makes Done On, Reviewer and Outcome Decision required when done, and its section 8.1 says such a link points to an accepted decision. OP-08 checks none of the three today, for a person either.
- **Example.** An owner tells Claude Code: "Mark the tier 3 security review done." Today the review becomes done with no reviewer, no date and no decision, and the go-live gate reads its review requirement as met.
- **Options.**
  - **A. An AI actor never sets a review done.** The reviewer marks it on the product's page or on a confirmation card, as question 9's working default does for a gate-relevant document. *Pros:* simple, and the same shape as the document rule. *Cons:* widens G-08's word list by judgment, and a person can still mark a review done with no decision.
  - **B. Enforce O21's rule for every writer.** Done needs Done On, Reviewer and an Outcome Decision whose meaning is accepted (one more fact the adapter fetches). *Pros:* closes the gap for people and AI clients alike; an AI client can mark a review done only after people accepted the outcome decision, which RA-03's "an AI client may submit for the person" allows. *Cons:* one more fact and one more rule.
  - **C. Both.**
- **Recommendation.** B, because the gap is not only an AI one. A can follow as an extension of question 9 if AI clients should stay out of reviews altogether.
- **What the answer changes.** OP-08 (a rule for O21 and its fact), the review-done input of OP-07, and the guard fixtures.
- **Decision it touches.** D-02 (AI never approves), through question 9: a done review opens a gate, which makes it the same kind of act as marking a gate-relevant document current. Section 3.5 ("One more act of the same kind") carries it.

### 4.2 Item 2: a final column the facts leave out

- **Background.** The guard reads a class F row's final columns from the pre-image it is given: O08 Tier Decision, O22 Finished On, and the O13 fill-once columns Digest, Source Commit and Source Repository Url. Today an absent column reads as empty. Decision and Approval Record ask for their meaning column instead (`input.required`). A Dataverse pre-image leaves out every null attribute, so absent and empty are the same thing there, and requiring the column would refuse every first update. But an adapter whose pre-image does not register Tier Decision would let a frozen assessment change.
- **Example.** A person updates a tier assessment's answers. The adapter's pre-image does not carry Tier Decision, so the guard reads it as empty and allows the change, though a tier decision exists.
- **Options.**
  - **A. Keep "absent reads as empty"** and make the binding parity suite (document 25, section 10) prove that each adapter's pre-image registers every final and fill-once column the guard reads. *Pros:* matches Dataverse images, with no runtime change. *Cons:* the guard trusts each adapter's registration.
  - **B. Require the column in the pre-image, null when empty,** for an update of O08, O22 and O13 (`input.required` otherwise), and have each adapter add explicit nulls for the columns the guard names (the runtime from the logical model, the Dataverse adapter from its image registration list). *Pros:* fails closed, like Decision and Approval Record. *Cons:* the runtime and every adapter must fill nulls; today's runtime passes only the columns a row holds, so B needs that change first.
- **Recommendation.** B, with the runtime change in the same slice.
- **What the answer changes.** Document 25, section 5.10 (the `before` map: every final and fill-once column, null when empty), the runtime's guarded write, and the class F fixtures. Document 22, B22-3 notes it.
- **Decision it touches.** None. D-10 (the engine enforces every write) holds either way; this is how the engine reads its input.

### 4.3 Item 3: must every Estates Covered key be a value of the `estate` list?

- **Background.** Document 22 (section 2.2, items 5 and 6) says the engine validates every key in Estates Covered and that a list value belongs to its list. The guard now checks the shape (a list of distinct keys) but not the list, because `run_owner_check` records the estate `entra-owners` (documents 23 and 24 call it "a new `estate` list value"), while the only value in the shipped `estate` list is `power-platform-inventory` (document 21, section 12). Checking the list today would refuse every owner check.
- **Example.** An admin runs the owner check. The O22 row names `entra-owners`, and with the list check on, every shipped preset refuses the write.
- **Options.**
  - **A. Add `entra-owners` to the `estate` list of every preset,** then check the list. A data change; the preset hashes change. *Pros:* one rule for every key list, and it matches documents 23 and 24 and question 18's "What the answer changes". *Cons:* the owner check is not an inventory estate, so the gap check must skip it when it reads "all covered estates".
  - **B. Give the owner check its own run kind or column** instead of an estate key, then check the list. *Pros:* keeps the list to inventory estates. *Cons:* a model and contract change (O22, tool 49), and documents 23 and 24 change.
- **Recommendation.** A, the smaller change and what documents 23 and 24 already describe, unless estates should stay limited to inventories.
- **What the answer changes.** The `estate` list in document 21, section 12 and in every preset, and the guard's Estates Covered check; with B also O22 and tool 49.
- **Decision it touches.** None. D-15 (no copy of Microsoft's data) holds either way, and question 18 is a working default, not one of the six open questions.
