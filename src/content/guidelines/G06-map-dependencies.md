# G6. Map what each app depends on, before you release or retire it

- **Number:** G6
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 7 (Architecture review and decisions)
- **Cited in chapters:** 1, 5, 7, 9, 14, 21
- **Processes:** P03, P04, P09

**Rule.** Record what each application depends on and what depends on it: other apps, APIs, data and outside systems. Check the map before every release and every retirement.

**Why it matters.** A change or a removal can break something that looks unrelated. Hidden dependencies cause failed releases and painful retirements.

**How the product is designed to support it.** It is designed to hold typed, directed dependencies and to answer "what breaks if this changes or retires" in both directions.

## Microsoft anchors

- [Recommendations for performing failure mode analysis](https://learn.microsoft.com/power-platform/well-architected/reliability/failure-mode-analysis) (Power Platform Well-Architected, RE:03), checked 2026-11-22: "Identify and document the dependencies in your workload and include them in your flow documentation artifacts."
- [Architecture strategies for safe deployment practices](https://learn.microsoft.com/azure/well-architected/operational-excellence/safe-deployments) (Azure Well-Architected), checked 2026-11-22: "Even something that appears idle or insignificant may still support a critical hidden dependency."

## Maps to

NIST CSF 2.0 ID.AM (Asset Management).

## Working shape

- **When:** at design review, before each release, and before a retirement.
- **Who:** the technical owner records dependencies; architects review them.
- **Steps:**
  1. List each dependency with its direction and kind: app, API, data or outside system.
  2. Before a release, find who depends on what is changing, and tell their owners.
  3. Before a retirement, confirm that nothing still depends on the app.
- **Done when:** the map is current at each release decision and each retirement decision.
- **Evidence:** the dependency list with its last review date, linked from the release or retirement decision.
