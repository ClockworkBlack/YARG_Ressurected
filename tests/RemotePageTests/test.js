// The phone page (overlay/Assets/Resources/YargRemote/remote.html) in a real browser, with touch
// and a fake microphone, talking to the real phone server (RemoteServer.cs) through Host/, which
// answers the way the game's RemotePlayManager does. Checks what a guest's phone sends and what
// its page shows: joining, touch timing, lanes, chords, frets, strum, whammy, the menu buttons,
// singing (a 440 Hz tone in the fake microphone must reach the host as 440 Hz), joining mid-song,
// reconnecting, and the same phone open twice.
//
//   cd tests/RemotePageTests && npm install && npx playwright install chromium && cd ../..
//   bash android/remote/make-cert.sh /tmp/cert.bytes
//   node tests/RemotePageTests/test.js /tmp/cert.bytes
'use strict';

const { execFileSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { chromium } = require('playwright');
const { Host, Fingers, sleep, bits, countStrums, check, results } = require('./lib');

const repo = path.resolve(__dirname, '../..');
const pagePath = path.join(repo, 'overlay/Assets/Resources/YargRemote/remote.html');
const certPath = process.argv[2];
if (!certPath || !fs.existsSync(certPath)) {
  console.log('usage: node test.js <cert.bytes from android/remote/make-cert.sh>');
  process.exit(2);
}

const work = fs.mkdtempSync(path.join(os.tmpdir(), 'yarg-remote-page-'));

function buildHost() {
  const out = path.join(work, 'host');
  execFileSync('dotnet', ['build', path.join(__dirname, 'Host'), '-c', 'Release', '-o', out, '-nologo', '-v', 'q'],
    { stdio: 'inherit' });
  return path.join(out, 'RemotePageHost.dll');
}

// 16-bit mono WAV of a sine tone, for the fake microphone
function writeTone(file, hz, rate, seconds) {
  const n = rate * seconds;
  const wav = Buffer.alloc(44 + n * 2);
  wav.write('RIFF', 0);
  wav.writeUInt32LE(36 + n * 2, 4);
  wav.write('WAVEfmt ', 8);
  wav.writeUInt32LE(16, 16);
  wav.writeUInt16LE(1, 20);
  wav.writeUInt16LE(1, 22);
  wav.writeUInt32LE(rate, 24);
  wav.writeUInt32LE(rate * 2, 28);
  wav.writeUInt16LE(2, 32);
  wav.writeUInt16LE(16, 34);
  wav.write('data', 36);
  wav.writeUInt32LE(n * 2, 40);
  for (let i = 0; i < n; i++) {
    wav.writeInt16LE(Math.round(0.5 * 32767 * Math.sin(2 * Math.PI * hz * i / rate)), 44 + i * 2);
  }
  fs.writeFileSync(file, wav);
}

async function newPhone(browser) {
  const context = await browser.newContext({
    ignoreHTTPSErrors: true, // the game's certificate is self-signed; guests tap "Proceed"
    hasTouch: true,
    isMobile: true,
    viewport: { width: 915, height: 412 }, // a phone turned sideways
    permissions: ['microphone'],
  });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(String(e)));
  return { context, page, errors };
}

async function join(page, name, role, opts = {}) {
  await page.fill('#name', name);
  await page.click(`.role[data-role="${role}"]`);
  if (opts.difficulty) await page.selectOption('#difficulty', opts.difficulty);
  if (opts.challenge !== undefined) await page.setChecked('#challenge', opts.challenge);
  await page.click('#joinBtn');
}

const isPad = (e) => e.ev === 'pad';

(async () => {
  const toneFile = path.join(work, 'tone440.wav');
  writeTone(toneFile, 440, 44100, 12);
  const host = await new Host(buildHost(), pagePath, certPath).start();

  const secure = `https://127.0.0.1:${host.port}/`;
  // Browsers count http://127.0.0.1 as secure, but not a named host, like a phone's
  // http://192.168.x.x: so "yarg.test", sent to 127.0.0.1 (and kept off any proxy)
  const plain = `http://yarg.test:${host.port}/`;
  const browser = await chromium.launch({
    env: {
      ...process.env,
      no_proxy: [process.env.no_proxy, 'yarg.test'].filter(Boolean).join(','),
      NO_PROXY: [process.env.NO_PROXY, 'yarg.test'].filter(Boolean).join(','),
    },
    args: [
      '--host-resolver-rules=MAP yarg.test 127.0.0.1',
      '--use-fake-ui-for-media-stream',
      '--use-fake-device-for-media-stream',
      '--use-file-for-fake-audio-capture=' + toneFile,
      '--autoplay-policy=no-user-gesture-required',
      '--ignore-certificate-errors',
    ],
  });
  const phones = [];

  try {
    // ---------- The join screen ----------
    const a = await newPhone(browser);
    phones.push(a);
    await a.page.goto(secure);
    check(await a.page.title() === 'YARG Remote', 'the page loads over https');
    check(await a.page.isVisible('#join') && !(await a.page.isVisible('#app')), 'it opens on the join screen');
    check(!(await a.page.isVisible('#challengeRow')), 'Challenge Mode is not offered for guitar');
    await a.page.click('.role[data-role="vocals"]');
    check(await a.page.isVisible('#challengeRow'), 'Challenge Mode is offered for vocals');
    await a.page.click('#joinBtn');
    check(await a.page.isVisible('#join'), 'Join needs a name');

    // ---------- Joining ----------
    let m = host.mark();
    await join(a.page, 'Alice', 'guitar', { difficulty: 'hard' });
    const ha = await host.waitFor((e) => e.ev === 'hello', 5000, 'Alice to join', m);
    check(ha.name === 'Alice' && ha.secure === true, 'the phone joins over wss with its name', ha);
    const role = await host.waitFor((e) => e.ev === 'text' && e.msg.startsWith('role'), 5000, 'the role', m);
    check(role.msg === 'role\nguitar\nhard\n0', 'it asks for guitar on Hard', role.msg);
    await host.waitFor((e) => isPad(e) && e.buttons === 0, 5000, 'the first controller state', m);
    check(true, 'it sends its controller state (nothing pressed) on joining');
    await a.page.waitForSelector('#status.ok', { timeout: 5000 });
    check((await a.page.textContent('#status')).includes('Alice · player 1'), 'the bar shows the name and player number',
      await a.page.textContent('#status'));
    await a.page.waitForFunction(() => document.getElementById('notice').textContent.includes("You're in on guitar"),
      null, { timeout: 5000 });
    check(true, "the host's answer is shown (\"You're in on guitar\")");

    // Clock sync: once pongs come back, each ping carries the measured round trip
    await host.waitFor((e) => e.ev === 'text' && /^ping\n[\d.]+\n[\d.]+$/.test(e.msg), 5000, 'a ping with its round trip', m);
    check(true, 'the clocks are synchronised (pings report their round trip)');
    await a.page.waitForFunction(() => / ms$/.test(document.getElementById('status').textContent), null, { timeout: 5000 });
    check(true, 'the bar shows the delay');

    // ---------- Lanes: a tap holds the fret and strums ----------
    const fa = await Fingers.on(a.page);
    const green = await fa.center('#lanes .lane[data-fret="0"]');
    const red = await fa.center('#lanes .lane[data-fret="1"]');
    m = host.mark();
    await fa.press({ id: 1, x: green.x, y: green.y });
    const press = await host.waitFor((e) => isPad(e) && e.buttons !== 0, 3000, 'the lane press', m);
    check(press.buttons === bits('GREEN', 'DOWN'), 'green lane = green fret + strum, in one packet', press.buttons);
    check(press.len === 14, 'the packet carries the touch time', press.len);
    const lag = press.arrival - press.touch;
    check(press.touch > 0 && lag >= 0 && lag < 100, `the touch is stamped on the host's clock (${lag.toFixed(1)} ms before it arrived)`, press);
    const strumOff = await host.waitFor((e) => isPad(e) && e.buttons === bits('GREEN'), 3000, 'the strum to end', m);
    check(strumOff.touch - press.touch < 300, `the strum ends by itself while the fret stays held (${(strumOff.touch - press.touch).toFixed(0)} ms)`);
    await fa.release(1);
    await host.waitFor((e) => isPad(e) && e.buttons === 0, 3000, 'the lane release', m);
    check(true, 'lifting the finger lets go of the fret');

    // Two lanes at once: a chord, one strum
    await sleep(150);
    m = host.mark();
    await fa.press({ id: 1, x: green.x, y: green.y }, { id: 2, x: red.x, y: red.y });
    await sleep(150);
    await fa.release(1, 2);
    await host.waitFor((e) => isPad(e) && e.buttons === 0, 3000, 'the chord release', m);
    const chord = host.since(m, isPad);
    check(countStrums(chord) === 1, 'two lanes at once = one strum (a chord)', chord.map((e) => e.buttons));
    check(chord.some((e) => (e.buttons & bits('GREEN', 'RED')) === bits('GREEN', 'RED')), 'the chord holds both frets',
      chord.map((e) => e.buttons));
    check(chord.every((e, i) => i === 0 || e.touch >= chord[i - 1].touch), 'packet times never go backwards',
      chord.map((e) => e.touch));

    // Quick taps on one lane: every tap strums
    await sleep(150);
    m = host.mark();
    for (let i = 0; i < 4; i++) {
      await fa.tap('#lanes .lane[data-fret="2"]', 40);
      await sleep(70);
    }
    await sleep(400);
    const taps = host.since(m, isPad);
    check(countStrums(taps) === 4, 'four quick taps = four strums', taps.map((e) => e.buttons));

    // Star Power and Pause
    m = host.mark();
    await fa.tap('#lanes [data-pulse="10"]', 30);
    const sp = await host.waitFor((e) => isPad(e) && (e.buttons & bits('SELECT')), 3000, 'Star Power', m);
    const spOff = await host.waitFor((e) => isPad(e) && e.t > sp.t && !(e.buttons & bits('SELECT')), 3000, 'Star Power to end', m);
    check(spOff.touch - sp.touch >= 60, `Star Power = Select, held about 90 ms (${(spOff.touch - sp.touch).toFixed(0)})`);
    m = host.mark();
    await fa.tap('#lanes [data-pulse="9"]', 30);
    const pause = await host.waitFor((e) => isPad(e) && (e.buttons & bits('START')), 3000, 'Pause', m);
    check(true, 'Pause = Start');
    await host.waitFor((e) => isPad(e) && e.t > pause.t && e.buttons === 0, 3000, 'Pause to end', m);

    // ---------- Frets layout: frets, strum pad, whammy ----------
    await a.page.click('#layoutBtn');
    check(await a.page.isVisible('#frets') && !(await a.page.isVisible('#lanes')), 'the layout switches to frets');
    m = host.mark();
    const fret = await fa.center('#frets .fret[data-fret="0"]');
    await fa.press({ id: 1, x: fret.x, y: fret.y });
    const held = await host.waitFor((e) => isPad(e) && e.buttons !== 0, 3000, 'the fret', m);
    check(held.buttons === bits('GREEN'), 'a fret on its own does not strum', held.buttons);
    const strum = await fa.center('#strum');
    await fa.press({ id: 2, x: strum.x, y: strum.y });
    const down = await host.waitFor((e) => isPad(e) && (e.buttons & bits('DOWN')), 3000, 'the strum pad', m);
    check(down.buttons === bits('GREEN', 'DOWN'), 'tapping the strum pad strums down, with the fret', down.buttons);
    await sleep(80);
    const m2 = host.mark();
    await fa.move(2, strum.x, strum.y - 40);
    await host.waitFor((e) => isPad(e) && (e.buttons & bits('UP')), 3000, 'a strum up', m2);
    check(true, 'swiping up on the strum pad strums up');
    await fa.release(2);
    await fa.release(1);
    const whammy = await fa.center('#whammy');
    m = host.mark();
    await fa.press({ id: 3, x: whammy.x, y: whammy.box.y + whammy.box.height - 2 });
    await fa.move(3, whammy.x, whammy.box.y + whammy.box.height * 0.5);
    await fa.move(3, whammy.x, whammy.box.y + 2);
    await sleep(100);
    const bends = host.since(m, isPad).map((e) => e.whammy);
    check(Math.max(...bends) >= 240, 'the whammy bar goes all the way', bends);
    await fa.release(3);
    await host.waitFor((e) => isPad(e) && e.whammy === 0, 3000, 'the whammy to spring back', m);
    check(true, 'the whammy bar springs back on letting go');
    await a.page.click('#layoutBtn');

    // ---------- Menu tab ----------
    m = host.mark();
    await a.page.click('.tab[data-tab="menu"]');
    const ok = await fa.center('.actions [data-hold="0"]');
    await fa.press({ id: 1, x: ok.x, y: ok.y });
    const okDown = await host.waitFor((e) => isPad(e) && e.buttons !== 0, 3000, 'OK', m);
    check(okDown.buttons === bits('GREEN'), 'menu OK = green, without a strum', okDown.buttons);
    await sleep(300);
    check(host.since(m, (e) => isPad(e) && e.t > okDown.t && e.buttons === 0).length === 0,
      'menu buttons stay pressed while touched');
    await fa.release(1);
    await host.waitFor((e) => isPad(e) && e.buttons === 0 && e.t > okDown.t, 3000, 'OK to be let go', m);
    m = host.mark();
    await fa.tap('.dpad [data-hold="5"]', 50);
    const up = await host.waitFor((e) => isPad(e) && e.buttons !== 0, 3000, 'up', m);
    check(up.buttons === bits('UP'), 'D-pad up', up.buttons);
    m = host.mark();
    await fa.tap('.actions [data-hold="1"]', 50);
    const back = await host.waitFor((e) => isPad(e) && e.buttons !== 0, 3000, 'back', m);
    check(back.buttons === bits('RED'), 'menu Back = red', back.buttons);
    await a.page.click('.tab[data-tab="play"]');

    // ---------- A second phone, on the plain http link ----------
    const b = await newPhone(browser);
    phones.push(b);
    await b.page.goto(plain);
    check((await b.page.textContent('#joinNote')).includes('https://'), 'the http page points to the https link for the mic');
    m = host.mark();
    await join(b.page, '<b>Bob</b>', 'bass');
    const hb = await host.waitFor((e) => e.ev === 'hello', 5000, 'Bob to join', m);
    check(hb.n === 2 && hb.name === 'bBob/b' && hb.secure === false, 'the second phone is player 2 over plain ws, name cleaned', hb);
    await b.page.waitForSelector('#status.ok', { timeout: 5000 });
    check((await b.page.textContent('#status')).includes('bBob/b · player 2'), 'its page shows the cleaned name',
      await b.page.textContent('#status'));
    await b.page.click('.tab[data-tab="mic"]');
    await b.page.click('#micBtn');
    await sleep(300);
    check((await b.page.textContent('#micStatus')).includes('secure link'), 'the mic on http explains it needs the https link',
      await b.page.textContent('#micStatus'));

    // ---------- A singer: the mic streams to the host ----------
    const c = await newPhone(browser);
    phones.push(c);
    await c.page.goto(secure);
    m = host.mark();
    await join(c.page, 'Cara', 'vocals', { challenge: true });
    const hc = await host.waitFor((e) => e.ev === 'hello', 5000, 'Cara to join', m);
    const rc = await host.waitFor((e) => e.ev === 'text' && e.n === hc.n && e.msg.startsWith('role'), 5000, "Cara's role", m);
    check(rc.msg === 'role\nvocals\nmedium\n1', 'the singer asks for vocals in Challenge Mode', rc.msg);
    const micOn = await host.waitFor((e) => e.ev === 'text' && e.n === hc.n && e.msg.startsWith('mic\n'), 8000, 'the mic', m);
    const rate = parseInt(micOn.msg.split('\n')[1], 10);
    check(rate >= 8000, `the mic starts by itself on Join (${rate} Hz)`, micOn.msg);
    await c.page.waitForFunction(() => document.getElementById('micStatus').textContent.includes('Mic is live'),
      null, { timeout: 5000 });
    check(true, 'the page says the mic is live');
    const audio = await host.waitFor((e) => e.ev === 'mic' && e.n === hc.n, 5000, 'audio', m);
    check(audio.type === 3, 'audio packets carry their place in the stream (type 3)', audio.type);
    check(audio.packetSamples === Math.round(rate / 100), `10 ms per packet (${audio.packetSamples} samples)`, audio.packetSamples);
    const heard = await host.waitFor((e) => e.ev === 'mic' && e.n === hc.n && e.totalSamples >= rate * 3, 10000,
      'three seconds of audio', m);
    const pitches = host.since(m, (e) => e.ev === 'mic' && e.n === hc.n && e.totalPackets > 3).map((e) => e.hz)
      .sort((x, y) => x - y);
    const pitch = pitches[Math.floor(pitches.length / 2)];
    check(Math.abs(pitch - 440) < 440 * 0.03, `the host hears the 440 Hz tone (${pitch.toFixed(1)} Hz)`, pitches);
    check(heard.gaps === 0, 'no audio lost on a good connection', heard.gaps);

    // ---------- The host loses a phone: it comes back as the same player ----------
    m = host.mark();
    host.send(`kick ${hc.n}`);
    await host.waitFor((e) => e.ev === 'closed' && e.n === hc.n, 3000, 'the kick', m);
    const again = await host.waitFor((e) => e.ev === 'hello' && e.key === hc.key, 8000, 'Cara to come back', m);
    check(again.n === hc.n, 'the phone reconnects by itself as the same player', again);
    await host.waitFor((e) => e.ev === 'text' && e.msg.startsWith('mic\n') && e.t > again.t, 5000, 'the mic again', m);
    await host.waitFor((e) => e.ev === 'mic' && e.t > again.t, 5000, 'audio again', m);
    check(true, 'the mic carries on after reconnecting');

    // ---------- Joining while a song plays ----------
    host.send('ingame on');
    const d = await newPhone(browser);
    phones.push(d);
    await d.page.goto(secure);
    await join(d.page, 'Dev', 'guitar');
    await d.page.waitForFunction(() => document.getElementById('notice').textContent.includes('song is playing'),
      null, { timeout: 5000 });
    check(true, "joining mid-song: told they're added when the song ends");
    host.send('ingame off');

    // ---------- The page hidden in the middle of a press lets go ----------
    m = host.mark();
    await fa.press({ id: 1, x: green.x, y: green.y });
    await host.waitFor((e) => isPad(e) && (e.buttons & bits('GREEN')), 3000, 'the hold', m);
    await a.page.evaluate(() => {
      Object.defineProperty(document, 'hidden', { configurable: true, get: () => true });
      document.dispatchEvent(new Event('visibilitychange'));
    });
    await host.waitFor((e) => isPad(e) && e.n === 1 && e.buttons === 0, 3000, 'everything to be let go', m);
    check(true, 'hiding the page lets go of every button');
    await fa.release(1);
    await a.page.evaluate(() => { delete document.hidden; });

    // ---------- The page's old connection closing after it opened a new one ----------
    // (It was still closing when the page came back into view.) Its late close must not cut off
    // the new connection; it used to, and every new connection then cut off the one before it.
    const e = await newPhone(browser);
    phones.push(e);
    await e.page.addInitScript(() => {
      const Real = window.WebSocket;
      window.__sockets = [];
      function Tracked(url, protocols) {
        const ws = protocols === undefined ? new Real(url) : new Real(url, protocols);
        window.__sockets.push(ws);
        return ws;
      }
      Tracked.prototype = Real.prototype;
      ['CONNECTING', 'OPEN', 'CLOSING', 'CLOSED'].forEach((k) => { Tracked[k] = Real[k]; });
      window.WebSocket = Tracked;
    });
    await e.page.goto(secure);
    m = host.mark();
    await join(e.page, 'Eve', 'guitar');
    const he = await host.waitFor((ev) => ev.ev === 'hello' && ev.name === 'Eve', 5000, 'Eve to join', m);
    await e.page.waitForSelector('#status.ok', { timeout: 5000 });
    m = host.mark();
    const closedAt = Date.now();
    await e.page.evaluate(() => {
      window.__sockets[window.__sockets.length - 1].close(); // starts closing...
      document.dispatchEvent(new Event('visibilitychange')); // ...the page is back in view first
    });
    await host.waitFor((ev) => ev.ev === 'hello' && ev.key === he.key, 5000, 'Eve to reconnect', m);
    const rejoinMs = Date.now() - closedAt;
    // (Before, the old connection's close threw away the new one before it said hello, and the
    // page only got back in on its 1.5 s retry)
    check(rejoinMs < 1000 && e.errors.length === 0,
      `the new connection joins at once, untouched by the old one's late close (${rejoinMs} ms)`, e.errors);
    await sleep(5000);
    const eveJoins = host.since(m, (ev) => ev.ev === 'hello' && ev.key === he.key).length;
    check(eveJoins === 1, `and stays: no more reconnecting (${eveJoins} join in 5 s)`, eveJoins);
    const fe = await Fingers.on(e.page);
    m = host.mark();
    await fe.tap('#lanes .lane[data-fret="3"]', 40);
    await host.waitFor((ev) => isPad(ev) && ev.n === he.n && (ev.buttons & bits('BLUE')), 3000, "Eve's blue lane", m);
    check(true, 'the controller works on the new connection');

    // ---------- The same phone open in two tabs (e.g. the QR code scanned again) ----------
    const t = await newPhone(browser);
    phones.push(t);
    await t.page.goto(secure);
    await join(t.page, 'Tess', 'guitar');
    const ht = await host.waitFor((ev) => ev.ev === 'hello' && ev.name === 'Tess', 5000, 'Tess to join');
    const tab2 = await t.context.newPage();
    const tab2errors = [];
    tab2.on('pageerror', (err) => tab2errors.push(String(err)));
    await tab2.goto(secure);
    m = host.mark();
    await tab2.fill('#name', 'Tess');
    await tab2.click('#joinBtn');
    await host.waitFor((ev) => ev.ev === 'hello' && ev.key === ht.key, 5000, 'the second tab', m);
    await sleep(6000);
    const tessJoins = host.since(m, (ev) => ev.ev === 'hello' && ev.key === ht.key).length;
    check(tessJoins === 1, `two tabs of one phone don't take the connection from each other (${tessJoins} join in 6 s)`, tessJoins);
    check((await t.page.textContent('#status')).includes('another tab'), 'the first tab says the phone plays from another tab',
      await t.page.textContent('#status'));
    await tab2.waitForSelector('#status.ok', { timeout: 3000 });
    check(true, 'the newest tab keeps the connection');
    m = host.mark();
    await t.page.click('#status');
    await host.waitFor((ev) => ev.ev === 'hello' && ev.key === ht.key, 5000, 'the first tab to take it back', m);
    await t.page.waitForSelector('#status.ok', { timeout: 3000 });
    await sleep(4000);
    const backJoins = host.since(m, (ev) => ev.ev === 'hello' && ev.key === ht.key).length;
    check(backJoins === 1 && (await tab2.textContent('#status')).includes('another tab'),
      `tapping the bar takes the connection back, once (${backJoins})`, backJoins);
    check(tab2errors.length === 0, 'no script errors in the second tab', tab2errors);

    for (const p of phones) {
      check(p.errors.length === 0, 'no script errors on a phone page', p.errors);
    }
  } catch (err) {
    check(false, 'the run stopped: ' + err.message);
  } finally {
    await browser.close();
    host.stop();
    fs.rmSync(work, { recursive: true, force: true });
    const r = results();
    console.log(r.failures ? `${r.failures} OF ${r.checks} CHECKS FAILED` : `ALL ${r.checks} CHECKS PASSED`);
    process.exit(r.failures ? 1 : 0);
  }
})();
