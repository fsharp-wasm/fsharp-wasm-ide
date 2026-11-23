// End-to-end: the widget editor, compiled from F# to WasmGC (dist/WidgetEditor.wasm), runs in a real Chromium.  It draws
// everything on one canvas, so it is driven by pointer and key events at canvas coordinates and judged by what the canvas shows.
//   bash build/build.sh && node tests/e2e/editor.mjs
import { fileURLToPath } from 'node:url';
import { serve, launch, checker } from '../../lib/runtime/tests/e2e/lib.mjs';

const { server, base } = await serve(fileURLToPath(new URL('../..', import.meta.url)));
const { check, failures } = checker();
const { browser, page, errors } = await launch();
// every frame starts with a clearRect: count them (the host draws a frame only when something changed or an animation runs)
await page.addInitScript(() => {
  window.__frames = 0;
  const clear = CanvasRenderingContext2D.prototype.clearRect;
  CanvasRenderingContext2D.prototype.clearRect = function (...a) { window.__frames++; return clear.apply(this, a); };
});
const drawn = async (ms) => { const before = await page.evaluate(() => window.__frames); await page.waitForTimeout(ms); return (await page.evaluate(() => window.__frames)) - before; };

try {
  await page.setViewportSize({ width: 1280, height: 1100 });
  await page.goto(`${base}/web/`);
  await page.waitForFunction(() => /Loaded|✗|Error/.test(document.getElementById('status')?.textContent ?? ''), null, { timeout: 30000 });
  check('the module loads', /Loaded/.test(await page.textContent('#status')), await page.textContent('#status'));
  await page.waitForTimeout(500);

  const box = await page.locator('#gfx').boundingBox();
  const at = (x, y) => [box.x + x, box.y + y];
  const TOP = 52;                               // the height of the bar: its Undo / Redo buttons are enabled by the log, so a state is compared below it
  const look = (from = 0) => page.evaluate((from) => {      // a sample of the pixels from `from` (a y in css pixels) down, and how many colours there are
    const c = document.getElementById('gfx');
    const y0 = Math.round(from * c.height / c.clientHeight);
    const d = c.getContext('2d').getImageData(0, y0, c.width, c.height - y0).data;
    const px = []; const colours = new Set();
    for (let i = 0; i < d.length; i += 4 * 13) { px.push(d[i], d[i + 1], d[i + 2]); colours.add((d[i] << 16) | (d[i + 1] << 8) | d[i + 2]); }
    return { px, colours: colours.size };
  }, from);
  // Two frames of the same scene are the same picture up to the browser's rasterisation: the soft shadows differ by one or two levels of 255 between a frame that
  // has a layer of reduced opacity in it (a disabled button) and one that has not (the draw calls are the same, checked 2026-11-22), so pictures are compared with a tolerance.
  const same = (a, b, tolerance = 3) => a.length === b.length && a.every((v, i) => Math.abs(v - b[i]) <= tolerance);
  const changed = async (name, act) => { const before = (await look()).px; await act(); await page.waitForTimeout(300); check(name, !same((await look()).px, before)); };

  check('the interface is painted', (await look()).colours > 8);
  const idle = await drawn(700);
  check('a page that nobody touches draws no frames', idle === 0, `${idle} frames in 700 ms`);
  // The pointer is parked on the empty surface before every comparison, so that a hover does not count as a change.
  const settled = async () => { await page.mouse.move(...at(600, 700)); await page.waitForTimeout(250); return (await look(TOP)).px; };
  const beforeHover = (await look()).px;
  await page.mouse.move(...at(906, 26)); await page.waitForTimeout(250);
  check('hovering a button redraws it', !same((await look()).px, beforeHover));
  // the cursor: the pointing hand over a button, the open hand over an entry of the palette, the text cursor over a field, the default one over the empty surface
  const cursor = () => page.locator('#gfx').evaluate((c) => c.style.cursor);
  const cursorAt = async (x, y) => { await page.mouse.move(...at(x, y)); await page.waitForTimeout(120); return cursor(); };
  check('the pointing hand over a button', (await cursorAt(906, 26)) === 'pointer');
  check('the open hand over an entry of the palette', (await cursorAt(95, 179)) === 'grab');
  check('the default cursor over the empty surface', (await cursorAt(600, 700)) === 'default');
  check('the text cursor over a text field of the inspector', (await cursorAt(1055, 137)) === 'text');
  // the text field of the inspector has the focus: its caret blinks, so frames keep coming; they stop when the focus is elsewhere
  await page.mouse.click(...at(1055, 137)); await page.waitForTimeout(150);
  const blinking = await drawn(2100);
  check('a text field that has the focus draws a frame at each step of its caret (twice a second), not every 16 ms', blinking >= 3 && blinking <= 8, `${blinking} frames in 2100 ms`);
  await page.mouse.click(...at(95, 78)); await page.waitForTimeout(250);
  const quiet = await drawn(700);
  check('and the frames stop when the focus has left it', quiet === 0, `${quiet} frames in 700 ms`);
  const h0 = await settled();
  await page.mouse.click(...at(95, 179));
  const h1 = await settled();
  check('Label from the palette is added', !same(h1, h0));
  await page.mouse.click(...at(742, 26));
  check('Undo takes it out again, selection and all', same(await settled(), h0));
  await page.mouse.click(...at(813, 26));
  check('Redo brings it back', same(await settled(), h1));

  // An entry of the palette dragged onto the surface: the frame shows where it lands, the drop adds it, one Ctrl+Z takes it away.
  const entry = at(95, 347), over = at(500, 400);
  const drag = async (release) => {
    await page.mouse.move(...entry); await page.mouse.down();
    await page.mouse.move(...over, { steps: 10 });
    const during = (await look(TOP)).px;
    await release();
    return during;
  };
  let closed = '';
  const during = await drag(async () => { closed = await cursor(); await page.mouse.up(); });
  check('the closed hand while an entry is dragged, wherever the pointer is', closed === 'grabbing', closed);
  const h2 = await settled();
  check('a dragged entry shows where it would land', !same(during, h1));
  check('dropped on the surface, the element is added', !same(h2, h1) && !same(h2, during));
  await page.keyboard.press('Control+z');
  check('Ctrl+Z takes the dropped element away', same(await settled(), h1));
  await drag(async () => { await page.keyboard.press('Escape'); await page.mouse.up(); });
  check('Escape cancels a drag: nothing is added', same(await settled(), h1));
  check('the cursor of moving over an element on the surface', (await cursorAt(378, 293)) === 'move');
  await changed('the selected button is dragged', async () => {
    await page.mouse.move(...at(378, 293)); await page.mouse.down();
    await page.mouse.move(...at(520, 450), { steps: 8 }); await page.mouse.up();
  });
  await changed('preview hides the editing chrome', () => page.mouse.click(...at(906, 26)));
  await changed('preview is left again', () => page.mouse.click(...at(906, 26)));
  await changed('Export F# shows the generated code', () => page.mouse.click(...at(1018, 26)));
  await page.mouse.click(...at(1018, 26)); await page.waitForTimeout(200);
  await changed('typing in the inspector edits the element', async () => {
    await page.mouse.click(...at(1055, 137)); await page.waitForTimeout(150);
    for (const ch of 'ok') { await page.keyboard.press(ch); await page.waitForTimeout(40); }
  });
  check('the canvas keeps the focus while typing', await page.evaluate(() => document.activeElement?.id === 'gfx'));
  check('no page errors', errors.length === 0, JSON.stringify(errors).slice(0, 300));
} catch (e) {
  check('ran to the end', false, String(e.message).split('\n')[0]);
}

await browser.close();
server.close();
const failed = failures();
console.log(failed ? `\n${failed} check(s) failed` : '\nall e2e checks passed');
process.exit(failed ? 1 : 0);
