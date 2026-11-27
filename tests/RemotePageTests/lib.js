// Helpers for test.js: the host stand-in, real touches, and pass/fail bookkeeping.
'use strict';

const { spawn } = require('child_process');
const readline = require('readline');

// The game's side: Host/ (the real RemoteServer) printing one JSON line per protocol event.
class Host {
  constructor(hostDll, pagePath, certPath) {
    this.events = [];
    this.waiters = [];
    this.proc = spawn('dotnet', [hostDll, pagePath, certPath], { stdio: ['pipe', 'pipe', 'inherit'] });
    readline.createInterface({ input: this.proc.stdout }).on('line', (line) => {
      let ev;
      try { ev = JSON.parse(line); } catch (e) { return; }
      this.events.push(ev);
      if (process.env.REMOTE_PAGE_TESTS_VERBOSE && ev.ev !== 'mic') console.log('     [host] ' + line);
      this.waiters = this.waiters.filter((w) => !w(ev));
    });
  }

  async start() {
    const ev = await this.waitFor((e) => e.ev === 'listening', 30000, 'the host to listen');
    this.port = ev.port;
    return this;
  }

  // ingame on|off, kick <player>, send <player> <text>
  send(cmd) { this.proc.stdin.write(cmd + '\n'); }

  // Position in the event list, to look only at what came after it
  mark() { return this.events.length; }

  since(mark, pred) { return this.events.slice(mark).filter(pred); }

  // The first event from `from` on that matches pred, waiting up to ms for it
  waitFor(pred, ms, what, from = 0) {
    for (let i = from; i < this.events.length; i++) {
      if (pred(this.events[i])) return Promise.resolve(this.events[i]);
    }
    return new Promise((resolve, reject) => {
      const check = (ev) => {
        if (!pred(ev)) return false;
        clearTimeout(timer);
        resolve(ev);
        return true;
      };
      const timer = setTimeout(() => {
        this.waiters = this.waiters.filter((w) => w !== check);
        reject(new Error('timed out waiting for ' + what));
      }, ms);
      this.waiters.push(check);
    });
  }

  stop() {
    try { this.send('quit'); } catch (e) { }
    setTimeout(() => this.proc.kill(), 500);
  }
}

// Touches sent the way a touch screen sends them, so the page gets real pointer events with
// their own timestamps (several fingers at once for chords)
class Fingers {
  constructor(page, cdp) {
    this.page = page;
    this.cdp = cdp;
    this.down = new Map();
  }

  static async on(page) {
    return new Fingers(page, await page.context().newCDPSession(page));
  }

  async center(selector) {
    const box = await this.page.locator(selector).first().boundingBox();
    if (!box) throw new Error('not on screen: ' + selector);
    return { x: box.x + box.width / 2, y: box.y + box.height / 2, box };
  }

  points() {
    return Array.from(this.down.entries()).map(([id, p]) => ({ x: p.x, y: p.y, id }));
  }

  // fingers: [{ id, x, y }], all touching down together
  async press(...fingers) {
    for (const f of fingers) this.down.set(f.id, { x: f.x, y: f.y });
    await this.cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: this.points() });
  }

  async move(id, x, y) {
    this.down.set(id, { x, y });
    await this.cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: this.points() });
  }

  async release(...ids) {
    for (const id of ids) this.down.delete(id);
    await this.cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: this.points() });
  }

  async tap(selector, holdMs = 60, id = 1) {
    const c = await this.center(selector);
    await this.press({ id, x: c.x, y: c.y });
    await sleep(holdMs);
    await this.release(id);
  }
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Button bits, as in remote.html and RemotePlayManager.cs
const BIT = { GREEN: 0, RED: 1, YELLOW: 2, BLUE: 3, ORANGE: 4, UP: 5, DOWN: 6, LEFT: 7, RIGHT: 8, START: 9, SELECT: 10 };
const bits = (...names) => names.reduce((m, n) => m | (1 << BIT[n]), 0);

// How many separate strums down a run of controller packets holds
function countStrums(packets) {
  let count = 0;
  let held = false;
  for (const p of packets) {
    const down = (p.buttons & bits('DOWN')) !== 0;
    if (down && !held) count++;
    held = down;
  }
  return count;
}

let checks = 0;
let failures = 0;

function check(ok, what, detail) {
  checks++;
  if (ok) {
    console.log('ok   ' + what);
  } else {
    failures++;
    console.log('FAIL ' + what + (detail !== undefined ? '  -> ' + JSON.stringify(detail) : ''));
  }
  return ok;
}

const results = () => ({ checks, failures });

module.exports = { Host, Fingers, sleep, bits, countStrums, check, results };
