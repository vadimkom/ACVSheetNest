# Third-party notices

SheetNest itself is released under the MIT License (see [LICENSE](LICENSE)).

## Nesting engine

To arrange parts on the sheet, the SheetNest installer bundles an offline nesting engine
(`sparrow.exe`, next to `SheetNest.exe`). SheetNest runs it as a **separate program**; it does
**not** link against these libraries. `sparrow.exe` is a self-contained Rust binary; the
`jagua-rs` geometry library is compiled into it **unmodified**, while `sparrow` carries a small
local change so that nesting the same job twice gives the same result: a `--max-iterations`
budget that ends the search on a counter instead of on elapsed time, a fixed worker-thread count
instead of one derived from the machine's cores, and the failure-based (rather than time-based)
compression decay whenever that budget is used. The change is kept in this repository as
`SparrowEngine/sheetnest-sparrow.patch`, which applies to the upstream commit below.
Corresponding source is available from the upstream projects below.

| Component | Version | License | Source |
|-----------|---------|---------|--------|
| sparrow | 2025 (upstream `961ec31`) | MIT | https://github.com/JeroenGar/sparrow |
| jagua-rs | 0.7.2 | MPL-2.0 | https://github.com/JeroenGar/jagua-rs |

If you need the corresponding source for any bundled component, it is available from the
upstream project links above; you may also request it through the
[SheetNest repository](https://github.com/ManuelMRosa/SheetNest).
