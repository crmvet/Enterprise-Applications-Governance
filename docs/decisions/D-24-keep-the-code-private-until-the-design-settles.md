# D-24 Keep the code private until the main design choices are settled

- **Status:** Superseded by D-27
- **Date:** 2026-11-22
- **Decided by:** the product owner

## Context

By this point the design pages for the next version were ready to read. The code existed too: the rules engine, the core of the MCP server, the configuration schema and presets, the tool contracts and the tests. It still carried placeholder names, and markers for questions that waited on the product owner.

## Options considered

- **Publish the code now,** with its placeholder names and open markers.
- **Keep the code private until the main design choices are settled,** then publish it with its final names. This was the recommended option.

## Decision

The code stays private until three things are settled: the data store (D-22), the product name (D-05) and the open design questions. It then moves to the public repository with its final names. No placeholder names or open markers go public.

## Why

Publishing once, with final names, avoids a public history of renames and placeholder markers.

## Consequences

- D-27 superseded this decision the same day. The technical work, the code included, is now public under the MIT License.
- Under D-27, the placeholder names (Product, and a table prefix parameter) stay in the public code until a name is picked. Markers for open questions read "open design question".

## Related

- [D-05](D-05-a-product-name-not-a-version-label.md)
- [D-22 Choose the foundation by what the product needs and what it costs to run](D-22-choose-the-foundation-by-need-and-running-cost.md)
- [D-25 The technical reference lives in this repository](D-25-technical-reference-lives-in-this-repository.md)
- [D-27 The technical work is public under the MIT License](D-27-technical-work-is-public-under-mit.md)
