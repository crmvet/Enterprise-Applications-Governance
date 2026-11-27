// The web app types every tool of wave 4 (tools 95 to 102), read or write as tools.json says (access), and the action
// sheet knows the write tools. A new tool numbered after 102 fails here until the web app types it.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import catalog from '../../../../mcp/contracts/tools.json';
import { WRITE_TOOLS, type ToolName } from './contracts';

// "satisfies" refuses a name the web app does not type (ToolData).
const WAVE4 = {
  list_teams: 'read',
  get_team: 'read',
  list_data_uses: 'read',
  list_data_mappings: 'read',
  import_data_taxonomy: 'write',
  renew_approval_request: 'write',
  retry_approval_card: 'write',
  get_demo_freshness: 'read',
} as const satisfies Partial<Record<ToolName, 'read' | 'write'>>;

const tools: { number: number; name: string; access: string }[] = catalog.tools;

test('every tool from 95 on is typed by the web app, with the kind tools.json gives it', () => {
  const late = tools.filter(t => t.number >= 95).map(t => t.name).sort();
  assert.deepEqual(late, Object.keys(WAVE4).sort());
  for (const [name, kind] of Object.entries(WAVE4)) {
    assert.equal(tools.find(t => t.name === name)?.access, kind, name);
  }
});

test('the action sheet knows each wave 4 write tool, and no read tool', () => {
  for (const [name, kind] of Object.entries(WAVE4)) {
    assert.equal((WRITE_TOOLS as readonly string[]).includes(name), kind === 'write', name);
  }
});
