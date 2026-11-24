# UX audit: the Newsletter Designer and the Widget Editor, against a professional IDE base

Written 2026-11-24. Two tools, one question: is what they are built on thick enough to be the base of a professional IDE? The short answer is no, and the reason is the same in both:
**each tool has the editing it needs for its own task, and none of the workbench around it** (commands, keymap, panels, focus, selection, status). The tools are good at what they
do; the base they share does not exist yet, so every tool would have to write it again, once for the DOM and once for the canvas.

## What was measured, and how

| | |
|---|---|
| Designer | [fsharp-wasm-newsletter](https://github.com/fsharp-wasm/fsharp-wasm-newsletter) `develop` at 6015282, `dist/NewsletterDesigner.wasm` built 2026-11-23 (464 KB) |
| Editor | this repository `develop` at bdc7e3f, `dist/WidgetEditor.wasm` built 2026-11-23 (269 KB) |
| Where | headless Chromium (Playwright) in a cloud container without a GPU, 1440 × 900 unless said otherwise, local server, nothing throttled |
| By | `docs/ux-audit/probe-designer.mjs` and `probe-editor.mjs` (rerun them; they write `*-evidence.json` and the screenshots next to them) |
| Not measured | a real screen reader, a real GPU, a touch device, a person using it for an hour. Numbers of one container on one day are a baseline for comparing later, not a benchmark. |

The editor draws one `<canvas>`, so its behaviour is judged by what the canvas shows (a hash of its pixels after each action) and by the screenshots; "changed" says that something was
drawn differently, the images say what. Where a statement comes from reading the source and not from running it, it says so.

## What is already good (and must not be lost)

- **Designer**: a three-pane layout that reads at once (palette and structure, preview, settings); icons on every control, named for assistive technology; drag by a handle with a drop line and
  an auto-scrolling page, with buttons as the keyboard alternative (WCAG 2.2 2.5.7); editing text in place; undo per project with typing as one step; a library of projects that survives
  a reload; checks with jump-to-block; a sandboxed preview; a live region that announces changes; axe-core finds no violation on three panels (its own test). Selecting a block paints in 27 ms,
  a keystroke in the subject field reaches the regenerated preview in 54 to 63 ms (median of 20, two runs, 2026-11-24), the module is up in 0.3 to 0.5 s.
- **Editor**: a model that is a pure function (`Model.update`), `History` from the core's `Edit` (typing is one step), a grid that snaps to 8 px, a drag that shows the widget at the place where
  it lands, a design/preview switch, an export to F#, a canvas that keeps the keyboard focus, one tab stop, Delete, arrows (8 px), Escape, Undo and Redo on the keyboard. First frame 0.5 to 0.9 s.

## Findings

Severity: **S1** a professional user stops there; **S2** slows every session; **S3** polish. "D" is the designer, "E" the editor.

### 1. Commands and discoverability

| | | Evidence |
|---|---|---|
| **S1** | No command palette, no shortcut list, no menu bar, no context menu, in either | D: `Ctrl+K`, `Ctrl+Shift+P`, `?`, `F1`, `/`, `Ctrl+F`, `Ctrl+S`, `Ctrl+P` open nothing (0 dialogs); a right click is not intercepted (no `[role=menu]`). E: the same keys change nothing; a right click on a widget only drops the selection (`editor-rightclick.png`). |
| **S1** | A command exists once per place that offers it, and its shortcut is shown nowhere | The designer's Duplicate and Delete are buttons (6 per block row); the editor's are inspector buttons; no tooltip or label names a key. Nothing says that the editor's arrows nudge except the empty-inspector hint. |
| S2 | The designer has no keyboard shortcut for its own actions | `Ctrl+Z`, `Ctrl+D`, `Ctrl+C`/`Ctrl+V`, `Delete` with a block selected: the block count does not change (10 → 10 in each case). Undo and Redo are buttons only (the canvas host adds `Ctrl+Z`/`Ctrl+Y` for the editor, which is why the editor has them). |
| S2 | Commands are not data | Actions are closures in the view or `Msg` cases of one app. Nothing can list them, rebind them, replay them, or drive the tool from a script or an agent. |

### 2. Keyboard and focus

| | | Evidence |
|---|---|---|
| **S1** | **D**: 84 tab stops from the top of the page to the preview, **58 of them in the structure list** (10 blocks × 6 buttons − 2 disabled); the arrow keys do nothing in the list | `tabStopsInStructure: 58`; `ArrowDown` on a block leaves focus where it was. A tree or list in an IDE is one tab stop with arrows inside (WAI-ARIA Authoring Practices: roving tabindex). A keyboard user crosses the whole list to reach the preview, every time. |
| S2 | **D**: the segmented controls (Surface, Version, Width; Block / Newsletter / Theme) are `aria-pressed` buttons; the settings panel is a tab pattern without the tab roles | roles found in the page: `tree`, `tablist`, `tab`, `tabpanel`, `toolbar`, `menu`, `dialog`, `listbox` 0 each; `status` 2. The structure is a list of buttons, not a tree, so a screen reader cannot say "item 3 of 10, selected". |
| S2 | **E**: one canvas, one tab stop; the widgets are not reachable one by one for assistive technology | the accessibility tree has the canvas labelled "Widget editor canvas" and nothing inside; no live region. Tab moves the toolkit's focus between the drawn controls (`Ui.root`), but nobody outside the canvas is told where it is. The semantic layer is task #45 in progress. |
| S3 | **D**: focus indicators are present on all 84 stops (good); Escape closes drags and in-place editing | `focusIndicatorMissing: []` |

### 3. Selection and manipulation

| | | Evidence |
|---|---|---|
| **S1** | **E**: one widget at a time; no multi-select, no marquee | Shift+click on a second widget replaces the selection (the inspector shows the text field only, `editor-shift-click.png`); dragging on an empty spot selects nothing. `Selected: int option` in `Model.fs`. Everything below depends on this. |
| **S1** | **E**: no resize beyond the width; no alignment, distribution, guides, grouping | the inspector has X, Y and Width sliders only; the selection draws corner handles, but a `Node` has a width (`W`, 0 = natural) and no height; the only help is the 8 px snap, and the arrows nudge by 8 px (no finer step). |
| S2 | **E**: no zoom or pan, no rulers, no layers or outline panel | wheel and `Ctrl`+wheel change nothing; a layout of 40 widgets could not be navigated by name. |
| S2 | **E**: no copy and paste, `Ctrl+D` does nothing (Duplicate is a button) | probe: "ctrl+d duplicates: no change". |
| S2 | **E**: text is edited in the inspector only; a double click selects and does not open the text | `editor-dblclick.png`. The designer edits in place (its click-on-the-selected-block), so the two tools differ on the most common action. |
| S2 | **D**: one block at a time; no multi-select, no copy/paste of blocks between newsletters | reading `Studio.fs` (`Selected` is a single id). |
| S3 | **D**: no formatting toolbar: bold, italic and links are typed as `**…**`, `_…_`, `[…](…)` | README; no button for it in the block panel. |

### 4. Workbench: layout, panels, documents, status

| | | Evidence |
|---|---|---|
| **S1** | No resizable, collapsible or dockable panels; no remembered layout | D: 0 elements with a resize cursor; panels are fixed Bootstrap columns. E: fixed 190 px palette and 270 px inspector in the canvas. Neither remembers anything about the layout. |
| **S1** | **E**: nothing is saved: a layout is lost on reload; no import or open | `layoutSurvivesReload: false` (a widget added from the palette is gone after a reload); no `localStorage` use in `src/`. The designer keeps projects (good) and imports/exports JSON; the editor only exports F#. |
| S2 | No documents as tabs, no recent files, no split view | D: one project open at a time, behind a Projects dialog. E: one layout. An IDE base has tabs and a way to compare. |
| S2 | No status bar, no notifications | D: the checks summary is a collapsed `<details>` under the preview; a save shows a time in the project list only. E: nothing says "saved", "exported", "copied". |
| S2 | **E**: not an application shell: on the page it sits under a hero, and the canvas does not fit a 900 px window | `canvasFitsViewport: {top: 310, height: 702, viewport: 900, fits: false}` (and 420/666 on the demo page). An editor should be the viewport. |
| S2 | **D**: the layout breaks between phone and desktop | `scrollWidth` 1040 at 1024 px and 823 at 768 px (horizontal scroll), at 720 px (the 200 % zoom of a 1440 px window) too; at 1024 the rows of the structure show only the first letters of a block's name (`designer-1024.png`) because the four buttons take the width; at 390 px it fits, but the palette, structure and settings are below the preview and the sticky header overlaps the designer's bar (`designer-390.png`). |

### 5. Display

| | | Evidence |
|---|---|---|
| S2 | **D**: light only; it ignores `prefers-color-scheme: dark` | `darkBg: rgb(255, 255, 255)` with the scheme emulated dark. The platform has OKLCH ramps and tokens (`FSharp.Wasm.Design`); the designer does not use them. |
| S3 | **E**: a design toggle (dark/light) that is the editor's, not the system's; the canvas theme is another blue-on-slate than the brand | open decision: move `FSharp.Graphics.Widgets.Theme` onto the tokens. |
| S3 | Neither has a high-contrast or reduced-motion mode of its own | the designer's own test turns reduced motion on so that axe reads the resting colours. |

### 6. Help, onboarding, empty and error states

| | | Evidence |
|---|---|---|
| S2 | No help overlay, no first-run hint beyond the empty inspector | E: "Select an element on the canvas. Arrow keys nudge, Delete removes." is the only line; D: "Load sample" and "New empty issue" are good starts, nothing explains drag, in-place editing or checks. |
| S3 | **D** import errors and **E** export have no recorded error states | not probed beyond the happy path. |

## What an IDE base would be

The two tools render differently (DOM and Bootstrap; canvas and `FSharp.Graphics.Widgets`) and will keep doing so: a canvas cannot take a CSS framework and a DOM cannot draw a million cells.
What they have in common is **behaviour**, and behaviour does not need a renderer. The proposal: a headless **workbench** of pure state machines, in the lowest layer all its users can reach
(core, as [AGENTS.md §4](https://github.com/fsharp-wasm/fsharp-wasm/blob/develop/AGENTS.md) asks), and two thin renderers.

```text
FSharp.Wasm.Workbench (core)       pure, no host; .NET and WasmGC; like FSharp.Wasm.Actors and Edit
  Command    id, title, category, when-context, arguments as JSON; a registry that can be listed, run, logged
  Keymap     chords and sequences by context, conflicts found at load, user overrides as a JSON document
  Palette    ranking of commands and documents by a query (subsequence match, recency)
  Layout     a dock tree: split / tabs / panel, sizes, collapsed, minimum sizes; a JSON document
  Roving     the focus model of a list, tree, toolbar or grid (one tab stop, arrows, Home/End, type-ahead) as a state machine
  Selection  single, multiple, range with an anchor; the same for blocks, widgets, rows, cells
  Notify     toasts and the status line as a queue of messages with a lifetime
Browser.Workbench (runtime)         DOM renderer: the ARIA roles of those patterns, Bootstrap or tokens
FSharp.Graphics.Workbench (graphics) canvas renderer, with the semantic layer of #45 for assistive technology
```

Why this shape, and the step ahead of it:

- **Commands are data.** A command is an id and JSON arguments; the app maps it to a `Msg` (as `Studio.update` and `Model.update` already are pure functions of one). So a palette, a menu, a
  shortcut, a script, a test and **an agent** all call the same thing; a macro is a list of commands; `Edit`'s operations are already data, so the log is the undo history and the stream of
  [Ghost patches](https://github.com/fsharp-wasm/fsharp-wasm-backend) (task #60) for collaboration. An editor that can be driven by a model of language is not an extra, it is what falls out.
- **One behaviour, two renderers.** The roving focus, the keymap and the selection are the same code in the designer and in the editor, so a fix is one fix and the keyboard works the same on
  both. The accessibility patterns (tree, menu, tabs, dialog, toolbar) are written once as state machines and rendered with the right roles in the DOM and through the semantic layer on the canvas.
- **Layout, keymap and tokens are documents.** Like the design tokens: a JSON file that a team shares, diffs and tests. A layout that the user arranged survives the reload.
- **The document can live in a process.** With the actor library (`FSharp.Wasm.Actors`, [docs/actors.md](https://github.com/fsharp-wasm/fsharp-wasm-runtime/blob/develop/docs/actors.md))
  the document model, the checks and the rendering can be separate processes with the GUI loop in front: a long newsletter or a million-row grid never blocks the keyboard.
- Not new repositories: a project in core (state machines), a folder in runtime (DOM), a folder in graphics (canvas). Each repository pins the others by commit, and a new one is a new pin.

## Roadmap

Ordered by what a user feels first. Each step has an acceptance test that fails before it and passes after, in the repository's own end-to-end suite.

| Step | What | Acceptance (measured the way this audit measured) |
|---|---|---|
| **W0** (days) | The quick wins that need no kit: **D** `Ctrl+Z`/`Ctrl+Shift+Z`, `Delete`, `Ctrl+D`, `Alt+↑/↓`; the structure list as one tab stop with arrows (roving tabindex); the layout fixed between 700 and 1100 px (the structure rows keep their names; the settings stay on screen); `prefers-color-scheme` through the tokens. **E** the editor as the whole viewport (an app-shell page); `Ctrl+D`; a hint of the keys. | tab stops in the structure ≤ 1 (now 58); the block count changes by `Ctrl+D`/`Delete`/`Ctrl+Z`; `scrollWidth` = viewport at 700, 768, 1024; the canvas fits at 1440 × 900. |
| **W1** | `Workbench` v0 in core: `Command`, `Keymap`, `Palette`, `Roving`; the shortcut list and the palette (`Ctrl+K`) in both tools; shortcuts shown on buttons and in menus | every command of both tools in the palette (a test lists the registry against the buttons); a rebinding as JSON changes the key; the same `Roving` tests pass under .NET and WasmGC. |
| **W2** | `Layout` and `Notify`: panels that resize, collapse and remember; a status bar; documents as tabs (the designer's projects; the editor gets projects, local first) | the layout of a session comes back after a reload; a drag of a splitter keeps a minimum size; the editor survives a reload (`layoutSurvivesReload: true`). |
| **W3** | The editor's missing editing: multi-select and marquee, height and all four edges, align and distribute, guides, copy and paste, in-place text, a layers panel, zoom and pan | the scripted session (`tests/WidgetEditor.Session`) selects two widgets by marquee, aligns them and undoes it in one step. |
| **W4** | The semantic layer for the canvas (#45) and the ARIA patterns on the DOM side from `Selection` and `Roving` | axe-core on the DOM; a screen-reader pass by a person, recorded; the canvas lists its widgets as a tree for assistive technology. |
| **W5** | The agent surface: the command registry served over a message channel (and by the edge host), with the schema of the arguments | a script drives a session end to end: new issue, five blocks, checks, export, through commands only. |

W0 is independent of the rest and can be done at once; W1 is the keystone, everything after it uses it.

## Decisions that are the maintainer's

1. **Where the workbench lives**: core (recommended: the lowest layer, nothing above it needed) or inside graphics until it outgrows it.
2. **W0 first, or W1 first**: W0 is days and makes both tools feel better at once; W1 is weeks and makes them the same tool. Recommended: both, in that order, W0 in parallel.
3. **The canvas theme**: whether `FSharp.Graphics.Widgets.Theme` (blue on slate) moves onto the design tokens (violet), which changes every canvas application's look. Open since the design library.
4. **The editor's persistence**: local first (like the designer's projects), or straight to Ghost documents on the edge.
