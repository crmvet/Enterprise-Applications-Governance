# Content: the playbook and the guidelines

This folder holds the playbook and the guidelines as Markdown. The text is written so it can be published later. Nothing here names a person, an organization or an environment.

| Path | What |
|---|---|
| `playbook/README.md` | The chapter index, with the guidelines and processes each chapter uses |
| `playbook/00-...md` to `playbook/22-...md` | The 23 chapters: chapter 0 opens the playbook, chapters 1 to 22 follow |
| `playbook/processes.md` | The eleven processes, P01 to P11 |
| `playbook/img/` | The diagrams the chapters show, as PNG files |
| `playbook/visio/` | The Visio sources of those diagrams |
| `guidelines/README.md` | The guideline list, version 1.1: G1 to G24, with G10 and G11 retired |
| `guidelines/G01-...md` to `guidelines/G24-...md` | One file per guideline number, retired numbers included |

The diagrams use Microsoft's official product icons, unchanged. Those icons stay under Microsoft's terms, not under this repository's license.

## Rules for this text

- Every product statement says what the product **is designed to** do. Nothing is built yet.
- No claim about which data store the product uses. That choice is still open.
- Every Microsoft fact has a link and the date it was checked.
- Outside frameworks appear only as "maps to".
- Guideline numbers are permanent. A retired number keeps its file and is never reused.
- Every chapter lists the guidelines and processes it uses in its header, and the header matches the text.
- Every guideline has the working shape: when, who, steps, done when, evidence.
- No em dashes or en dashes. ASCII text only.

## The check

`node tools/content-check.mjs` (from `src/`) checks this folder. `node tools/ci.mjs` runs it as the `content` step. It fails when:

- a chapter, a process or a guideline cites a guideline number that does not exist, or one that is retired;
- a chapter cites a process that does not exist;
- a chapter's header lists differ from the guidelines and processes its text uses;
- a guideline's "Cited in chapters" or a process's "Used in chapters" differs from the chapter headers, or a guideline's processes differ from the processes' guidelines;
- the chapter index differs from the chapter headers;
- the guideline files differ from the shipped guideline rows (number, version, retired state, rule text);
- an active guideline lacks a working shape part, a Microsoft anchor lacks a link or a checked date, or its product line lacks "is designed to";
- a relative link points to a missing file or heading;
- an image has no alt text or is not a local image file, an image file is shown by no page, or a binary file is neither an image nor a Visio diagram source;
- the chapters are not numbered 0 to 22 with no gap;
- the text holds a non-ASCII character or an internal marker.
