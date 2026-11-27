// Writes the data taxonomy's fixtures (data taxonomy slice 2; docs/architecture/data-taxonomy.md):
// OP-02 scope fields, OP-07 requirement kinds, OP-08 G-18 to G-25, OP-09 V49 to V60, OP-15, OP-16 and OP-17.
// Run from src/: node fixtures/data-taxonomy.gen.mjs. The files are checked in; the generator keeps them in one place.
// Fictional data only: fixture principals, fixture applications, example.org hosts.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const at = '2026-11-24T09:00:00Z';
const written = [];

function write(op, name, body) {
  const folder = path.join(here, op.toLowerCase());
  fs.mkdirSync(folder, { recursive: true });
  const fixture = { schemaVersion: '1.0', operation: op, case: name, ...body };
  fs.writeFileSync(path.join(folder, name + '.json'), JSON.stringify(fixture, null, 2) + '\n');
  written.push(op + '/' + name);
}

const person = (id, roles) => ({ principalId: id, principalKind: 'user', actorKind: 'person', roleKeys: roles });
const ai = (id, roles) => ({ principalId: id, principalKind: 'user', actorKind: 'ai-client-for-person', onBehalfOfPrincipalId: id, roleKeys: roles });
const ctx = (actor) => (actor ? { asOfUtc: at, actor } : { asOfUtc: at });
const upsert = (entity, key, values) => ({ op: 'upsert', entity, key, values });

// The worked tree, on Balanced: People / Payroll (AI use never) / Bank details; Field operations /
// Safety findings (restricted); Commercial / Contracts / Supplier contracts; Customers / Client accounts; Warehouse.
const tree = [
  upsert('data-domain', 'people', { label: 'People', levelKey: 'domain', classificationKey: 'confidential', retentionClassKey: 'keep-7-years' }),
  upsert('data-domain', 'payroll', { label: 'Payroll', parentKey: 'people', levelKey: 'subdomain', classificationKey: 'restricted', aiUseRule: 'never' }),
  upsert('data-domain', 'bank-details', { label: 'Bank details', parentKey: 'payroll', levelKey: 'data-set' }),
  upsert('data-domain', 'timesheets', { label: 'Timesheets', parentKey: 'people', levelKey: 'subdomain' }),
  upsert('data-domain', 'field-operations', { label: 'Field operations', levelKey: 'domain', classificationKey: 'internal' }),
  upsert('data-domain', 'safety-findings', { label: 'Safety findings', parentKey: 'field-operations', levelKey: 'subdomain', classificationKey: 'restricted' }),
  upsert('data-domain', 'parts-catalog', { label: 'Parts catalog', parentKey: 'field-operations', levelKey: 'subdomain' }),
  upsert('data-domain', 'commercial', { label: 'Commercial', levelKey: 'domain', classificationKey: 'confidential' }),
  upsert('data-domain', 'contracts', { label: 'Contracts', parentKey: 'commercial', levelKey: 'subdomain' }),
  upsert('data-domain', 'supplier-contracts', { label: 'Supplier contracts', parentKey: 'contracts', levelKey: 'data-set' }),
  upsert('data-domain', 'customers', { label: 'Customers', levelKey: 'domain', classificationKey: 'confidential' }),
  upsert('data-domain', 'client-accounts', { label: 'Client accounts', parentKey: 'customers', levelKey: 'subdomain' }),
];
const worked = (more = []) => ({ presetKey: 'balanced', operations: [...tree, ...more] });
const overlay = (more) => ({ presetKey: 'balanced', overlays: ['data-domains-starter'], ...(more ? { operations: more } : {}) });

// Owner facts (O06 with a domain, location or application subject) and the directory.
const own = (id, role, principal, subject, extra = {}) => ({ assignmentId: id, ownerRoleKey: role, principalId: principal, startOn: '2026-01-05', ...subject, ...extra });
const domainOwners = [
  own('fixture-owner-hana', 'data-owner', 'fixture-person-hana', { dataDomainKey: 'people' }),
  own('fixture-owner-rosa', 'data-owner', 'fixture-person-rosa', { dataDomainKey: 'people' }, { isDeputy: true, deputyForId: 'fixture-owner-hana' }),
  own('fixture-owner-greta', 'data-owner', 'fixture-person-greta', { dataDomainKey: 'payroll' }),
  own('fixture-owner-ines', 'data-owner', 'fixture-person-ines', { dataDomainKey: 'field-operations' }),
  own('fixture-owner-ravi', 'data-owner', 'fixture-person-ravi', { dataDomainKey: 'field-operations' }, { isDeputy: true, deputyForId: 'fixture-owner-ines' }),
  own('fixture-owner-kai', 'data-owner', 'fixture-person-kai', { dataDomainKey: 'safety-findings' }),
  own('fixture-owner-kai-deputy', 'data-owner', 'fixture-person-ravi', { dataDomainKey: 'safety-findings' }, { isDeputy: true, deputyForId: 'fixture-owner-kai' }),
  own('fixture-owner-clara', 'data-owner', 'fixture-person-clara', { dataDomainKey: 'contracts' }),
  own('fixture-owner-greta-2', 'data-owner', 'fixture-person-greta', { dataDomainKey: 'contracts' }, { isDeputy: true, deputyForId: 'fixture-owner-clara' }),
  own('fixture-owner-nadia', 'data-owner', 'fixture-person-nadia', { dataDomainKey: 'customers' }),
  own('fixture-owner-wren', 'data-owner', 'fixture-person-wren', { dataDomainKey: 'customers' }, { isDeputy: true, deputyForId: 'fixture-owner-nadia' }),
];
const directory = [
  { principalId: 'fixture-person-hana', directoryState: 'enabled' },
  { principalId: 'fixture-person-kai', directoryState: 'enabled' },
];
const ownerFacts = { ownerAssignments: domainOwners, principals: directory };

// ---------------------------------------------------------------- OP-15 ----
write('OP-15', 'own-owner-resolved', {
  description: 'Safety findings names its own data owner and deputy, so OP-15 resolves Kai at distance 0, with the effective classification and rules and where each comes from.',
  snapshot: worked(), context: ctx(), input: { domainKey: 'safety-findings' }, facts: ownerFacts,
  expected: {
    outcome: 'resolved',
    payload: {
      domainKey: 'safety-findings', pathText: 'Field operations / Safety findings', levelKey: 'subdomain', declarable: true,
      classification: { key: 'restricted', rank: 4, setOn: 'safety-findings' },
      rules: { ai: { name: 'ai', value: 'decision', fromClassification: 'restricted' }, copy: { name: 'copy', value: 'never', fromClassification: 'restricted' } },
      gaps: [],
    },
    reasons: [], reasonsMatch: 'exact',
  },
});
write('OP-15', 'inherited-owner-resolved', {
  description: 'Timesheets names no owner of its own, so it resolves to Hana, inherited from People one level up, with Rosa as deputy (docs/architecture/data-taxonomy.md, Who answers, the example).',
  snapshot: worked(), context: ctx(), input: { domainKey: 'timesheets' }, facts: ownerFacts,
  expected: {
    outcome: 'resolved',
    payload: { classification: { key: 'confidential', setOn: 'people' }, retention: { retentionClassKey: 'keep-7-years', setOn: 'people' }, gaps: [] },
    reasons: [], reasonsMatch: 'exact',
  },
});
write('OP-15', 'never-rule-inherited', {
  description: 'Bank details inherits AI use never from Payroll, its classification restricted, and Greta as data owner from Payroll (the worked rules, never). Greta has no deputy (DC-03).',
  snapshot: worked(), context: ctx(), input: { domainKey: 'bank-details' }, facts: ownerFacts,
  expected: {
    outcome: 'resolved',
    payload: { rules: { ai: { name: 'ai', value: 'never', setOn: 'payroll' } }, gaps: ['data.no-deputy'] },
    reasons: [{ code: 'data.no-deputy', severity: 'warning', ref: 'payroll' }], reasonsMatch: 'exact',
  },
});
write('OP-15', 'no-owner-dc01', {
  description: 'Commercial has no data owner on it or above it, so OP-15 reports DC-01.',
  snapshot: worked(), context: ctx(), input: { domainKey: 'commercial' }, facts: ownerFacts,
  expected: { outcome: 'resolved', payload: { gaps: ['data.no-owner'] }, reasons: [{ code: 'data.no-owner', severity: 'warning', ref: 'commercial' }], reasonsMatch: 'exact' },
});
write('OP-15', 'own-owner-missing-dc02', {
  description: 'When the subdomain level needs its own owner, Timesheets with only People\'s owner shows DC-02. The decision still routes to the inherited owner.',
  snapshot: worked([upsert('data-domain-level', 'subdomain', { ownerRequired: true })]), context: ctx(), input: { domainKey: 'timesheets' }, facts: ownerFacts,
  expected: { outcome: 'resolved', payload: { ownerRequired: true, gaps: ['data.own-owner-missing'] }, reasons: [{ code: 'data.own-owner-missing', severity: 'warning', ref: 'timesheets' }], reasonsMatch: 'exact' },
});
write('OP-15', 'owner-disabled-deputy-acts-dc04', {
  description: 'Hana is disabled in the directory, so Rosa, her deputy, acts for People (P08, step 2), and OP-15 reports DC-04.',
  snapshot: worked(), context: ctx(), input: { domainKey: 'people' },
  facts: { ownerAssignments: domainOwners, principals: [{ principalId: 'fixture-person-hana', directoryState: 'disabled' }] },
  expected: { outcome: 'resolved', payload: { gaps: ['data.owner-inactive'] }, reasons: [{ code: 'data.owner-inactive', severity: 'warning', ref: 'people' }], reasonsMatch: 'exact' },
});
write('OP-15', 'ended-assignment-not-counted', {
  description: 'An owner assignment that ended before today no longer holds the role, so Commercial\'s former owner leaves DC-01.',
  snapshot: worked(), context: ctx(), input: { domainKey: 'commercial' },
  facts: { ownerAssignments: [own('fixture-owner-old', 'data-owner', 'fixture-person-old', { dataDomainKey: 'commercial' }, { endOn: '2026-06-30' })], principals: [] },
  expected: { outcome: 'resolved', payload: { gaps: ['data.no-owner'] }, reasons: [{ code: 'data.no-owner', severity: 'warning' }], reasonsMatch: 'exact' },
});
write('OP-15', 'unknown-domain-invalid-input', {
  description: 'A domain key the configuration does not hold is invalid input.',
  snapshot: worked(), context: ctx(), input: { domainKey: 'no-such-domain' },
  expected: { outcome: 'invalid-input', reasons: [{ code: 'input.unknown-key', severity: 'error' }], reasonsMatch: 'exact' },
});
write('OP-15', 'owner-fact-two-subjects-invalid-input', {
  description: 'An owner fact that names two subjects is invalid input: an owner assignment names exactly one (G-19).',
  snapshot: worked(), context: ctx(), input: { domainKey: 'people' },
  facts: { ownerAssignments: [own('fixture-owner-x', 'data-owner', 'fixture-person-x', { dataDomainKey: 'people', applicationId: 'fixture-app-1' })] },
  expected: { outcome: 'invalid-input', reasons: [{ code: 'input.malformed', severity: 'error' }], reasonsMatch: 'exact' },
});

// ---------------------------------------------------------------- OP-16 ----
const app = { applicationId: 'fixture-app-1', tierKey: 'tier-2', applicationKindKey: 'agent', lifecycleStageKey: 'build', portfolioPath: [] };
write('OP-16', 'restricted-ai-zero-of-two-proposed', {
  description: 'Restricted data to AI: Visit Report Agent reads Safety findings with AI. The restricted AI group wins, so Kai and the security reviewer are asked: 0 of 2 approvals on the proposed decision.',
  tags: ['worked-rule'],
  snapshot: worked(), context: ctx(),
  input: { subjectKind: 'data-use', domainKey: 'safety-findings', reachesAi: true, ...app },
  facts: { ...ownerFacts, decisions: [{ decisionId: 'fixture-decision-dc1', decisionTypeKey: 'data-use', statusMeaning: 'proposed', outcomeKey: 'allow', approvalsGiven: 0 }] },
  expected: {
    outcome: 'proposed',
    payload: {
      state: 'proposed', decisionNeeded: true, approvalsNeeded: 2, approvalsGiven: 0, decisionId: 'fixture-decision-dc1',
      governingRule: { name: 'ai', value: 'decision', fromClassification: 'restricted' },
      approvers: [
        { requirementKey: 'data-use-restricted-ai-owner', ownerRoleKey: 'data-owner', ownerSubject: 'data-domain', principalId: 'fixture-person-kai', isConsultation: false, noOneToAsk: false },
        { requirementKey: 'data-use-restricted-ai-security', roleKey: 'security-reviewer', isConsultation: false, noOneToAsk: false },
      ],
      impliedAnswer: { questionKey: 'sensitivity', answerKey: 'restricted' },
    },
    reasons: [{ code: 'data.no-one-to-ask', severity: 'info', ref: 'data-use-restricted-ai-steward' }], reasonsMatch: 'exact',
    trace: [{ entity: 'Data Classification', key: 'restricted', role: 'won' }],
  },
});
write('OP-16', 'never-breaks-rule', {
  description: 'The never rule: Expense Assistant reads Bank details with AI. Payroll sets AI use never and Bank details inherits it, so the state is breaks rule and no approver is resolved (DC-07).',
  tags: ['worked-rule'],
  snapshot: worked(), context: ctx(), input: { subjectKind: 'data-use', domainKey: 'bank-details', reachesAi: true, ...app }, facts: ownerFacts,
  expected: {
    outcome: 'breaks-rule',
    payload: { state: 'breaks-rule', decisionNeeded: false, approvers: [], governingRule: { name: 'ai', value: 'never', setOn: 'payroll' } },
    reasons: [{ code: 'data.breaks-rule', severity: 'warning', ref: 'bank-details' }], reasonsMatch: 'exact',
    trace: [{ entity: 'Data Domain', key: 'payroll', role: 'won' }],
  },
});
write('OP-16', 'masked-copy-needs-decision', {
  description: 'A masked copy: Finance Test holds a masked copy of Supplier contracts. The copy rule of confidential data is masked only, so the copy needs a decision, and the card goes to Clara (DC-08).',
  tags: ['worked-rule'],
  snapshot: worked(), context: ctx(), input: { subjectKind: 'data-mapping', domainKey: 'supplier-contracts', origin: 'masked-copy', locationId: 'fixture-location-finance-test', locationStageKey: 'test' }, facts: ownerFacts,
  expected: {
    outcome: 'needed',
    payload: { state: 'needed', governingRule: { name: 'copy', value: 'masked-only' }, approvers: [{ requirementKey: 'data-copy', principalId: 'fixture-person-clara', inheritedFrom: 'contracts' }], approvalsNeeded: 1 },
    reasons: [{ code: 'data.copy-outside-production', severity: 'warning' }], reasonsMatch: 'exact',
  },
});
write('OP-16', 'unmasked-copy-breaks-rule', {
  description: 'An unmasked copy: Customer Test holds an unmasked production copy of Client accounts. The copy rule is masked only, so the copy breaks the rule (DC-08).',
  tags: ['worked-rule'],
  snapshot: worked(), context: ctx(), input: { subjectKind: 'data-mapping', domainKey: 'client-accounts', origin: 'production-copy', locationId: 'fixture-location-customer-test', locationStageKey: 'test' }, facts: ownerFacts,
  expected: { outcome: 'breaks-rule', payload: { state: 'breaks-rule' }, reasons: [{ code: 'data.copy-outside-production', severity: 'warning' }], reasonsMatch: 'exact' },
});
write('OP-16', 'system-of-record-not-governed', {
  description: 'A system of record in production is no copy, so no rule governs the mapping and no decision is needed.',
  snapshot: worked(), context: ctx(), input: { subjectKind: 'data-mapping', domainKey: 'client-accounts', origin: 'system-of-record', locationId: 'fixture-location-customer-prod', locationStageKey: 'production' }, facts: ownerFacts,
  expected: { outcome: 'not-needed', payload: { state: 'not-needed', decisionNeeded: false }, reasons: [], reasonsMatch: 'exact' },
});
write('OP-16', 'internal-read-not-needed', {
  description: 'One example: an app that only reads the internal parts catalog needs no decision, because Balanced allows internal data to be read without asking.',
  snapshot: worked(), context: ctx(), input: { subjectKind: 'data-use', domainKey: 'parts-catalog', reachesAi: false, ...app }, facts: ownerFacts,
  expected: { outcome: 'not-needed', payload: { state: 'not-needed', governingRule: { name: 'use', value: 'allowed', fromClassification: 'internal' } }, reasons: [], reasonsMatch: 'exact' },
});
write('OP-16', 'internal-to-ai-needed', {
  description: 'Internal data that reaches AI needs a decision (D-41, question 6, option A); Ines owns Field operations and answers for Parts catalog below it (DC-05).',
  snapshot: worked(), context: ctx(), input: { subjectKind: 'data-use', domainKey: 'parts-catalog', reachesAi: true, ...app }, facts: ownerFacts,
  expected: { outcome: 'needed', payload: { state: 'needed', approvers: [{ requirementKey: 'data-use-domain', principalId: 'fixture-person-ines', inheritedFrom: 'field-operations' }] }, reasons: [{ code: 'data.decision-missing', severity: 'warning' }], reasonsMatch: 'includes' },
});
write('OP-16', 'accepted-allow-allowed', {
  description: 'An accepted allow, decided under the rules still in force, leaves the use allowed.',
  snapshot: worked(), context: ctx(), input: { subjectKind: 'data-use', domainKey: 'safety-findings', reachesAi: true, ...app },
  facts: { ...ownerFacts, decisions: [{ decisionId: 'fixture-decision-dc2', decisionTypeKey: 'data-use', statusMeaning: 'accepted', outcomeKey: 'allow', decidedOn: '2026-10-01T10:00:00Z', judgedClassificationRank: 4, judgedRule: 'decision' }] },
  expected: { outcome: 'allowed', payload: { state: 'allowed', decisionId: 'fixture-decision-dc2' }, reasons: [{ code: 'data.no-one-to-ask', severity: 'info' }], reasonsMatch: 'exact' },
});
write('OP-16', 'classification-raised-re-decide', {
  description: 'The decision allowed the use when the data was classified internal (rank 2). It is restricted now (rank 4), so the use must be decided again.',
  snapshot: worked(), context: ctx(), input: { subjectKind: 'data-use', domainKey: 'safety-findings', reachesAi: true, ...app },
  facts: { ...ownerFacts, decisions: [{ decisionId: 'fixture-decision-dc3', decisionTypeKey: 'data-use', statusMeaning: 'accepted', outcomeKey: 'allow', decidedOn: '2026-10-01T10:00:00Z', judgedClassificationRank: 2, judgedRule: 'decision' }] },
  expected: { outcome: 're-decide', payload: { state: 're-decide' }, reasons: [{ code: 'data.decision-missing', severity: 'warning' }], reasonsMatch: 'includes' },
});
write('OP-16', 'accepted-deny-denied', {
  description: 'An accepted deny leaves the use denied.',
  snapshot: worked(), context: ctx(), input: { subjectKind: 'data-use', domainKey: 'safety-findings', reachesAi: false, ...app },
  facts: { ...ownerFacts, decisions: [{ decisionId: 'fixture-decision-dc4', decisionTypeKey: 'data-use', statusMeaning: 'accepted', outcomeKey: 'deny', decidedOn: '2026-10-01T10:00:00Z' }] },
  expected: { outcome: 'denied', payload: { state: 'denied' }, reasons: [], reasonsMatch: 'includes' },
});
write('OP-16', 'no-owner-no-one-to-ask', {
  description: 'Contracts data would route to its owner, but in this case nobody owns Commercial or Contracts, so the counting data owner resolves to nobody (DC-06).',
  snapshot: worked(), context: ctx(), input: { subjectKind: 'data-use', domainKey: 'contracts', reachesAi: false, ...app },
  facts: { ownerAssignments: [], principals: [] },
  expected: { outcome: 'no-one-to-ask', payload: { state: 'no-one-to-ask', approvers: [{ requirementKey: 'data-use-domain', noOneToAsk: true }] }, reasons: [{ code: 'data.no-one-to-ask', severity: 'warning', ref: 'contracts' }], reasonsMatch: 'includes' },
});
write('OP-16', 'sensitivity-below-data-warning', {
  description: 'The application answered internal to the sensitivity question, and Safety findings implies restricted, so OP-16 warns with DC-09.',
  snapshot: worked(), context: ctx(), input: { subjectKind: 'data-use', domainKey: 'safety-findings', reachesAi: false, ...app },
  facts: { ...ownerFacts, sensitivityAnswerKey: 'internal' },
  expected: { outcome: 'needed', reasons: [{ code: 'data.sensitivity-below-data', severity: 'warning' }], reasonsMatch: 'includes' },
});
write('OP-16', 'mapping-missing-info', {
  description: 'The use names a location where its domain has no mapping, so OP-16 adds DC-11 as information.',
  snapshot: worked(), context: ctx(), input: { subjectKind: 'data-use', domainKey: 'parts-catalog', reachesAi: false, locationId: 'fixture-location-1', ...app },
  facts: { ...ownerFacts, mappedAtLocation: false },
  expected: { outcome: 'not-needed', reasons: [{ code: 'data.mapping-missing', severity: 'info' }], reasonsMatch: 'exact' },
});
write('OP-16', 'mapping-without-origin-invalid-input', {
  description: 'A mapping needs its data origin.',
  snapshot: worked(), context: ctx(), input: { subjectKind: 'data-mapping', domainKey: 'client-accounts' },
  expected: { outcome: 'invalid-input', reasons: [{ code: 'input.required', severity: 'error' }], reasonsMatch: 'exact' },
});

// ---------------------------------------------------------------- OP-02 ----
write('OP-02', 'data-use-restricted-ai-two-approvals', {
  description: 'A data-use decision about a use of restricted data that reaches AI matches the restricted AI group (specificity 3): the domain\'s data owner and the security reviewer, with the steward consulted (docs/architecture/data-taxonomy.md, Approval rules).',
  snapshot: { presetKey: 'balanced' }, context: ctx(),
  input: { decisionTypeKey: 'data-use', tierKey: 'tier-2', subjectKind: 'data-use', classificationKey: 'restricted', aiUse: true, portfolioPath: [] },
  expected: {
    outcome: 'required',
    payload: {
      requirements: [
        { ruleKey: 'data-use-restricted-ai-owner', approverOwnerRoleKey: 'data-owner', ownerSubject: 'data-domain' },
        { ruleKey: 'data-use-restricted-ai-security', approverRoleKey: 'security-reviewer' },
      ],
      consultations: [{ ruleKey: 'data-use-restricted-ai-steward', approverOwnerRoleKey: 'data-steward', ownerSubject: 'data-domain', isConsultation: true }],
    },
    trace: [{ entity: 'Approval Rule', key: 'data-use-domain', role: 'dropped' }],
  },
});
write('OP-02', 'data-use-domain-owner', {
  description: 'A data-use decision about a use of confidential data without AI matches the data use group: the domain\'s data owner, resolved on the data domain.',
  snapshot: { presetKey: 'balanced' }, context: ctx(),
  input: { decisionTypeKey: 'data-use', tierKey: 'tier-3', subjectKind: 'data-use', classificationKey: 'confidential', aiUse: false, portfolioPath: [] },
  expected: { outcome: 'required', payload: { requirements: [{ ruleKey: 'data-use-domain', approverOwnerRoleKey: 'data-owner', ownerSubject: 'data-domain' }] } },
});
write('OP-02', 'data-use-application-tier-3-unchanged', {
  description: 'A data-use decision about a whole application at tier 3 keeps the tier 3 group, now scoped to the application subject: a request that names no subject kind is about an application.',
  snapshot: { presetKey: 'balanced' }, context: ctx(), input: { decisionTypeKey: 'data-use', tierKey: 'tier-3', portfolioPath: [] },
  expected: { outcome: 'required', payload: { requirements: [{ ruleKey: 'data-use-tier-3-data-owner' }, { ruleKey: 'data-use-tier-3-security' }] } },
});
write('OP-02', 'ownership-top-domain-governance-lead', {
  description: 'An ownership decision about a top-level domain goes to the data governance lead, and each proposed owner accepts (D-41, question 4).',
  snapshot: { presetKey: 'balanced' }, context: ctx(), input: { decisionTypeKey: 'ownership', subjectKind: 'data-domain', dataDomainLevelKey: 'domain', portfolioPath: [] },
  expected: { outcome: 'required', payload: { requirements: [{ ruleKey: 'ownership-top-domain-lead', approverRoleKey: 'data-governance-lead' }, { ruleKey: 'ownership-top-domain-proposed-owners', approverProposedOwners: true }] } },
});
write('OP-02', 'ownership-subdomain-parent-owner', {
  description: 'An ownership decision about a lower domain goes to the owner of its parent domain (docs/architecture/data-taxonomy.md, Appointing owners).',
  snapshot: { presetKey: 'balanced' }, context: ctx(), input: { decisionTypeKey: 'ownership', subjectKind: 'data-domain', dataDomainLevelKey: 'subdomain', portfolioPath: [] },
  expected: { outcome: 'required', payload: { requirements: [{ ruleKey: 'ownership-domain-parent-owner', approverOwnerRoleKey: 'data-owner', ownerSubject: 'parent-data-domain' }, { ruleKey: 'ownership-domain-proposed-owners' }] } },
});
write('OP-02', 'ownership-top-domain-platform-admin-starter', {
  description: 'Starter: the platform admin appoints the owners of top-level domains (D-41, question 4).',
  snapshot: { presetKey: 'starter' }, context: ctx(), input: { decisionTypeKey: 'ownership', subjectKind: 'data-domain', dataDomainLevelKey: 'domain', portfolioPath: [] },
  expected: { outcome: 'required', payload: { requirements: [{ ruleKey: 'ownership-top-domain-lead', approverRoleKey: 'platform-admin' }, { ruleKey: 'ownership-top-domain-proposed-owners' }] } },
});
write('OP-02', 'configuration-change-touches-consults-owners', {
  description: 'A configuration draft that touches the data taxonomy keeps the configuration admin, and consults the data owners of the touched domains (docs/architecture/data-taxonomy.md, Changing the taxonomy).',
  snapshot: { presetKey: 'balanced' }, context: ctx(), input: { decisionTypeKey: 'configuration-change', dataTaxonomyChange: 'touches', portfolioPath: [] },
  expected: { outcome: 'required', payload: { requirements: [{ ruleKey: 'config-data-touches-admin', approverRoleKey: 'configuration-admin' }], consultations: [{ ruleKey: 'config-data-touches-owners', ownerSubject: 'touched-data-domains' }] } },
});
write('OP-02', 'configuration-change-loosens-owners-approve', {
  description: 'A draft that loosens a data rule needs the touched domains\' data owners to approve, not only to be consulted (docs/architecture/data-taxonomy.md, Changing the taxonomy).',
  snapshot: { presetKey: 'balanced' }, context: ctx(), input: { decisionTypeKey: 'configuration-change', dataTaxonomyChange: 'loosens', portfolioPath: [] },
  expected: { outcome: 'required', payload: { requirements: [{ ruleKey: 'config-data-loosens-admin' }, { ruleKey: 'config-data-loosens-owners', approverOwnerRoleKey: 'data-owner', ownerSubject: 'touched-data-domains' }], consultations: [] } },
});
write('OP-02', 'regulated-confidential-ai-security', {
  description: 'Regulated: AI use of confidential data also asks the security reviewer.',
  snapshot: { presetKey: 'regulated' }, context: ctx(),
  input: { decisionTypeKey: 'data-use', tierKey: 'tier-2', subjectKind: 'data-use', classificationKey: 'confidential', aiUse: true, portfolioPath: [] },
  expected: { outcome: 'required', payload: { requirements: [{ ruleKey: 'data-use-confidential-ai-owner' }, { ruleKey: 'data-use-confidential-ai-security', approverRoleKey: 'security-reviewer' }] } },
});
write('OP-02', 'retention-owner-and-business-owner-consulted', {
  description: 'A data-retention decision goes to the domain\'s data owner, with the application\'s business owner consulted (docs/architecture/data-taxonomy.md, Approval rules).',
  snapshot: { presetKey: 'balanced' }, context: ctx(), input: { decisionTypeKey: 'data-retention', tierKey: 'tier-2', subjectKind: 'data-use', portfolioPath: [] },
  expected: { outcome: 'required', payload: { requirements: [{ ruleKey: 'data-retention-owner', ownerSubject: 'data-domain' }], consultations: [{ ruleKey: 'data-retention-business-owner', approverOwnerRoleKey: 'business-owner' }] } },
});
write('OP-02', 'unknown-classification-invalid-input', {
  description: 'A classification key the configuration does not hold is invalid input.',
  snapshot: { presetKey: 'balanced' }, context: ctx(), input: { decisionTypeKey: 'data-use', subjectKind: 'data-use', classificationKey: 'top-secret', aiUse: true },
  expected: { outcome: 'invalid-input', reasons: [{ code: 'input.unknown-key', severity: 'error' }], reasonsMatch: 'exact' },
});

// ---------------------------------------------------------------- OP-07 ----
const design = (tier) => ({ gateKey: 'design', tierKey: tier, applicationKindKey: 'power-platform-app' });
const adr = { complete: true, items: [{ requirementKey: 'adr-tier-3', documentTypeKey: 'architecture-decision-record', status: 'current', documentLinkId: 'fixture-link-a1' }], missingCount: 0, draftCount: 0, staleCount: 0 };
write('OP-07', 'data-uses-undeclared-not-ready', {
  description: 'A tier 2 application at the design gate with no data use, no Holds No Business Data and no data owner is not ready (docs/architecture/data-taxonomy.md, Gate requirements).',
  snapshot: { presetKey: 'balanced' }, context: ctx(), input: design('tier-2'),
  facts: { dataUses: { declaredCount: 0, holdsNoBusinessData: false, undecidedCount: 0, retentionOpenCount: 0 } },
  expected: { outcome: 'not-ready', payload: { ready: false }, reasons: [{ code: 'gate.requirement-data-uses-undeclared', ref: 'design-data-uses-declared-tier-2' }, { code: 'gate.not-ready' }], reasonsMatch: 'exact' },
});
write('OP-07', 'data-uses-declared-and-decided-ready', {
  description: 'Two uses declared and both decided: the design gate is ready.',
  snapshot: { presetKey: 'balanced' }, context: ctx(), input: design('tier-2'),
  facts: { dataUses: { declaredCount: 2, holdsNoBusinessData: false, undecidedCount: 0, retentionOpenCount: 0 } },
  expected: { outcome: 'ready', payload: { ready: true }, reasons: [], reasonsMatch: 'exact' },
});
write('OP-07', 'holds-no-business-data-ready', {
  description: 'An application that holds no business data, such as meeting room signage, meets data-uses-declared with no uses.',
  snapshot: { presetKey: 'balanced' }, context: ctx(), input: design('tier-2'),
  facts: { dataUses: { declaredCount: 0, holdsNoBusinessData: true, undecidedCount: 0, retentionOpenCount: 0 } },
  expected: { outcome: 'ready', reasons: [], reasonsMatch: 'exact' },
});
write('OP-07', 'app-data-owner-fallback-domains-optional', {
  description: 'Balanced is domains-optional, so the application-level data owner still meets data-uses-declared, as the fallback route of D-41, question 3.',
  snapshot: { presetKey: 'balanced' }, context: ctx(), input: design('tier-2'),
  facts: { assignedOwnerRoleKeys: ['data-owner'], dataUses: { declaredCount: 0, holdsNoBusinessData: false, undecidedCount: 0, retentionOpenCount: 0 } },
  expected: { outcome: 'ready', reasons: [], reasonsMatch: 'exact' },
});
write('OP-07', 'app-data-owner-no-fallback-domains-required', {
  description: 'Regulated is domains-required, so an application-level data owner no longer meets data-uses-declared: only a declared use or Holds No Business Data does.',
  snapshot: { presetKey: 'regulated' }, context: ctx(), input: design('tier-2'),
  facts: { assignedOwnerRoleKeys: ['data-owner'], dataUses: { declaredCount: 0, holdsNoBusinessData: false, undecidedCount: 0, retentionOpenCount: 0 } },
  expected: { outcome: 'not-ready', reasons: [{ code: 'gate.requirement-data-uses-undeclared' }, { code: 'gate.not-ready' }], reasonsMatch: 'exact' },
});
write('OP-07', 'data-use-undecided-not-ready', {
  description: 'One open use is still needed, proposed, breaks a rule or has no one to ask, so the design gate is not ready (data-uses-decided, data-use).',
  snapshot: { presetKey: 'balanced' }, context: ctx(), input: design('tier-3'),
  facts: { documentation: adr, dataUses: { declaredCount: 3, holdsNoBusinessData: false, undecidedCount: 1, retentionOpenCount: 0 } },
  expected: { outcome: 'not-ready', reasons: [{ code: 'gate.requirement-data-uses-undecided', ref: 'design-data-uses-decided-tier-3' }, { code: 'gate.not-ready' }], reasonsMatch: 'exact' },
});
write('OP-07', 'retention-requirement-open-not-ready', {
  description: 'An install that adds a retirement gate requirement of kind data-uses-decided with data-retention: one write use on a system of record has no accepted retention decision, so the gate is not ready (P09).',
  snapshot: { presetKey: 'balanced', operations: [upsert('gate-requirement', 'value-data-retention-decided', { label: 'Retention decided', gateKey: 'value', requirementKind: 'data-uses-decided', requiredDecisionTypeKey: 'data-retention' })] },
  context: ctx(), input: { gateKey: 'value', tierKey: 'tier-1', applicationKindKey: 'power-platform-app' },
  facts: { dataUses: { declaredCount: 1, holdsNoBusinessData: false, undecidedCount: 0, retentionOpenCount: 1 } },
  expected: { outcome: 'not-ready', payload: { requirements: [{ requirementKey: 'value-data-retention-decided', requirementKind: 'data-uses-decided', requiredDecisionTypeKey: 'data-retention', met: false }] }, reasons: [{ code: 'gate.requirement-data-uses-undecided' }, { code: 'gate.not-ready' }], reasonsMatch: 'exact' },
});
write('OP-07', 'mode-off-data-kinds-met', {
  description: 'With data-taxonomy-mode off the install keeps the behavior before data domains, so the data requirement kinds are met without facts.',
  snapshot: { presetKey: 'balanced', operations: [upsert('setting', 'data-taxonomy-mode', { value: 'off' })] }, context: ctx(), input: design('tier-2'),
  expected: { outcome: 'ready', reasons: [], reasonsMatch: 'exact' },
});
write('OP-07', 'data-uses-missing-invalid-input', {
  description: 'Without the data use counts the engine cannot tell, and it never guesses.',
  snapshot: { presetKey: 'balanced' }, context: ctx(), input: design('tier-2'),
  expected: { outcome: 'invalid-input', reasons: [{ code: 'input.required', severity: 'error' }], reasonsMatch: 'exact' },
});

// ---------------------------------------------------------------- OP-08 ----
const owner = person('fixture-person-1', ['application-owner']);
const useRow = (domain, extra = {}) => ({ businessApplication: 'fixture-app-1', dataDomain: domain, access: 'read', purpose: 'Fixture purpose.', reachesAi: false, isDeidentified: false, status: 'data-use-registered', startOn: '2026-11-24', ...extra });
const guard = (name, description, ruleIds, snapshot, actor, input, facts, expected, tags) => write('OP-08', name, { description, ...(tags ? { tags } : {}), ruleIds, snapshot, context: ctx(actor), input, ...(facts ? { facts } : {}), expected });
const refused = (code, ruleId) => ({ outcome: 'refused', payload: { allowed: false, refusals: [{ code, ruleId }] }, reasons: [{ code, severity: 'error' }], reasonsMatch: 'includes' });
const allowed = { outcome: 'allowed', payload: { allowed: true } };

guard('g18-use-on-active-domain-allowed', 'A data use names an active domain at a declarable level.', ['G-18'], worked(), owner,
  { entity: 'data-use', operation: 'create', after: useRow('parts-catalog') }, null, allowed);
guard('g18-duplicate-use-refused', 'Data design review: the second key of O27. Another open use already has the same application, domain, location and AI flag, so a second one is refused and the refusal names it.', ['G-18'], worked(), owner,
  { entity: 'data-use', operation: 'create', after: useRow('safety-findings') }, { dataFacts: { duplicateUse: 'DU-0003' } }, refused('data.use-duplicate', 'G-18'));
guard('g18-ending-a-duplicate-allowed', 'Ending a use is never refused for its key: the duplicate check judges only a use that stays open.', ['G-18'], worked(), owner,
  { entity: 'data-use', operation: 'update', rowId: 'fixture-use-3', after: { endedOn: '2026-11-24' } }, { before: { ...useRow('safety-findings'), statusMeaning: 'registered' }, dataFacts: { duplicateUse: 'DU-0003' } }, allowed);
guard('g18-use-on-retired-domain-refused', 'A data use cannot point at a retired domain.', ['G-18'], worked([{ op: 'retire', entity: 'data-domain', key: 'timesheets' }]), owner,
  { entity: 'data-use', operation: 'create', after: useRow('timesheets') }, null, refused('data.domain-retired', 'G-18'));
guard('g18-use-on-undeclarable-level-refused', 'Regulated\'s top level is not declarable, so a use of a top-level domain is refused.', ['G-18'],
  { presetKey: 'regulated', operations: tree }, owner, { entity: 'data-use', operation: 'create', after: useRow('people') }, null, refused('data.level-not-declarable', 'G-18'));

const ownershipFacts = (subjectKind, subjectColumn, subject, role) => ({
  before: { statusMeaning: 'proposed', decisionType: 'ownership', subjectKind, [subjectColumn]: subject, outcome: 'assign', proposedChangesJson: { owners: [{ ownerRoleKey: role, addObjectId: 'fixture-person-kai' }] } },
  acceptance: { basis: 'approvals-satisfied', approvalStatusOutcome: 'satisfied' },
  subject: { owners: [], newRowIds: ['fixture-owner-new'] },
});
const runner = { principalId: 'fixture-runner-1', principalKind: 'service-principal', actorKind: 'approval-runner', onBehalfOfPrincipalId: 'fixture-person-2', roleKeys: ['approval-writer'], isEngineNested: true };
guard('g19-domain-ownership-writes-domain-subject-allowed', 'An accepted ownership decision about a data domain writes the owner assignment with the domain as its subject and Subject Kind data-domain (docs/architecture/data-taxonomy.md, Appointing owners).', ['G-19'], worked(), runner,
  { entity: 'decision', operation: 'update', rowId: 'fixture-decision-60', after: { status: 'decision-accepted' }, nestedEffect: 'accept-decision' },
  ownershipFacts('data-domain', 'subjectDataDomain', 'safety-findings', 'data-owner'),
  { outcome: 'allowed', payload: { allowed: true, effects: [{ kind: 'write-audit-entry' }, { kind: 'update-subject', entity: 'Owner Assignment', operation: 'create', rowId: 'fixture-owner-new', columns: { subjectKind: 'data-domain', subjectDataDomain: 'safety-findings', ownerRole: 'data-owner', principal: 'fixture-person-kai' } }, { kind: 'write-audit-entry' }] } }, ['update-subject']);
guard('g19-owner-role-not-held-on-domain-refused', 'The business owner role is held on applications only, so an owner assignment of it on a data domain is refused.', ['G-19'], worked(), { ...runner, actorKind: 'engine' },
  { entity: 'owner-assignment', operation: 'create', rowId: 'fixture-owner-x', after: { subjectKind: 'data-domain', subjectDataDomain: 'people', ownerRole: 'business-owner', principal: 'fixture-person-kai', isDeputy: false, startOn: '2026-11-24' }, nestedEffect: 'update-subject' },
  null, refused('data.owner-subject', 'G-19'));
guard('g19-two-subjects-refused', 'An owner assignment names exactly one subject.', ['G-19'], worked(), { ...runner, actorKind: 'engine' },
  { entity: 'owner-assignment', operation: 'create', rowId: 'fixture-owner-y', after: { businessApplication: 'fixture-app-1', subjectDataDomain: 'people', ownerRole: 'data-owner', principal: 'fixture-person-kai', isDeputy: false, startOn: '2026-11-24' }, nestedEffect: 'update-subject' },
  null, refused('data.owner-subject', 'G-19'));

const decisionAfter = (outcome) => ({ decisionType: 'data-use', subjectKind: 'data-use', subjectDataUse: 'fixture-use-1', subjectApplication: 'fixture-app-1', statement: 'Fixture data use.', outcome, status: 'decision-proposed' });
guard('g20-allow-on-decision-rule-allowed', 'A proposed allow of a use whose rule is decision passes.', ['G-20'], worked(), person('fixture-person-1', ['application-owner']),
  { entity: 'decision', operation: 'create', after: decisionAfter('allow') }, { dataFacts: { decisionSubject: { subjectKind: 'data-use', domainKey: 'safety-findings', reachesAi: true } } }, allowed);
guard('g20-allow-on-never-refused', 'The never rule: save_decision_proposal with allow on Bank details with AI is refused with data.decision-cannot-allow.', ['G-20'], worked(), person('fixture-person-1', ['application-owner']),
  { entity: 'decision', operation: 'create', after: decisionAfter('allow') }, { dataFacts: { decisionSubject: { subjectKind: 'data-use', domainKey: 'bank-details', reachesAi: true } } }, refused('data.decision-cannot-allow', 'G-20'), ['worked-rule']);
guard('g20-deny-on-never-allowed', 'A deny can always be proposed.', ['G-20'], worked(), person('fixture-person-1', ['application-owner']),
  { entity: 'decision', operation: 'create', after: decisionAfter('deny') }, { dataFacts: { decisionSubject: { subjectKind: 'data-use', domainKey: 'bank-details', reachesAi: true } } }, allowed);

guard('g21-ai-drafts-data-use-allowed', 'An AI client\'s data use starts as draft (G-08 and G-21).', ['G-21'], worked(), ai('fixture-person-1', ['application-owner']),
  { entity: 'data-use', operation: 'create', after: useRow('parts-catalog') }, null,
  { outcome: 'allowed', payload: { setColumns: { status: 'data-use-draft', statusMeaning: 'draft', isAiCreated: true } }, reasons: [{ code: 'ai.forced-draft', severity: 'info' }], reasonsMatch: 'exact' });
guard('g21-ai-confirms-data-use-refused', 'An AI client cannot confirm a data use; a person does.', ['G-21'], worked(), ai('fixture-person-1', ['application-owner']),
  { entity: 'data-use', operation: 'update', rowId: 'fixture-use-1', after: { status: 'data-use-registered' } },
  { before: { ...useRow('parts-catalog'), status: 'data-use-draft', statusMeaning: 'draft' } }, refused('ai.data-row-draft-only', 'G-21'));

guard('g21-ai-ends-confirmed-use-refused', 'Security review: an AI client cannot end or change a data use a person confirmed; a person does.', ['G-21'], worked(), ai('fixture-person-1', ['application-owner']),
  { entity: 'data-use', operation: 'update', rowId: 'fixture-use-1', after: { endedOn: '2026-11-24' } },
  { before: { ...useRow('parts-catalog'), statusMeaning: 'registered' } }, refused('ai.data-row-draft-only', 'G-21'));

const locationRow = (extra) => ({ dataPlatform: 'azure-sql', linkKind: 'azure-data-resource', labelAsSeen: 'sql-fixture', status: 'data-location-registered', targetStage: 'production', ...extra });
guard('g21-ai-refreshes-confirmed-location-allowed', 'An AI client may record when it last saw a confirmed location, and nothing more.', ['G-21'], worked(), ai('fixture-person-1', ['platform-admin']),
  { entity: 'data-location', operation: 'update', rowId: 'fixture-location-1', after: { lastSeenOn: '2026-11-24T09:00:00Z' } },
  { before: { ...locationRow({ externalId: 'sql-fixture.database.example.org' }), statusMeaning: 'registered' } }, allowed);
guard('g22-external-id-without-secret-allowed', 'An external ID without a secret part is stored.', ['G-22'], worked(), person('fixture-person-1', ['platform-admin']),
  { entity: 'data-location', operation: 'create', after: locationRow({ externalId: 'sql-fixture.database.example.org' }) }, null, allowed);
guard('g22-connection-string-refused', 'An external ID that carries a connection string password is refused (forbidden-url-patterns).', ['G-22'], worked(), person('fixture-person-1', ['platform-admin']),
  { entity: 'data-location', operation: 'create', after: locationRow({ externalId: 'Server=sql-fixture;User ID=app;Password=hunter2' }) }, null, refused('link.secret', 'G-22'));
guard('g22-container-with-account-key-refused', 'A container name that carries an account key is refused.', ['G-22'], worked(), person('fixture-person-1', ['platform-admin']),
  { entity: 'data-mapping', operation: 'create', after: { dataDomain: 'client-accounts', dataLocation: 'fixture-location-1', dataOrigin: 'replica', containers: ['account', 'AccountKey=abc'], status: 'data-mapping-registered', startOn: '2026-11-24' } }, null, refused('link.secret', 'G-22'));

guard('g18-duplicate-location-refused', 'The alternate key of O25 (docs/architecture/data-taxonomy.md): a location with no External Id is known by its platform and URL, so a second one in service with the same pair is refused and the refusal names the first.', ['G-18'], worked(), person('fixture-person-1', ['platform-admin']),
  { entity: 'data-location', operation: 'create', after: locationRow({ url: 'https://portal.example.org/sql-fixture' }) }, { dataFacts: { duplicateLocation: 'sql-fixture' } }, refused('data.location-duplicate', 'G-18'));
guard('g18-duplicate-location-with-external-id-allowed', 'A location with an External Id is known by it, so the platform and URL pair is not its key.', ['G-18'], worked(), person('fixture-person-1', ['platform-admin']),
  { entity: 'data-location', operation: 'create', after: locationRow({ externalId: 'sql-fixture-2.database.example.org', url: 'https://portal.example.org/sql-fixture' }) }, { dataFacts: { duplicateLocation: 'sql-fixture' } }, allowed);
guard('g18-retiring-a-duplicate-location-allowed', 'Retiring a location is never refused for its key.', ['G-18'], worked(), person('fixture-person-1', ['platform-admin']),
  { entity: 'data-location', operation: 'update', rowId: 'fixture-location-2', after: { retiredOn: '2026-11-24' } },
  { before: { ...locationRow({ url: 'https://portal.example.org/sql-fixture' }), statusMeaning: 'registered' }, dataFacts: { duplicateLocation: 'sql-fixture' } }, allowed);

const mappingRow = (origin, domain = 'client-accounts') => ({ dataDomain: domain, dataLocation: 'fixture-location-1', dataOrigin: origin, status: 'data-mapping-registered', startOn: '2026-11-24' });
guard('g23-masked-copy-in-test-allowed', 'A masked copy sits at a test location.', ['G-23'], worked(), person('fixture-person-1', ['platform-admin']),
  { entity: 'data-mapping', operation: 'create', after: mappingRow('masked-copy') }, { dataFacts: { locationStageKey: 'test' } }, allowed);
guard('g23-copy-in-production-refused', 'A production copy cannot sit at a production location; it would be a replica or the system of record.', ['G-23'], worked(), person('fixture-person-1', ['platform-admin']),
  { entity: 'data-mapping', operation: 'create', after: mappingRow('production-copy') }, { dataFacts: { locationStageKey: 'production' } }, refused('data.copy-origin', 'G-23'));
guard('g23-system-of-record-in-test-warning', 'A system of record outside production is allowed with a warning.', [], worked(), person('fixture-person-1', ['platform-admin']),
  { entity: 'data-mapping', operation: 'create', after: mappingRow('system-of-record') }, { dataFacts: { locationStageKey: 'test' } },
  { outcome: 'allowed', reasons: [{ code: 'data.copy-origin', severity: 'warning' }], reasonsMatch: 'includes' });

guard('g24-store-under-instance-allowed', 'A database under a server takes the server\'s stage and sets none of its own.', ['G-24'], worked(), person('fixture-person-1', ['platform-admin']),
  { entity: 'data-location', operation: 'create', after: locationRow({ parentLocation: 'fixture-location-server', targetStage: null, externalId: 'sql-fixture/db' }) },
  { dataFacts: { parentLocation: { hasParent: false, targetStageKey: 'production' } } }, allowed);
guard('g24-three-levels-refused', 'A location goes two levels deep at most (D-41, question 9).', ['G-24'], worked(), person('fixture-person-1', ['platform-admin']),
  { entity: 'data-location', operation: 'create', after: locationRow({ parentLocation: 'fixture-location-db', targetStage: null, externalId: 'sql-fixture/db/schema' }) },
  { dataFacts: { parentLocation: { hasParent: true, targetStageKey: 'production' } } }, refused('location.depth', 'G-24'));
guard('g24-target-stage-differs-refused', 'A location linked to a deployment target has the target\'s stage.', ['G-24'], worked(), person('fixture-person-1', ['platform-admin']),
  { entity: 'data-location', operation: 'create', after: locationRow({ deploymentTarget: 'fixture-target-1', linkKind: 'dataverse-environment', dataPlatform: 'dataverse', externalId: '00000000-0000-4000-8000-000000000991' }) },
  { dataFacts: { deploymentTargetStageKey: 'test' } }, refused('location.stage', 'G-24'));

guard('g25-use-on-decision-rule-allowed', 'A use whose governing rule is decision is saved; its decision follows.', ['G-25'], worked(), owner,
  { entity: 'data-use', operation: 'create', after: useRow('safety-findings', { reachesAi: true }) }, null, allowed);
guard('g25-person-saves-never-use-refused', 'D-41, question 7, as the product owner changed it: a person saving a use of Bank details that reaches AI, where Payroll sets AI use never, is refused, and the refusal names the rule, the domain and where it is set.', ['G-25'], worked(), owner,
  { entity: 'data-use', operation: 'create', after: useRow('bank-details', { reachesAi: true }) }, null,
  { outcome: 'refused', payload: { allowed: false, refusals: [{ code: 'data.rule-never', ruleId: 'G-25' }] }, reasons: [{ code: 'data.rule-never', severity: 'error', entity: 'Data Use', ref: 'payroll' }], reasonsMatch: 'exact', trace: [{ entity: 'Data Domain', key: 'payroll', role: 'won' }] }, ['worked-rule']);
guard('g25-ai-saves-never-use-refused', 'The same use drafted by an AI client is refused too: the rule holds for people and AI clients alike.', ['G-25'], worked(), ai('fixture-person-1', ['application-owner']),
  { entity: 'data-use', operation: 'create', after: useRow('bank-details', { reachesAi: true, status: 'data-use-draft' }) }, null, refused('data.rule-never', 'G-25'));
guard('g25-future-end-on-create-refused', 'Security review: a create is judged whatever End On it names. A use of Bank details that reaches AI with End On 2099-12-31 is still open today, so it is refused.', ['G-25'], worked(), owner,
  { entity: 'data-use', operation: 'create', after: useRow('bank-details', { reachesAi: true, endedOn: '2099-12-31' }) }, null, refused('data.rule-never', 'G-25'));
guard('g25-never-use-can-be-ended-allowed', 'A use that breaks a never rule after the rule tightened can always be ended today (End On today or earlier).', ['G-25'], worked(), owner,
  { entity: 'data-use', operation: 'update', rowId: 'fixture-use-2', after: { endedOn: '2026-11-24' } }, { before: { ...useRow('bank-details', { reachesAi: true }), statusMeaning: 'registered' } }, allowed);
guard('g25-restricted-copy-to-test-refused', 'Restricted data never goes to test: a masked copy of Payroll at a test location is refused (copy to test rule never, the default of restricted).', ['G-25'], worked(), person('fixture-person-1', ['platform-admin']),
  { entity: 'data-mapping', operation: 'create', after: mappingRow('masked-copy', 'payroll') }, { dataFacts: { locationStageKey: 'test' } }, refused('data.rule-never', 'G-25'));

// ---------------------------------------------------------------- OP-09 ----
const val = (name, description, ruleIds, snapshot, outcome, reasons, input = { mode: 'draft', baseVersionNumber: 1 }, facts) =>
  write('OP-09', name, { description, ruleIds, snapshot, context: ctx(), input, ...(facts ? { facts } : {}), expected: { outcome, payload: { isValid: outcome === 'valid' }, ...(reasons ? { reasons, reasonsMatch: reasons.length === 0 ? 'exact' : 'includes' } : {}) } });
const domain = (key, values) => upsert('data-domain', key, { label: key, ...values });

val('v49-domain-cycle', 'Two domains that name each other as parent make a cycle.', ['V49'],
  overlay([domain('fixture-a', { parentKey: 'fixture-b', levelKey: 'subdomain' }), domain('fixture-b', { parentKey: 'fixture-a', levelKey: 'data-set' })]), 'invalid',
  [{ code: 'data.domain-parent', severity: 'error', ruleId: 'V49' }]);
val('v49-domain-parent-valid', 'A data set under a subdomain under a domain is a tree.', ['V49'], overlay([domain('fixture-orders-archive', { parentKey: 'client-orders', levelKey: 'data-set' })]), 'valid');
val('v50-child-level-not-below', 'A child at the same level as its parent is refused.', ['V50'], overlay([domain('fixture-child', { parentKey: 'customers', levelKey: 'domain' })]), 'invalid',
  [{ code: 'data.domain-level', severity: 'error', ruleId: 'V50', ref: 'fixture-child' }]);
val('v50-child-level-below-valid', 'A child one level down passes.', ['V50'], overlay([domain('fixture-child', { parentKey: 'customers', levelKey: 'subdomain' })]), 'valid');
val('v50-top-level-below-top-warning', 'A domain with no parent below the top level is a warning: no parent owner can appoint its owner.', [],
  overlay([domain('fixture-orphan', { levelKey: 'subdomain', classificationKey: 'internal' })]), 'valid', [{ code: 'data.domain-level', severity: 'warning', ruleId: 'V50' }]);
val('v51-duplicate-level-rank', 'Two active levels share a rank.', ['V51'], overlay([upsert('data-domain-level', 'data-set', { rank: 2 })]), 'invalid', [{ code: 'data.level-rank', severity: 'error', ruleId: 'V51' }]);
val('v51-level-ranks-valid', 'Distinct level ranks pass.', ['V51'], overlay([upsert('data-domain-level', 'data-set', { rank: 4 })]), 'valid');
val('v52-top-level-unclassified', 'A top-level domain must set a classification.', ['V52'], overlay([domain('fixture-top', { levelKey: 'domain' })]), 'invalid',
  [{ code: 'data.classification', severity: 'error', ruleId: 'V52', ref: 'fixture-top' }]);
val('v52-implied-answer-unknown', 'A classification that implies an answer its question does not have is refused.', ['V52'],
  { presetKey: 'balanced', operations: [upsert('data-classification', 'internal', { impliedAnswerKey: 'secret' })] }, 'invalid', [{ code: 'data.classification', severity: 'error', ruleId: 'V52', ref: 'internal' }]);
val('v52-classification-valid', 'A classified top-level domain and resolvable implied answers pass.', ['V52'], overlay([domain('fixture-top', { levelKey: 'domain', classificationKey: 'confidential' })]), 'valid');
val('v53-child-classified-lower-warning', 'A child classified lower than its parent is a warning the approver acknowledges.', ['V53'],
  overlay([domain('fixture-public', { parentKey: 'customers', levelKey: 'subdomain', classificationKey: 'public' })]), 'valid', [{ code: 'data.classification-lower', severity: 'warning', ruleId: 'V53', ref: 'fixture-public' }]);
val('v53-child-classified-higher-valid', 'A child classified higher than its parent passes with no warning.', ['V53'],
  overlay([domain('fixture-restricted', { parentKey: 'customers', levelKey: 'subdomain', classificationKey: 'restricted' })]), 'valid', []);
val('v54-domain-in-use-retired', 'A domain that operational rows use cannot be retired.', ['V54'],
  overlay([{ op: 'retire', entity: 'data-domain', key: 'room-bookings' }]), 'invalid', [{ code: 'data.domain-in-use', severity: 'error', ruleId: 'V54', ref: 'room-bookings' }],
  { mode: 'draft', baseVersionNumber: 1, activeSnapshot: { presetKey: 'balanced', overlays: ['data-domains-starter'] } }, { rowsInUse: [{ entity: 'data-domain', key: 'room-bookings' }] });
val('v54-unused-domain-retired-valid', 'An unused domain can be retired.', ['V54'], overlay([{ op: 'retire', entity: 'data-domain', key: 'room-bookings' }]), 'valid', null,
  { mode: 'draft', baseVersionNumber: 1, activeSnapshot: { presetKey: 'balanced', overlays: ['data-domains-starter'] } });
val('v55-retention-unrouted', 'With every retention rule and right retired, a retention decision has nobody to ask.', ['V55'],
  { presetKey: 'balanced', operations: ['data-retention-owner', 'data-retention-business-owner'].map(k => ({ op: 'retire', entity: 'approval-rule', key: k })).concat(['data-retention-a', 'data-retention-r', 'data-retention-c'].map(k => ({ op: 'retire', entity: 'decision-right', key: k }))) },
  'invalid', [{ code: 'data.decision-unrouted', severity: 'error', ruleId: 'V55' }]);
val('v55-decisions-routed-valid', 'Balanced routes every data decision.', ['V55'], { presetKey: 'balanced' }, 'valid');
val('v56-owner-subject-not-held', 'The data owner role is not held on data locations, so a rule resolving it there is refused.', ['V56'],
  { presetKey: 'balanced', operations: [upsert('approval-rule', 'data-copy', { ownerSubject: 'data-location' })] }, 'invalid', [{ code: 'data.owner-subject-rule', severity: 'error', ruleId: 'V56', ref: 'data-copy' }]);
val('v56-touched-domains-off-configuration-change', 'touched-data-domains belongs on configuration change rows only.', ['V56'],
  { presetKey: 'balanced', operations: [upsert('approval-rule', 'data-copy', { ownerSubject: 'touched-data-domains' })] }, 'invalid', [{ code: 'data.owner-subject-rule', severity: 'error', ruleId: 'V56' }]);
val('v56-owner-subjects-valid', 'Balanced\'s owner subjects fit their roles.', ['V56'], { presetKey: 'balanced' }, 'valid');
val('v57-top-level-without-accountable', 'With the top-level accountable row retired, an ownership decision about a top-level domain has no accountable holder.', ['V57'],
  { presetKey: 'balanced', operations: [{ op: 'retire', entity: 'decision-right', key: 'ownership-top-domain-a' }] }, 'invalid', [{ code: 'data.ownership-accountable', severity: 'error', ruleId: 'V57' }]);
val('v57-parent-at-top-level', 'At the top level there is no parent, so the parent domain\'s owner cannot be accountable there.', ['V57'],
  { presetKey: 'balanced', operations: [{ op: 'retire', entity: 'decision-right', key: 'ownership-top-domain-a' }, upsert('decision-right', 'ownership-top-domain-parent-a', { label: 'Top-level domain owner: the parent owner', decisionTypeKey: 'ownership', subjectKind: 'data-domain', dataDomainLevelKey: 'domain', letter: 'accountable', ownerRoleKey: 'data-owner', ownerSubject: 'parent-data-domain' })] },
  'invalid', [{ code: 'data.ownership-accountable', severity: 'error', ruleId: 'V57', ref: 'ownership-top-domain-parent-a' }]);
val('v57-ownership-valid', 'Balanced has one accountable per level.', ['V57'], { presetKey: 'balanced' }, 'valid');
val('v58-platform-wrong-link-kind', 'A platform must name a data location link kind.', ['V58'],
  { presetKey: 'balanced', operations: [upsert('data-platform', 'azure-sql', { defaultLinkKindKey: 'ado-wiki-page' })] }, 'invalid', [{ code: 'data.platform', severity: 'error', ruleId: 'V58', ref: 'azure-sql' }]);
val('v58-no-production-stage', 'With no production stage, the copy rule cannot tell production from a copy.', ['V58'],
  { presetKey: 'balanced', operations: [upsert('target-stage', 'production', { isProduction: false })] }, 'invalid', [{ code: 'data.platform', severity: 'error', ruleId: 'V58' }]);
val('v58-platforms-valid', 'Balanced\'s platforms and stages pass.', ['V58'], { presetKey: 'balanced' }, 'valid');
val('v59-loosened-rule-warning', 'Loosening the AI rule of internal data warns and lists the affected domains.', ['V59'],
  overlay([upsert('data-classification', 'internal', { defaultAiUseRule: 'allowed' })]), 'valid', [{ code: 'data.loosens', severity: 'warning', ruleId: 'V59' }],
  { mode: 'draft', baseVersionNumber: 1, activeSnapshot: { presetKey: 'balanced', overlays: ['data-domains-starter'] } });
val('v59-tightened-rule-valid', 'Tightening a rule gives no V59 warning.', ['V59'],
  overlay([upsert('data-domain', 'payroll', { aiUseRule: 'never' })]), 'valid', [],
  { mode: 'draft', baseVersionNumber: 1, activeSnapshot: { presetKey: 'balanced', overlays: ['data-domains-starter'] } });
val('v60-domain-with-tenant-url', 'A shipped preset never holds a domain with a tenant URL in its examples.', ['V60'],
  overlay([upsert('data-domain', 'payroll', { examples: 'See https://contoso.example.org/payroll for the list.' })]), 'invalid', [{ code: 'data.preset-tenant', severity: 'error', ruleId: 'V60', ref: 'payroll' }], { mode: 'preset-build' });
val('v60-fabric-domain-match-in-preset', 'A shipped preset never matches a domain to a tenant\'s Fabric domain.', ['V60'],
  overlay([upsert('vocabulary-mapping', 'people-fabric', { label: 'People as a Fabric domain', sourceEntity: 'data-domain', sourceKey: 'people', vocabularyKey: 'fabric-domain', term: 'HR', checkedOn: '2026-11-24' })]), 'invalid',
  [{ code: 'data.preset-tenant', severity: 'error', ruleId: 'V60', ref: 'people-fabric' }], { mode: 'preset-build' });
val('v60-preset-domains-valid', 'The data domains starter overlay names business words only.', ['V60'], overlay(), 'valid', null, { mode: 'preset-build' });

// ---------------------------------------------------------------- OP-17 ----
const loc = (id, stage, extra = {}) => ({ locationId: id, targetStageKey: stage, statusMeaning: 'registered', lastSeenOn: '2026-11-01T00:00:00Z', label: id, ...extra });
const appFact = (id, extra = {}) => ({ applicationId: id, label: id, tierKey: 'tier-2', applicationKindKey: 'power-platform-app', lifecycleStageKey: 'build', statusMeaning: 'registered', ...extra });
const use = (id, appId, domainKey, extra = {}) => ({ useId: id, applicationId: appId, domainKey, access: 'read', reachesAi: false, isDeidentified: false, statusMeaning: 'registered', ...extra });
const techOwner = own('fixture-owner-tech', 'technical-owner', 'fixture-person-sana', { dataLocationId: 'fixture-loc-prod' }, {});
const techDeputy = own('fixture-owner-tech-deputy', 'technical-owner', 'fixture-person-felix', { dataLocationId: 'fixture-loc-prod' }, { isDeputy: true, deputyForId: 'fixture-owner-tech' });
const fullOwners = [...domainOwners,
  own('fixture-owner-greta-deputy', 'data-owner', 'fixture-person-otto', { dataDomainKey: 'payroll' }, { isDeputy: true, deputyForId: 'fixture-owner-greta' }),
  own('fixture-owner-commercial', 'data-owner', 'fixture-person-clara', { dataDomainKey: 'commercial' }),
  own('fixture-owner-commercial-deputy', 'data-owner', 'fixture-person-greta', { dataDomainKey: 'commercial' }, { isDeputy: true, deputyForId: 'fixture-owner-commercial' }),
  techOwner, techDeputy];
const clean = {
  ownerAssignments: fullOwners, principals: directory,
  locations: [loc('fixture-loc-prod', 'production')],
  mappings: [{ mappingId: 'fixture-map-1', domainKey: 'parts-catalog', locationId: 'fixture-loc-prod', origin: 'system-of-record', statusMeaning: 'registered' }],
  uses: [use('fixture-use-1', 'fixture-app-1', 'parts-catalog', { locationId: 'fixture-loc-prod' })],
  applications: [appFact('fixture-app-1', { sensitivityAnswerKey: 'internal', hasDataDependency: true })],
  decisions: [],
};
const counts = (over) => Object.fromEntries(Array.from({ length: 14 }, (_, i) => 'DC-' + String(i + 1).padStart(2, '0')).map(c => [c, over[c] ?? 0]));
const checks = (name, description, facts, over, findings, input = {}, tags) => write('OP-17', name, {
  description, ...(tags ? { tags } : {}), snapshot: worked(), context: ctx(), input, facts, expected: { outcome: 'checked', payload: { counts: counts(over), ...(findings ? { findings } : {}) }, reasons: [], reasonsMatch: 'exact' },
});
checks('clean-estate-no-findings', 'Every domain owned with a deputy, the one location owned and seen, the one use mapped and not needing a decision: no finding.', clean, {}, []);
checks('dc01-no-owner', 'DC-01: nobody owns Commercial or the domains below it.', { ...clean, ownerAssignments: fullOwners.filter(o => !o.assignmentId.startsWith('fixture-owner-commercial') && o.dataDomainKey !== 'contracts') },
  { 'DC-01': 3 }, null);
write('OP-17', 'dc02-own-owner-missing', {
  description: 'DC-02: with the subdomain level needing its own owner, Timesheets, Client accounts and Parts catalog have only inherited owners.',
  snapshot: worked([upsert('data-domain-level', 'subdomain', { ownerRequired: true })]), context: ctx(), input: {}, facts: clean,
  expected: { outcome: 'checked', payload: { counts: counts({ 'DC-02': 3 }) }, reasons: [], reasonsMatch: 'exact' },
});
checks('dc03-no-deputy', 'DC-03: Kai owns Safety findings with a deputy, but the payroll deputy is gone, so Greta has none on Payroll.', { ...clean, ownerAssignments: fullOwners.filter(o => o.assignmentId !== 'fixture-owner-greta-deputy') },
  { 'DC-03': 1 }, [{ check: 'DC-03', code: 'data.no-deputy', severity: 'medium', subjectKind: 'data-domain', subjectId: 'payroll', principalId: 'fixture-person-greta' }]);
checks('dc04-owner-inactive', 'DC-04: the deputy of Field operations is disabled in the directory.', { ...clean, principals: [...directory, { principalId: 'fixture-person-ravi', directoryState: 'disabled' }] },
  { 'DC-04': 2 }, null);
checks('dc05-decision-missing', 'DC-05: a use of Safety findings needs a decision and none is proposed. It reaches no AI, so the finding is medium.',
  { ...clean, uses: [...clean.uses, use('fixture-use-2', 'fixture-app-1', 'safety-findings')], applications: [appFact('fixture-app-1', { sensitivityAnswerKey: 'restricted' })] }, { 'DC-05': 1 },
  [{ check: 'DC-05', code: 'data.decision-missing', severity: 'medium', subjectKind: 'data-use', subjectId: 'fixture-use-2', principalId: 'fixture-person-kai' }]);
checks('dc06-no-one-to-ask', 'DC-06: Contracts has no owner on it or above it, so its use has no one to ask (and DC-01 shows the gap).',
  { ...clean, ownerAssignments: fullOwners.filter(o => !o.assignmentId.startsWith('fixture-owner-commercial') && o.dataDomainKey !== 'contracts'), uses: [...clean.uses, use('fixture-use-3', 'fixture-app-1', 'contracts')], applications: [appFact('fixture-app-1', { sensitivityAnswerKey: 'restricted', hasDataDependency: true })] }, { 'DC-01': 3, 'DC-06': 1 }, null);
checks('dc07-breaks-rule-draft', 'DC-07: a draft use of Bank details with AI breaks the never rule of Payroll. DC-07 reads drafts too, so it shows before anyone confirms it.',
  { ...clean, uses: [...clean.uses, use('fixture-use-4', 'fixture-app-1', 'bank-details', { reachesAi: true, statusMeaning: 'draft' })] }, { 'DC-07': 1 },
  [{ check: 'DC-07', code: 'data.breaks-rule', severity: 'high', subjectKind: 'data-use', subjectId: 'fixture-use-4' }]);
checks('dc08-copy-outside-production', 'DC-08: an unmasked production copy of Client accounts at a test location breaks the copy rule.',
  { ...clean, locations: [...clean.locations, loc('fixture-loc-test', 'test')], mappings: [...clean.mappings, { mappingId: 'fixture-map-2', domainKey: 'client-accounts', locationId: 'fixture-loc-test', origin: 'production-copy', statusMeaning: 'registered' }], ownerAssignments: [...fullOwners, own('fixture-owner-tech-2', 'technical-owner', 'fixture-person-sana', { dataLocationId: 'fixture-loc-test' }), own('fixture-owner-tech-2d', 'technical-owner', 'fixture-person-felix', { dataLocationId: 'fixture-loc-test' }, { isDeputy: true, deputyForId: 'fixture-owner-tech-2' })] },
  { 'DC-08': 1 }, [{ check: 'DC-08', code: 'data.copy-outside-production', severity: 'high', subjectKind: 'data-mapping', subjectId: 'fixture-map-2' }]);
checks('dc09-sensitivity-below-data', 'DC-09: the application answered internal, and its use of Safety findings implies restricted.',
  { ...clean, uses: [...clean.uses, use('fixture-use-5', 'fixture-app-1', 'safety-findings')], decisions: [{ decisionId: 'fixture-decision-5', decisionTypeKey: 'data-use', subjectKind: 'data-use', subjectId: 'fixture-use-5', statusMeaning: 'accepted', outcomeKey: 'allow', judgedClassificationRank: 4, judgedRule: 'decision' }] },
  { 'DC-09': 1 }, [{ check: 'DC-09', code: 'data.sensitivity-below-data', severity: 'medium', subjectKind: 'business-application', subjectId: 'fixture-app-1' }]);
checks('dc10-undeclared-data', 'DC-10: an application that uses AI declares no data use; one that holds no business data is not flagged.',
  { ...clean, applications: [...clean.applications, appFact('fixture-app-2', { usesAi: true }), appFact('fixture-app-3', { usesAi: true, holdsNoBusinessData: true })] },
  { 'DC-10': 1 }, [{ check: 'DC-10', code: 'data.undeclared', severity: 'low', subjectKind: 'business-application', subjectId: 'fixture-app-2' }]);
checks('dc11-mapping-missing', 'DC-11: a use names a location where its domain has no mapping.',
  { ...clean, applications: [appFact('fixture-app-1', { sensitivityAnswerKey: 'restricted', hasDataDependency: true })], uses: [use('fixture-use-1', 'fixture-app-1', 'timesheets', { locationId: 'fixture-loc-prod' }), use('fixture-use-6', 'fixture-app-1', 'parts-catalog', { locationId: 'fixture-loc-prod' })], decisions: [{ decisionId: 'fixture-decision-6', decisionTypeKey: 'data-use', subjectKind: 'data-use', subjectId: 'fixture-use-1', statusMeaning: 'accepted', outcomeKey: 'allow', judgedClassificationRank: 3, judgedRule: 'decision' }] },
  { 'DC-11': 1 }, [{ check: 'DC-11', code: 'data.mapping-missing', severity: 'low', subjectId: 'fixture-use-1' }]);
checks('dc12-location-unowned-and-stale', 'DC-12: one top-level location has no technical owner, another was last seen in May; a child location takes its parent\'s owner and freshness.',
  { ...clean, locations: [...clean.locations, loc('fixture-loc-host', 'production'), loc('fixture-loc-lib', null, { parentLocationId: 'fixture-loc-host' }), loc('fixture-loc-old', 'production', { lastSeenOn: '2026-05-02T00:00:00Z' })],
    ownerAssignments: [...fullOwners, own('fixture-owner-tech-old', 'technical-owner', 'fixture-person-felix', { dataLocationId: 'fixture-loc-old' }), own('fixture-owner-tech-oldd', 'technical-owner', 'fixture-person-sana', { dataLocationId: 'fixture-loc-old' }, { isDeputy: true, deputyForId: 'fixture-owner-tech-old' })] },
  { 'DC-12': 2 }, null);
checks('dc13-retention-open', 'DC-13: a retiring application writes Timesheets on its system of record without an accepted retention decision (the worked rules, retention).',
  { ...clean, mappings: [...clean.mappings, { mappingId: 'fixture-map-7', domainKey: 'timesheets', locationId: 'fixture-loc-prod', origin: 'system-of-record', statusMeaning: 'registered' }],
    uses: [...clean.uses, use('fixture-use-7', 'fixture-app-7', 'timesheets', { access: 'write', locationId: 'fixture-loc-prod' })],
    applications: [...clean.applications, appFact('fixture-app-7', { isRetiring: true, sensitivityAnswerKey: 'restricted' })],
    decisions: [{ decisionId: 'fixture-decision-7', decisionTypeKey: 'data-use', subjectKind: 'data-use', subjectId: 'fixture-use-7', statusMeaning: 'accepted', outcomeKey: 'allow', judgedClassificationRank: 3, judgedRule: 'decision' }] },
  { 'DC-13': 1 }, [{ check: 'DC-13', code: 'data.retention-open', severity: 'high', subjectId: 'fixture-use-7', applicationId: 'fixture-app-7' }], {}, ['worked-rule']);
checks('dc13-retention-decided-no-finding', 'The same retiring application with an accepted retention decision (archive) leaves no DC-13.',
  { ...clean, mappings: [...clean.mappings, { mappingId: 'fixture-map-7', domainKey: 'timesheets', locationId: 'fixture-loc-prod', origin: 'system-of-record', statusMeaning: 'registered' }],
    uses: [...clean.uses, use('fixture-use-7', 'fixture-app-7', 'timesheets', { access: 'write', locationId: 'fixture-loc-prod' })],
    applications: [...clean.applications, appFact('fixture-app-7', { isRetiring: true, sensitivityAnswerKey: 'restricted' })],
    decisions: [{ decisionId: 'fixture-decision-7', decisionTypeKey: 'data-use', subjectKind: 'data-use', subjectId: 'fixture-use-7', statusMeaning: 'accepted', outcomeKey: 'allow', judgedClassificationRank: 3, judgedRule: 'decision' },
      { decisionId: 'fixture-decision-8', decisionTypeKey: 'data-retention', subjectKind: 'data-use', subjectId: 'fixture-use-7', statusMeaning: 'accepted', outcomeKey: 'archive' }] },
  {}, []);
write('OP-17', 'dc14-rows-on-retired-domain', {
  description: 'DC-14: a use and an owner assignment point at Timesheets, which a write outside the engine retired.',
  snapshot: worked([{ op: 'retire', entity: 'data-domain', key: 'timesheets' }]), context: ctx(), input: {},
  facts: { ...clean, ownerAssignments: [...fullOwners, own('fixture-owner-ts', 'data-steward', 'fixture-person-rosa', { dataDomainKey: 'timesheets' })], uses: [...clean.uses, use('fixture-use-8', 'fixture-app-1', 'timesheets')] },
  expected: { outcome: 'checked', payload: { counts: counts({ 'DC-14': 2 }) }, reasons: [], reasonsMatch: 'exact' },
});
checks('scope-domain-subtree', 'The domain scope with its subtree keeps only the findings on Field operations and below.',
  { ...clean, principals: [...directory, { principalId: 'fixture-person-ravi', directoryState: 'disabled' }, { principalId: 'fixture-person-greta', directoryState: 'disabled' }] },
  { 'DC-04': 2 }, null, { scope: { domainKey: 'field-operations', includeSubtree: true } });
checks('scope-person', 'The person scope keeps only the findings about Kai: the uses whose data Kai owns.',
  { ...clean, uses: [...clean.uses, use('fixture-use-2', 'fixture-app-1', 'safety-findings'), use('fixture-use-9', 'fixture-app-1', 'contracts')], applications: [appFact('fixture-app-1', { sensitivityAnswerKey: 'restricted' })] },
  { 'DC-05': 1 }, [{ check: 'DC-05', subjectId: 'fixture-use-2', principalId: 'fixture-person-kai' }], { scope: { principalId: 'fixture-person-kai' } });
write('OP-17', 'mode-off-no-checks', {
  description: 'With data-taxonomy-mode off the data checks are off (docs/architecture/data-taxonomy.md, Levels and the mode).',
  snapshot: worked([upsert('setting', 'data-taxonomy-mode', { value: 'off' })]), context: ctx(), input: {},
  facts: { ...clean, ownerAssignments: [] },
  expected: { outcome: 'checked', payload: { counts: counts({}), findings: [] }, reasons: [], reasonsMatch: 'exact', trace: [{ entity: 'Setting', key: 'data-taxonomy-mode', role: 'matched' }] },
});

console.log('wrote ' + written.length + ' fixtures');
