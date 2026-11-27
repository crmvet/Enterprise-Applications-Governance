// The wave 4b addresses: Approvals and AI activity take a portfolio, so the Overview's "Approvals waiting" and "AI
// drafts waiting" tiles open the rows they count under a portfolio. Fictional patterned IDs only.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { appHref, parseAppPath } from './routes';

const BASE = '/app/';
const PORTFOLIO = '00000000-0000-4000-8000-000000000a31';
const APP = '00000000-0000-4000-8000-000000000511';

test('Approvals reads a portfolio from the address, lowercased, and drops one that is not an ID', () => {
  assert.deepEqual(parseAppPath('/app/approvals', `?view=open&portfolio=${PORTFOLIO.toUpperCase()}`, BASE), {
    page: 'approvals',
    view: 'open',
    portfolioId: PORTFOLIO,
  });
  assert.deepEqual(parseAppPath('/app/approvals', '?portfolio=field-operations', BASE), { page: 'approvals' });
  assert.deepEqual(parseAppPath('/app/approvals', `?app=${APP}&portfolio=${PORTFOLIO}&q=risk`, BASE), {
    page: 'approvals',
    applicationId: APP,
    portfolioId: PORTFOLIO,
    text: 'risk',
  });
});

test('Approvals writes the portfolio into the address it links to', () => {
  assert.equal(appHref({ page: 'approvals', view: 'open', portfolioId: PORTFOLIO }, BASE), `/app/approvals?view=open&portfolio=${PORTFOLIO}`);
  const href = appHref({ page: 'approvals', view: 'open', portfolioId: PORTFOLIO, typeKey: 'exception' }, BASE);
  const [path, search] = href.split('?');
  assert.deepEqual(parseAppPath(path, `?${search}`, BASE), { page: 'approvals', view: 'open', portfolioId: PORTFOLIO, typeKey: 'exception' });
});

test('AI activity reads and writes a portfolio, beside the application and mine', () => {
  assert.deepEqual(parseAppPath('/app/ai-activity', `?view=waiting&portfolio=${PORTFOLIO}`, BASE), {
    page: 'ai-activity',
    view: 'waiting',
    portfolioId: PORTFOLIO,
  });
  assert.deepEqual(parseAppPath('/app/ai', `?portfolio=${PORTFOLIO}&mine=1`, BASE), { page: 'ai-activity', portfolioId: PORTFOLIO, mine: true });
  assert.deepEqual(parseAppPath('/app/ai-activity', '?portfolio=not-an-id', BASE), { page: 'ai-activity' });
  assert.equal(appHref({ page: 'ai-activity', view: 'waiting', portfolioId: PORTFOLIO }, BASE), `/app/ai-activity?view=waiting&portfolio=${PORTFOLIO}`);
  assert.equal(appHref({ page: 'ai-activity' }, BASE), '/app/ai-activity');
});
