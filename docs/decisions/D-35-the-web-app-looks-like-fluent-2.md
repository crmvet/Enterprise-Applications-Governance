# D-35 The web app looks like Fluent 2

- **Status:** Accepted. Amends D-32.
- **Date:** 2026-11-23
- **Decided by:** the product owner

## Context

D-32 moved the web app from Fluent UI to shadcn/ui and kept the Academy palette. The product owner asked whether the pages could still look close to Microsoft's own screens, since the people who use the product work in Microsoft 365 every day. With shadcn/ui, the look comes from theme tokens, so it can follow Fluent 2 without Fluent's library. The same pages were rendered in three looks and compared side by side.

## Options considered

- **A. The Academy look.** shadcn/ui shape with the Academy colors (blue, navy, gold).
- **B. Fluent 2 shape, Academy colors.** Fluent's corners, shadows and neutral grays, with the Academy brand colors.
- **C. Fluent 2 shape and colors.** Fluent's corners, shadows and neutral grays, Microsoft's brand blue and Fluent's status colors.

## Decision

**C.** The theme tokens follow Fluent 2: corners of 4 px on controls and 8 px on cards, Fluent's layered shadows and neutral grays, brand blue `0F6CBD`, Fluent's status colors, and orange `F7630C` for attention. Text stays navy `0A2A4A`, light only.

## Why

The product's users are owners, reviewers and admins on Microsoft estates. A look they already know makes the pages easy to read from the first visit, and it sits well next to the approval cards in Teams and Outlook.

## Consequences

- Only the theme tokens, the chart colors and a few fixed colors change. The pages, their text, their behavior, the accessibility check (all pages at 1280 px and 390 px) and the library (D-32) stay the same.
- The product keeps its own name and mark. It is not a Microsoft product and is not endorsed by Microsoft; the public documents say so.

## Related

- [D-18 Modern pages are the core interface](D-18-pages-are-the-core-interface.md)
- [D-32 The web app is built on shadcn/ui](D-32-the-web-app-uses-shadcn-ui.md)
- [Screens](../architecture/screens.md)
