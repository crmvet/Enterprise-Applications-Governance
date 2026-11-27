// The video of "demo play --record": the page's own screencast from Chrome (the DevTools protocol's Page.startScreencast),
// written as a Motion JPEG AVI file. Playwright's own video needs an ffmpeg binary it downloads separately, and this
// suite downloads nothing; Chrome already sends JPEG frames, and an AVI file is a small header, the frames and an index.
// VLC and Windows Media Player play it; any video tool converts it to another format.
//
// Chrome sends a frame only when the page changes. The file runs at a fixed rate, and a time with no new frame is an
// empty chunk, which players show as the frame before (the AVI way to hold a frame).
import fs from 'node:fs';
import path from 'node:path';
import type { BrowserContext, CDPSession, Page } from '@playwright/test';

const FPS = 10;

interface Frame {
  offset: number;
  size: number;
  time: number;
}

export class Recorder {
  private session: CDPSession | null = null;
  private frames: Frame[] = [];
  private spool: number | null = null;
  private spoolBytes = 0;
  private readonly spoolFile: string;
  // The first page load (frames before it are the blank start page) and the end of the last step (frames after it
  // are the teardown, which drew the page at half scale over grey in a QA video).
  private firstLoad: number | null = null;
  private lastMark: { time: number; frames: number } | null = null;

  constructor(private readonly file: string, private readonly width: number, private readonly height: number) {
    this.spoolFile = `${file}.frames`;
  }

  async start(context: BrowserContext, page: Page) {
    fs.mkdirSync(path.dirname(this.file), { recursive: true });
    this.spool = fs.openSync(this.spoolFile, 'w');
    this.session = await context.newCDPSession(page);
    this.session.on('Page.screencastFrame', (event: { data: string; sessionId: number; metadata: { timestamp?: number } }) => {
      const jpeg = Buffer.from(event.data, 'base64');
      if (this.spool !== null) {
        fs.writeSync(this.spool, jpeg);
        this.frames.push({ offset: this.spoolBytes, size: jpeg.length, time: event.metadata.timestamp ?? Date.now() / 1000 });
        this.spoolBytes += jpeg.length;
      }
      this.session?.send('Page.screencastFrameAck', { sessionId: event.sessionId }).catch(() => {});
    });
    page.once('load', () => {
      this.firstLoad = Date.now() / 1000;
    });
    await this.session.send('Page.startScreencast', { format: 'jpeg', quality: 80, maxWidth: this.width, maxHeight: this.height, everyNthFrame: 1 });
  }

  // The end of a step: the video ends at the last mark, holding its last frame for a second.
  mark() {
    this.lastMark = { time: Date.now() / 1000, frames: this.frames.length };
  }

  // Stops the screencast and writes the AVI file. Answers its path, or null when no frame came.
  async stop(): Promise<string | null> {
    await this.session?.send('Page.stopScreencast').catch(() => {});
    await this.session?.detach().catch(() => {});
    this.session = null;
    if (this.spool !== null) fs.closeSync(this.spool);
    this.spool = null;
    try {
      // Only the frames between the first page load and the last step's end.
      let frames = this.lastMark ? this.frames.slice(0, this.lastMark.frames) : this.frames;
      if (this.firstLoad !== null) {
        const firstShown = frames.findIndex(f => f.time >= (this.firstLoad as number));
        if (firstShown > 0) frames = frames.slice(firstShown);
      }
      if (!frames.length) return null;
      const end = this.lastMark ? this.lastMark.time + 1 : Date.now() / 1000;
      writeAvi(this.file, this.spoolFile, frames, this.width, this.height, end);
      return this.file;
    } finally {
      fs.rmSync(this.spoolFile, { force: true });
    }
  }
}

// RIFF AVI 1.0 with one Motion JPEG video stream and an idx1 index.
function writeAvi(file: string, spoolFile: string, frames: Frame[], width: number, height: number, end: number) {
  const start = frames[0].time;
  const ticks = Math.max(1, Math.ceil((end - start) * FPS));
  // For each tick, the frame that is new at that tick, or -1 to hold the one before.
  const plan: number[] = [];
  let next = 0;
  for (let t = 0; t < ticks; t++) {
    const at = start + t / FPS;
    let shown = -1;
    while (next < frames.length && frames[next].time <= at + 1e-6) shown = next++;
    plan.push(t === 0 && shown < 0 ? 0 : shown);
  }
  const largest = frames.reduce((m, f) => Math.max(m, f.size), 0);
  const spool = fs.openSync(spoolFile, 'r');
  const out = fs.openSync(file, 'w');
  let position = 0;
  const write = (buf: Buffer) => {
    fs.writeSync(out, buf, 0, buf.length, position);
    position += buf.length;
  };
  const fourcc = (s: string) => Buffer.from(s, 'ascii');
  const u32 = (n: number) => {
    const b = Buffer.alloc(4);
    b.writeUInt32LE(n >>> 0);
    return b;
  };
  const u16 = (n: number) => {
    const b = Buffer.alloc(2);
    b.writeUInt16LE(n);
    return b;
  };

  const avih = Buffer.concat([
    u32(Math.round(1_000_000 / FPS)), u32(largest * FPS), u32(0), u32(0x10), u32(ticks), u32(0), u32(1), u32(largest + 8),
    u32(width), u32(height), u32(0), u32(0), u32(0), u32(0),
  ]);
  const strh = Buffer.concat([
    fourcc('vids'), fourcc('MJPG'), u32(0), u16(0), u16(0), u32(0), u32(1), u32(FPS), u32(0), u32(ticks), u32(largest + 8),
    u32(0xffffffff), u32(0), u16(0), u16(0), u16(width), u16(height),
  ]);
  const strf = Buffer.concat([
    u32(40), u32(width), u32(height), u16(1), u16(24), fourcc('MJPG'), u32(width * height * 3), u32(0), u32(0), u32(0), u32(0),
  ]);
  const chunk = (id: string, body: Buffer) => Buffer.concat([fourcc(id), u32(body.length), body, body.length % 2 ? Buffer.alloc(1) : Buffer.alloc(0)]);
  const list = (type: string, body: Buffer) => Buffer.concat([fourcc('LIST'), u32(body.length + 4), fourcc(type), body]);
  const hdrl = list('hdrl', Buffer.concat([chunk('avih', avih), list('strl', Buffer.concat([chunk('strh', strh), chunk('strf', strf)]))]));

  write(Buffer.concat([fourcc('RIFF'), u32(0), fourcc('AVI ')]));
  write(hdrl);
  const moviStart = position;
  write(Buffer.concat([fourcc('LIST'), u32(0), fourcc('movi')]));
  const index: Buffer[] = [];
  for (const shown of plan) {
    const offset = position - (moviStart + 8);
    if (shown < 0) {
      write(Buffer.concat([fourcc('00dc'), u32(0)]));
      index.push(Buffer.concat([fourcc('00dc'), u32(0), u32(offset), u32(0)]));
      continue;
    }
    const f = frames[shown];
    const jpeg = Buffer.alloc(f.size);
    fs.readSync(spool, jpeg, 0, f.size, f.offset);
    write(chunk('00dc', jpeg));
    index.push(Buffer.concat([fourcc('00dc'), u32(0x10), u32(offset), u32(f.size)]));
  }
  const moviSize = position - (moviStart + 8);
  write(chunk('idx1', Buffer.concat(index)));
  const riffSize = position - 8;
  fs.writeSync(out, u32(riffSize), 0, 4, 4);
  fs.writeSync(out, u32(moviSize), 0, 4, moviStart + 4);
  fs.closeSync(out);
  fs.closeSync(spool);
}
