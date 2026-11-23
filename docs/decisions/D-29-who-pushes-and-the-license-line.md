# D-29 Who pushes to GitHub, and the license line

- **Status:** Accepted
- **Date:** 2026-11-22
- **Decided by:** the product owner

## Context

The product is built with AI sessions (Claude Code). Until this decision the rule was that AI sessions prepare and check commits, and only the product owner pushes to GitHub. Once the technical work was public (D-27), every push waited on the product owner, even when every check was green. The license file still read "Copyright (c) 2021 crmvet", although new code had been added in 2026.

## Options considered

- **The AI session pushes both repositories, after the final checks pass.** This was the recommended option.
- **The AI session pushes the private backup, and the product owner pushes the public repository** after a short review.
- **The product owner pushes both,** as before.

For the license line:

- **Keep the holder "crmvet" and add a year range.**
- Name a legal person instead.
- Name the company that published the 2021 Marketplace listing instead.

## Decision

- **The AI session working in the product's workspace pushes both repositories,** the private backup and this public repository. A public push happens only after the final checks pass: the scan for private material on the files and on the commit range, the build and tests, the links and the license. There is never a force push, and pushed history is never rewritten.
- **The license line reads "Copyright (c) 2021-2026 crmvet".**

## Why

Pushing after automated checks pass keeps the public history moving without waiting on a person for each commit. The product owner keeps control through the checks, the decision records and the private backup. The year range covers the work added since 2021, under the holder that has been public since then.

## Consequences

- Every public commit is checked before it is pushed, and the public CI runs again on GitHub.
- Commit messages carry no AI attribution lines. The decision records and the build log say how the work is made.

## Related

- [D-27 The technical work is public under the MIT License](D-27-technical-work-is-public-under-mit.md)
- [D-30 Sixteen hours of independent work](D-30-sixteen-hours-of-independent-work.md)
- [Build log](../build-log/README.md)
- [License](../../LICENSE)
