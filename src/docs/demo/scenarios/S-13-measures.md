# S-13 PMO: measures and slide-back signals

| | |
|---|---|
| Persona | Mateo Brandt, PMO |
| Use cases | A-MEASURES |
| Screens | Measures |
| Time | 15 seconds in the 15-minute run, 3 minutes in full |
| End-to-end spec | `src/tests/e2e/specs/S-13-measures.spec.ts` |
| Story section | W9 Run it and measure it |
| Ready check | `demo check --base <url> --scenario S-13` |

**Before it starts.** Act as Mateo Brandt ("Mateo Brandt, PMO"). The values move with what was played before, so the
script names the measures and signals, not their numbers.

**Reset.** Nothing to reset: the scenario only reads. "Record a decision" is not used here.

## Steps

### S-13.1 Nine measures, read in pairs

- **Persona:** Mateo Brandt, PMO.
- **Page and clicks:** More, Govern, Measures (`/app/measures`). Window 30 days.
- **Say:** "Is AI app development working? Read the measures in pairs: lead time with deployments on record, standard
  changes with failed changes, AI work accepted with decisions on time."
- **Proof on screen:** the header "9 of 9 measures have a target set in your configuration; 4 of 10 signal thresholds
  are set in your configuration, the rest are the playbook's defaults." The figures Signals firing, Measures improving,
  Measures slipping, Nothing to compare and "Targets met, Of 9 measures with a target". "The measures" with the count
  9, from "Lead time from idea to production" to "Slide-back signals".
- **Reset:** nothing.

### S-13.2 One measure, its target and its rows

- **Persona:** Mateo Brandt.
- **Page and clicks:** the target filter "Short of their target", then "Owner coverage" and its "Open the ... rows".
- **Say:** "Each measure has a baseline, a target from the configuration, and the rows behind its value."
- **Proof on screen:** "Owner coverage, Watched by the PMO, G2", "Short of its target", "Target: 95%, set in your
  configuration", and the rows of the registered applications with an owner role missing a holder or a deputy.
- **Reset:** nothing.

### S-13.3 The slide-back signals

- **Persona:** Mateo Brandt.
- **Page and clicks:** scroll to "Slide-back signals" (10).
- **Say:** "Ten signs that governance is slipping back to tickets and meetings, each with its threshold and who
  watches it. Some are read outside the product, and the page says so."
- **Proof on screen:** on a local run, Signal 6 "AI work outside the sanctioned clients ..." and Signal 8 "An approval
  with no named human approver, or an AI client asks to approve" read "Firing", each with "Open the ... rows" (on a
  demo install Signal 8 fires on the estate's AI answer alone). Signal 2 and Signal 7
  read "Read outside the product". Each signal offers "Record a decision".
- **Reset:** nothing.
