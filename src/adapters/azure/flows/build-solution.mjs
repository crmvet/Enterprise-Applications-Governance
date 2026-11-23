#!/usr/bin/env node
// Writes the approval runner as an unpacked Power Platform solution for one publisher prefix, ready for
// "pac solution pack". No packages; Node 20 or later. It reads approval-runner/solution.json and the two workflow files
// next to it, replaces the placeholders, and writes the folder layout the Power Platform CLI packs: Other/Solution.xml,
// Other/Customizations.xml (with the connection references), Workflows/ (each flow as JSON and its .data.xml) and
// environmentvariabledefinitions/. It never signs in, never calls a tenant, and never packs or imports anything.
//
// The output goes outside the repository, so no generated XML and no solution zip is ever committed. Workflow IDs are
// derived from the prefix and the flow's key, so a second build for the same prefix gives the same IDs and an import
// upgrades the flows instead of adding new ones.
//
// Usage: node build-solution.mjs --prefix <prefix> --out <folder> [--publisher-name <name>] [--version <a.b.c.d>]
//   --prefix          the client's publisher prefix: 2 to 8 lowercase letters or digits, starting with a letter
//   --out             a new or empty folder outside this repository
//   --publisher-name  the publisher's display name (default: the prefix)
//   --version         the solution version (default: the manifest's)
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const SOURCE = path.join(HERE, 'approval-runner');

// Environment variable types as Dataverse stores them.
const VARIABLE_TYPES = { string: 100000000, secret: 100000005 };

function option(args, name, fallback) {
  const i = args.indexOf(name);
  if (i < 0) return fallback;
  if (i + 1 >= args.length || args[i + 1].startsWith('--')) fail(`${name} needs a value.`);
  return args[i + 1];
}

function fail(message) {
  console.error(`build-solution: ${message}`);
  process.exit(2);
}

// The repository root: the nearest folder above this script that holds .git.
function repositoryRoot(start) {
  let dir = start;
  for (;;) {
    if (fs.existsSync(path.join(dir, '.git'))) return dir;
    const parent = path.dirname(dir);
    if (parent === dir) return null;
    dir = parent;
  }
}

function isInside(child, parent) {
  const rel = path.relative(parent, child);
  return rel === '' || (!rel.startsWith('..') && !path.isAbsolute(rel));
}

// A stable GUID from text: SHA-256, shaped as a version 4 GUID.
export function stableGuid(text) {
  const hash = crypto.createHash('sha256').update(text, 'utf8').digest();
  hash[6] = (hash[6] & 0x0f) | 0x40;
  hash[8] = (hash[8] & 0x3f) | 0x80;
  const hex = hash.subarray(0, 16).toString('hex');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20, 32)}`;
}

function xml(text) {
  return String(text).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function fill(text, values) {
  let out = text;
  for (const [token, value] of Object.entries(values)) out = out.split(`{{${token}}}`).join(value);
  const left = out.match(/\{\{[A-Za-z]+\}\}/);
  if (left) fail(`placeholder ${left[0]} has no value.`);
  return out;
}

function write(file, text) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, text, 'utf8');
}

function address(number) {
  const empty = ['City', 'County', 'Country', 'Fax', 'FreightTermsCode', 'ImportSequenceNumber', 'Latitude', 'Line1', 'Line2', 'Line3', 'Longitude', 'Name',
    'PostalCode', 'PostOfficeBox', 'PrimaryContactName'];
  const after = ['StateOrProvince', 'Telephone1', 'Telephone2', 'Telephone3', 'TimeZoneRuleVersionNumber', 'UPSZone', 'UTCOffset', 'UTCConversionTimeZoneCode'];
  return [
    '        <Address>',
    `          <AddressNumber>${number}</AddressNumber>`,
    '          <AddressTypeCode>1</AddressTypeCode>',
    ...empty.map(n => `          <${n} xsi:nil="true"></${n}>`),
    '          <ShippingMethodCode>1</ShippingMethodCode>',
    ...after.map(n => `          <${n} xsi:nil="true"></${n}>`),
    '        </Address>',
  ].join('\n');
}

function solutionXml(manifest, values, workflows, version, publisherName) {
  const roots = [
    ...workflows.map(w => `      <RootComponent type="29" id="{${w.id}}" behavior="0" />`),
    ...manifest.environmentVariables.map(v => `      <RootComponent type="380" schemaName="${xml(fill(v.schemaName, values))}" behavior="0" />`),
  ];
  return [
    '<?xml version="1.0" encoding="utf-8"?>',
    '<ImportExportXml version="9.2.0.0" SolutionPackageVersion="9.2" languagecode="1033" generatedBy="CrmLive" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">',
    '  <SolutionManifest>',
    `    <UniqueName>${xml(fill(manifest.uniqueName, values))}</UniqueName>`,
    '    <LocalizedNames>',
    `      <LocalizedName description="${xml(manifest.displayName)}" languagecode="1033" />`,
    '    </LocalizedNames>',
    '    <Descriptions>',
    `      <Description description="${xml(manifest.solutionDescription)}" languagecode="1033" />`,
    '    </Descriptions>',
    `    <Version>${xml(version)}</Version>`,
    '    <Managed>0</Managed>',
    '    <Publisher>',
    `      <UniqueName>${xml(fill(manifest.publisher.uniqueName, values))}</UniqueName>`,
    '      <LocalizedNames>',
    `        <LocalizedName description="${xml(publisherName)}" languagecode="1033" />`,
    '      </LocalizedNames>',
    '      <Descriptions />',
    '      <EMailAddress xsi:nil="true"></EMailAddress>',
    '      <SupportingWebsiteUrl xsi:nil="true"></SupportingWebsiteUrl>',
    `      <CustomizationPrefix>${xml(fill(manifest.publisher.customizationPrefix, values))}</CustomizationPrefix>`,
    `      <CustomizationOptionValuePrefix>${Number(manifest.publisher.optionValuePrefix)}</CustomizationOptionValuePrefix>`,
    '      <Addresses>',
    address(1),
    address(2),
    '      </Addresses>',
    '    </Publisher>',
    '    <RootComponents>',
    ...roots,
    '    </RootComponents>',
    '    <MissingDependencies />',
    '  </SolutionManifest>',
    '</ImportExportXml>',
    '',
  ].join('\n');
}

function customizationsXml(manifest, values) {
  const references = manifest.connectionReferences.flatMap(r => [
    `    <connectionreference connectionreferencelogicalname="${xml(fill(r.logicalName, values))}">`,
    `      <connectionreferencedisplayname>${xml(r.displayName)}</connectionreferencedisplayname>`,
    `      <connectorid>${xml(r.connectorId)}</connectorid>`,
    '      <iscustomizable>1</iscustomizable>',
    '      <statecode>0</statecode>',
    '      <statuscode>1</statuscode>',
    '    </connectionreference>',
  ]);
  return [
    '<?xml version="1.0" encoding="utf-8"?>',
    '<ImportExportXml xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">',
    '  <Entities />',
    '  <Roles />',
    '  <Workflows />',
    '  <FieldSecurityProfiles />',
    '  <Templates />',
    '  <EntityMaps />',
    '  <EntityRelationships />',
    '  <OrganizationSettings />',
    '  <optionsets />',
    '  <CustomControls />',
    '  <EntityDataProviders />',
    '  <connectionreferences>',
    ...references,
    '  </connectionreferences>',
    '  <Languages>',
    '    <Language>1033</Language>',
    '  </Languages>',
    '</ImportExportXml>',
    '',
  ].join('\n');
}

// A modern cloud flow (category 5), imported turned off: an admin sets the connections and turns it on.
function workflowDataXml(workflow) {
  return [
    '<?xml version="1.0" encoding="utf-8"?>',
    `<Workflow WorkflowId="{${workflow.id}}" Name="${xml(workflow.displayName)}" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">`,
    `  <JsonFileName>/Workflows/${workflow.fileName}</JsonFileName>`,
    '  <Type>1</Type>',
    '  <Subprocess>0</Subprocess>',
    '  <Category>5</Category>',
    '  <Mode>0</Mode>',
    '  <Scope>4</Scope>',
    '  <OnDemand>0</OnDemand>',
    '  <TriggerOnCreate>0</TriggerOnCreate>',
    '  <TriggerOnDelete>0</TriggerOnDelete>',
    '  <AsyncAutodelete>0</AsyncAutodelete>',
    '  <SyncWorkflowLogOnFailure>0</SyncWorkflowLogOnFailure>',
    '  <StateCode>0</StateCode>',
    '  <StatusCode>1</StatusCode>',
    '  <RunAs>1</RunAs>',
    '  <IsTransacted>1</IsTransacted>',
    '  <IntroducedVersion>1.0.0.0</IntroducedVersion>',
    '  <IsCustomizable>1</IsCustomizable>',
    '  <BusinessProcessType>0</BusinessProcessType>',
    '  <IsCustomProcessingStepAllowedForOtherPublishers>1</IsCustomProcessingStepAllowedForOtherPublishers>',
    '  <PrimaryEntity>none</PrimaryEntity>',
    '  <LocalizedNames>',
    `    <LocalizedName languagecode="1033" description="${xml(workflow.displayName)}" />`,
    '  </LocalizedNames>',
    '</Workflow>',
    '',
  ].join('\n');
}

function variableXml(variable, values) {
  const type = VARIABLE_TYPES[variable.type];
  if (!type) fail(`environment variable ${variable.schemaName} has the unknown type ${variable.type}.`);
  return [
    `<environmentvariabledefinition schemaname="${xml(fill(variable.schemaName, values))}">`,
    `  <description default="${xml(variable.description)}">`,
    `    <label description="${xml(variable.description)}" languagecode="1033" />`,
    '  </description>',
    `  <displayname default="${xml(variable.displayName)}">`,
    `    <label description="${xml(variable.displayName)}" languagecode="1033" />`,
    '  </displayname>',
    '  <introducedversion>1.0.0.0</introducedversion>',
    '  <iscustomizable>1</iscustomizable>',
    '  <isrequired>1</isrequired>',
    `  <secretstore>0</secretstore>`,
    `  <type>${type}</type>`,
    '</environmentvariabledefinition>',
    '',
  ].join('\n');
}

export function build({ prefix, out, publisherName, version }) {
  if (!/^[a-z][a-z0-9]{1,7}$/.test(prefix || '') || prefix.startsWith('mscrm')) {
    fail('--prefix is 2 to 8 lowercase letters or digits, starts with a letter, and does not start with mscrm.');
  }

  if (!out) fail('--out names the folder to write.');
  const target = path.resolve(out);
  const root = repositoryRoot(HERE);
  if (root && isInside(target, root)) fail(`--out must be outside the repository (${root}), so nothing generated is committed.`);
  if (fs.existsSync(target) && fs.readdirSync(target).length > 0) fail(`--out ${target} is not empty.`);

  const manifest = JSON.parse(fs.readFileSync(path.join(SOURCE, 'solution.json'), 'utf8'));
  const solutionVersion = version || manifest.version;
  if (!/^\d+\.\d+\.\d+\.\d+$/.test(solutionVersion)) fail('--version is four numbers, such as 1.0.0.0.');

  const workflows = manifest.workflows.map(w => {
    const id = stableGuid(`${prefix}:approval-runner:${w.key}`);
    const safe = w.displayName.replace(/[^A-Za-z0-9]/g, '');
    return { ...w, id, fileName: `${safe}-${id.toUpperCase()}.json` };
  });
  const child = workflows.find(w => w.kind === 'child');
  if (!child) fail('the manifest names no child flow.');
  const values = { prefix, childFlowId: child.id };

  write(path.join(target, 'Other', 'Solution.xml'), solutionXml(manifest, values, workflows, solutionVersion, publisherName || prefix));
  write(path.join(target, 'Other', 'Customizations.xml'), customizationsXml(manifest, values));
  for (const workflow of workflows) {
    const text = fill(fs.readFileSync(path.join(SOURCE, workflow.file), 'utf8'), values);
    JSON.parse(text);
    write(path.join(target, 'Workflows', workflow.fileName), text);
    write(path.join(target, 'Workflows', `${workflow.fileName}.data.xml`), workflowDataXml(workflow));
  }

  for (const variable of manifest.environmentVariables) {
    const schemaName = fill(variable.schemaName, values);
    write(path.join(target, 'environmentvariabledefinitions', schemaName, 'environmentvariabledefinition.xml'), variableXml(variable, values));
  }

  return { target, workflows };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  const { target, workflows } = build({
    prefix: option(args, '--prefix'),
    out: option(args, '--out'),
    publisherName: option(args, '--publisher-name'),
    version: option(args, '--version'),
  });
  console.log(`Wrote the unpacked approval runner solution to ${target}:`);
  for (const w of workflows) console.log(`  ${w.displayName}: ${w.id}`);
  console.log('Next: pac solution pack --zipfile <file>.zip --folder <that folder> --packagetype Unmanaged, then import it (flows/README.md).');
}
