// What the widget editor does with the keyboard, a second widget, an empty spot, a right click, measured in Chromium (docs/ux-audit.md).
// A change of the picture is judged by a hash of the canvas, so "changed" says that something was drawn differently, not what; the screenshots say what.
//   bash build/build.sh && node docs/ux-audit/probe-editor.mjs
// Writes docs/ux-audit/editor-evidence.json and the screenshots next to it.
import { launch, serve } from '../../lib/runtime/tests/e2e/lib.mjs';
import { createHash } from 'node:crypto';
import { mkdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
const here = fileURLToPath(new URL('.', import.meta.url));
const { server, base } = await serve(resolve(here, '../..'), 0);
const out = here; mkdirSync(out, { recursive: true });
const { browser, page, errors } = await launch();
await page.setViewportSize({ width: 1440, height: 900 });
const t0 = Date.now();
await page.goto(`${base}/web/`);
await page.waitForFunction(() => document.querySelector('canvas#gfx'), null, { timeout: 20000 });
const R0 = {};
const cv = page.locator('canvas#gfx');
await page.waitForFunction(() => { const c = document.querySelector('canvas#gfx'); const d = c.getContext('2d')?.getImageData(c.width / 2, c.height / 2, 1, 1).data; return d && d[0] + d[1] + d[2] > 0; }, null, { timeout: 20000, polling: 20 });
R0.firstFrameMs = Date.now() - t0;
R0.canvasFitsViewport = await page.evaluate(() => { const c = document.querySelector('canvas#gfx').getBoundingClientRect(); return { top: Math.round(c.top), height: Math.round(c.height), viewport: innerHeight, fits: c.bottom <= innerHeight }; });
await page.setViewportSize({ width: 1440, height: 1000 });        // room for the whole canvas, for the rest
await cv.scrollIntoViewIfNeeded();
const box = await cv.boundingBox();
const R = { ...R0, canvasBox: box };
const at = (x, y) => [box.x + x, box.y + y];            // canvas-relative CSS px
const look = async () => createHash('md5').update(await page.screenshot({ clip: box })).digest('hex').slice(0, 8);
const shot = (n) => page.screenshot({ path: out + n, clip: box });
await page.waitForTimeout(300);
await shot('editor-canvas.png');
const h0 = await look();
const step = async (name, f) => { const a = await look(); await f(); await page.waitForTimeout(200); const b = await look(); R[name] = a !== b ? 'changed' : 'no change'; };

// the layout in canvas pixels (from the screenshot at 1200 wide): palette x<175, surface 175..915, inspector >915
await step('click widget selects', async () => { await page.mouse.click(...at(300, 245)); });          // the Button "Sign in" area (surface)
await shot('editor-selected.png');
await step('arrow right moves/changes', async () => { await page.keyboard.press('ArrowRight'); });
await step('shift+arrow', async () => { await page.keyboard.press('Shift+ArrowRight'); });
await step('ctrl+z undoes', async () => { await page.keyboard.press('Control+z'); });
await step('ctrl+shift+z / ctrl+y redoes', async () => { await page.keyboard.press('Control+y'); });
await step('ctrl+d duplicates', async () => { await page.keyboard.press('Control+d'); });
await step('escape deselects', async () => { await page.keyboard.press('Escape'); });
await step('click selects again', async () => { await page.mouse.click(...at(300, 245)); });
await step('delete removes', async () => { await page.keyboard.press('Delete'); });
await step('tab (moves the toolkit focus)', async () => { await page.keyboard.press('Tab'); });
await step('ctrl+k opens something', async () => { await page.keyboard.press('Control+k'); });
await page.keyboard.press('Escape');
await step('ctrl+shift+p opens something', async () => { await page.keyboard.press('Control+Shift+p'); });
await page.keyboard.press('Escape');
await step('right-click on a widget', async () => { await page.mouse.click(...at(300, 245), { button: 'right' }); });
await shot('editor-rightclick.png');
await page.keyboard.press('Escape');
await step('double-click label (inline edit?)', async () => { await page.mouse.dblclick(...at(240, 150)); });
await shot('editor-dblclick.png');
await page.keyboard.press('Escape');
await step('shift+click a second widget', async () => { await page.mouse.click(...at(300, 245)); await page.keyboard.down('Shift'); await page.mouse.click(...at(260, 195)); await page.keyboard.up('Shift'); });
await shot('editor-shift-click.png');
await step('marquee drag on empty surface', async () => { await page.mouse.move(...at(250, 330)); await page.mouse.down(); await page.mouse.move(...at(520, 90), { steps: 6 }); await shot('editor-marquee.png'); await page.mouse.up(); });
await step('wheel on the surface (zoom/pan)', async () => { await page.mouse.move(...at(500, 300)); await page.mouse.wheel(0, -300); });
await step('ctrl+wheel (zoom)', async () => { await page.keyboard.down('Control'); await page.mouse.wheel(0, -300); await page.keyboard.up('Control'); });
await step('toolbar: Preview toggle', async () => { await page.mouse.click(...at(900, 20)); });
await shot('editor-preview.png');
await step('toolbar: back', async () => { await page.mouse.click(...at(900, 20)); });
// does the layout survive a reload?  A widget is added from the palette, the selection is cleared, the surface is compared before and after
const surface = { x: box.x + 190, y: box.y + 52, width: 740, height: box.height - 52 };
const surfaceHash = async () => createHash('md5').update(await page.screenshot({ clip: surface })).digest('hex').slice(0, 8);
await page.mouse.click(...at(95, 515));                       // Divider in the palette
await page.mouse.click(...at(700, 600));                      // an empty spot: nothing selected
await page.waitForTimeout(300);
const beforeReload = await surfaceHash();
await page.reload();
await page.waitForFunction(() => document.querySelector('canvas#gfx'), null, { timeout: 20000 });
await page.waitForTimeout(1200);
R.layoutSurvivesReload = beforeReload === (await surfaceHash());
// canvas tab stop and a11y
R.a11yTree = await page.locator('body').ariaSnapshot().then((s) => s.split('\n').filter((l) => /canvas|application|button|slider|textbox|checkbox|group|region/.test(l)).slice(0, 12));
R.liveRegions = await page.evaluate(() => document.querySelectorAll('[aria-live],[role=status],[role=alert]').length);
R.hiddenSemanticNodes = await page.evaluate(() => document.querySelectorAll('canvas ~ [role], [data-a11y], .sr-only').length);
R.errors = errors;
console.log(JSON.stringify(R, null, 1));
writeFileSync(out + 'editor-evidence.json', JSON.stringify(R, null, 1));
await browser.close(); server.close();
