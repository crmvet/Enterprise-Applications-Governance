// The run's summary.json: every step played, by project, with its outcome, the checks that failed, the writes it
// skipped, the write tools and side sheets the page performed (sent for real and answered ok), the writes the server
// refused, the engine's answer of each side sheet and its screenshot. Several invocations into the same run folder (run.mjs plays one project at a time) add
// to the same file; a step played again in the same project replaces its earlier row.
import fs from 'node:fs';
import path from 'node:path';
import type { FullConfig, FullResult, Reporter, Suite, TestCase, TestResult } from '@playwright/test/reporter';
import { env } from './env';

interface Row {
  step: string;
  scenario: string;
  title: string;
  project: string;
  status: string;
  seconds: number;
  failures: string[];
  skipped: string[];
  performed: string[];
  // The side sheets the step performed, as tool@site, and the writes the server refused (wave 4b).
  sites: string[];
  refusedWrites: string[];
  answers: string[];
  notes: string[];
  screenshot: string | null;
}

// Strips the terminal colors Playwright puts in error messages.
const plain = (s: string) => s.replace(/\u001b\[[0-9;]*m/g, '');

export default class SummaryReporter implements Reporter {
  private rows: Row[] = [];

  onBegin(_config: FullConfig, _suite: Suite) {}

  onTestEnd(test: TestCase, result: TestResult) {
    const step = test.annotations.find(a => a.type === 'step')?.description ?? test.title.split(' ')[0];
    const project = test.parent.project()?.name ?? '';
    // Annotations added while a step runs: on the result in recent Playwright, on the test in older ones.
    const seen = new Set<string>();
    const notes = [...test.annotations, ...((result as unknown as { annotations?: typeof test.annotations }).annotations ?? [])].filter(a => {
      const k = `${a.type}|${a.description}`;
      if (seen.has(k)) return false;
      seen.add(k);
      return true;
    });
    const shot = notes.filter(a => a.type === 'screenshot').pop()?.description ?? null;
    this.rows.push({
      step,
      scenario: step.split('.')[0],
      title: test.title,
      project,
      status: result.status,
      seconds: Math.round(result.duration / 100) / 10,
      failures: result.errors.map(e => plain(e.message ?? String(e.value ?? '')).split('\n').slice(0, 6).join('\n')),
      skipped: notes.filter(a => a.type === 'skipped write' || a.type === 'skipped').map(a => a.description ?? ''),
      performed: notes.filter(a => a.type === 'performed').flatMap(a => (a.description ?? '').split(', ').filter(Boolean)),
      sites: notes.filter(a => a.type === 'performed site').flatMap(a => (a.description ?? '').split(', ').filter(Boolean)),
      refusedWrites: notes.filter(a => a.type === 'refused write').flatMap(a => (a.description ?? '').split(', ').filter(Boolean)),
      answers: notes.filter(a => a.type === 'answer').map(a => a.description ?? ''),
      notes: notes.filter(a => a.type === 'note').map(a => a.description ?? ''),
      screenshot: shot,
    });
  }

  onEnd(result: FullResult) {
    fs.mkdirSync(env.runDir, { recursive: true });
    const file = path.join(env.runDir, 'summary.json');
    let before: { steps?: Row[] } = {};
    try {
      before = JSON.parse(fs.readFileSync(file, 'utf8'));
    } catch {
      before = {};
    }
    const played = new Set(this.rows.map(r => `${r.project}|${r.step}`));
    const steps = [...(before.steps ?? []).filter(r => !played.has(`${r.project}|${r.step}`)), ...this.rows];
    const projects = [...new Set(steps.map(r => r.project))];
    const byProject = Object.fromEntries(
      projects.map(p => {
        const rows = steps.filter(r => r.project === p);
        return [p, { passed: rows.filter(r => r.status === 'passed').length, failed: rows.filter(r => r.status === 'failed' || r.status === 'timedOut').length, skipped: rows.filter(r => r.status === 'skipped').length }];
      }),
    );
    const summary = {
      run: env.runId,
      app: env.base.toString(),
      mode: env.mode,
      readOnly: env.readOnly,
      finishedOn: new Date().toISOString(),
      lastStatus: result.status,
      projects: byProject,
      passed: steps.filter(r => r.status === 'passed').length,
      failed: steps.filter(r => r.status === 'failed' || r.status === 'timedOut').length,
      skippedWrites: steps.reduce((n, r) => n + r.skipped.length, 0),
      failedSteps: steps.filter(r => r.status === 'failed' || r.status === 'timedOut').map(r => `${r.project} ${r.step}`),
      steps,
    };
    fs.writeFileSync(file, `${JSON.stringify(summary, null, 2)}\n`);
  }
}
