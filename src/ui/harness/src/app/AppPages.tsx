// The web app's own views beside the pages of src/pages:
// - a plain panel for a record link of another entity, which the web app does not show;
// - a page for an address that does not exist.
// The applications register moved to pages/register-extras/ApplicationsRegister.tsx (wave 3).
import { humanizeKey } from '../data/format';
import { useHost } from '../harness';
import { PageHeader, Panel, TextLink } from '../components/blocks';

const plain = 'm-0 max-w-[720px] text-sm leading-relaxed text-navy-2';
const mono = 'font-mono text-xs [overflow-wrap:anywhere] text-navy';

export function OtherRecord({ entity, id }: { entity: string; id: string }) {
  const host = useHost();
  return (
    <>
      <PageHeader eyebrow="Record" title={humanizeKey(entity)} />
      <Panel title="Not shown here">
        <p className={plain}>
          The web app has no page for a record of this kind yet. Your AI client can read it with the product's tools;
          give it this ID.
        </p>
        <span className={mono}>{id}</span>
        <TextLink className="self-start text-sm" href={host.href({ page: 'overview' })}>Go to the overview</TextLink>
      </Panel>
    </>
  );
}

export function NotFound() {
  const host = useHost();
  return (
    <>
      <PageHeader eyebrow="Not found" title="This page does not exist" />
      <Panel title="Where to go">
        <p className={plain}>The address does not match a page of the web app. Check the link, or start from the overview.</p>
        <TextLink className="self-start text-sm" href={host.href({ page: 'overview' })}>Go to the overview</TextLink>
      </Panel>
    </>
  );
}
