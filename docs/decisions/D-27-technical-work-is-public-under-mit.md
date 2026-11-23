# D-27 The technical work is public under the MIT License

- **Status:** Accepted. Supersedes D-24.
- **Date:** 2026-11-22
- **Decided by:** the product owner

## Context

D-24 kept the code private until the main design choices were settled. The product is meant to be open source, and the 2021 release has been public under the MIT License since 2021. The question came back the same day. What should be public now, and what should stay private?

## Options considered

- **Keep D-24.** The code stays private until the data store, the name and the open design questions are settled.
- **Open everything,** working notes included.
- **Open the technical work now,** and keep internal notes, goals and plans private. This was the recommended option.

## Decision

The third option. Everything technical is public under the MIT License:

- the source code in `src/`, after a public pass;
- technical design documents written for readers;
- one public decision record per decision, in `docs/decisions/`;
- the diagrams, with their Visio source;
- the playbook and the guidelines.

Internal notes, goals and plans stay private, along with anything that is not technical.

The public pass on the code:

- markers for questions that wait on the product owner become "open design question";
- private paths and internal notes come out of comments;
- code, comments and test data are checked for private material before they move;
- the placeholder names (Product, and a table prefix parameter) stay until a name is picked.

The diagrams carry a notice. Microsoft's icons stay under Microsoft's terms, not the MIT License.

## Why

The product is open source by intent. Publishing the technical work now establishes its authorship with dated public commits. It also lets readers check the design against the code.

## Consequences

- D-24 is superseded. D-25 and D-26 stand.
- The code sits in this repository next to its design. The product is still in design, so every capability is something it is designed to do.
- The working documents stay private. Public versions are written from them, for readers.

## Related

- [D-23 The 2021 release and its name are the author's own work](D-23-2021-release-and-name-are-the-authors-own.md)
- [D-24 Keep the code private until the main design choices are settled](D-24-keep-the-code-private-until-the-design-settles.md)
- [D-25 The technical reference lives in this repository](D-25-technical-reference-lives-in-this-repository.md)
- [D-26 The first release runs in the organization's own Azure subscription](D-26-first-release-runs-in-the-clients-azure.md)
- [The source](../../src/)
- [The playbook and the guidelines](../../src/content/README.md)
- [License](../../LICENSE)
