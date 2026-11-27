# ui: the shadcn/ui components

These files are the shadcn/ui components, copied as source from the shadcn registry (style `new-york-v4`, shadcn
4.23.0, https://ui.shadcn.com) on 2026-11-23 (D-32). They are third-party code under the MIT License below, not code
of this product. `../../styles/shadcn-tailwind.css` is shadcn's Tailwind helper CSS from the same release, under the
same license.

Changes made here:

- imports point at `../../lib/utils` for `cn` (clsx and tailwind-merge) instead of the registry's alias;
- the `"use client"` lines are removed (no server rendering here);
- `badge.tsx` and `alert.tsx` carry the product's tone variants (success, warning, danger, brand, informative, subtle;
  info, warning, error, success);
- `tabs.tsx`, `button.tsx`, `dialog.tsx` and `sheet.tsx` use the Academy palette (light only): white fills on the gray
  page, navy text and overlays, AA contrast for unselected tabs;
- `textarea.tsx`, `label.tsx` and `checkbox.tsx` (added on 2026-11-24 for the action sheets' forms) use the same
  palette: white fills, navy label text, and no dark variants;
- `toast.tsx` is written for this product on the Radix Toast primitive in the shadcn style. sonner is not used, because
  it writes a style tag at run time.

To add a component later, take it from the same registry style and apply the same import change.

## License

MIT License

Copyright (c) 2023 shadcn

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
