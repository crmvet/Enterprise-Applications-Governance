// S-13 PMO: measures and slide-back signals (src/docs/demo/scenarios/S-13-measures.md). Reads only; the values move
// with what was played before, so the checks name the measures and signals, not their numbers.
import { scenario } from '../lib/demo';

// Signal n's own block of the list: its words up to the next signal, then the state it reads.
const signal = (n: number, state: string) => new RegExp(`Signal ${n}:((?!Signal \\d+:).){0,900}${state}`);

scenario('S-13', 'PMO: measures and slide-back signals', step => {
  step('S-13.1 Nine measures, read in pairs', async ({ demo }) => {
    await demo.open('measures', 'mateo');
    await demo.see("9 of 9 measures have a target set in your configuration; 4 of 10 signal thresholds are set in your configuration, the rest are the playbook's defaults.");
    for (const figure of ['Signals firing', 'Measures improving', 'Measures slipping', 'Nothing to compare', 'Targets met']) await demo.see(figure, { what: `the figure "${figure}"` });
    await demo.see('Of 9 measures with a target');
    await demo.count('The measures', 9);
    await demo.see('Lead time from idea to production');
    await demo.see('Slide-back signals');
  });

  step('S-13.2 One measure, its target and its rows', async ({ demo }) => {
    await demo.open('measures', 'mateo');
    await demo.click({ text: 'Short of their target', prefix: true, scope: 'main' });
    await demo.see('Owner coverage');
    await demo.see(/Owner coverage.{0,200}Watched by the PMO/, { what: 'Owner coverage is watched by the PMO' });
    await demo.see(/Owner coverage.{0,300}G2/, { what: 'Owner coverage carries G2' });
    await demo.see('Short of its target');
    await demo.see('Target: 95%, set in your configuration');
    await demo.click({ text: 'Open the', prefix: true, near: 'Owner coverage', scope: 'main' });
    await demo.see(/Close the rows|Hide the rows|rows? behind/i, { what: 'the rows behind Owner coverage open' });
  });

  step('S-13.3 The slide-back signals', async ({ demo }) => {
    await demo.open('measures', 'mateo');
    await demo.count('Slide-back signals', 10);
    if (demo.seeded('Signal 6 fires on Claude Code\'s seeded work')) await demo.see(signal(6, 'Firing'), { what: 'Signal 6, AI work outside the sanctioned clients, is firing' });
    await demo.see(signal(8, 'Firing'), { what: 'Signal 8, an approval with no named human approver, is firing' });
    await demo.see(signal(2, 'Read outside the product'), { what: 'Signal 2 is read outside the product' });
    await demo.see(signal(7, 'Read outside the product'), { what: 'Signal 7 is read outside the product' });
    await demo.see('Record a decision');
  });
});
