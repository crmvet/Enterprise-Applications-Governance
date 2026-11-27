// The read-deduplicating client: one request per identical read, writes always sent, failures not kept.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { Envelope } from '../../data/contracts';
import type { ToolClient } from '../../data/toolClient';
import { dedupeReads } from './dedupe';

function fakeClient(answer: (tool: string) => Envelope<unknown>) {
  const calls: string[] = [];
  const client: ToolClient = {
    call: (tool, input) => {
      calls.push(`${tool} ${JSON.stringify(input)}`);
      return Promise.resolve(answer(tool) as never);
    },
  };
  return { client, calls };
}

const ok: Envelope<unknown> = { ok: true, data: {}, links: {}, warnings: [], couldNotRead: [], asOf: '2026-11-24T09:00:00Z', configurationSetVersion: 1, correlationId: 'c' } as never;
const failed: Envelope<unknown> = { ok: false, error: { code: 'rate_limited', message: 'Too many requests.' }, correlationId: 'c' };

test('an identical read is asked once; another input is asked again', async () => {
  const { client, calls } = fakeClient(() => ok);
  const d = dedupeReads(client);
  await Promise.all([d.call('get_configuration', { entity: 'owner-role' }), d.call('get_configuration', { entity: 'owner-role' })]);
  await d.call('get_configuration', { entity: 'owner-role' });
  await d.call('get_configuration', { entity: 'tier' });
  assert.equal(calls.length, 2);
});

test('a write is always sent', async () => {
  const { client, calls } = fakeClient(() => ok);
  const d = dedupeReads(client);
  const input = { documentLinkId: 'doc-1', stillTrue: true };
  await d.call('confirm_document_checked', input);
  await d.call('confirm_document_checked', input);
  assert.equal(calls.length, 2);
});

test('a failed read is not kept, so the next asking tries again', async () => {
  let n = 0;
  const { client, calls } = fakeClient(() => (n++ === 0 ? failed : ok));
  const d = dedupeReads(client);
  const first = await d.call('get_configuration', { entity: 'gate' });
  assert.equal(first.ok, false);
  const second = await d.call('get_configuration', { entity: 'gate' });
  assert.equal(second.ok, true);
  assert.equal(calls.length, 2);
});
