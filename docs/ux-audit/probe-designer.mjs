// What the Newsletter Designer does with a keyboard, a narrow window and a dark scheme, measured in Chromium (docs/ux-audit.md).
//   node docs/ux-audit/probe-designer.mjs [path to a fsharp-wasm-newsletter checkout with dist/NewsletterDesigner.wasm built]
// Writes docs/ux-audit/designer-evidence.json and the screenshots next to it.
import { launch, serve } from '../../lib/runtime/tests/e2e/lib.mjs';
import { mkdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
const here = fileURLToPath(new URL('.', import.meta.url));
const { server, base } = await serve(resolve(process.argv[2] ?? resolve(here, '../../../fsharp-wasm-newsletter')), 0);
const out = here; mkdirSync(out, { recursive: true });
const { browser, page, errors } = await launch();
const R = {};   // the evidence
await page.setViewportSize({ width: 1440, height: 900 });
const t0 = Date.now();
await page.goto(`${base}/web/index.html`);
await page.waitForSelector('header .navbar-brand', { timeout: 20000 });
await page.waitForFunction(() => /Loaded/.test(document.getElementById('status')?.textContent ?? ''));
R.loadMs = Date.now() - t0;
await page.waitForTimeout(800);

// 1. tab stops from the top of the page to the preview (iframe), and how many are in the structure list
const stops = [];
await page.evaluate(() => document.activeElement?.blur());
for (let i = 0; i < 120; i++) {
  await page.keyboard.press('Tab');
  const d = await page.evaluate(() => { const e = document.activeElement; if (!e || e === document.body) return null; const cs = getComputedStyle(e); return { tag: e.tagName, name: (e.getAttribute('aria-label') || e.textContent || '').trim().slice(0, 40), outline: cs.outlineStyle + ' ' + cs.outlineWidth, shadow: cs.boxShadow !== 'none' ? 'shadow' : '', inList: !!e.closest('ol') }; });
  if (!d) break; stops.push(d);
  if (d.tag === 'IFRAME') break;
}
R.tabStopsToPreview = stops.length;
R.tabStopsInStructure = stops.filter((s) => s.inList).length;
R.focusIndicatorMissing = stops.filter((s) => (s.outline.startsWith('none') || s.outline.startsWith('0')) && !s.shadow).map((s) => s.name);

// 2. global shortcuts: what does the page do with Ctrl+K, Ctrl+Shift+P, ?, Ctrl+S, Ctrl+F, Ctrl+Z (after a change), Delete, Ctrl+D, Ctrl+C/V
const snap = async () => page.evaluate(() => ({ dialogs: document.querySelectorAll('dialog[open],[role=dialog],[role=combobox],[role=listbox],.modal.show').length, blocks: document.querySelectorAll('ol[aria-label^="Blocks"] > li').length, url: location.hash }));
await page.click('body', { position: { x: 700, y: 8 } });
const before = await snap(); R.before = before;
const keys = ['Control+k', 'Control+Shift+p', '?', 'Control+s', 'Control+f', 'Control+p', 'F1', '/'];
R.shortcuts = {};
for (const k of keys) { await page.keyboard.press(k); await page.waitForTimeout(150); const s = await snap(); R.shortcuts[k] = s.dialogs - before.dialogs; await page.keyboard.press('Escape'); }
// select a block (click it in the structure) and try block-level keys
await page.locator('ol[aria-label^="Blocks"] > li').nth(2).getByRole('button').nth(1).click();
await page.waitForTimeout(200);
R.afterSelect = await snap();
const blocks = () => page.locator('ol[aria-label^="Blocks"] > li').count();
const n0 = await blocks();
await page.keyboard.press('Control+d'); await page.waitForTimeout(150); R.ctrlD = (await blocks()) - n0;
await page.keyboard.press('Control+c'); await page.keyboard.press('Control+v'); await page.waitForTimeout(150); R.ctrlCV = (await blocks()) - n0 - R.ctrlD;
await page.keyboard.press('Delete'); await page.waitForTimeout(150); R.deleteKey = (await blocks()) - n0 - R.ctrlD - R.ctrlCV;
// undo by keyboard after the delete/duplicate
const nx = await blocks();
await page.keyboard.press('Control+z'); await page.waitForTimeout(150); R.ctrlZ = (await blocks()) - nx;
// arrow keys in the structure list
await page.locator('ol[aria-label^="Blocks"] > li').nth(1).getByRole('button').nth(1).focus();
await page.keyboard.press('ArrowDown'); R.arrowDownMovesFocus = await page.evaluate(() => (document.activeElement.getAttribute('aria-label') || document.activeElement.textContent).trim().slice(0, 30));

// 3. roles that an IDE-like widget set would use
R.roles = await page.evaluate(() => Object.fromEntries(['tree', 'treeitem', 'tablist', 'tab', 'tabpanel', 'toolbar', 'menu', 'menubar', 'menuitem', 'separator', 'dialog', 'combobox', 'listbox', 'status', 'log', 'search'].map((r) => [r, document.querySelectorAll(`[role=${r}]`).length])));
R.splitters = await page.evaluate(() => [...document.querySelectorAll('*')].filter((e) => ['col-resize', 'row-resize', 'ew-resize', 'ns-resize'].includes(getComputedStyle(e).cursor)).length);
R.contextMenu = await (async () => { const li = page.locator('ol[aria-label^="Blocks"] > li').nth(1); let prevented = null; await page.evaluate(() => { window.__cm = null; document.addEventListener('contextmenu', (e) => { window.__cm = e.defaultPrevented; }, { once: true }); }); await li.click({ button: 'right' }); await page.waitForTimeout(150); prevented = await page.evaluate(() => window.__cm); await page.keyboard.press('Escape'); return { defaultPrevented: prevented, menus: await page.evaluate(() => document.querySelectorAll('[role=menu]').length) }; })();

// 4. latency: select another block and wait until the settings panel shows it
const t1 = await page.evaluate(async () => { const b = document.querySelectorAll('ol[aria-label^="Blocks"] > li')[4].querySelectorAll('button')[1]; const t = performance.now(); b.click(); await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r))); return performance.now() - t; });
R.selectToPaintMs = Math.round(t1);

// 5. typing latency in the subject field with the preview iframe re-rendering
await page.getByRole('button', { name: 'Newsletter', exact: true }).click();
const subj = page.getByRole('textbox', { name: 'Subject line' });
await subj.click(); await subj.press('End');
const tt = await page.evaluate(async () => { const el = [...document.querySelectorAll('input')].find((i) => i.value.startsWith('Small software')); const ts = []; for (let i = 0; i < 20; i++) { const t = performance.now(); el.value += 'x'; el.dispatchEvent(new Event('input', { bubbles: true })); await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r))); ts.push(performance.now() - t); } ts.sort((a, b) => a - b); return { median: Math.round(ts[10]), max: Math.round(ts[19]) }; });
R.typingMs = tt;

// 6. responsive: overflow and layout at 1024 / 768 / 390
R.responsive = {};
for (const w of [1280, 1024, 768, 390]) {
  await page.setViewportSize({ width: w, height: 800 }); await page.waitForTimeout(300);
  R.responsive[w] = await page.evaluate(() => ({ hScroll: document.documentElement.scrollWidth > innerWidth + 1, scrollW: document.documentElement.scrollWidth }));
  await page.screenshot({ path: out + `designer-${w}.png` });
}
// 7. dark mode
await page.setViewportSize({ width: 1440, height: 900 });
await page.emulateMedia({ colorScheme: 'dark' }); await page.waitForTimeout(300);
R.darkBg = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
await page.screenshot({ path: out + 'designer-dark.png' });
await page.emulateMedia({ colorScheme: 'light' });
// 8. zoom 200%: reflow
await page.setViewportSize({ width: 720, height: 450 }); await page.waitForTimeout(300);
R.zoom200 = await page.evaluate(() => ({ hScroll: document.documentElement.scrollWidth > innerWidth + 1 }));
await page.screenshot({ path: out + 'designer-zoom200.png' });
R.errors = errors;
console.log(JSON.stringify(R, null, 1));
writeFileSync(out + 'designer-evidence.json', JSON.stringify(R, null, 1));
await browser.close(); server.close();
