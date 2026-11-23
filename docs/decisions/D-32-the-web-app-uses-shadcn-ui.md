# D-32 The web app is built on shadcn/ui

- **Status:** Accepted. Amends D-26. Amended by D-35.
- **Date:** 2026-11-23
- **Decided by:** the product owner

## Context

D-26 described the first release's web app as React and Fluent UI, Microsoft's design system. The four pages (Overview, Application 360, the dependency map and the timeline) were built that way. The UI library came in with the platform choice; it had not been chosen on its own. The question was whether a better base existed for a modern, presentable interface that people and AI clients can extend.

## Options considered

- **Keep Fluent UI v9.** It looks native in Microsoft 365 and is well tested for accessibility. Its look reads as an admin screen, and it writes its styles at run time.
- **shadcn/ui.** Accessible Radix primitives styled with Tailwind CSS. The components are copied into the repository as MIT source, so the product owns them. It is the stack most AI app builders generate by default.
- **MUI or Mantine.** Mature libraries, but neither fits better than shadcn/ui here, and both are large dependencies.

## Decision

The web app is built on **shadcn/ui**: React 19, Radix primitives, Tailwind CSS 4 and lucide icons. The Academy palette stays as the theme, light only, with navy text.

## Why

- A modern, presentable look, as D-18 asked.
- It fits the keynote (D-28): the product governs AI development, and its own interface uses the stack AI clients build with most.
- The components are source in the repository, not a large library to upgrade.
- The style sheet is static: Tailwind builds it at build time.

## Consequences

- Fluent UI and its icons leave the package list. The shadcn/ui components and their MIT license text live in `src/ui/harness/src/components/ui/`.
- The four pages keep their designs, their tests, the accessibility check and the screenshots at 1280 px and 390 px.
- The native Microsoft 365 look is lost. That would matter only inside Teams or a model-driven app, and neither is planned. Approval cards are Adaptive Cards and do not use the web app's library.
- The page's Content-Security-Policy still allows inline styles: the Radix primitives write a style tag when a dialog or popover locks page scrolling. A per-request nonce would remove that, as a later step.

## Amendment, 2026-11-23

[D-35](D-35-the-web-app-looks-like-fluent-2.md) changes the theme from the Academy palette to Fluent 2's shape and colors (Microsoft's corners, shadows, neutral grays, brand blue and status colors), with navy text. The library stays shadcn/ui.

## Related

- [D-18 Modern pages are the core interface](D-18-pages-are-the-core-interface.md)
- [D-26 The first release runs in the organization's own Azure subscription](D-26-first-release-runs-in-the-clients-azure.md)
- [D-28 Govern AI development of the apps](D-28-govern-ai-development-of-the-apps.md)
- [Screens](../architecture/screens.md)
- [The pages' source](../../src/ui/)
