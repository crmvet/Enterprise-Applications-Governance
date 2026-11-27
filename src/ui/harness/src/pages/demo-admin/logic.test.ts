// "Ask again" and the Demo guide: which requests the page offers to ask again, and the commands it gives.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  askedAgainBy,
  askedWords,
  directorCommand,
  expiryWords,
  freshnessFilterOf,
  freshnessMatches,
  isLoopback,
  mayAskAgain,
  refreshCommand,
  renewState,
  storyPersonaName,
  type AskShape,
} from './logic';

const NOW = Date.parse('2026-11-25T12:00:00Z');
const at = (hours: number) => new Date(NOW + hours * 3600 * 1000).toISOString();

test('a request that expired, or has fewer than 48 hours left, may be asked again while the decision is proposed', () => {
  assert.equal(renewState({ outcome: { meaning: 'expired' }, expiresOn: at(-5) }, 'proposed', NOW), 'expired');
  assert.equal(renewState({ outcome: { meaning: 'pending' }, expiresOn: at(-1) }, 'proposed', NOW), 'expired');
  assert.equal(renewState({ outcome: { meaning: 'pending' }, expiresOn: at(47) }, 'proposed', NOW), 'expires-soon');
  assert.equal(renewState({ outcome: { meaning: 'pending' }, expiresOn: at(48) }, 'proposed', NOW), null);
  assert.equal(renewState({ outcome: { meaning: 'pending' }, expiresOn: null }, 'proposed', NOW), null);
});

test('an answered or canceled request, or a final decision, is never offered', () => {
  assert.equal(renewState({ outcome: { meaning: 'approved' }, expiresOn: at(-5) }, 'proposed', NOW), null);
  assert.equal(renewState({ outcome: { meaning: 'cancelled' }, expiresOn: at(-5) }, 'proposed', NOW), null);
  assert.equal(renewState({ outcome: { meaning: 'expired' }, expiresOn: at(-5) }, 'accepted', NOW), null);
  assert.equal(renewState({ outcome: { meaning: 'pending' }, expiresOn: at(1) }, 'withdrawn', NOW), null);
});

const role = (key: string) => ({ key, label: key });
const ask = (id: string, over: Partial<AskShape> = {}): AskShape => ({
  approvalRecordId: id,
  approvalRuleKey: 'change-auth-manager',
  isConsultation: false,
  askedRole: role('change-manager'),
  askedOwnerRole: null,
  outcome: { meaning: 'pending' },
  requestedOn: at(-120),
  expiresOn: at(24),
  ...over,
});

test('an expired request that was asked again names the newer open request of the same ask', () => {
  const old = ask('a', { outcome: { meaning: 'expired' }, expiresOn: at(-2) });
  const restart = ask('b', { requestedOn: at(-1), expiresOn: at(119) });
  assert.equal(askedAgainBy(old, [old, restart], NOW)?.approvalRecordId, 'b');
  // Another rule, another role, a consultation, an older request or one past its own time is not the same ask.
  assert.equal(askedAgainBy(old, [old, ask('c', { approvalRuleKey: 'other', requestedOn: at(-1) })], NOW), null);
  assert.equal(askedAgainBy(old, [old, ask('d', { askedRole: role('change-board'), requestedOn: at(-1) })], NOW), null);
  assert.equal(askedAgainBy(old, [old, ask('e', { isConsultation: true, requestedOn: at(-1) })], NOW), null);
  assert.equal(askedAgainBy(old, [old, ask('f', { requestedOn: at(-200) })], NOW), null);
  assert.equal(askedAgainBy(old, [old, ask('g', { requestedOn: at(-1), expiresOn: at(-1) })], NOW), null);
  assert.equal(askedAgainBy(old, [old, ask('h', { requestedOn: at(-1), outcome: { meaning: 'approved' } })], NOW), null);
});

test('the page offers "Ask again" to the proposer and a platform admin, as a person, when the Tool Policy allows it', () => {
  const session = (over: { isPerson?: boolean; objectId?: string | null; tools?: string[]; roles?: string[] } = {}) => ({
    isPerson: over.isPerson ?? true,
    objectId: over.objectId === undefined ? 'aaaa' : over.objectId,
    can: (t: string) => (over.tools ?? ['renew_approval_request']).includes(t),
    hasRole: (...keys: string[]) => keys.some(k => (over.roles ?? []).includes(k)),
  });
  assert.equal(mayAskAgain(session(), { objectId: 'AAAA' }), true);
  assert.equal(mayAskAgain(session(), { objectId: 'bbbb' }), false);
  assert.equal(mayAskAgain(session({ roles: ['platform-admin'] }), { objectId: 'bbbb' }), true);
  assert.equal(mayAskAgain(session({ roles: ['platform-admin'] }), null), true);
  assert.equal(mayAskAgain(session({ isPerson: false }), { objectId: 'aaaa' }), false);
  assert.equal(mayAskAgain(session({ tools: [] }), { objectId: 'aaaa' }), false);
  assert.equal(mayAskAgain(session({ objectId: null }), { objectId: 'aaaa' }), false);
});

test('expiry and who is asked read in words', () => {
  assert.equal(expiryWords(-1, 'expired'), 'Expired 1 hour ago');
  assert.equal(expiryWords(0, 'expired'), 'Expired within the last hour');
  assert.equal(expiryWords(-30, 'expired'), 'Expired 30 hours ago');
  assert.equal(expiryWords(-73, 'expired'), 'Expired 3 days ago');
  assert.equal(expiryWords(0, 'expires-soon'), 'Expires within the hour');
  assert.equal(expiryWords(20, 'expires-soon'), 'Expires in 20 hours');
  assert.equal(askedWords({ askedPerson: { objectId: 'x', displayLabel: 'Kai Ostrowski' }, askedRole: null, askedOwnerRole: null, isConsultation: false }), 'Kai Ostrowski');
  assert.equal(askedWords({ askedPerson: null, askedRole: { key: 'change-manager', label: 'Change manager' }, askedOwnerRole: null, isConsultation: false }), 'the change manager');
  assert.equal(askedWords({ askedPerson: null, askedRole: { key: 'ai-lead', label: 'AI lead' }, askedOwnerRole: null, isConsultation: true }), 'the AI lead, for advice');
  assert.equal(askedWords({ askedPerson: null, askedRole: null, askedOwnerRole: { key: 'business-owner', label: 'Business owner' }, isConsultation: false }), 'the business owner');
});

test('the demo commands: the development sign-in on a local run, the operator token on a demo install', () => {
  assert.equal(isLoopback('http://127.0.0.1:5080'), true);
  assert.equal(isLoopback('http://localhost:5081/'), true);
  assert.equal(isLoopback('https://demo.example.org'), false);
  assert.equal(refreshCommand('http://127.0.0.1:5080/'), 'node src/demo/kit/demo.mjs refresh --base http://127.0.0.1:5080');
  assert.equal(refreshCommand('https://demo.example.org'), "PRODUCT_DEMO_TOKEN=<the operator's web app token> node src/demo/kit/demo.mjs refresh --base https://demo.example.org");
  assert.equal(directorCommand('http://127.0.0.1:5080'), 'node seed/demo-director.mjs --base http://127.0.0.1:5080');
  assert.equal(directorCommand('https://demo.example.org'), "PRODUCT_DEMO_TOKEN=<the operator's web app token> node seed/demo-director.mjs --mode demo --base https://demo.example.org");
});

test('a story persona is played by the install persona of the same name, or the local run\'s demo- or -web persona', () => {
  const demo = [{ name: 'kai' }, { name: 'priya' }];
  const local = [{ name: 'priya-web' }, { name: 'demo-kai' }, { name: 'demo-mateo' }, { name: 'mateo-web' }];
  assert.equal(storyPersonaName('kai', demo), 'kai');
  assert.equal(storyPersonaName('kai', local), 'demo-kai');
  assert.equal(storyPersonaName('priya', local), 'priya-web');
  assert.equal(storyPersonaName('mateo', local), 'demo-mateo');
  assert.equal(storyPersonaName('noor', local), null);
});

test('the freshness filter keeps the expired or the soon requests', () => {
  assert.equal(freshnessFilterOf('expired'), 'expired');
  assert.equal(freshnessFilterOf('soon'), 'soon');
  assert.equal(freshnessFilterOf('later'), undefined);
  assert.equal(freshnessMatches('expired', 'expired'), true);
  assert.equal(freshnessMatches('expires-soon', 'expired'), false);
  assert.equal(freshnessMatches('expires-soon', 'soon'), true);
  assert.equal(freshnessMatches('expired', undefined), true);
});
