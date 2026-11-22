// End-to-end: a frame that redraws only what changed comes out as a frame drawn whole.  The widget editor runs twice in one page (tests/e2e/redraw-host.html): in `a` the canvas host
// compares each frame with the one before and redraws the region that changed; in `b` the page says `?fullframes` and every frame is drawn whole.  The same pointer and key events go to
// both, one after the other, and after each step the two canvases are compared pixel by pixel.  The Canvas 2D calls of both are counted: what the region saves.
//   bash build/build.sh && node tests/e2e/redraw.mjs
import { fileURLToPath } from 'node:url';
import { serve, launch, checker } from '../../lib/runtime/tests/e2e/lib.mjs';

const { server, base } = await serve(fileURLToPath(new URL('../..', import.meta.url)));
const { check, failures } = checker();
const { browser, page, errors } = await launch();
const TOLERANCE = 3;        // the soft shadows differ by up to 2 of 255 between frames that have a layer of reduced opacity in them and ones that have not (docs/performance.md of fsharp-wasm-graphics)

try {
  await page.setViewportSize({ width: 2640, height: 1150 });
  await page.addInitScript(() => {
    window.__calls = { a: 0, b: 0 };                                          // every method of the context, per frame (the page that owns the canvas says which)
    const proto = CanvasRenderingContext2D.prototype;
    for (const k of Object.getOwnPropertyNames(proto)) {
      const d = Object.getOwnPropertyDescriptor(proto, k);
      if (typeof d.value !== 'function' || k === 'constructor' || k === 'getImageData') continue;
      const f = d.value;
      proto[k] = function (...args) { const who = this.canvas.ownerDocument.defaultView.location.search ? 'b' : 'a'; (window.top.__calls ??= { a: 0, b: 0 })[who]++; return f.apply(this, args); };
    }
  });
  await page.goto(`${base}/tests/e2e/redraw-host.html`);
  const frameOf = { a: page.frame({ url: /web\/$/ }), b: page.frame({ url: /fullframes/ }) };
  for (const f of Object.values(frameOf)) await f.waitForFunction(() => /Loaded|✗|Error/.test(document.getElementById('status')?.textContent ?? ''), null, { timeout: 30000 });
  await page.waitForTimeout(800);
  const origin = {};
  for (const k of ['a', 'b']) { const b = await page.locator(`#${k}`).boundingBox(); const c = await frameOf[k].locator('#gfx').boundingBox(); origin[k] = [c.x, c.y]; }
  const both = async (act) => { for (const k of ['a', 'b']) { const [ox, oy] = origin[k]; await act((x, y) => [ox + x, oy + y], k); } };
  const settle = () => page.waitForTimeout(350);
  const compare = async (name) => {
    await settle();
    const r = await page.evaluate((t) => window.compare(t), TOLERANCE);
    check(name, r.sizes && r.beyond === 0, r.sizes ? `${r.beyond} pixels differ by more than ${TOLERANCE} (largest ${r.largest}, first at ${JSON.stringify(r.first)})` : 'the canvases are not the same size');
    return r;
  };
  const calls = () => page.evaluate(() => ({ ...window.__calls }));
  // one mouse cannot hold a gesture in two editors at once: the middle of it is a picture taken in each, one after the other, and compared
  const midGesture = async (name, begin, end) => {
    for (const k of ['a', 'b']) {
      const [ox, oy] = origin[k];
      const at = (x, y) => [ox + x, oy + y];
      await begin(at);
      await settle();
      await page.evaluate((id) => window.snap(id), k);
      await end(at);
    }
    const r = await page.evaluate((t) => window.compareSnaps(t), TOLERANCE);
    check(name, r.sizes && r.beyond === 0, r.sizes ? `${r.beyond} pixels differ by more than ${TOLERANCE} (largest ${r.largest}, first at ${JSON.stringify(r.first)})` : 'the canvases are not the same size');
  };

  await compare('both draw the same first frame');

  // hover: a button, a palette entry, the surface, a button again (a hover is a few commands)
  const before = await calls();
  for (const [x, y] of [[906, 26], [1018, 26], [95, 120], [95, 190], [500, 400], [742, 26], [906, 26], [600, 700]]) {
    await both(async (at) => { await page.mouse.move(...at(x, y)); });
    await compare(`after the pointer moved to ${x},${y}`);
  }
  const hovered = await calls();
  const aCalls = hovered.a - before.a, bCalls = hovered.b - before.b;
  check('a hover draws a fraction of what a frame drawn whole does (Canvas 2D calls)', aCalls < 0.5 * bCalls, `${aCalls} against ${bCalls}`);

  // click a palette entry (an element is added, selected, the inspector fills), undo, redo
  await both(async (at) => { await page.mouse.click(...at(95, 179)); });
  await compare('after an element was added from the palette');
  await both(async (at) => { await page.mouse.click(...at(742, 26)); });
  await compare('after Undo');
  await both(async (at) => { await page.mouse.click(...at(813, 26)); });
  await compare('after Redo');

  // a drag from the palette onto the surface: in the middle of it, further along, and after the drop
  await midGesture('in the middle of a drag from the palette',
    async (at) => { await page.mouse.move(...at(95, 347)); await page.mouse.down(); await page.mouse.move(...at(500, 400), { steps: 8 }); },
    async () => { await page.mouse.up(); });
  await compare('after that drag was dropped');
  await midGesture('further along in a drag',
    async (at) => { await page.mouse.move(...at(95, 347)); await page.mouse.down(); await page.mouse.move(...at(500, 400), { steps: 8 }); await page.mouse.move(...at(300, 300), { steps: 4 }); },
    async () => { await page.keyboard.press('Escape'); await page.mouse.up(); });
  await compare('after a drag was cancelled');

  // the selected element dragged across the surface
  await both(async (at) => { await page.mouse.move(...at(378, 293)); await page.mouse.down(); await page.mouse.move(...at(520, 450), { steps: 8 }); await page.mouse.up(); });
  await compare('after the selected element was dragged');

  // text in the inspector: the caret blinks (frames at each step), a letter changes the label
  // (the caret of a field that has the focus blinks on the clock of its own page, so the two pictures are compared with the focus away from the field)
  await both(async (at) => { await page.mouse.click(...at(1055, 137)); for (const ch of 'ok') { await page.keyboard.press(ch); await page.waitForTimeout(40); } await page.mouse.click(...at(600, 700)); });
  await compare('after typing into the inspector and leaving the field');

  // preview and the code panel (the whole surface changes: more than half the canvas, a frame drawn whole)
  await both(async (at) => { await page.mouse.click(...at(906, 26)); });
  await compare('in preview');
  await both(async (at) => { await page.mouse.click(...at(906, 26)); });
  await compare('out of preview');
  await both(async (at) => { await page.mouse.click(...at(1018, 26)); });
  await compare('with the code panel open');
  await both(async (at) => { await page.mouse.click(...at(1018, 26)); });
  await compare('with the code panel closed');

  // a seeded walk: moves, presses and releases over the whole canvas, compared after every ten
  let seed = 20261122;
  const rnd = () => (seed = (seed * 1664525 + 1013904223) % 4294967296) / 4294967296;
  for (let round = 0; round < 6; round++) {
    for (let i = 0; i < 10; i++) {
      const x = Math.round(20 + rnd() * 1150), y = Math.round(20 + rnd() * 680), kind = rnd();
      await both(async (at) => {
        await page.mouse.move(...at(x, y), { steps: 2 });
        if (kind > 0.8) await page.mouse.click(...at(x, y));
        if (kind > 0.93) await page.keyboard.press('Control+z');
      });
    }
    await compare(`after ten random moves and clicks (round ${round + 1})`);
  }

  // a canvas of another size: the next frame is drawn whole, whatever the frame before was
  await page.evaluate(() => { for (const id of ['a', 'b']) document.getElementById(id).style.width = '1100px'; });
  await settle();
  await compare('after both canvases were resized');
  check('no page errors', errors.length === 0, JSON.stringify(errors).slice(0, 300));
} catch (e) {
  check('ran to the end', false, String(e.message).split('\n')[0]);
}

await browser.close();
server.close();
const failed = failures();
console.log(failed ? `\n${failed} check(s) failed` : '\nall e2e checks passed');
process.exit(failed ? 1 : 0);
