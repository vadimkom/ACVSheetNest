//! ASCII DXF writer. Port of the essentials of `DxfExporter`
//! (`DeepNestLib.Core/IO/DxfExporter.cs`), `DxfOutputLayers.cs` and
//! `DxfExporter.AppendSheetOutline`: a nested sheet written on the shop's three
//! output layers, cut geometry first, every entity ByLayer.
//!
//! The C# names the layers `0` (cut, colour 7), `9` (paint, colour 1) and
//! `Background` (colour 8), and so does this port (see `crate::OutputLayer`),
//! with the colour indices and the entity ordering contract of `DxfExporter.Finish`.
//!
//! File layout mirrors the R2000 ASCII DXF the IxMilia library writes for
//! `DxfLwPolyline` entities: HEADER (`$INSUNITS` 4 = mm), a minimal LAYER
//! table, ENTITIES, ENDSEC/EOF. Coordinates are snapped to the micro-inch grid
//! in place (`DxfExporter.Snap`).

use nest_geom::SvgPoint;
use crate::{OutputLayer, RawDetail};

/// The sheet of the layout as an offcut of any shape. Port of the ISheet outline
/// that `AppendSheetOutline` carries over to `DxfOutputLayers.Background`.
/// A `None` sheet (a plain rectangle) writes nothing — the sheet size lives in
/// the CAM/job setup.
#[derive(Debug, Clone, Default, PartialEq, serde::Serialize, serde::Deserialize)]
pub struct SheetOutline {
    pub outer: Vec<SvgPoint>,
    /// Holes of the offcut, as in `sheet.Children`.
    pub children: Vec<Vec<SvgPoint>>,
}

/// One placed part. Port of the `polygon`/`IPartPlacement` slice the exporter
/// needs: contour plus holes moved to where the placement puts them.
#[derive(Debug, Clone, Default, PartialEq, serde::Serialize, serde::Deserialize)]
pub struct PlacedPart {
    /// The part's name; engraved export (`DxfPartNameExport`) is not ported, so
    /// it is metadata here.
    pub name: String,
    pub outer: Vec<SvgPoint>,
    /// Holes, always cut (`CUT`), as in the original.
    pub children: Vec<Vec<SvgPoint>>,
    /// Bend lines (open polylines), always written on `PAINT` and never cut.
    #[serde(default)]
    pub bends: Vec<Vec<SvgPoint>>,
    /// `None` — cut (`CUT`). `PAINT` could carry bend lines; `BACKGROUND` is
    /// never cut. Everything else in the C# collapses onto the cut layer
    /// (`Finish`, plus the guard that demotes a bend-layer name the part was
    /// drawn on to cut).
    pub layer: Option<OutputLayer>,
}

/// A whole sheet layout ready for export. Port of the pieces of
/// `GenerateDxfFile`/`Finish` that matter without common-line clusters,
/// offcut lines and part-name engraving.
#[derive(Debug, Clone, Default, PartialEq, serde::Serialize, serde::Deserialize)]
pub struct LayoutExport {
    pub sheet: Option<SheetOutline>,
    pub parts: Vec<PlacedPart>,
    /// When true, engrave part names as TEXT entities on the PAINT layer
    /// (port of `DxfPartNameExport.Paint` / `AppendPartNameEntities`).
    #[serde(default)]
    pub export_part_names: bool,
    /// Inner cuts (open lines, sheet coordinates), cut on `CUT` after the parts.
    #[serde(default)]
    pub inner_cuts: Vec<Vec<SvgPoint>>,
}

/// Writes one detail as closed `LWPOLYLINE` entities on `CUT`, outer first and
/// then its holes. The returned ASCII DXF is accepted by [`crate::parse_dxf`].
pub fn export_dxf(detail: &RawDetail) -> String {
    export_layout(&LayoutExport {
        sheet: None,
        parts: vec![PlacedPart {
            name: detail.name.clone(),
            outer: detail.outer.clone(),
            children: detail.children.clone(),
            bends: detail.bends.clone(),
            layer: None,
        }],
        inner_cuts: Vec::new(), export_part_names: false,
    })
}

/// The bytes of a DXF for the disk: Windows-1251, as the header declares
/// (`$DWGCODEPAGE` `ANSI_1251`), so Cyrillic part names read as text in the
/// shop's CAM instead of `\U+XXXX` escapes. A character the code page lacks
/// becomes `_`.
pub fn encode_dxf(text: &str) -> Vec<u8> {
    text.chars()
        .map(|c| match c {
            '\u{0}'..='\u{7f}' => c as u8,
            'А'..='я' => (c as u32 - 0x410 + 0xC0) as u8,
            'Ё' => 0xA8,
            'ё' => 0xB8,
            '№' => 0xB9,
            '«' => 0xAB,
            '»' => 0xBB,
            '°' => 0xB0,
            '±' => 0xB1,
            '\u{a0}' => 0xA0,
            _ => b'_',
        })
        .collect()
}

/// Port of `DxfExporter.Snap`: rounds a coordinate to the export grid
/// (micro-inch).
fn snap(v: f64) -> f64 {
    (v * 1.0e6).round() / 1.0e6
}

fn write_value(out: &mut String, code: i32, value: &str) {
    out.push_str(&code.to_string());
    out.push('\n');
    out.push_str(value);
    out.push('\n');
}

fn write_num(out: &mut String, code: i32, value: i64) {
    write_value(out, code, &value.to_string());
}

fn write_coord(out: &mut String, code: i32, value: f64) {
    write_value(out, code, &format_number(snap(value)));
}

fn format_number(v: f64) -> String {
    let v = if v == 0.0 { 0.0 } else { v }; // avoid `-0`
    format!("{v}")
}

/// One contour as a closed LWPOLYLINE. Port of the closing convention of
/// `DxfExporter.AppendSheetOutline`: a repeated final vertex is dropped and the
/// entity carries the closed flag, so a reader sees an explicit ring.
fn write_ring(out: &mut String, layer: &str, points: &[SvgPoint], handle: &mut u32) {
    let mut ring: Vec<SvgPoint> = points.to_vec();
    if ring.len() > 3 {
        let first = ring[0];
        let last = ring[ring.len() - 1];
        if (first.x - last.x).abs() < 1e-9 && (first.y - last.y).abs() < 1e-9 {
            ring.pop();
        }
    }
    if ring.len() < 2 {
        return;
    }
    let closed = ring.len() >= 3;

    write_value(out, 0, "LWPOLYLINE");
    write_value(out, 5, &format!("{handle:X}"));
    *handle += 1;
    write_value(out, 100, "AcDbEntity");
    write_value(out, 8, layer);
    write_num(out, 62, 256); // ByLayer, as `Finish` writes every entity
    write_value(out, 100, "AcDbPolyline");
    write_num(out, 90, ring.len() as i64);
    write_num(out, 70, if closed { 1 } else { 0 });
    write_num(out, 43, 0); // constant width
    for p in &ring {
        write_coord(out, 10, p.x);
        write_coord(out, 20, p.y);
    }
}

/// One bend line as an open `LWPOLYLINE` (no closed flag, never a ring).
fn write_open_polyline(out: &mut String, layer: &str, points: &[SvgPoint], handle: &mut u32) {
    if points.len() < 2 {
        return;
    }
    write_value(out, 0, "LWPOLYLINE");
    write_value(out, 5, &format!("{handle:X}"));
    *handle += 1;
    write_value(out, 100, "AcDbEntity");
    write_value(out, 8, layer);
    write_num(out, 62, 256); // ByLayer
    write_value(out, 100, "AcDbPolyline");
    write_num(out, 90, points.len() as i64);
    write_num(out, 70, 0); // open
    write_num(out, 43, 0);
    for p in points {
        write_coord(out, 10, p.x);
        write_coord(out, 20, p.y);
    }
}

/// Port of `DxfExporter.GenerateDxfFile` + `Finish` + `AppendSheetOutline` (the
/// essentials): cut geometry first on `CUT` (each part outer, then holes), then
/// `PAINT` (including optional part-name engraving), then the sheet outline on
/// `BACKGROUND` for offcuts of any shape. The three layers carry the colour
/// indices of `DxfOutputLayers` (7 / 1 / 8) in a minimal LAYER table.
pub fn export_layout(layout: &LayoutExport) -> String {
    let mut out = String::with_capacity(2048);
    let mut handle = 0x10u32;

    // HEADER — R2000, millimetres.
    write_value(&mut out, 0, "SECTION");
    write_value(&mut out, 2, "HEADER");
    write_value(&mut out, 9, "$ACADVER");
    write_value(&mut out, 1, "AC1015");
    write_value(&mut out, 9, "$DWGCODEPAGE");
    write_value(&mut out, 3, "ANSI_1251");
    write_value(&mut out, 9, "$INSUNITS");
    write_num(&mut out, 70, 4);
    write_value(&mut out, 0, "ENDSEC");

    // TABLES — the LAYER table with the output layers.
    write_value(&mut out, 0, "SECTION");
    write_value(&mut out, 2, "TABLES");
    write_value(&mut out, 0, "TABLE");
    write_value(&mut out, 2, "LAYER");
    write_value(&mut out, 100, "AcDbSymbolTable");
    write_num(&mut out, 70, 3);
    for (name, color) in [
        (OutputLayer::Cut.as_str(), 7),
        (OutputLayer::Paint.as_str(), 1),
        (OutputLayer::Background.as_str(), 8),
    ] {
        write_value(&mut out, 0, "LAYER");
        write_value(&mut out, 5, &format!("{handle:X}"));
        handle += 1;
        write_value(&mut out, 100, "AcDbSymbolTableRecord");
        write_value(&mut out, 100, "AcDbLayerTableRecord");
        write_value(&mut out, 2, name);
        write_num(&mut out, 70, 0); // frozen flags
        write_num(&mut out, 62, color);
        write_value(&mut out, 6, "CONTINUOUS");
    }
    write_value(&mut out, 0, "ENDTAB");
    write_value(&mut out, 0, "ENDSEC");

    // ENTITIES — cut first, then paint, then background, as `Finish` orders.
    write_value(&mut out, 0, "SECTION");
    write_value(&mut out, 2, "ENTITIES");

    for part in &layout.parts {
        let layer = part.layer.unwrap_or(OutputLayer::Cut);
        if layer == OutputLayer::Cut {
            write_ring(&mut out, layer.as_str(), &part.outer, &mut handle);
            for child in &part.children {
                write_ring(&mut out, layer.as_str(), child, &mut handle);
            }
        }
    }
    for cut in &layout.inner_cuts {
        write_ring(&mut out, OutputLayer::Cut.as_str(), cut, &mut handle);
    }
    for output_layer in [OutputLayer::Paint, OutputLayer::Background] {
        for part in &layout.parts {
            let layer = part.layer.unwrap_or(OutputLayer::Cut);
            if layer == output_layer {
                write_ring(&mut out, layer.as_str(), &part.outer, &mut handle);
                for child in &part.children {
                    write_ring(&mut out, layer.as_str(), child, &mut handle);
                }
            }
        }
    }

    // Bend lines of every part, on PAINT whatever the part's own layer is.
    for part in &layout.parts {
        for bend in &part.bends {
            write_open_polyline(&mut out, OutputLayer::Paint.as_str(), bend, &mut handle);
        }
    }

    // Part name engraving on PAINT (port of AppendPartNameEntities, Paint mode).
    if layout.export_part_names {
        for part in &layout.parts {
            if part.name.trim().is_empty() { continue; }
            let label = part_name_label(&part.name);
            if label.trim().is_empty() { continue; }
            let (cx, cy) = compute_centroid(&part.outer);
            let (h, vertical) = compute_text_properties(&part.outer, &label);
            write_text(&mut out, OutputLayer::Paint.as_str(), &label, cx, cy, h, if vertical { 90.0 } else { 0.0 }, &mut handle);
        }
    }

    // The offcut outline for the eye only, never cut (`AppendSheetOutline`).
    if let Some(sheet) = &layout.sheet {
        write_ring(
            &mut out,
            OutputLayer::Background.as_str(),
            &sheet.outer,
            &mut handle,
        );
        for child in &sheet.children {
            write_ring(
                &mut out,
                OutputLayer::Background.as_str(),
                child,
                &mut handle,
            );
        }
    }

    write_value(&mut out, 0, "ENDSEC");
    write_value(&mut out, 0, "EOF");
    out
}


fn part_name_label(source: &str) -> String {
    // Simplified port of GetPartNameLabel: strip "Flat-Pattern - " prefix and use file stem-like name.
    let name = std::path::Path::new(source)
        .file_stem().and_then(|s| s.to_str()).unwrap_or(source);
    let lower = name.to_ascii_lowercase();
    if let Some(rest) = lower.strip_prefix("flat-pattern -") {
        let trimmed = rest.trim();
        if !trimmed.is_empty() {
            // return original-cased tail
            let idx = name.len().saturating_sub(trimmed.len());
            return name[idx..].trim_start_matches(|c: char| c=='-'||c=='_'||c.is_whitespace()).trim().to_string();
        }
    }
    name.to_string()
}

fn compute_centroid(poly: &[SvgPoint]) -> (f64, f64) {
    if poly.len() < 3 {
        if let Some(p) = poly.first() { return (p.x, p.y); } else { return (0.0, 0.0); }
    }
    let mut twice_area = 0.0;
    let mut cx = 0.0;
    let mut cy = 0.0;
    for i in 0..poly.len() {
        let p = poly[i];
        let q = poly[(i + 1) % poly.len()];
        let cross = p.x * q.y - q.x * p.y;
        twice_area += cross;
        cx += (p.x + q.x) * cross;
        cy += (p.y + q.y) * cross;
    }
    if twice_area.abs() < 1e-12 {
        let (minx,maxx,miny,maxy) = bounds4(poly);
        return ((minx+maxx)/2.0, (miny+maxy)/2.0);
    }
    let x = cx / (3.0 * twice_area);
    let y = cy / (3.0 * twice_area);
    // accept if inside-ish? skip expensive point-in-poly-with-holes; fallback to bounds center if NaN
    if x.is_finite() && y.is_finite() { (x,y) } else { let (minx,maxx,miny,maxy)=bounds4(poly); ((minx+maxx)/2.0,(miny+maxy)/2.0) }
}

fn bounds4(poly: &[SvgPoint]) -> (f64,f64,f64,f64) {
    let mut minx = f64::INFINITY; let mut maxx = f64::NEG_INFINITY;
    let mut miny = f64::INFINITY; let mut maxy = f64::NEG_INFINITY;
    for p in poly { if p.x<minx{minx=p.x;} if p.x>maxx{maxx=p.x;} if p.y<miny{miny=p.y;} if p.y>maxy{maxy=p.y;} }
    if !minx.is_finite() { (0.0,0.0,0.0,0.0) } else { (minx,maxx,miny,maxy) }
}

fn compute_text_properties(poly: &[SvgPoint], text: &str) -> (f64, bool) {
    let (minx,maxx,miny,maxy) = bounds4(poly);
    let w = (maxx-minx).max(0.0);
    let h = (maxy-miny).max(0.0);
    let is_vertical = h > w * 1.4;
    let dim_along = if is_vertical { h } else { w };
    let dim_across = if is_vertical { w } else { h };
    let text_len = text.chars().count().max(1) as f64;
    let max_h_by_len = (dim_along * 0.75) / (0.6 * text_len);
    let max_h_by_thick = dim_across * 0.35;
    let computed = max_h_by_len.min(max_h_by_thick);
    let target = if w.max(h) > 25.0 {
        computed.clamp(1.5, 10.0)
    } else {
        computed.clamp(0.06, 0.4)
    };
    (snap(target.max(0.1)), is_vertical)
}

fn write_text(out: &mut String, layer: &str, text: &str, x: f64, y: f64, height: f64, rotation: f64, handle: &mut u32) {
    // DXF TEXT (AcDbText) with middle-center alignment
    write_value(out, 0, "TEXT");
    write_value(out, 5, &format!("{handle:X}"));
    *handle += 1;
    write_value(out, 100, "AcDbEntity");
    write_value(out, 8, layer);
    write_num(out, 62, 256); // ByLayer
    write_value(out, 100, "AcDbText");
    write_coord(out, 10, x);
    write_coord(out, 20, y);
    write_num(out, 30, 0);
    write_coord(out, 40, height.max(0.1));
    write_value(out, 1, text);
    write_coord(out, 50, rotation);
    write_num(out, 72, 1); // horizontal center
    write_num(out, 73, 2); // vertical middle
    write_coord(out, 11, x);
    write_coord(out, 21, y);
    write_num(out, 31, 0);
    write_value(out, 100, "AcDbText");
}


#[cfg(test)]
mod tests {
    use super::*;
    use crate::dxf::parse_dxf;
    use nest_geom::polygon_area;

    fn ring(points: &[(f64, f64)]) -> Vec<SvgPoint> {
        points
            .iter()
            .map(|(x, y)| SvgPoint::exact(*x, *y))
            .collect()
    }

    fn sample_layout() -> LayoutExport {
        LayoutExport {
            sheet: Some(SheetOutline {
                outer: ring(&[(0.0, 0.0), (500.0, 0.0), (500.0, 300.0), (0.0, 300.0)]),
                children: Vec::new(),
            }),
            parts: vec![PlacedPart {
                bends: Vec::new(),
                name: "rect_100x60".to_owned(),
                outer: ring(&[(10.0, 10.0), (110.0, 10.0), (110.0, 70.0), (10.0, 70.0)]),
                children: vec![ring(&[(20.0, 20.0), (40.0, 20.0), (40.0, 40.0), (20.0, 40.0)])],
                layer: None,
            }],
            inner_cuts: Vec::new(), export_part_names: false,
        }
    }

    #[test]
    fn cyrillic_names_are_written_in_windows_1251() {
        assert_eq!(encode_dxf("1\nДверь №2 ёЁ x\n"), b"1\n\xC4\xE2\xE5\xF0\xFC \xB92 \xB8\xA8 x\n");
        assert_eq!(encode_dxf("ü"), b"_");
        assert!(export_layout(&sample_layout()).contains("$DWGCODEPAGE\n3\nANSI_1251"));
    }

    #[test]
    fn writes_header_tables_and_layers() {
        let dxf = export_layout(&sample_layout());
        assert!(dxf.contains("$INSUNITS\n70\n4"));
        for layer in ["0", "9", "Background"] {
            assert!(dxf.contains(&format!("2\n{layer}\n")), "layer {layer} declared");
        }
        assert!(dxf.ends_with("0\nEOF\n"));
    }

    #[test]
    fn round_trip_rectangle_keeps_length_and_area() {
        let layout = sample_layout();
        let text = export_layout(&layout);
        let detail =
            parse_dxf(&text, "exported.dxf").expect("exported DXF must reparse");

        // The largest chained contour is the sheet, the part rectangle is a
        // hole, the part's own small hole is a hole of the assembly — exactly
        // how a reimported nest reads.
        assert_eq!(detail.outer.len(), 4);
        assert!((polygon_area(&detail.outer).abs() - 500.0 * 300.0).abs() < 1e-6);

        assert!(!detail.children.is_empty());
        let part = &detail.children[0];
        assert_eq!(part.len(), 4);
        assert!((polygon_area(part).abs() - 100.0 * 60.0).abs() < 1e-6);

        // Every exported point is a drawing vertex, and the round trip keeps
        // coordinates exact: the ring came back closed and square.
        let matched = &layout.parts[0];
        let got = &part[..4.min(part.len())];
        for (want, got) in matched.outer.iter().zip(got) {
            assert!((want.x - got.x).abs() < 1e-6 && (want.y - got.y).abs() < 1e-6);
            assert!(got.exact, "exported vertices reimport as exact");
        }
    }

    #[test]
    fn detail_export_parse_round_trip_preserves_rings() {
        let source = RawDetail {
            bends: Vec::new(),
            name: "plate.dxf".to_owned(),
            outer: ring(&[(0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0)]),
            children: vec![ring(&[(2.0, 2.0), (4.0, 2.0), (4.0, 4.0), (2.0, 4.0)])],
        };
        let text = export_dxf(&source);
        let parsed = parse_dxf(&text, "round-trip.dxf").unwrap();

        assert_eq!(parsed.outer, source.outer);
        assert_eq!(parsed.children, source.children);
        assert_eq!(text.matches("0\nLWPOLYLINE\n").count(), 2);
        let entities = text.split("2\nENTITIES\n").nth(1).unwrap();
        assert!(!entities.contains("8\n9\n"));
        assert!(!entities.contains("8\nBackground\n"));
    }

    #[test]
    fn sheetless_layout_writes_only_parts() {
        let layout = LayoutExport {
            sheet: None,
            parts: vec![PlacedPart {
                bends: Vec::new(),
                name: "rect".to_owned(),
                outer: ring(&[(0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0)]),
                children: Vec::new(),
                layer: None,
            }],
            inner_cuts: Vec::new(), export_part_names: false,
        };
        let dxf = export_layout(&layout);
        assert_eq!(dxf.matches("LWPOLYLINE").count(), 1);
        let entities = dxf.split("2\nENTITIES\n").nth(1).unwrap();
        assert!(!entities.contains("8\nBackground\n"));
    }

    #[test]
    fn cut_paint_and_background_entities_are_ordered() {
        let layout = LayoutExport {
            sheet: Some(SheetOutline {
                outer: ring(&[(0.0, 0.0), (100.0, 0.0), (100.0, 100.0), (0.0, 100.0)]),
                children: Vec::new(),
            }),
            parts: vec![
                PlacedPart {
                    bends: Vec::new(),
                    name: "bend".to_owned(),
                    outer: ring(&[(40.0, 40.0), (60.0, 40.0), (60.0, 60.0), (40.0, 60.0)]),
                    children: Vec::new(),
                    layer: Some(OutputLayer::Paint),
                },
                PlacedPart {
                    bends: Vec::new(),
                    name: "cut".to_owned(),
                    outer: ring(&[(10.0, 10.0), (30.0, 10.0), (30.0, 30.0), (10.0, 30.0)]),
                    children: Vec::new(),
                    layer: None,
                },
            ],
            inner_cuts: Vec::new(), export_part_names: false,
        };
        let dxf = export_layout(&layout);
        let entities = dxf.split("2\nENTITIES\n").nth(1).unwrap();
        let cut_at = entities.find("8\n0\n").expect("cut entity present");
        let paint_at = entities.find("8\n9\n").expect("paint entity present");
        let background_at = entities
            .find("8\nBackground\n")
            .expect("background entity present");
        assert!(cut_at < paint_at && paint_at < background_at);
    }

    #[test]
    fn repeated_closing_vertex_is_dropped_not_duplicated() {
        let layout = LayoutExport {
            sheet: None,
            parts: vec![PlacedPart {
                bends: Vec::new(),
                name: "rect".to_owned(),
                // the ring's first point repeated at the end
                outer: ring(&[
                    (0.0, 0.0),
                    (10.0, 0.0),
                    (10.0, 10.0),
                    (0.0, 10.0),
                    (0.0, 0.0),
                ]),
                children: Vec::new(),
                layer: None,
            }],
            inner_cuts: Vec::new(), export_part_names: false,
        };
        let text = export_layout(&layout);
        let lines: Vec<&str> = text.split('\n').collect();
        let vertex_count = lines
            .windows(3)
            .filter(|w| w[0] == "10" && w[1] == "0" && w[2] == "20")
            .count();
        // x == 0 occurs at the first and the fourth vertex; the repeated final
        // vertex was dropped.
        assert_eq!(vertex_count, 2);
        let detail = parse_dxf(&text, "exported.dxf").unwrap();
        assert_eq!(detail.outer.len(), 4);
        assert!((polygon_area(&detail.outer).abs() - 100.0).abs() < 1e-6);
    }
}
