<p align="center">
  <img src="assets/banner.png" alt="SheetNest, free nesting software for sheet metal" width="900">
</p>

<p align="center">
  <a href="https://github.com/vadimkom/ACVSheetNest/releases/latest"><img src="https://img.shields.io/badge/Download%20for%20Windows-3aa851?style=for-the-badge&logo=windows&logoColor=white" alt="Download SheetNest for Windows"></a>
  <a href="https://ko-fi.com/sheetnest"><img src="https://img.shields.io/badge/Support-ff5e5b?style=for-the-badge&logo=ko-fi&logoColor=white" alt="Support SheetNest on Ko-fi"></a>
</p>

<p align="center">
  <sub>Free · Windows 10/11, 64-bit · no account, no subscription</sub>
</p>

# SheetNest: free nesting software for sheet metal

SheetNest is free nesting software for sheet metal fabrication. It fits your parts onto stock sheets for CNC laser cutting and plasma cutting, on Windows, with no account and no subscription.

If you cut sheet metal, SheetNest helps you get the most parts out of every sheet, buy less material, and hand your machine cut-ready files, all from a simple, one-toolbar app.

## About this fork

This is the ACV fork of [ManuelMRosa/SheetNest](https://github.com/ManuelMRosa/SheetNest), developed for and tested at a working laser-cutting shop. It branched from SheetNest 1.1.10; the installer is on the [releases page](https://github.com/vadimkom/ACVSheetNest/releases/latest). Everything the original does is still here, plus:

**Workflow**
- Four steps across the top: **Parts → Sheets → Nesting → Layout & Report** (Ctrl+1…4).
- Two buttons: **⚡ Draft**, a few-second rough nest for a quote (sheets, area, metres of cut), and **▶ Nest** for the layout you cut. Their search settings live in `%LOCALAPPDATA%\SheetNest\nest-profiles.json`.
- Every press adds a finished nest of the whole job to a list of variants; the best one is starred.
- **Tick sheets as cut, nest the rest**: their parts come off the order and the sheets off the stock.

**Parts and sheets**
- **Flip** a part left/right or top/bottom, and a **Mirrored** quantity column for left/right pairs.
- **Shapes without a DXF**: a rectangle, square or circle in any quantity.
- A kit **multiplier**.
- **Material and thickness** per job.
- **Sheet priority**: offcuts and remnants are used first and listed first.
- Metric sheets by default.

**Nesting**
- **Fill-up**: big parts still waiting are offered to each finished sheet before a new one is started. A real three-sheet job now nests on two.
- **Move a part to another sheet** in manual editing, into the first free spot.
- Thumbnails of every sheet.
- Part names drawn on the layout.
- Click a part to see its size and position.

**Import**
- **SC2000 `.chf` drawings**, the whole format. Cut and engraving layers are told apart; the background layer is left out.
- SolidWorks flat patterns: bend lines are kept.
- DXF reading is up to 200× faster on drawings with many holes (a 1393-hole grid took four minutes to open, now seconds).
- Elliptical arcs crossing zero are read correctly.

**Export**
- DXF on three layers your CAM can map: **CUT**, **PAINT** (bend lines and engraved names) and **BACKGROUND**. Cut geometry is written first.
- One DXF per layout: `name_N_WxH_xRepeats.dxf`.

**Report**
- Material, thickness, sheet and part weight, cut length in metres, pierce count and part sizes.
- Unicode PDF, so Cyrillic and other scripts print.
- Works on PCs without native zlib.
- On-screen preview with zoom and direct printing.

**Languages**
- English, Русский, Български, Deutsch, Français, Español, Português, switchable live.
- The texts are standard `.resx` files in `DeepNestSharp/Ui`, so adding a language means adding a file. Corrections from native speakers are welcome.

**Under the hood**
- .NET 10.
- Saved projects about 3× smaller.
- No crash when a project's flash drive has been pulled.
- 3D import (FreeCAD) removed, and the installer is smaller.

Bug fixes that apply to the original are offered back upstream as separate pull requests.

## What SheetNest does

- **Fits the most parts on every sheet, automatically.** True-shape nesting packs your parts tightly by their real outline, so you waste less material and buy fewer sheets. Real 800-part jobs nest in seconds.
- **Imports your DXF part drawings.** Drop in the DXF files you already have and start nesting.
- **Tell it what you need.** Enter how many of each part you want (including mirrored copies), and list the sheet sizes and stock you actually have. Save your own sheet-size presets so they're one click next time.
- **Nest with one click, then fine-tune by hand.** Let SheetNest lay everything out, then drag, rotate, nudge, and drop parts to contact yourself, with spacing kept safe and full undo/redo.
- **Compatible parts can share one cut.** Choose per part whether it may share with any compatible part or only another copy of itself. SheetNest keeps ordinary spacing everywhere else, seats selected straight outside edges at the configurable Common Line Gap, and exports each cluster as a closed outer boundary plus one midpoint seam on clearly named layers your CAM can read.
- **Picks the best sheet size for you.** Stock several sizes and SheetNest puts the bulk on the size that packs densest and the leftovers on the size that wastes least. Standard US sheet sizes are built in, plus your own custom sizes.
- **Exports cut-ready DXF.** One clean DXF per layout, parts only, so your CAM software has nothing extra to trip over. Identical sheets are grouped into a simple "cut N of this layout" plan.
- **One-click PDF report.** A purchasing-ready report showing how much material to buy (sheets per size and total area), the cutting plan, part totals, and a scaled drawing of each layout.
- **Works in inches or millimeters.** Switch units to match your shop, with sheets up to 6000 × 2000 mm.
- **Keeps your work safe.** Autosave and crash recovery, a backup copy on every save, a Recent Projects list, and double-click a project file to open it right where you left off. Each saved project remembers its own sheet stock and its finished nest.
- **Free and open.** No account, no subscription, no license key. Open-source software you can just use.

## Screenshots

<p align="center">
  <img src="imgs/main-nest.png" alt="SheetNest nesting sheet-metal parts on a stock sheet for laser cutting" width="900"><br>
  <em>One-click true-shape nesting. Your parts packed tight on the sheet, ready to cut.</em>
</p>

<p align="center">
  <img src="imgs/edit-nest.png" alt="Editing a nest by hand in SheetNest: select, drag, rotate and mirror parts" width="900"><br>
  <em>Fine-tune by hand: select, drag, rotate, mirror, nudge, or type a coordinate to place a part exactly.</em>
</p>

<p align="center">
  <img src="imgs/nest-report.png" alt="SheetNest PDF nest report: material required, job summary, cutting plan and part totals" width="640"><br>
  <em>The one-click PDF report: how much material to buy, the cutting plan, and what is on each layout.</em>
</p>

<p align="center">
  <img src="imgs/nest-report-layout.png" alt="SheetNest nest report layout page: the sheet drawn to scale with every part numbered" width="640"><br>
  <em>Every layout drawn to scale, with each part numbered so a cut piece can be matched to its row and marked.</em>
</p>

## License

SheetNest is open-source software released under the MIT License. See [LICENSE](LICENSE).

SheetNest bundles the open-source sparrow nesting engine (MIT) with the jagua-rs geometry library (MPL-2.0) compiled in. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for licenses and sources.

Building from source? See [BUILDING.md](BUILDING.md).
