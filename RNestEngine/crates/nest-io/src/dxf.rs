//! Faithful port of the contour assembly of `DxfParser.ConvertDxfToRawDetail`
//! (`DeepNestLib.Core/IO/DxfParser.cs`): ASCII DXF entities → tessellated
//! segments → chained contours → `RawDetail` (the largest contour is the
//! outer, the rest are children/holes).
//!
//! Entities: `LINE`, `CIRCLE`, `ARC`, `LWPOLYLINE`, `POLYLINE`(+`VERTEX`/`SEQEND`),
//! `ELLIPSE`, `SPLINE`. `INSERT` is a hard error, as in the original: a part
//! drawn as a block must be exploded first. The annotation types of
//! `DxfParser.AnnotationTypes` draw nothing that gets cut and are dropped.
//!
//! Layer handling follows the source of truth (`DxfParser.ApproximateEntities`):
//! every entity is a cut path regardless of its layer, except entities on
//! `SHEETNEST_INNER_CUT` (`DxfOutputLayers.InnerCutSourceName`) — cuts inside
//! the part that enclose nothing and are not part of its shape. There is no
//! background/weld layer filtering in the C# parser; nothing of the sort is
//! invented here.
//!
//! Units: `$INSUNITS` from the HEADER is honoured and coordinates are scaled to
//! millimetres (4 = mm, as in the demo drawings; 1 = in ×25.4, 2 = ft ×304.8,
//! 5 = cm, 6 = m). Unitless or missing means untouched, like the C#, which reads
//! plain coordinates. This is the one deliberate extension of the port: the C#
//! library does no unit conversion at all.
//!
//! Only ASCII DXF is in scope; binary DXF fails with a Malformed error.

use crate::{IoError, RawDetail};
use nest_geom::{point_in_polygon, polygon_area, SvgPoint};

/// Port of `DxfParser.RemoveThreshold` (written `10e-5` there, i.e. 1e-4):
/// segments shorter than this vanish.
const REMOVE_THRESHOLD: f64 = 1.0e-4;

/// Port of `DxfParser.ClosingThreshold` (`10e-2`): source endpoints this close
/// are associated while chaining. The demo DXFs round LINE endpoints to six
/// decimal places, so their ARC/LINE junctions are about 1e-6 apart.
const CLOSING_THRESHOLD: f64 = 1.0e-1;

/// Topological tolerance of the finished contours. Coincident vertices are
/// coalesced and degenerate rings are rejected at this tolerance.
const CONTOUR_TOLERANCE: f64 = 1.0e-9;

/// Port of `DxfParser.CurveChordTolerance`: arcs/circles/splines are
/// approximated by chords that stay within this deviation (drawing units).
const CURVE_CHORD_TOLERANCE: f64 = 0.05;

/// Port of `DxfParser.MinArcStepDeg` / `MaxArcStepDeg`: the angular step of a
/// curve is clamped so tiny holes look smooth and huge arcs don't explode.
const MIN_ARC_STEP_DEG: f64 = 1.0;
const MAX_ARC_STEP_DEG: f64 = 6.0;

/// Port of `DxfOutputLayers.InnerCutSourceName`: open lines on this layer of a
/// PART drawing are cuts inside the part that enclose nothing; not part of the shape.
const INNER_CUT_SOURCE_LAYER: &str = "SHEETNEST_INNER_CUT";

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct V3 {
    x: f64,
    y: f64,
}

impl V3 {
    fn new(x: f64, y: f64) -> Self {
        Self { x, y }
    }

    fn dist_to(&self, other: V3) -> f64 {
        ((self.x - other.x) * (self.x - other.x) + (self.y - other.y) * (self.y - other.y)).sqrt()
    }
}

/// Manhattan-free Euclidean helper shared by the chaining port (the C# calls
/// `PointF.DistTo`; here statically resolved, since `SvgPoint` is foreign).
fn dist(a: SvgPoint, b: SvgPoint) -> f64 {
    let dx = a.x - b.x;
    let dy = a.y - b.y;
    (dx * dx + dy * dy).sqrt()
}

/// One tessellated segment. Port of `LineElement` (IO/LineElement.cs) with the
/// point exactness of `SvgPoint` in place of `LineElement.Curved`: the per-point
/// `exact` flag marks points that are vertices of the original drawing (LINE
/// endpoints, polyline vertices, arc/circle/ellipse ends, spline fit points)
/// rather than chord interior samples.
#[derive(Debug, Clone, Copy, PartialEq)]
struct Segment {
    start: SvgPoint,
    end: SvgPoint,
}

impl Segment {
    fn dist_to(&self, p: SvgPoint) -> f64 {
        dist(self.start, p).min(dist(self.end, p))
    }
}

/// One vertex of a polyline, with the DXF `bulge` (tan(θ/4)) of the segment that
/// starts here.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct PolyVertex {
    pub location: V3,
    pub bulge: f64,
}

/// One control point of a `SPLINE`, with its rational weight (0 when absent).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SplineControl {
    pub point: V3,
    pub weight: f64,
}

/// One parsed drawing entity, with coordinates in drawing units (the `$INSUNITS`
/// scale is applied only when segments are built).
#[derive(Debug, Clone, PartialEq)]
pub enum Entity {
    Line {
        a: V3,
        b: V3,
    },
    Circle {
        center: V3,
        radius: f64,
    },
    Arc {
        center: V3,
        radius: f64,
        start_deg: f64,
        end_deg: f64,
    },
    Polyline {
        closed: bool,
        vertices: Vec<PolyVertex>,
    },
    Ellipse {
        center: V3,
        major_axis: V3,
        minor_ratio: f64,
        start_param: f64,
        end_param: f64,
    },
    Spline {
        degree: i32,
        control: Vec<SplineControl>,
        fit: Vec<V3>,
        knots: Vec<f64>,
    },
    /// A block reference — the original refuses these outright.
    Insert,
}

/// Port of `DxfParser.AnnotationTypes` mapped onto ASCII DXF record names: types
/// that are never a cut path, so dropping them is the right answer.
fn is_annotation(entity_type: &str) -> bool {
    matches!(
        entity_type,
        "TEXT"
            | "MTEXT"
            | "ARCALIGNEDTEXT"
            | "RTEXT"
            | "ATTRIB"
            | "ATTDEF"
            | "DIMENSION"
            | "LEADER"
            | "TOLERANCE"
            | "POINT"
            | "SEQEND"
            | "VERTEX"
            | "XLINE"
            | "RAY"
            | "HATCH"
            | "IMAGE"
            | "WIPEOUT"
            | "OLEFRAME"
            | "OLE2FRAME"
            | "DGNUNDERLAY"
            | "DWFUNDERLAY"
            | "PDFUNDERLAY"
            | "UNDERLAY"
            | "LIGHT"
            | "SHAPE"
            | "SECTION"
    )
}

/// Port of `DxfParser.IsInnerCut`: a line on `SHEETNEST_INNER_CUT` is a cut but
/// not part of the shape.
fn is_inner_cut(layer: &str) -> bool {
    layer.eq_ignore_ascii_case(INNER_CUT_SOURCE_LAYER)
}

/// Walks the file as strictly alternating group-code/value lines, which is how
/// the ASCII DXF format is built.
fn read_pairs(text: &str, file: &str) -> Result<Vec<(i32, String)>, IoError> {
    let mut pairs = Vec::new();
    let mut expecting_code = true;
    for line in text.lines().map(str::trim) {
        if expecting_code {
            match line.parse::<i32>() {
                Ok(code) => pairs.push((code, String::new())),
                Err(e) => {
                    return Err(IoError::Malformed {
                        file: file.to_owned(),
                        reason: format!("group code line {line:?} is not an integer: {e}"),
                    });
                }
            }
        } else if let Some(last) = pairs.last_mut() {
            last.1.push_str(line);
        }
        expecting_code = !expecting_code;
    }
    Ok(pairs)
}

/// The `$INSUNITS` scale to millimetres. 4 = mm is the demo files' convention.
fn insunits_scale(pairs: &[(i32, String)]) -> f64 {
    for i in 1..pairs.len() {
        if pairs[i].0 == 9 && pairs[i].1.trim() == "$INSUNITS" {
            for pair in &pairs[i + 1..] {
                if pair.0 == 9 {
                    break;
                }
                if pair.0 == 70 {
                    if let Ok(units) = pair.1.trim().parse::<f64>() {
                        return insunits_to_mm(units);
                    }
                    break;
                }
            }
        }
    }
    1.0
}

/// DXF `$INSUNITS` code → millimetres per drawing unit. Unknown codes are left
/// alone (the C# also treats coordinates as given).
fn insunits_to_mm(units: f64) -> f64 {
    match units as i64 {
        1 => 25.4,        // inches
        2 => 304.8,       // feet
        3 => 1_609_344.0, // miles
        4 => 1.0,         // millimetres
        5 => 10.0,        // centimetres
        6 => 1_000.0,     // metres
        7 => 1_000_000.0, // kilometres
        _ => 1.0,         // unitless or exotic
    }
}

/// First numeric field `code` in the record starting at `start` (the record ends
/// at the next zero-code line).
fn numeric_field(pairs: &[(i32, String)], start: usize, code: i32) -> Option<f64> {
    let mut k = start + 1;
    while k < pairs.len() && pairs[k].0 != 0 {
        if pairs[k].0 == code {
            return pairs[k].1.trim().parse::<f64>().ok();
        }
        k += 1;
    }
    None
}

fn f_or(pairs: &[(i32, String)], start: usize, code: i32, default: f64) -> f64 {
    numeric_field(pairs, start, code).unwrap_or(default)
}

/// Point from group codes `xc`/`yc` of the record starting at `start`.
fn point_field(pairs: &[(i32, String)], start: usize, xc: i32, yc: i32) -> V3 {
    V3::new(f_or(pairs, start, xc, 0.0), f_or(pairs, start, yc, 0.0))
}

/// Layer name (group code 8) within the record starting at `start`.
fn layer_name(pairs: &[(i32, String)], start: usize) -> String {
    let mut k = start + 1;
    while k < pairs.len() && pairs[k].0 != 0 {
        if pairs[k].0 == 8 {
            return pairs[k].1.trim().to_string();
        }
        k += 1;
    }
    String::from("0")
}

/// Index of the next zero-code record after `start`.
fn next_zero(pairs: &[(i32, String)], start: usize, until: usize) -> usize {
    let mut k = start + 1;
    while k < until {
        if pairs[k].0 == 0 {
            break;
        }
        k += 1;
    }
    k
}

/// AutoCAD colour index 8 (grey) and 1 (red). SolidWorks draws bends red as
/// phantom lines on layer 9; the flat-pattern exports use grey on layer 0.
const BEND_COLOURS: [i64; 2] = [8, 1];
const BEND_LAYER: &str = "9";
const BEND_LINETYPES: [&str; 3] = ["PHANTOM", "CENTER", "DASHDOT"];

/// The ENTITIES section split into cut geometry and bend lines.
struct Graphics {
    /// Cut geometry with layer names.
    entities: Vec<(String, Entity)>,
    /// Grey LINE entities: bend marks, drawn on the paint layer, never cut.
    bends: Vec<Entity>,
}

/// The entities of the ENTITIES section with their layer names (bend lines
/// excluded). Record scanning is confined to that section: block definitions and
/// tables are not graphics.
fn read_entities(pairs: &[(i32, String)], file: &str) -> Result<Vec<(String, Entity)>, IoError> {
    Ok(read_graphics(pairs, file)?.entities)
}

/// The colour (group 62) of the record starting at `start`; ByLayer (256) if absent.
fn colour_of(pairs: &[(i32, String)], start: usize) -> i64 {
    numeric_field(pairs, start, 62).map(|c| c as i64).unwrap_or(256)
}

/// The linetype (group 6) of the record starting at `start`, upper-cased.
fn linetype_of(pairs: &[(i32, String)], start: usize) -> String {
    let mut k = start + 1;
    while k < pairs.len() && pairs[k].0 != 0 {
        if pairs[k].0 == 6 {
            return pairs[k].1.trim().to_ascii_uppercase();
        }
        k += 1;
    }
    String::new()
}

/// A LINE is a bend mark (not cut) when it is grey or red, sits on layer 9, or
/// has a phantom/centre/dash-dot linetype. Inner cuts are never bends.
fn is_bend_line(pairs: &[(i32, String)], start: usize, layer: &str) -> bool {
    if is_inner_cut(layer) {
        return false;
    }
    BEND_COLOURS.contains(&colour_of(pairs, start))
        || layer == BEND_LAYER
        || BEND_LINETYPES.contains(&linetype_of(pairs, start).as_str())
}

fn read_graphics(pairs: &[(i32, String)], file: &str) -> Result<Graphics, IoError> {
    let n = pairs.len();
    // Find the ENTITIES section.
    let mut start = None;
    let mut i = 0;
    while i + 1 < n {
        if pairs[i].0 == 0
            && pairs[i].1.trim() == "SECTION"
            && pairs[i + 1].0 == 2
            && pairs[i + 1].1.trim() == "ENTITIES"
        {
            start = Some(i + 2);
            break;
        }
        i += 1;
    }
    let Some(mut i) = start else {
        return Ok(Graphics { entities: Vec::new(), bends: Vec::new() });
    };

    let mut entities = Vec::new();
    let mut bends = Vec::new();
    while i < n {
        if pairs[i].0 != 0 {
            i += 1;
            continue;
        }
        let value = pairs[i].1.trim().to_string();
        match value.as_str() {
            "ENDSEC" | "EOF" => break,
            "LINE" => {
                let line = Entity::Line {
                    a: point_field(pairs, i, 10, 20),
                    b: point_field(pairs, i, 11, 21),
                };
                let layer = layer_name(pairs, i);
                if is_bend_line(pairs, i, &layer) {
                    bends.push(line);
                } else {
                    entities.push((layer, line));
                }
                i = next_zero(pairs, i, n);
            }
            "CIRCLE" => {
                entities.push((
                    layer_name(pairs, i),
                    Entity::Circle {
                        center: point_field(pairs, i, 10, 20),
                        radius: f_or(pairs, i, 40, 0.0),
                    },
                ));
                i = next_zero(pairs, i, n);
            }
            "ARC" => {
                entities.push((
                    layer_name(pairs, i),
                    Entity::Arc {
                        center: point_field(pairs, i, 10, 20),
                        radius: f_or(pairs, i, 40, 0.0),
                        start_deg: f_or(pairs, i, 50, 0.0),
                        end_deg: f_or(pairs, i, 51, 0.0),
                    },
                ));
                i = next_zero(pairs, i, n);
            }
            "LWPOLYLINE" => {
                entities.push((
                    layer_name(pairs, i),
                    Entity::Polyline {
                        closed: read_lwpolyline_closed(pairs, i),
                        vertices: read_lwpolyline_vertices(pairs, i),
                    },
                ));
                i = next_zero(pairs, i, n);
            }
            "POLYLINE" => {
                let closed = (f_or(pairs, i, 70, 0.0) as i64) & 1 == 1;
                let mut vertices = Vec::new();
                let mut k = next_zero(pairs, i, n);
                while k < n {
                    if pairs[k].0 == 0 {
                        match pairs[k].1.trim() {
                            "VERTEX" => {
                                vertices.push(PolyVertex {
                                    location: point_field(pairs, k, 10, 20),
                                    bulge: f_or(pairs, k, 42, 0.0),
                                });
                                k = next_zero(pairs, k, n);
                                continue;
                            }
                            "SEQEND" => {
                                k = next_zero(pairs, k, n);
                            }
                            _ => {}
                        }
                        break;
                    }
                    k += 1;
                }
                entities.push((layer_name(pairs, i), Entity::Polyline { closed, vertices }));
                i = k;
            }
            "ELLIPSE" => {
                entities.push((
                    layer_name(pairs, i),
                    Entity::Ellipse {
                        center: point_field(pairs, i, 10, 20),
                        major_axis: point_field(pairs, i, 11, 21),
                        minor_ratio: f_or(pairs, i, 40, 1.0),
                        start_param: f_or(pairs, i, 41, 0.0),
                        end_param: f_or(pairs, i, 42, 0.0),
                    },
                ));
                i = next_zero(pairs, i, n);
            }
            "SPLINE" => {
                entities.push((
                    layer_name(pairs, i),
                    Entity::Spline {
                        degree: f_or(pairs, i, 71, 0.0) as i32,
                        control: read_spline_controls(pairs, i),
                        fit: read_spline_fit_points(pairs, i),
                        knots: read_spline_knots(pairs, i),
                    },
                ));
                i = next_zero(pairs, i, n);
            }
            "INSERT" => {
                return Err(IoError::UnsupportedEntity {
                    file: file.to_owned(),
                    entity: "INSERT (geometry is drawn as a block reference; explode the blocks and save again)"
                        .to_owned(),
                });
            }
            other => {
                if is_annotation(other) {
                    i = next_zero(pairs, i, n);
                } else {
                    return Err(IoError::UnsupportedEntity {
                        file: file.to_owned(),
                        entity: format!(
                            "{other} (cannot measure it; explode it or redraw as lines, arcs, polylines, ellipses or splines)"
                        ),
                    });
                }
            }
        }
    }
    Ok(Graphics { entities, bends })
}

/// The closed flag (bit 1 of `70`) of an `LWPOLYLINE` record.
fn read_lwpolyline_closed(pairs: &[(i32, String)], start: usize) -> bool {
    let mut closed = false;
    let mut k = start + 1;
    while k < pairs.len() && pairs[k].0 != 0 {
        if pairs[k].0 == 70 {
            if let Ok(flag) = pairs[k].1.trim().parse::<i64>() {
                closed |= flag & 1 == 1;
            }
            break;
        }
        k += 1;
    }
    closed
}

/// Vertices (+ per-vertex `42` bulges) of an `LWPOLYLINE` record.
fn read_lwpolyline_vertices(pairs: &[(i32, String)], start: usize) -> Vec<PolyVertex> {
    let mut vertices: Vec<PolyVertex> = Vec::new();
    let mut cur: Option<(f64, f64, f64)> = None; // x, y, bulge
    fn flush(cur: &mut Option<(f64, f64, f64)>, vertices: &mut Vec<PolyVertex>) {
        if let Some((x, y, bulge)) = cur.take() {
            vertices.push(PolyVertex {
                location: V3::new(x, y),
                bulge,
            });
        }
    }
    let mut k = start + 1;
    while k < pairs.len() && pairs[k].0 != 0 {
        let (code, raw) = &pairs[k];
        let value = raw.trim().parse::<f64>().ok();
        match code {
            10 => {
                flush(&mut cur, &mut vertices);
                cur = value.map(|x| (x, 0.0, 0.0));
            }
            20 => {
                if let Some(entry) = cur.as_mut() {
                    entry.1 = value.unwrap_or(0.0);
                }
            }
            42 => {
                if let Some(entry) = cur.as_mut() {
                    entry.2 = value.unwrap_or(0.0);
                }
            }
            _ => {}
        }
        k += 1;
    }
    flush(&mut cur, &mut vertices);
    vertices
}

/// Control points (+weights) of a `SPLINE`: sequences of `10`/`20`/`30`/`41`,
/// ended by another `10`, a fit point (`11`) or the end of the record.
fn read_spline_controls(pairs: &[(i32, String)], start: usize) -> Vec<SplineControl> {
    let mut control: Vec<SplineControl> = Vec::new();
    let mut pending: Option<(f64, f64, f64)> = None; // x, y, weight
    let flush = |pending: &mut Option<(f64, f64, f64)>, control: &mut Vec<SplineControl>| {
        if let Some((x, y, weight)) = pending.take() {
            control.push(SplineControl {
                point: V3::new(x, y),
                weight,
            });
        }
    };
    let mut k = start + 1;
    while k < pairs.len() && pairs[k].0 != 0 {
        let (code, raw) = &pairs[k];
        let value = raw.trim().parse::<f64>().ok();
        match code {
            10 => {
                flush(&mut pending, &mut control);
                pending = value.map(|x| (x, 0.0, 0.0));
            }
            20 => {
                if let Some(entry) = pending.as_mut() {
                    entry.1 = value.unwrap_or(0.0);
                }
            }
            41 => {
                if let Some(entry) = pending.as_mut() {
                    entry.2 = value.unwrap_or(0.0);
                }
            }
            11 => break, // fit-point block follows; controls are done
            _ => {}
        }
        k += 1;
    }
    flush(&mut pending, &mut control);
    control
}

/// Fit points (`11`/`21`) of a `SPLINE`.
fn read_spline_fit_points(pairs: &[(i32, String)], start: usize) -> Vec<V3> {
    let mut fit = Vec::new();
    let mut pending: Option<(f64, f64)> = None;
    let flush = |pending: &mut Option<(f64, f64)>, fit: &mut Vec<V3>| {
        if let Some((x, y)) = pending.take() {
            fit.push(V3::new(x, y));
        }
    };
    let mut k = start + 1;
    while k < pairs.len() && pairs[k].0 != 0 {
        let (code, raw) = &pairs[k];
        let value = raw.trim().parse::<f64>().ok();
        match code {
            11 => flush(&mut pending, &mut fit),
            21 => {
                if let Some(entry) = pending.as_mut() {
                    entry.1 = value.unwrap_or(0.0);
                }
                // the fit block is the last one in the record
                if k + 1 >= pairs.len() || pairs[k + 1].0 == 0 {
                    break;
                }
            }
            _ => {}
        }
        k += 1;
    }
    flush(&mut pending, &mut fit);
    fit
}

/// Knot values (all `40` entries) of a `SPLINE`.
fn read_spline_knots(pairs: &[(i32, String)], start: usize) -> Vec<f64> {
    let mut knots = Vec::new();
    let mut k = start + 1;
    while k < pairs.len() && pairs[k].0 != 0 {
        if pairs[k].0 == 40 {
            if let Ok(v) = pairs[k].1.trim().parse::<f64>() {
                knots.push(v);
            }
        }
        k += 1;
    }
    knots
}

/// Port of `DxfParser.ArcStepDegrees` — angular step of the curve tessellation:
/// chord deviation stays within `CurveChordTolerance`, adaptive to the radius
/// and clamped to 1..6 degrees.
fn arc_step_degrees(radius: f64) -> f64 {
    if radius <= 0.0 {
        return MAX_ARC_STEP_DEG;
    }
    let cos_half = (1.0 - CURVE_CHORD_TOLERANCE / radius).clamp(-1.0, 1.0);
    let step_deg = 2.0 * cos_half.acos().to_degrees();
    step_deg.clamp(MIN_ARC_STEP_DEG, MAX_ARC_STEP_DEG)
}

/// Intermediate arc points of a polyline "bulge" segment (DXF encodes arcs
/// inside polylines as bulge = tan(includedAngle/4) on the start vertex).
/// Port of `DxfParser.BulgePoints`: the points strictly between the two
/// vertices; the vertices themselves are added by the caller. Empty for a
/// straight run.
fn bulge_points(x1: f64, y1: f64, x2: f64, y2: f64, bulge: f64) -> Vec<V3> {
    if bulge.abs() < 1e-9 {
        return Vec::new();
    }
    let theta = 4.0 * bulge.atan(); // signed included angle
    let dx = x2 - x1;
    let dy = y2 - y1;
    let chord = (dx * dx + dy * dy).sqrt();
    let sin_half = (theta / 2.0).sin();
    if chord < 1e-9 || sin_half.abs() < 1e-12 {
        return Vec::new();
    }
    let radius = chord / (2.0 * sin_half); // signed
    let mx = (x1 + x2) / 2.0;
    let my = (y1 + y2) / 2.0;
    let apothem = radius * (theta / 2.0).cos(); // signed
    let ux = -dy / chord; // left perpendicular to the chord
    let uy = dx / chord;
    let cx = mx + apothem * ux;
    let cy = my + apothem * uy;
    let abs_r = radius.abs();
    let a1 = (y1 - cy).atan2(x1 - cx);
    let step_rad = arc_step_degrees(abs_r).to_radians();
    let n = ((theta.abs() / step_rad).ceil() as usize).max(1);
    let da = theta / n as f64;
    (1..n)
        .map(|k| {
            let a = a1 + da * k as f64;
            V3::new(cx + abs_r * a.cos(), cy + abs_r * a.sin())
        })
        .collect()
}

/// Small helper: a scaled `SvgPoint` from a raw drawing coordinate. Only
/// coordinates are scaled; tessellation decisions use raw radii, exactly as the
/// C# works in drawing units throughout.
fn pt(v: V3, exact: bool, scale: f64) -> SvgPoint {
    SvgPoint {
        x: v.x * scale,
        y: v.y * scale,
        exact,
    }
}

/// Segments tessellating an ARC from the exact start point over chord samples
/// to the exact end point: `(point, is a drawing vertex)`.
fn tessellate_arc(center: V3, radius: f64, start_deg: f64, end_deg: f64) -> Vec<(V3, bool)> {
    let mut pp: Vec<(V3, bool)> = Vec::new();
    if radius > 0.0 {
        let arc_step = arc_step_degrees(radius);
        let mut a = start_deg;
        while a < end_deg {
            let rad = a.to_radians();
            pp.push((
                V3::new(center.x + radius * rad.cos(), center.y + radius * rad.sin()),
                false,
            ));
            a += arc_step;
        }
    }
    {
        let rad = end_deg.to_radians();
        pp.push((
            V3::new(center.x + radius * rad.cos(), center.y + radius * rad.sin()),
            true,
        ));
    }
    if let Some(first) = pp.first_mut() {
        first.1 = true; // the start point sits exactly at the start angle: a drawing vertex
    }
    pp
}

/// Curves chain without a wrapping closing segment (`Chain` in the C#); closed
/// rings wrap instead (`ConnectTheDots`).
enum Chaining {
    /// `DxfParser.Chain`: consecutive samples, no wrap.
    Open,
    /// `DxfParser.ConnectTheDots`: wraps the last point back to the first.
    Wrapped,
}

/// Port of `DxfParser.ApproximateEntities`: entities → segments. The segments
/// of every entity are filtered to `> RemoveThreshold`, then concatenated.
/// Layer handling: only `SHEETNEST_INNER_CUT` is skipped; annotation types were
/// dropped at parse time.
fn approximate_entities(
    entities: &[(String, Entity)],
    scale: f64,
    file: &str,
) -> Result<Vec<Segment>, IoError> {
    let mut all = Vec::new();
    for (layer, ent) in entities {
        if is_inner_cut(layer) {
            continue;
        }
        let mut elems: Vec<(V3, bool)> = Vec::new();
        let mut chaining = Chaining::Open;
        match ent {
            Entity::Line { a, b } => {
                elems.push((*a, true));
                elems.push((*b, true));
            }
            Entity::Arc {
                center,
                radius,
                start_deg,
                end_deg,
            } => {
                // DXF arcs run counter-clockwise; as in the original, a start
                // past the end is swept back a full turn instead of assumed CW.
                let mut start = *start_deg;
                if start > *end_deg {
                    start -= 360.0;
                }
                elems = tessellate_arc(*center, *radius, start, *end_deg);
            }
            Entity::Circle { center, radius } => {
                // A full ring of chords; the first point is added again so the
                // ring closes back on it, as in the original.
                let mut cc: Vec<(V3, bool)> = Vec::new();
                let circle_step = arc_step_degrees(*radius);
                let mut a = 0.0f64;
                while a < 360.0 {
                    let ang = a.to_radians();
                    cc.push((
                        V3::new(center.x + radius * ang.cos(), center.y + radius * ang.sin()),
                        false,
                    ));
                    a += circle_step;
                }
                cc.push((cc[0].0, false));
                elems = cc;
                chaining = Chaining::Wrapped;
            }
            Entity::Polyline { closed, vertices } => {
                if vertices.len() >= 2 {
                    for (vi, vert) in vertices.iter().enumerate() {
                        elems.push((vert.location, true));
                        let has_next = vi < vertices.len() - 1 || *closed;
                        if has_next && vert.bulge.abs() > 1e-9 {
                            let next = &vertices[(vi + 1) % vertices.len()];
                            for bp in bulge_points(
                                vert.location.x,
                                vert.location.y,
                                next.location.x,
                                next.location.y,
                                vert.bulge,
                            ) {
                                elems.push((bp, false)); // chord samples of the bulge arc
                            }
                        }
                    }
                    // Only an explicitly closed polyline gets the implicit last
                    // side. An open one can still form a ring by repeating its
                    // first vertex, but is never closed by invention.
                    chaining = if *closed { Chaining::Wrapped } else { Chaining::Open };
                }
            }
            Entity::Ellipse {
                center,
                major_axis,
                minor_ratio,
                start_param,
                end_param,
            } => {
                let ax = major_axis.x;
                let ay = major_axis.y;
                let major = (ax * ax + ay * ay).sqrt();
                if major >= 1e-12 {
                    let ratio = minor_ratio.abs();
                    let mut from = *start_param;
                    let mut to = *end_param;
                    if (to - from).abs() < 1e-12 {
                        to = from + std::f64::consts::TAU; // full ellipse / unset sweep
                    } else if to < from {
                        to += std::f64::consts::TAU; // sweep runs anticlockwise through zero
                    }
                    let ellipse_step = arc_step_degrees(major).to_radians();
                    let steps = (((to - from) / ellipse_step).ceil() as usize).max(2);
                    for i in 0..=steps {
                        let t = from + (to - from) * i as f64 / steps as f64;
                        let (c, s) = (t.cos(), t.sin());
                        // The minor axis is the major one turned a quarter turn and shortened by the ratio.
                        elems.push((
                            V3::new(
                                center.x + ax * c - ay * ratio * s,
                                center.y + ay * c + ax * ratio * s,
                            ),
                            i == 0 || i == steps,
                        ));
                    }
                }
            }
            Entity::Spline {
                degree,
                control,
                fit,
                knots,
            } => {
                let sp = spline_points(
                    &Spline {
                        degree: *degree,
                        control: control.as_slice(),
                        fit: fit.as_slice(),
                        knots: knots.as_slice(),
                    },
                    file,
                )?;
                elems = sp;
            }
            Entity::Insert => {
                return Err(IoError::UnsupportedEntity {
                    file: file.to_owned(),
                    entity: "INSERT (geometry is drawn as a block reference; explode the blocks and save again)"
                        .to_owned(),
                });
            }
        }

        // Segments shorter than RemoveThreshold vanish (this is what erases the
        // zero-length duplicates a full circle produces).
        let count = elems.len();
        let last = match chaining {
            Chaining::Open => count.saturating_sub(1),
            Chaining::Wrapped => count,
        };
        let mut segments: Vec<Segment> = Vec::with_capacity(count);
        for i in 0..last {
            let (v0, e0) = elems[i];
            let (v1, e1) = elems[(i + 1) % count];
            segments.push(Segment {
                start: pt(v0, e0, scale),
                end: pt(v1, e1, scale),
            });
        }
        segments.retain(|z| dist_gt_remove(z));
        all.extend(segments);
    }
    Ok(all)
}

fn dist_gt_remove(segment: &Segment) -> bool {
    let dx = segment.end.x - segment.start.x;
    let dy = segment.end.y - segment.start.y;
    (dx * dx + dy * dy) > REMOVE_THRESHOLD * REMOVE_THRESHOLD
}

/// Borrowed view of a parsed spline, keeping `spline_points` close to the C#.
struct Spline<'a> {
    degree: i32,
    control: &'a [SplineControl],
    fit: &'a [V3],
    knots: &'a [f64],
}

/// Port of `DxfParser.SplinePoints`: samples a SPLINE finely enough that it
/// strays less than `CurveChordTolerance` from the true curve. Control point
/// weights are honoured (a rational spline is how a DXF stores an exact circle
/// or ellipse in spline form). Fit points are the faithful fallback when the
/// knot vector is missing or inconsistent; they are drawing vertices, the
/// sampled points are not.
fn spline_points(spline: &Spline, file: &str) -> Result<Vec<(V3, bool)>, IoError> {
    let control = spline.control;
    let knots = spline.knots;
    let degree = (spline.degree.max(1) as usize).max(1);

    if control.len() < degree + 1 || knots.len() != control.len() + degree + 1 {
        if spline.fit.len() >= 2 {
            return Ok(spline.fit.iter().map(|p| (*p, true)).collect());
        }
        return Err(IoError::UnsupportedEntity {
            file: file.to_owned(),
            entity: format!(
                "SPLINE (its {} control points and {} knots do not describe a curve; convert it to a polyline)",
                control.len(),
                knots.len()
            ),
        });
    }

    let first = knots[degree];
    let last = knots[control.len()];
    if last - first < 1e-12 {
        return Ok(Vec::new());
    }

    // Double the sampling until the curve's own midpoints sit within tolerance
    // of the chords being emitted.
    let mut segments = 8 * (control.len() - degree).max(1);
    let mut points = sample_spline(spline, degree, first, last, segments);
    for _ in 0..5 {
        if is_flat_enough(spline, degree, first, last, segments, &points) {
            break;
        }
        segments *= 2;
        points = sample_spline(spline, degree, first, last, segments);
    }

    Ok(points.into_iter().map(|p| (p, false)).collect())
}

fn sample_spline(spline: &Spline, degree: usize, first: f64, last: f64, segments: usize) -> Vec<V3> {
    (0..=segments)
        .map(|i| spline_point(spline, degree, first + (last - first) * i as f64 / segments as f64))
        .collect()
}

/// Port of `DxfParser.IsFlatEnough`: the curve at the middle of every emitted
/// chord is compared against that chord's midpoint.
fn is_flat_enough(
    spline: &Spline,
    degree: usize,
    first: f64,
    last: f64,
    segments: usize,
    points: &[V3],
) -> bool {
    for i in 1..points.len() {
        let mid =
            spline_point(spline, degree, first + (last - first) * (i as f64 - 0.5) / segments as f64);
        if mid.dist_to(V3::new(
            (points[i - 1].x + points[i].x) / 2.0,
            (points[i - 1].y + points[i].y) / 2.0,
        )) > CURVE_CHORD_TOLERANCE
        {
            return false;
        }
    }
    true
}

/// Port of `DxfParser.SplinePoint`: de Boor's algorithm, run in homogeneous
/// coordinates so the weights come out for free.
fn spline_point(spline: &Spline, degree: usize, u: f64) -> V3 {
    let control = spline.control;
    let knots = spline.knots;
    let mut span = degree;
    while span < control.len() - 1 && u >= knots[span + 1] {
        span += 1;
    }
    let n = degree + 1;
    let mut x = vec![0.0; n];
    let mut y = vec![0.0; n];
    let mut w = vec![0.0; n];
    for (j, cp) in control[span - degree..span + 1].iter().enumerate() {
        let weight = if cp.weight > 0.0 { cp.weight } else { 1.0 }; // some writers leave an unused weight at zero
        x[j] = cp.point.x * weight;
        y[j] = cp.point.y * weight;
        w[j] = weight;
    }
    for r in 1..=degree {
        for j in (r..=degree).rev() {
            let i = span - degree + j;
            let denominator = knots[i + degree - r + 1] - knots[i];
            let a = if denominator <= 0.0 {
                0.0
            } else {
                (u - knots[i]) / denominator
            };
            x[j] = (1.0 - a) * x[j - 1] + a * x[j];
            y[j] = (1.0 - a) * y[j - 1] + a * y[j];
            w[j] = (1.0 - a) * w[j - 1] + a * w[j];
        }
    }
    let divisor = if w[degree].abs() < 1e-12 { 1.0 } else { w[degree] };
    V3::new(x[degree] / divisor, y[degree] / divisor)
}

/// Port of `DxfParser.ConnectElements`: chains the segments into contours, the
/// endpoints indexed in a grid of cells a little wider than
/// `ClosingThreshold`, so a step only looks at its own neighbourhood. Contours
/// of fewer than 3 points are dropped; the closing segment is invented (the
/// chainer joins across a gap of up to `ClosingThreshold`). The original also
/// tracks entity provenance and per-segment `Curved` flags for the nester; this
/// port keeps only the contour points, which is everything `RawDetail` carries.
fn connect_elements(elements: &[Segment]) -> Vec<Vec<SvgPoint>> {
    let endpoints = EndpointGrid::new(elements);
    let mut used = vec![false; elements.len()];
    let mut remaining = elements.len();
    let mut first_unused = 0usize;

    let mut prior = SvgPoint::new(0.0, 0.0);
    let mut points: Vec<SvgPoint> = Vec::new();
    let mut result: Vec<Vec<SvgPoint>> = Vec::new();

    while remaining > 0 {
        if points.is_empty() {
            while used[first_unused] {
                first_unused += 1;
            }
            let seg = elements[first_unused];
            points.push(seg.start);
            prior = seg.end;
            points.push(prior);
            used[first_unused] = true;
            remaining -= 1;
        } else {
            match endpoints.nearest(prior, elements, &used) {
                None => close_contour(&mut points, &mut result),
                Some(next_index) => {
                    let next = elements[next_index];
                    used[next_index] = true;
                    remaining -= 1;
                    // traverse from the nearer endpoint to the farther one
                    prior = if end_is_closest(prior, next) {
                        next.end
                    } else {
                        next.start
                    };
                    points.push(prior);
                }
            }
        }
    }
    if !points.is_empty() {
        close_contour(&mut points, &mut result);
    }
    result
}

fn close_contour(points: &mut Vec<SvgPoint>, result: &mut Vec<Vec<SvgPoint>>) {
    let mut ring = std::mem::take(points);
    if ring.len() < 3 || dist(ring[0], ring[ring.len() - 1]) > CLOSING_THRESHOLD {
        return; // an open chain is not a cut contour
    }

    // The last point is the first junction visited a second time. Keep one copy
    // and combine provenance: an exact source vertex must not become a chord
    // sample merely because the chainer reached it from the other direction.
    let closing = ring.pop().expect("length checked");
    ring[0].exact |= closing.exact;

    // Coalesce any remaining coincident neighbours at the contour tolerance.
    // Polygons are implicitly closed, so no repeated sentinel vertex is stored.
    let mut clean: Vec<SvgPoint> = Vec::with_capacity(ring.len());
    for point in ring {
        if let Some(last) = clean.last_mut() {
            if dist(*last, point) <= CONTOUR_TOLERANCE {
                last.exact |= point.exact;
                continue;
            }
        }
        clean.push(point);
    }
    if clean.len() > 1 && dist(clean[0], clean[clean.len() - 1]) <= CONTOUR_TOLERANCE {
        let closing = clean.pop().expect("length checked");
        clean[0].exact |= closing.exact;
    }

    let area = polygon_area(&clean).abs();
    if clean.len() >= 3
        && area.is_finite()
        && area > CONTOUR_TOLERANCE
        && clean.iter().all(|p| p.x.is_finite() && p.y.is_finite())
    {
        result.push(clean);
    }
}

fn end_is_closest(prior: SvgPoint, next: Segment) -> bool {
    dist(next.start, prior) < dist(next.end, prior)
}

/// Port of `DxfParser.EndpointGrid`: both endpoints of every element, bucketed
/// in square cells a little wider than `ClosingThreshold`.
struct EndpointGrid {
    cells: std::collections::HashMap<(i64, i64), Vec<usize>>,
}

impl EndpointGrid {
    const CELL: f64 = CLOSING_THRESHOLD * 1.01;

    fn new(elements: &[Segment]) -> Self {
        let mut cells = std::collections::HashMap::new();
        for (i, segment) in elements.iter().enumerate() {
            Self::add(&mut cells, segment.start, i);
            Self::add(&mut cells, segment.end, i);
        }
        Self { cells }
    }

    fn key(p: SvgPoint) -> (i64, i64) {
        ((p.x / Self::CELL).floor() as i64, (p.y / Self::CELL).floor() as i64)
    }

    fn add(
        cells: &mut std::collections::HashMap<(i64, i64), Vec<usize>>,
        p: SvgPoint,
        index: usize,
    ) {
        if !p.x.is_finite() || !p.y.is_finite() {
            return; // NaN/infinite endpoints go nowhere
        }
        cells.entry(Self::key(p)).or_default().push(index);
    }

    /// The unused element nearest `prior` within the closing gap, the earliest
    /// in the list among equals; `None` when there is none.
    fn nearest(&self, prior: SvgPoint, elements: &[Segment], used: &[bool]) -> Option<usize> {
        let (cx, cy) = Self::key(prior);
        let mut best: Option<usize> = None;
        let mut best_d = f64::MAX;
        for x in cx - 1..=cx + 1 {
            for y in cy - 1..=cy + 1 {
                let Some(list) = self.cells.get(&(x, y)) else {
                    continue;
                };
                for &i in list {
                    if used[i] {
                        continue;
                    }
                    let d = elements[i].dist_to(prior);
                    if d <= CLOSING_THRESHOLD
                        && (d < best_d || (d == best_d && best.map_or(true, |b| i < b)))
                    {
                        best_d = d;
                        best = Some(i);
                    }
                }
            }
        }
        best
    }
}

/// Port of `DxfParser.ConvertDxfToRawDetail` (plus the `$INSUNITS` scale):
/// entities → segments (with exact drawing vertices flagged) → closed chained
/// contours → the largest |area| contour becomes the outer. Only non-degenerate
/// contours strictly inside that outer are retained as children/holes; open
/// chains and unrelated disjoint contours cannot be represented by `RawDetail`
/// and are not silently promoted to holes.
pub fn convert_to_raw_detail(
    name: &str,
    entities: &[(String, Entity)],
    scale: f64,
) -> Result<RawDetail, IoError> {
    let elements = approximate_entities(entities, scale, name)?;

    // A drawing can reach here with nothing to cut (annotation only): say that
    // plainly, like the original.
    if elements.is_empty() {
        return Err(IoError::EmptyDrawing(name.to_owned()));
    }

    let contours = connect_elements(&elements);
    if contours.is_empty() || contours.iter().all(|c| c.len() < 3) {
        return Err(IoError::TooFewPoints(name.to_owned()));
    }

    // Largest by |area| is the part's outside. A RawDetail has one outer, so
    // only contours whose vertices are strictly inside it are meaningful holes.
    let mut keyed: Vec<(f64, Vec<SvgPoint>)> = contours
        .into_iter()
        .map(|c| (polygon_area(&c).abs(), c))
        .collect();
    keyed.sort_by(|a, b| b.0.partial_cmp(&a.0).unwrap_or(std::cmp::Ordering::Equal));

    let (_, outer) = keyed.remove(0);
    let children = keyed
        .into_iter()
        .map(|(_, contour)| contour)
        .filter(|contour| {
            contour.iter().all(|point| {
                point_in_polygon(*point, &outer, (0.0, 0.0)) == Some(true)
            })
        })
        .collect();
    Ok(RawDetail {
        bends: Vec::new(),
        name: name.to_owned(),
        outer,
        children,
    })
}

/// Parses an ASCII DXF file into a `RawDetail`. Port of
/// `DxfParser.ConvertDxfToRawDetail`; see the module docs for layers, units and
/// entity scope.
pub fn parse_dxf(text: &str, file: &str) -> Result<RawDetail, IoError> {
    if text
        .lines()
        .next()
        .map(|l| l.trim_start().starts_with("AutoCAD Binary DXF"))
        .unwrap_or(false)
    {
        return Err(IoError::Malformed {
            file: file.to_owned(),
            reason: "binary DXF is out of scope (ASCII only)".to_owned(),
        });
    }
    let pairs = read_pairs(text, file)?;
    let scale = insunits_scale(&pairs);
    let graphics = read_graphics(&pairs, file)?;
    let mut detail = convert_to_raw_detail(file, &graphics.entities, scale)?;
    detail.bends = bend_polylines(graphics.bends, scale, file)?;
    Ok(detail)
}

/// The bend lines of a part drawing: the grey (colour 8) LINEs, which are marks
/// for the brake and are not cut. Each run of connected lines is one open
/// polyline, in the same millimetres as the outline.
pub fn parse_dxf_bend_lines(text: &str, file: &str) -> Result<Vec<Vec<SvgPoint>>, IoError> {
    let pairs = read_pairs(text, file)?;
    let scale = insunits_scale(&pairs);
    let graphics = read_graphics(&pairs, file)?;
    bend_polylines(graphics.bends, scale, file)
}

fn bend_polylines(bends: Vec<Entity>, scale: f64, file: &str) -> Result<Vec<Vec<SvgPoint>>, IoError> {
    let entities: Vec<(String, Entity)> = bends.into_iter().map(|e| ("0".to_string(), e)).collect();
    polylines_of(&entities, scale, file)
}

/// The inner cuts of a part drawing: the lines on `SHEETNEST_INNER_CUT`, which
/// enclose nothing and are cut as they are. Each run of connected segments is one
/// open polyline, in the same millimetres as the outline.
pub fn parse_dxf_inner_cuts(text: &str, file: &str) -> Result<Vec<Vec<SvgPoint>>, IoError> {
    let pairs = read_pairs(text, file)?;
    let scale = insunits_scale(&pairs);
    let entities = read_entities(&pairs, file)?;
    let inner: Vec<(String, Entity)> = entities.into_iter().filter(|(layer, _)| is_inner_cut(layer)).collect();
    polylines_of(&inner, scale, file)
}

/// Each entity's segments chained into open polylines (runs of joined segments).
fn polylines_of(entities: &[(String, Entity)], scale: f64, file: &str) -> Result<Vec<Vec<SvgPoint>>, IoError> {
    let mut out: Vec<Vec<SvgPoint>> = Vec::new();
    for (_, ent) in entities {
        let segments = approximate_entities(&[("0".to_string(), ent.clone())], scale, file)?;
        let mut run: Vec<SvgPoint> = Vec::new();
        for s in segments {
            let joins = run.last().is_some_and(|l| (l.x - s.start.x).hypot(l.y - s.start.y) < 1e-6);
            if joins {
                run.push(s.end);
            } else {
                if run.len() >= 2 {
                    out.push(std::mem::take(&mut run));
                }
                run = vec![s.start, s.end];
            }
        }
        if run.len() >= 2 {
            out.push(run);
        }
    }
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn coord_pairs(entries: &[(&str, &str)]) -> String {
        let mut out = String::new();
        for (k, v) in entries {
            out.push_str(k);
            out.push('\n');
            out.push_str(v);
            out.push('\n');
        }
        out
    }

    fn dxf_entities(inner: &str) -> String {
        format!(
            "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n{inner}0\nENDSEC\n0\nEOF\n"
        )
    }

    fn line(x1: f64, y1: f64, x2: f64, y2: f64) -> String {
        coord_pairs(&[
            ("0", "LINE"),
            ("8", "0"),
            ("10", &x1.to_string()),
            ("20", &y1.to_string()),
            ("11", &x2.to_string()),
            ("21", &y2.to_string()),
        ])
    }

    fn parse(dxf: &str) -> Result<RawDetail, IoError> {
        parse_dxf(dxf, "test.dxf")
    }

    #[test]
    fn red_phantom_lines_are_bends_and_inner_cuts_stay_cuts() {
        let square = [
            line(0.0, 0.0, 10.0, 0.0),
            line(10.0, 0.0, 10.0, 10.0),
            line(10.0, 10.0, 0.0, 10.0),
            line(0.0, 10.0, 0.0, 0.0),
        ]
        .concat();
        let red_bend = coord_pairs(&[
            ("0", "LINE"), ("8", "9"), ("62", "1"), ("6", "PHANTOM"),
            ("10", "0"), ("20", "5"), ("11", "10"), ("21", "5"),
        ]);
        let inner = coord_pairs(&[
            ("0", "LINE"), ("8", "SHEETNEST_INNER_CUT"), ("62", "1"),
            ("10", "2"), ("20", "2"), ("11", "4"), ("21", "2"),
        ]);
        let text = dxf_entities(&format!("{square}{red_bend}{inner}"));
        let detail = parse(&text).expect("parses");
        assert_eq!(detail.outer.len(), 4);
        assert_eq!(detail.bends.len(), 1);
        let cuts = parse_dxf_inner_cuts(&text, "test.dxf").unwrap();
        assert_eq!(cuts.len(), 1, "a red line on the inner-cut layer stays a cut");
    }

    #[test]
    fn grey_lines_are_bends_not_cuts() {
        let square = [
            line(0.0, 0.0, 10.0, 0.0),
            line(10.0, 0.0, 10.0, 10.0),
            line(10.0, 10.0, 0.0, 10.0),
            line(0.0, 10.0, 0.0, 0.0),
        ]
        .concat();
        let bend = coord_pairs(&[
            ("0", "LINE"),
            ("8", "0"),
            ("62", "8"),
            ("10", "0"),
            ("20", "5"),
            ("11", "10"),
            ("21", "5"),
        ]);
        let text = dxf_entities(&format!("{square}{bend}"));
        let detail = parse(&text).expect("parses");
        assert_eq!(detail.outer.len(), 4, "the square is the outline; the grey line is not a cut");
        assert_eq!(detail.bends.len(), 1);
        assert_eq!(detail.bends[0].len(), 2);
        assert!((detail.bends[0][0].y - 5.0).abs() < 1e-9);
        assert_eq!(parse_dxf_bend_lines(&text, "test.dxf").unwrap(), detail.bends);
    }

    #[test]
    fn arc_tessellation_has_exact_ends_and_chord_interior() {
        let pts = tessellate_arc(V3::new(0.0, 0.0), 10.0, 0.0, 360.0);
        assert!(pts.len() >= 24);
        assert!(pts.first().unwrap().1);
        assert!(pts.last().unwrap().1);
        assert!(!pts[pts.len() / 2].1);
        let last = pts.last().unwrap().0;
        assert!((last.x - 10.0).abs() < 0.15 && last.y.abs() < 0.15);
    }

    #[test]
    fn parses_one_line_entity() {
        let dxf = dxf_entities(&line(0.0, 0.0, 5.0, 5.0));
        let entities = read_entities(&read_pairs(&dxf, "t").unwrap(), "t").unwrap();
        assert_eq!(entities.len(), 1);
        assert_eq!(entities[0].0, "0");
        match &entities[0].1 {
            Entity::Line { a, b } => {
                assert_eq!(a.x, 0.0);
                assert_eq!(b.x, 5.0);
                assert_eq!(b.y, 5.0);
            }
            _ => panic!("expected line"),
        }
    }

    #[test]
    fn parses_circle_arc_and_flags() {
        let dxf = dxf_entities(concat!(
            "0\nCIRCLE\n8\nCUT\n10\n3.0\n20\n4.0\n40\n2.5\n",
            "0\nARC\n8\nCUT\n10\n0.0\n20\n0.0\n40\n1.0\n50\n90.0\n51\n180.0\n",
        ));
        let entities = read_entities(&read_pairs(&dxf, "t").unwrap(), "t").unwrap();
        assert_eq!(entities.len(), 2);
        match &entities[0].1 {
            Entity::Circle { center, radius } => {
                assert_eq!(center.x, 3.0);
                assert_eq!(*radius, 2.5);
            }
            _ => panic!("expected circle"),
        }
        match &entities[1].1 {
            Entity::Arc {
                start_deg,
                end_deg,
                ..
            } => {
                assert_eq!(*start_deg, 90.0);
                assert_eq!(*end_deg, 180.0);
            }
            _ => panic!("expected arc"),
        }
    }

    #[test]
    fn assembly_chains_a_square_of_lines() {
        let dxf = dxf_entities(concat!(
            "0\nLINE\n8\n0\n10\n0\n20\n0\n11\n10\n21\n0\n",
            "0\nLINE\n8\n0\n10\n10\n20\n0\n11\n10\n21\n10\n",
            "0\nLINE\n8\n0\n10\n10\n20\n10\n11\n0\n21\n10\n",
            "0\nLINE\n8\n0\n10\n0\n20\n10\n11\n0\n21\n0\n",
        ));
        let detail = parse(&dxf).unwrap();
        assert_eq!(detail.children.len(), 0);
        assert_eq!(detail.outer.len(), 4);
        assert!((polygon_area(&detail.outer).abs() - 100.0).abs() < 1e-9);
        assert!(detail.outer.iter().all(|p| p.exact));
    }

    #[test]
    fn assembly_picks_the_largest_contour_as_outer() {
        let dxf = dxf_entities(
            &(square_inner(0.0, 0.0, 10.0) + &square_inner(200.0, 200.0, 100.0)),
        );
        let detail = parse(&dxf).unwrap();
        assert!((polygon_area(&detail.outer).abs() - 10_000.0).abs() < 1e-9);
        assert!(detail.children.is_empty(), "a disjoint square is not a hole");
    }

    #[test]
    fn assembly_keeps_contours_inside_the_outer_as_holes() {
        let dxf = dxf_entities(
            &(square_inner(0.0, 0.0, 100.0) + &square_inner(20.0, 20.0, 10.0)),
        );
        let detail = parse(&dxf).unwrap();
        assert!((polygon_area(&detail.outer).abs() - 10_000.0).abs() < 1e-9);
        assert_eq!(detail.children.len(), 1);
        assert!((polygon_area(&detail.children[0]).abs() - 100.0).abs() < 1e-9);
    }

    fn square_inner(ox: f64, oy: f64, s: f64) -> String {
        line(ox, oy, ox + s, oy)
            + &line(ox + s, oy, ox + s, oy + s)
            + &line(ox + s, oy + s, ox, oy + s)
            + &line(ox, oy + s, ox, oy)
    }

    #[test]
    fn an_open_chain_is_not_closed_into_a_contour() {
        let dxf = dxf_entities(&(
            line(0.0, 0.0, 10.0, 0.0)
                + &line(10.0, 0.0, 10.0, 10.0)
                + &line(10.0, 10.0, 0.0, 10.0)
        ));
        assert!(matches!(parse(&dxf), Err(IoError::TooFewPoints(_))));
    }

    #[test]
    fn open_chain_shorter_than_three_points_is_discarded() {
        // a single open line: 1 segment → 2-point contour → dropped
        let dxf = dxf_entities(&line(0.0, 0.0, 5.0, 0.0));
        assert!(matches!(parse(&dxf), Err(IoError::TooFewPoints(_))));
    }

    #[test]
    fn annotation_only_drawing_is_reported() {
        let dxf = dxf_entities("0\nTEXT\n8\n0\n10\n0\n20\n0\n40\n1\n1\nhello\n");
        match parse(&dxf) {
            Err(IoError::EmptyDrawing(f)) => assert_eq!(f, "test.dxf"),
            other => panic!("expected EmptyDrawing, got {other:?}"),
        }
    }

    #[test]
    fn malformed_group_code_is_an_error_not_a_panic() {
        let dxf = "not-a-group-code\nSECTION\n";
        assert!(matches!(parse(dxf), Err(IoError::Malformed { .. })));
    }

    #[test]
    fn binary_dxf_is_refused() {
        let dxf = "AutoCAD Binary DXF\r\n\u{1a}\u{0}";
        assert!(matches!(parse(&dxf), Err(IoError::Malformed { .. })));
    }

    #[test]
    fn insunits_inches_scale_coordinates_to_mm() {
        let dxf = "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nCIRCLE\n8\n0\n10\n0\n20\n0\n40\n1\n0\nENDSEC\n0\nEOF\n";
        let detail = parse(dxf).unwrap();
        assert_eq!(detail.children.len(), 0);
        let max_x = detail.outer.iter().map(|p| p.x).fold(f64::MIN, f64::max);
        assert!((max_x - 25.4).abs() < 1e-6, "inch circle scaled to mm: {max_x}");
    }

    #[test]
    fn concentric_circles_are_outer_and_hole() {
        let dxf = dxf_entities(concat!(
            "0\nCIRCLE\n8\n0\n10\n45\n20\n45\n30\n0\n40\n45\n",
            "0\nCIRCLE\n8\n0\n10\n45\n20\n45\n30\n0\n40\n10\n",
        ));
        let detail = parse(&dxf).unwrap();
        assert_eq!(detail.children.len(), 1, "smaller circle is a hole");
        assert!(!detail.outer.first().unwrap().exact);
        assert!((polygon_area(&detail.outer).abs() - std::f64::consts::PI * 45.0f64.powi(2)).abs() < 30.0);
    }

    #[test]
    fn lwpolyline_bulge_tessellates_between_vertices() {
        // 2-vertex closed polyline with a half-circle bulge (bulge = tan(45°))
        let dxf = dxf_entities(concat!(
            "0\nLWPOLYLINE\n8\n0\n70\n1\n",
            "10\n0\n20\n0\n42\n1\n",
            "10\n10\n20\n0\n",
        ));
        let detail = parse(&dxf).unwrap();
        assert_eq!(detail.children.len(), 0);
        assert!(detail.outer.iter().filter(|p| p.exact).count() == 2);
        assert!(
            polygon_area(&detail.outer).abs() > 35.0,
            "half disc area ≈ 39.3, got {:?}",
            polygon_area(&detail.outer)
        );
    }

    #[test]
    fn inner_cut_lines_come_out_as_open_cuts_and_not_as_outline() {
        let slit = coord_pairs(&[("0", "LINE"), ("8", "SHEETNEST_INNER_CUT"), ("10", "10"), ("20", "10"), ("11", "20"), ("21", "10")]);
        let dxf = dxf_entities(&(line(0.0, 0.0, 30.0, 0.0) + &line(30.0, 0.0, 30.0, 30.0) + &line(30.0, 30.0, 0.0, 30.0) + &line(0.0, 30.0, 0.0, 0.0) + &slit));
        let cuts = parse_dxf_inner_cuts(&dxf, "test.dxf").expect("cuts");
        assert_eq!(cuts.len(), 1);
        assert_eq!(cuts[0].len(), 2);
        assert!((cuts[0][0].x - 10.0).abs() < 1e-9 && (cuts[0][1].x - 20.0).abs() < 1e-9);
        // The outline is the square alone.
        assert_eq!(parse(&dxf).expect("square").outer.len(), 4);
    }

    #[test]
    fn inner_cut_layer_entities_are_not_part_of_the_shape() {
        let dxf = dxf_entities(concat!(
            "0\nCIRCLE\n8\n0\n10\n0\n20\n0\n40\n10\n",
            "0\nLINE\n8\nsheetnest_inner_cut\n10\n0\n20\n0\n11\n5\n21\n0\n",
        ));
        let detail = parse(&dxf).unwrap();
        // the SHEETNEST_INNER_CUT line drew nothing that chains a contour
        assert!(detail.outer.len() >= 3);
        assert_eq!(detail.children.len(), 0);
    }
}
