// What a frame of the widget editor costs, measured in a real Chromium: how many Canvas 2D calls a frame makes, how busy an untouched page is, and where the time goes while a pointer moves.
//   bash build/build.sh && node tests/perf/frames.mjs
// Numbers vary by some percent between runs and a lot between machines; compare before and after on one machine (docs/performance.md of fsharp-wasm-graphics has the measurements of 2026-11-22).
import { fileURLToPath } from 'node:url';
import { serve, launch } from '../../lib/runtime/tests/e2e/lib.mjs';

const { server, base } = await serve(fileURLToPath(new URL('../..', import.meta.url)));
const { browser, page } = await launch();
const median = (xs) => [...xs].sort((a, b) => a - b)[xs.length >> 1];

// every method and setter of the 2D context, counted; a frame starts with a clearRect
await page.addInitScript(() => {
  window.__calls = {};
  const proto = CanvasRenderingContext2D.prototype;
  for (const k of Object.getOwnPropertyNames(proto)) {
    const d = Object.getOwnPropertyDescriptor(proto, k);
    if (typeof d.value === 'function' && k !== 'constructor') { const f = d.value; proto[k] = function (...a) { window.__calls[k] = (window.__calls[k] ?? 0) + 1; return f.apply(this, a); }; }
    else if (d.set) { const s = d.set; Object.defineProperty(proto, k, { ...d, set(v) { window.__calls['set ' + k] = (window.__calls['set ' + k] ?? 0) + 1; return s.call(this, v); } }); }
  }
});
const calls = () => page.evaluate(() => ({ ...window.__calls }));
const delta = (a, b) => Object.fromEntries(Object.keys(b).map((k) => [k, b[k] - (a[k] ?? 0)]).filter(([, v]) => v));

try {
  await page.setViewportSize({ width: 1280, height: 1100 });
  await page.goto(`${base}/web/`);
  await page.waitForFunction(() => /Loaded|✗|Error/.test(document.getElementById('status')?.textContent ?? ''), null, { timeout: 30000 });
  await page.waitForTimeout(500);
  const box = await page.locator('#gfx').boundingBox();
  const at = (x, y) => [box.x + x, box.y + y];
  for (let i = 0; i < 4; i++) await page.mouse.click(...at(95, 179));            // some elements on the surface
  await page.waitForTimeout(300);

  // 1. calls per frame: one click adds an element and draws a frame
  const a = await calls(); await page.mouse.click(...at(95, 179)); await page.waitForTimeout(300); const d = delta(a, await calls());
  const frames = d.clearRect ?? 1;
  const top = Object.entries(d).sort((x, y) => y[1] - x[1]).slice(0, 8).map(([k, v]) => `${k} ${(v / frames).toFixed(0)}`).join(', ');
  console.log(`1. a frame: ${(Object.values(d).reduce((x, y) => x + y, 0) / frames).toFixed(0)} Canvas 2D calls (${top})`);

  const cdp = await page.context().newCDPSession(page);
  await cdp.send('Profiler.enable');
  const profile = async (work, intervalUs = 200) => {
    await cdp.send('Profiler.setSamplingInterval', { interval: intervalUs });
    await cdp.send('Profiler.start'); await work(); const { profile } = await cdp.send('Profiler.stop');
    const byId = new Map(profile.nodes.map((n) => [n.id, n])); const self = new Map(); let total = 0;
    profile.samples.forEach((id, i) => { const dt = profile.timeDeltas[i + 1] ?? profile.timeDeltas[i]; const name = byId.get(id).callFrame.functionName || '(anonymous)'; self.set(name, (self.get(name) ?? 0) + dt); total += dt; });
    return { self, total };
  };

  // 2. an untouched page for 3 s
  const idle = await profile(() => page.waitForTimeout(3000));
  const busy = idle.total - (idle.self.get('(idle)') ?? 0);
  console.log(`2. nobody touches the page for 3 s: busy ${(busy / idle.total * 100).toFixed(1)} % (${(busy / 1000).toFixed(0)} ms of ${(idle.total / 1000).toFixed(0)} ms)`);

  // 3. 720 pointer moves (hover along the surface, a drag from the palette): where does the time go
  const moves = 720;
  const driven = await profile(async () => {
    for (let round = 0; round < 6; round++) {
      for (let i = 0; i < 80; i++) await page.mouse.move(...at(120 + i * 8, 250 + (i % 7) * 40));
      await page.mouse.move(...at(95, 347)); await page.mouse.down();
      for (let i = 0; i < 40; i++) await page.mouse.move(...at(95 + i * 10, 347 + i * 3));
      await page.mouse.up();
    }
  }, 100);
  let wasm = 0, host = 0, gc = 0, program = 0; const fromWasm = /^[A-Z][A-Za-z0-9]*_/;       // the names of the module's functions are Module_function
  for (const [name, t] of driven.self) { if (name === '(idle)') continue; if (name === '(program)') program += t; else if (name === '(garbage collector)') gc += t; else if (fromWasm.test(name)) wasm += t; else host += t; }
  const per = (t) => `${(t / 1000 / moves * 1000).toFixed(0)} µs`;
  console.log(`3. a pointer move (${moves} of them): F# code ${per(wasm)}, JavaScript glue and Canvas 2D calls ${per(host)}, garbage collection ${per(gc)}, the browser's own work ${per(program)}`);
  const builders = [...driven.self].filter(([n]) => n.startsWith('Builders_')).reduce((s, [, t]) => s + t, 0);
  console.log(`   of the F# code, the builders (Builders_*): ${(builders / 1000).toFixed(1)} ms in all`);
} finally { await browser.close(); server.close(); }
