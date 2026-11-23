# D-30 Sixteen hours of independent work

- **Status:** Accepted
- **Date:** 2026-11-22
- **Decided by:** the product owner

## Context

The product owner was going to be away for about sixteen hours. The design was public, the code was early, and the next goal was working software.

## Options considered

- **Pause until the product owner returns.**
- **Let the AI session build and publish what it can,** within fixed limits. This was chosen.

## Decision

For about sixteen hours, the AI session builds the working software, keeps the private backup pushed, and pushes this repository after each green check (D-29). These limits stand:

- no sign-in as a person, so nothing is deployed to Azure;
- no GitHub release or version tag without the product owner;
- nothing private goes public (D-27).

## Why

The design and the checks were in place, so the work could continue safely without a person at every step.

## Consequences

- Work lands in small, checked commits. The build log records what was done.
- A release, a version tag and any Azure deployment wait for the product owner.

## Related

- [D-29 Who pushes to GitHub, and the license line](D-29-who-pushes-and-the-license-line.md)
- [Build log](../build-log/README.md)
