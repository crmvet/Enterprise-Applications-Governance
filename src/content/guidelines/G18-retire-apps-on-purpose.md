# G18. Retire apps on purpose

- **Number:** G18
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 14 (Retirement)
- **Cited in chapters:** 1, 6, 14
- **Processes:** P06, P09

**Rule.** Retire an app through a recorded decision. Then settle its data, remove its identities and connections, and prove nothing is left. Disable before you delete.

**Why it matters.** Abandoned apps keep data, permissions and costs alive. A deletion is hard to undo, and an app that looks idle may still hide a dependency.

**How the product is designed to support it.** It is designed to move the app through "retiring" and "retired" stages with a named decider, create a retirement checklist from configuration, and show what still depends on the app.

## Microsoft anchors

- [Modernize applications with Power Platform](https://learn.microsoft.com/power-platform/guidance/adoption/application-modernization), checked 2026-11-22: "first determine whether it's still needed and useful or should be retired". Retire is one of the modernization options.
- [Architecture strategies for safe deployment practices](https://learn.microsoft.com/azure/well-architected/operational-excellence/safe-deployments) (Azure Well-Architected, safe decommissioning), checked 2026-11-22: validate inactivity, preserve state before removal, disable before deleting, monitor through a watch window, and clean up residual references.

## Maps to

NIST CSF 2.0 ID.AM-08 (life cycle management of systems, software and services).

## Working shape

- **When:** when a review, a replacement or low use shows the app is no longer needed.
- **Who:** the business owner decides; the technical owner carries it out; the data owner settles the data; the change manager schedules it.
- **Steps:**
  1. Check the dependency map (G6), then record the retirement decision with its reason and date (G4).
  2. Archive the data the data owner wants kept, then disable the app and watch for activity.
  3. Remove identities, connections, sharing and secrets, then delete.
  4. Confirm in Microsoft's inventory that the app's resources are gone.
- **Done when:** the app shows as retired in the register, and none of its resources remain in the inventory.
- **Evidence:** the retirement decision, the completed checklist and the inventory check.
