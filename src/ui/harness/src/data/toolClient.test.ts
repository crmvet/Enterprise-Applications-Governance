// cachingClient (product review, fix round 1): the same read asked twice is one call; a write, a failed answer and time end it.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { Envelope, ToolName } from './contracts';
import { cachingClient, type ToolClient } from './toolClient';

function counting(answer: (tool: string) => Envelope<unknown>) {
  const calls: string[] = [];
  const inner: ToolClient = {
    call: ((tool: string) => {
      calls.push(tool);
      return Promise.resolve(answer(tool));
    }) as ToolClient['call'],
  };
  return { calls, inner };
}

const ok = { ok: true, data: { items: [] }, summary: '', links: {}, couldNotRead: {}, warnings: [], nextSteps: [], correlationId: 'c' } as unknown as Envelope<unknown>;
const refused = { ok: false, error: { code: 'rate_limited', message: 'Too many requests just now.' }, correlationId: 'c' } as unknown as Envelope<unknown>;
const isWrite = (tool: ToolName) => tool === 'save_intake_draft';

test('the same read with the same input is one call, while it is on its way and right after', async () => {
  const { calls, inner } = counting(() => ok);
  const client = cachingClient(inner, isWrite);
  await Promise.all([client.call('get_configuration', { entity: 'list-value', top: 100 }), client.call('get_configuration', { entity: 'list-value', top: 100 })]);
  await client.call('get_configuration', { entity: 'list-value', top: 100 });
  assert.deepEqual(calls, ['get_configuration']);
  await client.call('get_configuration', { entity: 'decision-outcome', top: 100 });
  assert.equal(calls.length, 2);
});

test('a write empties the cache, a failed answer is never kept, and an old answer is read again', async () => {
  let clock = 0;
  let answer = refused;
  const { calls, inner } = counting(() => answer);
  const client = cachingClient(inner, isWrite, { ttlMs: 1000, now: () => clock });
  await client.call('list_approvals', {});
  answer = ok;
  await client.call('list_approvals', {});
  assert.equal(calls.length, 2, 'the refused answer was not kept');
  await client.call('list_approvals', {});
  assert.equal(calls.length, 2);
  await client.call('save_intake_draft', { title: 'x' });
  await client.call('list_approvals', {});
  assert.equal(calls.length, 4, 'the write emptied the cache');
  clock = 5000;
  await client.call('list_approvals', {});
  assert.equal(calls.length, 5, 'an answer older than the time to keep it is read again');
});
