// The addresses of Application 360's sections: /app/records/business-application/<id>?tab=<section>. The summary has
// no tab parameter, so its address stays the record link the tools return.
import { hrefTo, withParams } from '../../app/links';
import type { App360Tab } from './logic';

export function tabHref(applicationId: string, tab: App360Tab): string {
  return withParams(hrefTo({ page: 'app', id: applicationId }), { tab: tab === 'summary' ? undefined : tab });
}
