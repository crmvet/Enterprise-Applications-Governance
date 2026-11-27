// Wave 4 on the Govern pages: the outbox card's action, the measure targets and signal thresholds, the decision rights
// of owner roles and the Roles search. Fictional names and patterned placeholder IDs only.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { RoleAccess } from '../../data/contracts';
import { filterByText } from '../../data/textSearch';
import {
  cardActionOf,
  ownerRoleDuties,
  ownerRolesOf,
  roleSearchFields,
  slideBackKeyOf,
  subjectKindsText,
  targetCounts,
  targetFilterOf,
  targetsAndThresholdsText,
  targetState,
  thresholdSourceText,
  type ConfigRowLike,
  type OpenRequestFacts,
} from './logic';

const NOW = Date.parse('2026-11-25T12:00:00Z');
const at = (hours: number) => new Date(NOW + hours * 3600 * 1000).toISOString();
const ID = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

test('a card that needs a look is sent again while its request is open, and closed once it settled', () => {
  const open = new Map<string, OpenRequestFacts>([
    [ID(1), { expiresOn: at(24), decisionMeaning: 'proposed' }],
    [ID(2), { expiresOn: at(-1), decisionMeaning: 'proposed' }],
    [ID(3), { expiresOn: at(24), decisionMeaning: 'accepted' }],
  ]);
  assert.equal(cardActionOf({ approvalRecordId: ID(1).toUpperCase(), state: 'failed' }, open, true, NOW), 'send-again');
  assert.equal(cardActionOf({ approvalRecordId: ID(1), state: 'sent' }, open, true, NOW), 'send-again');
  assert.equal(cardActionOf({ approvalRecordId: ID(2), state: 'failed' }, open, true, NOW), 'close');
  assert.equal(cardActionOf({ approvalRecordId: ID(3), state: 'sent' }, open, true, NOW), 'close');
  // Not among the open requests: settled when the list is complete; unknown (the engine decides) when it is not.
  assert.equal(cardActionOf({ approvalRecordId: ID(4), state: 'failed' }, open, true, NOW), 'close');
  assert.equal(cardActionOf({ approvalRecordId: ID(4), state: 'failed' }, open, false, NOW), 'send-again');
  assert.equal(cardActionOf({ approvalRecordId: ID(4), state: 'failed' }, null, false, NOW), 'send-again');
  // Other states offer nothing.
  for (const state of ['pending', 'claimed', 'answered', 'expired', 'cancelled']) {
    assert.equal(cardActionOf({ approvalRecordId: ID(1), state }, open, true, NOW), null);
  }
});

test('a measure meets its target, falls short of it, has none, or has no value to compare', () => {
  assert.equal(targetState({ target: null, targetMet: null }), 'unset');
  assert.equal(targetState({ target: 5, targetMet: true }), 'met');
  assert.equal(targetState({ target: 5, targetMet: false }), 'missed');
  assert.equal(targetState({ target: 5, targetMet: null }), 'no-value');
  assert.equal(targetState({ target: 5 }), 'no-value');
  assert.deepEqual(targetCounts([{ target: null }, { target: 1, targetMet: true }, { target: 2, targetMet: false }, { target: 3, targetMet: true }]), { met: 2, missed: 1, unset: 1, 'no-value': 0 });
  assert.equal(targetFilterOf('met'), 'met');
  assert.equal(targetFilterOf('better'), undefined);
});

test('where the targets and thresholds come from reads in one sentence', () => {
  const m = (n: number, configured: number) => Array.from({ length: n }, (_, i) => ({ targetSource: (i < configured ? 'configuration' : 'not-set') as 'configuration' | 'not-set' }));
  const s = (n: number, configured: number) => Array.from({ length: n }, (_, i) => ({ thresholdSource: (i < configured ? 'configuration' : 'product-default') as 'configuration' | 'product-default' }));
  assert.equal(targetsAndThresholdsText(m(9, 0), s(10, 0)), "No measure has a target yet: measure first, then agree one; every signal threshold is the playbook's default.");
  assert.equal(targetsAndThresholdsText(m(9, 3), s(10, 10)), '3 of 9 measures have a target set in your configuration; every signal threshold is set in your configuration.');
  assert.equal(targetsAndThresholdsText(m(9, 9), s(10, 4)), "9 of 9 measures have a target set in your configuration; 4 of 10 signal thresholds are set in your configuration, the rest are the playbook's defaults.");
  assert.equal(thresholdSourceText('configuration'), 'set in your configuration');
  assert.equal(thresholdSourceText('product-default'), "the playbook's default");
  assert.equal(slideBackKeyOf('access-drift'), 'access-drift');
  assert.equal(slideBackKeyOf('not-a-signal'), null);
});

const row = (key: string, label: string, fields: Record<string, unknown>, isActive = true): ConfigRowLike => ({ key, label, isActive, fields });

const OWNER_ROLES = [
  row('business-owner', 'Business owner', {}),
  row('data-owner', 'Data owner', { subjectKinds: ['business-application', 'data-domain'] }),
  row('data-steward', 'Data steward', { subjectKinds: ['data-domain'] }),
  row('technical-owner', 'Technical owner', { subjectKinds: ['business-application', 'data-location'] }),
  row('old-owner', 'Old owner', {}, false),
];
const RIGHTS = [
  row('priority-a', 'Priority: accountable', { decisionTypeKey: 'priority', letter: 'accountable', ownerRoleKey: 'sponsor', fallbackOwnerRoleKey: 'business-owner' }),
  row('go-live-a', 'Go-live: accountable', { decisionTypeKey: 'go-live', letter: 'accountable', ownerRoleKey: 'business-owner' }),
  row('data-use-a', 'Data use: accountable', { decisionTypeKey: 'data-use', letter: 'accountable', ownerRoleKey: 'data-owner' }),
  row('intake-a', 'Intake: accountable', { decisionTypeKey: 'intake', letter: 'accountable', roleKey: 'platform-admin' }),
  row('retired-a', 'Retired', { decisionTypeKey: 'go-live', letter: 'consulted', ownerRoleKey: 'business-owner' }, false),
];
const RULES = [
  row('go-live-owner', 'Business owner approves go-live', { decisionTypeKey: 'go-live', approverOwnerRoleKey: 'business-owner' }),
  row('data-use-steward', 'Data steward consulted', { decisionTypeKey: 'data-use', approverOwnerRoleKey: 'data-steward', isConsultation: true }),
];

test('each owner role lists the active decision rights and approval rules that name it, fallbacks marked', () => {
  const duties = ownerRoleDuties(OWNER_ROLES, RIGHTS, RULES);
  assert.deepEqual(duties.map(d => d.ownerRole.key), ['business-owner', 'data-owner', 'data-steward', 'technical-owner']);
  const business = duties[0];
  assert.deepEqual(business.ownerRole.subjectKinds, ['business-application']);
  assert.deepEqual(business.rights.map(r => [r.key, r.fallback]), [['priority-a', true], ['go-live-a', false]]);
  assert.deepEqual(business.approvals.map(a => a.key), ['go-live-owner']);
  assert.equal(duties[2].approvals[0].isConsultation, true);
  assert.equal(duties[3].rights.length + duties[3].approvals.length, 0);
});

test('the application owner and data owner product roles decide through the owner roles held on their records', () => {
  const duties = ownerRoleDuties(OWNER_ROLES, RIGHTS, RULES);
  assert.deepEqual(ownerRolesOf('application-owner', duties).map(d => d.ownerRole.key), ['business-owner', 'data-owner']);
  assert.deepEqual(ownerRolesOf('data-owner', duties).map(d => d.ownerRole.key), ['data-owner', 'data-steward']);
  assert.deepEqual(ownerRolesOf('auditor', duties), []);
  assert.equal(subjectKindsText(['business-application', 'data-domain']), 'applications and data domains');
  assert.equal(subjectKindsText(['data-location']), 'data locations');
});

test('the Roles search finds a role by its name, group, party or the decisions it makes', () => {
  type RoleItem = RoleAccess['roles'][number];
  const role = (key: string, label: string, over: Partial<RoleItem> = {}): RoleItem => ({
    role: { key, label },
    party: null,
    duties: null,
    isWorkloadRole: false,
    isReadOnly: false,
    canChangeConfiguration: false,
    heldByMe: false,
    bindings: [],
    holders: null,
    workloadGrants: [],
    tools: [],
    decisionRights: [],
    approvals: [],
    separationRules: [],
    ...over,
  });
  const roles = [
    role('change-board', 'Change board', { bindings: [{ group: { objectId: ID(111), displayLabel: 'Change Advisory Board' }, bindingMethod: 'entra-app-role-assignment', lastVerifiedOn: null }] }),
    role('architect', 'Architect', { decisionRights: [{ decisionRight: { key: 'design-a', label: 'Design' }, decisionType: { key: 'design', label: 'Design' }, letter: 'accountable', scope: 'All' }] }),
    role('pmo', 'PMO', { party: { key: 'business', label: 'Business' }, duties: 'Scores demand.' }),
  ];
  const keys = (q: string) => filterByText(roles, q, roleSearchFields).map(r => r.role.key);
  assert.deepEqual(keys('advisory'), ['change-board']);
  assert.deepEqual(keys('design'), ['architect']);
  assert.deepEqual(keys('demand business'), ['pmo']);
  assert.deepEqual(keys(''), ['change-board', 'architect', 'pmo']);
});
