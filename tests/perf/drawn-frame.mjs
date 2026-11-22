// What a frame that is drawn costs in the widget editor, and which part of it the Canvas 2D calls are, measured in a real Chromium.
//   bash build/build.sh && node tests/perf/drawn-frame.mjs
// The two header buttons are hovered alternately, so every pointer move changes a hover state and draws a frame.
//   A  the callback of every animation frame is timed in the page, the context's methods and setters real
//   B  the same with the methods and setters replaced by empty functions (measureText stays real): what is left is the scene, the crossing from the module and the glue
//   C  a CPU profile of the same loop, self time by where the code lives (the profiler slows the code: read shares, not milliseconds)
// Numbers vary by some percent between runs and a lot between machines; compare before and after on one machine (docs/performance.md of fsharp-wasm-graphics has the measurements of 2026-11-22).
import { fileURLToPath } from 'node:url';
import { serve, launch } from '../../lib/runtime/tests/e2e/lib.mjs';

const root = fileURLToPath(new URL('../..', import.meta.url));
const median = (xs) => [...xs].sort((a, b) => a - b)[xs.length >> 1];
const MOVES = 500;

/** The editor loaded, some elements on its surface, the page's hooks installed: `emptyCalls` replaces the context's methods, `profile` takes a CPU profile of the loop. */
async function session({ emptyCalls = false, profile = false }) {
  const { server, base } = await serve(root);
  const { browser, page } = await launch();
  try {
    await page.addInitScript((emptyCalls) => {
      window.__frames = [];
      let draws = 0;
      const proto = CanvasRenderingContext2D.prototype;
      if (emptyCalls) {
        for (const k of Object.getOwnPropertyNames(proto)) {
          const d = Object.getOwnPropertyDescriptor(proto, k);
          if (k === 'constructor' || k === 'measureText' || k === 'clearRect') continue;
          if (typeof d.value === 'function') proto[k] = k.startsWith('create') ? d.value : function () {};
          else if (d.set) Object.defineProperty(proto, k, { ...d, set() {} });
        }
      }
      const clear = proto.clearRect;
      proto.clearRect = function (...a) { draws++; return clear.apply(this, a); };         // a frame that draws starts with a clearRect
      const next = window.requestAnimationFrame.bind(window);
      window.requestAnimationFrame = (cb) => next((t) => { draws = 0; const t0 = performance.now(); cb(t); window.__frames.push({ ms: performance.now() - t0, draws }); });
    }, emptyCalls);
    await page.setViewportSize({ width: 1280, height: 1100 });
    await page.goto(`${base}/web/`);
    await page.waitForFunction(() => /Loaded|✗|Error/.test(document.getElementById('status')?.textContent ?? ''), null, { timeout: 30000 });
    await page.waitForTimeout(800);
    const box = await page.locator('#gfx').boundingBox();
    const at = (x, y) => [box.x + x, box.y + y];
    for (let i = 0; i < 4; i++) await page.mouse.click(...at(95, 179));                   // some elements on the surface first
    await page.waitForTimeout(300);
    await page.evaluate(() => { window.__frames.length = 0; });
    const cdp = profile ? await page.context().newCDPSession(page) : null;
    if (cdp) { await cdp.send('Profiler.enable'); await cdp.send('Profiler.setSamplingInterval', { interval: 50 }); await cdp.send('Profiler.start'); }
    for (let i = 0; i < MOVES; i++) { await page.mouse.move(...at(i % 2 ? 906 : 1018, 26)); await page.waitForTimeout(12); }
    const frames = (await page.evaluate(() => window.__frames)).filter((f) => f.draws > 0).map((f) => f.ms);
    return { frames, profile: cdp ? (await cdp.send('Profiler.stop')).profile : null };
  } finally { await browser.close(); server.close(); }
}

for (let run = 0; run < 2; run++) {
  for (const emptyCalls of [false, true]) {
    const { frames } = await session({ emptyCalls });
    console.log(`${emptyCalls ? 'B  Canvas 2D calls empty' : 'A  Canvas 2D calls real '}: ${frames.length} drawn frames, the callback takes ${median(frames).toFixed(2)} ms (median)`);
  }
}

const { frames, profile } = await session({ profile: true });
const byId = new Map(profile.nodes.map((n) => [n.id, n]));
const where = new Map();
let busy = 0;
profile.samples.forEach((id, i) => {
  const dt = profile.timeDeltas[i + 1] ?? profile.timeDeltas[i];
  const f = byId.get(id).callFrame;
  const name = f.functionName || '(anonymous)';
  const c = name === '(idle)' ? 'idle' : name === '(garbage collector)' ? 'garbage collector' : name === '(program)' ? 'the browser itself (raster, paint, compositing)'
    : /gfx\.js/.test(f.url) ? 'JavaScript glue' : /\.wasm$/.test(f.url) ? 'F# (the module)' : 'other JavaScript';
  where.set(c, (where.get(c) ?? 0) + dt);
  if (c !== 'idle') busy += dt;
});
console.log(`C  profile of ${frames.length} drawn frames: busy ${(busy / frames.length / 1000).toFixed(2)} ms per frame (slowed by the profiler)`);
for (const [c, t] of [...where].filter(([c]) => c !== 'idle').sort((a, b) => b[1] - a[1])) console.log(`   ${(t / busy * 100).toFixed(0).padStart(3)} %  ${c}`);
