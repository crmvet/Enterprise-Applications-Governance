// The web app's two dialogs.
// Hand-off: the pages never write. Where a page offers an action, such as "Propose" on an owner gap, it shows the
// request for the person's AI client, which calls the tool; the engine checks the write and only a person decides on
// an approval card.
// Drill-down without a list: the web app lists applications; other lists are not in this release, and the dialog says
// how to get them.
// One modal dialog (Radix through components/ui/dialog): Escape, a click outside and Close all close it. It has no
// trigger of its own; the shell puts the focus back on the control that opened it.
import { Copy } from 'lucide-react';
import { useId, useState } from 'react';
import type { HandOff } from '../harness';
import { Button } from '../components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '../components/ui/dialog';

export type DialogRequest =
  | { kind: 'hand-off'; request: HandOff }
  | { kind: 'drill'; what: string };

const note = 'm-0 text-sm leading-relaxed text-navy-2';

export function AppDialog({ request, onClose }: { request: DialogRequest | null; onClose: () => void }) {
  const [copied, setCopied] = useState(false);
  const promptId = useId();
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

  const close = () => {
    setCopied(false);
    onClose();
  };

  return (
    <Dialog open onOpenChange={open => { if (!open) close(); }}>
      <DialogContent showCloseButton={false} className="gap-4 bg-card text-navy sm:max-w-xl">
        <DialogHeader className="text-left">
          <DialogTitle className="text-lg leading-6 text-navy">{title}</DialogTitle>
          {request.kind === 'hand-off' ? (
            <DialogDescription className={note}>
              These pages are read-only. Give this request to your AI client. It calls{' '}
              <code className="rounded bg-muted px-1 py-px font-mono text-[0.92em] text-navy">{request.request.tool}</code>,
              the engine checks the write, and only a person decides on the approval card.
            </DialogDescription>
          ) : (
            <DialogDescription className={note}>
              The web app lists applications. The list "{request.what}" is not here yet; your AI client can read it with
              the same tools these pages use.
            </DialogDescription>
          )}
        </DialogHeader>
        <div className="flex flex-col gap-1.5">
          <label htmlFor={promptId} className="text-sm font-medium text-navy">Request for your AI client</label>
          <textarea
            id={promptId}
            className="min-h-20 w-full resize-y rounded-md border border-input bg-background px-3 py-2 text-sm leading-relaxed text-navy shadow-xs outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50"
            value={prompt}
            rows={4}
            readOnly
          />
        </div>
        {copied && <span className="text-xs text-navy-3" role="status">Copied.</span>}
        <DialogFooter>
          <Button onClick={copy}>
            <Copy aria-hidden />
            Copy the request
          </Button>
          <Button variant="outline" onClick={close}>Close</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
