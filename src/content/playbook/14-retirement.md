# Chapter 14. Retirement

- **Version:** 0.2 draft, 2026-11-22
- **Track:** Organization
- **Parties:** the business owner, IT, change control, the data owner, the PMO
- **Guidelines:** [G4](../guidelines/G04-record-each-decision.md), [G6](../guidelines/G06-map-dependencies.md), [G18](../guidelines/G18-retire-apps-on-purpose.md)
- **Processes:** [P03](processes.md#p03-change-management), [P06](processes.md#p06-gap-check-and-claim), [P09](processes.md#p09-retirement)

## Why this chapter exists

"'Active' tells me nothing, and nobody ever decides to retire anything." Abandoned apps keep data, permissions and costs alive. Blocking, quarantining and deleting are actions in Microsoft's tools; none of them is a decision. Retirement needs a decision, an impact check, settled data, a scheduled removal and proof that nothing is left.

## What good looks like

- Every retirement starts with a recorded decision by the business owner, with a reason and a date.
- Before anything is removed, the dependency map shows nothing still depends on the app, or each dependant has a plan.
- The data owner decides what is kept, where and for how long.
- The removal is a scheduled change. Disable comes before delete, with a watch window between.
- A gap check confirms that none of the app's resources remain.
- The app shows as retired in the register, and its history still reads.

## How it works

1. **Decide.** The portfolio review, an owner's request, a replacement going live, or a part that vanished can start it. The PMO prepares the facts; the business owner decides on an approval card.
2. **Check impact.** Walk the dependency map in both directions. Tell the owners of dependants. A dependant that needs a replacement gets its own change or intake.
3. **Settle the data.** The data owner decides retention, and links where the data went.
4. **Schedule the removal.** A change carries the window and the targets. Its authorization follows the tier (chapter 9).
5. **Disable, watch, then delete.** People and their AI tools disable first and watch for activity, then remove identities, connections, sharing and secrets, then delete. This happens in Microsoft's admin centers, Entra and Agent 365. For agents, disable execution and revoke permissions. Link each step done as evidence.
6. **Confirm nothing is left.** An admin runs the gap check. Each of the app's parts should now show as vanished, and is resolved as retired. A part still seen keeps the retirement open.
7. **Complete.** A retirement decision marks it complete. Due reviews and document reminders stop. Dependencies stay, marked by the retired stage, so history still reads.

## Who decides (default decision rights)

| Decision or task | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Decide the future: keep, invest, replace, retire | Business owner | PMO | Data owner, platform admin, architect | Prepare the facts |
| Approve the removal change | Per the change's tier (chapter 9) | | | |
| Retire and clean up, with evidence | Business owner | Platform admin | Data owner, security reviewer, architect | List what is left and link evidence |
| Declare retirement complete | Business owner | Platform admin | | Nothing |

## Guidelines this chapter applies

- **G18 Retire apps on purpose.** Decision first, data settled, disable before delete, prove nothing is left.
- **G6 Map what each app depends on.** Confirm nothing still depends on the app before it goes.
- **G4 Record each decision in one line.** The portfolio, data and retirement decisions are entries in the decision log.

## Processes this chapter uses

- **P09 Retirement.** The whole path from decision to completion.
- **P03 Change management.** Schedules and authorizes the removal.
- **P06 Gap check and claim.** Confirms the app's resources are gone, and resolves vanished parts as retired.

## How the product is designed to support it

- It is designed to move the app through retiring and retired stages with a named decider and date.
- It is designed to show what still depends on the app, in both directions.
- It is designed to keep each removal step as an evidence link, and to keep the retirement open while a part is still seen by the gap check.
- It is designed to stop reviews and reminders for a retired app, and to keep its dependencies and decisions readable.
- It is designed never to delete, block or quarantine anything itself. The removal happens in Microsoft's tools.

## What you can tailor

- What must be true before a retirement can complete, for example "data use decided" and "gap check shows no part".
- The portfolio and retirement outcomes.
- Whether a retired app's registration is archived.

## Microsoft guidance it applies

- [Modernize applications with Power Platform](https://learn.microsoft.com/power-platform/guidance/adoption/application-modernization), checked 2026-11-22: "first determine whether it's still needed and useful or should be retired".
- [Architecture strategies for safe deployment practices](https://learn.microsoft.com/azure/well-architected/operational-excellence/safe-deployments) (Azure Well-Architected, safe decommissioning), checked 2026-11-22: validate inactivity, preserve state before removal, disable before deleting, monitor through a watch window, and clean up residual references.
- [Power Platform inventory](https://learn.microsoft.com/power-platform/admin/power-platform-inventory), checked 2026-11-22: the inventory confirms which resources still exist.

## How AI changes this

- AI makes apps cheap to start and easy to abandon. Retirement has to keep pace, or the register fills with apps nobody uses or owns.
- The AI client lists what depends on the app and what is left of it: identities, connections, sharing and secrets. As people remove each part, it links the evidence.
- The business owner still decides, on a card. The admin's gap check proves that nothing remains.

## Old paradigm to avoid

[3. The yearly inventory spreadsheet](17-old-paradigms-and-how-not-to-slide-back.md#3-the-yearly-inventory-spreadsheet). Here it shows up as abandoned apps that stay for a year, because nobody looks again until the next export.

**Slide-back signal:** unused apps with no portfolio decision.

## Done when

- The app shows as retired in the register, with a retirement decision.
- The data owner's retention decision and its evidence are linked.
- None of the app's resources remain in the inventory.
