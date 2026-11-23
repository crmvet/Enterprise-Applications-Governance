// The web app's two dialogs.
// Hand-off: the pages never write. Where a page offers an action, such as "Propose" on an owner gap, it shows the
// request for the person's AI client, which calls the tool; the engine checks the write and only a person decides on
// an approval card.
// Drill-down without a list: the web app lists applications; other lists are not in this release, and the dialog says
// how to get them.
import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Copy16Regular } from '@fluentui/react-icons';
import { useState } from 'react';
import type { HandOff } from '../harness';
import { palette } from '../theme';

export type DialogRequest =
  | { kind: 'hand-off'; request: HandOff }
  | { kind: 'drill'; what: string };

const useStyles = makeStyles({
  body: { display: 'flex', flexDirection: 'column', rowGap: '12px', color: palette.navy },
  note: { margin: 0, fontSize: tokens.fontSizeBase300, color: palette.navy2 },
  prompt: { width: '100%' },
  copied: { fontSize: tokens.fontSizeBase200, color: palette.navy3 },
});

export function AppDialog({ request, onClose }: { request: DialogRequest | null; onClose: () => void }) {
  const s = useStyles();
  const [copied, setCopied] = useState(false);
  if (!request) return null;

  const title = request.kind === 'hand-off' ? request.request.title : 'This list is not in the web app yet';
  const prompt = request.kind === 'hand-off' ? request.request.prompt : `List ${request.what.charAt(0).toLowerCase()}${request.what.slice(1)}.`;

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(prompt);
      setCopied(true);
    } catch {
      setCopied(false);
    }
  };

  return (
    <Dialog open modalType="modal" onOpenChange={(_, d) => { if (!d.open) { setCopied(false); onClose(); } }}>
      <DialogSurface aria-describedby="app-dialog-note">
        <DialogBody>
          <DialogTitle>{title}</DialogTitle>
          <DialogContent className={s.body}>
            {request.kind === 'hand-off' ? (
              <p className={s.note} id="app-dialog-note">
                These pages are read-only. Give this request to your AI client. It calls <code>{request.request.tool}</code>,
                the engine checks the write, and only a person decides on the approval card.
              </p>
            ) : (
              <p className={s.note} id="app-dialog-note">
                The web app lists applications. The list "{request.what}" is not here yet; your AI client can read it with
                the same tools these pages use.
              </p>
            )}
            <Field label="Request for your AI client">
              <Textarea className={s.prompt} value={prompt} readOnly resize="vertical" />
            </Field>
            {copied && <span className={s.copied} role="status">Copied.</span>}
          </DialogContent>
          <DialogActions>
            <Button appearance="primary" icon={<Copy16Regular />} onClick={copy}>Copy the request</Button>
            <Button appearance="secondary" onClick={() => { setCopied(false); onClose(); }}>Close</Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
