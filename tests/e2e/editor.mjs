// End-to-end: the widget editor, compiled from F# to WasmGC (dist/WidgetEditor.wasm), runs in a real Chromium.  It draws
// everything on one canvas, so it is driven by pointer and key events at canvas coordinates and judged by what the canvas shows.
//   bash build/build.sh && node tests/e2e/editor.mjs
import { fileURLToPath } from 'node:url';
import { serve, launch, checker } from '../../lib/runtime/tests/e2e/lib.mjs';

const { server, base } = await serve(fileURLToPath(new URL('../..', import.meta.url)));
const { check, failures } = checker();
const { browser, page, errors } = await launch();

try {
  await page.setViewportSize({ width: 1280, height: 1100 });
  await page.goto(`${base}/web/`);
  await page.waitForFunction(() => /Loaded|✗|Error/.test(document.getElementById('status')?.textContent ?? ''), null, { timeout: 30000 });
  check('the module loads', /Loaded/.test(await page.textContent('#status')), await page.textContent('#status'));
  await page.waitForTimeout(500);

  const box = await page.locator('#gfx').boundingBox();
  const at = (x, y) => [box.x + x, box.y + y];
  const TOP = 52;                               // the height of the bar: its Undo / Redo buttons are enabled by the log, so a state is compared below it
  const look = (from = 0) => page.evaluate((from) => {      // a hash of the pixels from `from` (a y in css pixels) down, and how many colours there are
    const c = document.getElementById('gfx');
    const y0 = Math.round(from * c.height / c.clientHeight);
    const d = c.getContext('2d').getImageData(0, y0, c.width, c.height - y0).data;
    let h = 0; const colours = new Set();
    for (let i = 0; i < d.length; i += 4 * 13) { h = (h * 31 + d[i] + d[i + 1] * 3 + d[i + 2] * 7) | 0; colours.add((d[i] << 16) | (d[i + 1] << 8) | d[i + 2]); }
    return { h, colours: colours.size };
  }, from);
  const changed = async (name, act) => { const before = (await look()).h; await act(); await page.waitForTimeout(300); check(name, (await look()).h !== before); };

  check('the interface is painted', (await look()).colours > 8);
  // The pointer is parked on the empty surface before every comparison, so that a hover does not count as a change.
  const settled = async () => { await page.mouse.move(...at(600, 700)); await page.waitForTimeout(250); return (await look(TOP)).h; };
  const h0 = await settled();
  await page.mouse.click(...at(95, 179));
  const h1 = await settled();
  check('Label from the palette is added', h1 !== h0);
  await page.mouse.click(...at(742, 26));
  check('Undo takes it out again, selection and all', (await settled()) === h0);
  await page.mouse.click(...at(813, 26));
  check('Redo brings it back', (await settled()) === h1);

  // An entry of the palette dragged onto the surface: the frame shows where it lands, the drop adds it, one Ctrl+Z takes it away.
  const entry = at(95, 347), over = at(500, 400);
  const drag = async (release) => {
    await page.mouse.move(...entry); await page.mouse.down();
    await page.mouse.move(...over, { steps: 10 });
    const during = (await look(TOP)).h;
    await release();
    return during;
  };
  const during = await drag(() => page.mouse.up());
  const h2 = await settled();
  check('a dragged entry shows where it would land', during !== h1);
  check('dropped on the surface, the element is added', h2 !== h1 && h2 !== during);
  await page.keyboard.press('Control+z');
  check('Ctrl+Z takes the dropped element away', (await settled()) === h1);
  await drag(async () => { await page.keyboard.press('Escape'); await page.mouse.up(); });
  check('Escape cancels a drag: nothing is added', (await settled()) === h1);
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
