//! Faithful port of `DeepNestLib.Core/IO/ChfReader.cs` (ACVSheetNest).
//!
//! Reads a CHF drawing (SC2000 laser CAM format) into a `RawDetail`.
//! Format: plain text, opens with `scFlie`, then `<Begin Graphs>` and count.
//! Each `####graph` is kind 8 (contour), 9 (group), or 10 (text).
//! Glyph pieces: 2=line, 3=arc, 4=circle, 6=polyline, 7=spline.
//! Layer mapping follows `LayerFor`: cam 1,8 -> PAINT, cam 10 -> background (skipped), others -> CUT.

use crate::{IoError, OutputLayer, RawDetail};
use nest_geom::{point_in_polygon, polygon_area, SvgPoint};

const SIGNATURE: &str = "scFlie";
const GRAPH_CONTOUR: i32 = 8;
const GRAPH_GROUP: i32 = 9;
const GRAPH_TEXT: i32 = 10;
const BACKGROUND: i32 = 10;
const CLOSING_TOL: f64 = 1e-3;

pub fn is_chf_bytes(bytes: &[u8]) -> bool {
    if bytes.len() < SIGNATURE.len() {
        return false;
    }
    let start = if bytes.len() >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF { 3 } else { 0 };
    bytes.len() >= start + SIGNATURE.len() && &bytes[start..start + SIGNATURE.len()] == SIGNATURE.as_bytes()
}

fn layer_for(cam_layer: i32) -> Option<OutputLayer> {
    match cam_layer {
        1 | 8 => Some(OutputLayer::Paint),
        BACKGROUND => None,
        _ => Some(OutputLayer::Cut),
    }
}

fn bad(file: &str, graph: &str, what: &str) -> IoError {
    let g = graph.trim_start_matches('#').trim();
    IoError::Malformed { file: file.to_string(), reason: format!("CHF {g}: {what}") }
}

fn parse_i32(lines: &[String], i: usize, file: &str, graph: &str) -> Result<i32, IoError> {
    lines.get(i)
        .and_then(|s| s.trim().parse::<i32>().ok())
        .ok_or_else(|| bad(file, graph, &format!("expected a whole number at line {}", i + 1)))
}

fn parse_f64(lines: &[String], i: usize, file: &str, graph: &str) -> Result<f64, IoError> {
    lines.get(i)
        .and_then(|s| s.trim().parse::<f64>().ok())
        .ok_or_else(|| bad(file, graph, &format!("expected a number at line {}", i + 1)))
}

fn parse_point(lines: &[String], i: usize, file: &str, graph: &str) -> Result<(f64, f64), IoError> {
    let s = lines.get(i).ok_or_else(|| bad(file, graph, &format!("expected a point \"x,y\" at line {}", i + 1)))?;
    let mut parts = s.trim().split(',');
    let x = parts.next().and_then(|v| v.parse::<f64>().ok());
    let y = parts.next().and_then(|v| v.parse::<f64>().ok());
    match (x, y, parts.next()) {
        (Some(x), Some(y), None) => Ok((x, y)),
        _ => Err(bad(file, graph, &format!("expected a point \"x,y\" at line {}", i + 1))),
    }
}

fn degrees(rad: f64) -> f64 {
    let mut d = (rad * 180.0 / std::f64::consts::PI) % 360.0;
    if d < 0.0 { d += 360.0; }
    d
}

fn push_pt(out: &mut Vec<SvgPoint>, x: f64, y: f64) {
    if let Some(last) = out.last() {
        if (last.x - x).abs() <= 1e-9 && (last.y - y).abs() <= 1e-9 { return; }
    }
    out.push(SvgPoint { x, y, exact: false });
}

fn tessellate_arc(out: &mut Vec<SvgPoint>, cx: f64, cy: f64, r: f64, start_deg: f64, end_deg: f64) {
    let mut sweep = end_deg - start_deg;
    while sweep <= 0.0 { sweep += 360.0; }
    let step = (2.0f64).max((5.0 / r.max(1e-6)).to_degrees().max(1.0)); // reasonable tessellation
    let mut a = start_deg;
    while a < start_deg + sweep - 1e-9 {
        let rad = a.to_radians();
        push_pt(out, cx + r * rad.cos(), cy + r * rad.sin());
        a += step.min(start_deg + sweep - a).max(step);
        if (a - start_deg).abs() < 1e-12 { break; }
    }
    let rad = (start_deg + sweep).to_radians();
    push_pt(out, cx + r * rad.cos(), cy + r * rad.sin());
}

fn arc_entity_points(cx: f64, cy: f64, r: f64, from: f64, to: f64) -> Vec<SvgPoint> {
    let sweep = to - from;
    if sweep.abs() >= (2.0 * std::f64::consts::PI) - 1e-9 {
        // full circle
        let mut pts = Vec::new();
        tessellate_arc(&mut pts, cx, cy, r, 0.0, 360.0);
        return pts;
    }
    let (start, end) = if sweep > 0.0 { (from, to) } else { (to, from) };
    let mut pts = Vec::new();
    tessellate_arc(&mut pts, cx, cy, r, degrees(start), degrees(end));
    pts
}

fn bulge_arc_points(x1: f64, y1: f64, x2: f64, y2: f64, bulge: f64) -> Vec<SvgPoint> {
    if bulge.abs() < 1e-12 {
        return vec![SvgPoint { x: x1, y: y1, exact: true }, SvgPoint { x: x2, y: y2, exact: true }];
    }
    let dx = x2 - x1;
    let dy = y2 - y1;
    let chord = (dx*dx + dy*dy).sqrt();
    if chord < 1e-12 { return vec![SvgPoint { x: x1, y: y1, exact: true }]; }
    let theta = 4.0 * bulge.atan();
    let r = chord / (2.0 * (theta / 2.0).sin().abs().max(1e-12));
    // center: midpoint rotated
    let mx = (x1 + x2) / 2.0;
    let my = (y1 + y2) / 2.0;
    let h = (r*r - (chord/2.0)*(chord/2.0)).max(0.0).sqrt();
    let ux = -dy / chord;
    let uy = dx / chord;
    let sign = if bulge > 0.0 { 1.0 } else { -1.0 };
    let cx = mx + ux * h * sign;
    let cy = my + uy * h * sign;
    let a1 = (y1 - cy).atan2(x1 - cx).to_degrees();
    let a2 = (y2 - cy).atan2(x2 - cx).to_degrees();
    let mut pts = Vec::new();
    if bulge > 0.0 {
        tessellate_arc(&mut pts, cx, cy, r, a1, a2);
    } else {
        // clockwise: go the other way by swapping and reversing
        tessellate_arc(&mut pts, cx, cy, r, a2, a1);
        pts.reverse();
    }
    pts
}

// Minimal de Boor spline sampling (degree derived like C#)
fn sample_spline_bspline(control: &[(f64,f64)], knots: &[f64], degree: usize, segments: usize) -> Vec<SvgPoint> {
    if control.is_empty() { return vec![]; }
    if degree == 0 || degree >= control.len() || knots.len() != control.len() + degree + 1 {
        // fallback: polyline through control points
        return control.iter().map(|&(x,y)| SvgPoint{x,y,exact:false}).collect();
    }
    let first = knots[degree];
    let last = knots[control.len()];
    let mut out = Vec::with_capacity(segments+1);
    for i in 0..=segments {
        let u = first + (last - first) * i as f64 / segments as f64;
        let mut span = degree;
        while span < control.len() - 1 && u >= knots[span + 1] { span += 1; }
        let n = degree + 1;
        let mut x = vec![0.0; n];
        let mut y = vec![0.0; n];
        let mut w = vec![1.0; n];
        for (j, &(cx,cy)) in control[span-degree..=span].iter().enumerate() { x[j]=cx; y[j]=cy; }
        for r in 1..=degree {
            for j in (r..=degree).rev() {
                let ii = span - degree + j;
                let denom = knots[ii + degree - r + 1] - knots[ii];
                let a = if denom.abs() <= 1e-12 { 0.0 } else { (u - knots[ii]) / denom };
                x[j] = (1.0-a)*x[j-1] + a*x[j];
                y[j] = (1.0-a)*y[j-1] + a*y[j];
                w[j] = (1.0-a)*w[j-1] + a*w[j];
            }
        }
        let d = if w[degree].abs() < 1e-12 { 1.0 } else { w[degree] };
        push_pt(&mut out, x[degree]/d, y[degree]/d);
    }
    out
}

struct ReadCtx<'a> { lines: &'a [String], file: &'a str }

fn read_glyphs(ctx: &ReadCtx, mut i: usize, graph: &str, contours: &mut Vec<(OutputLayer, Vec<SvgPoint>)>) -> Result<usize, IoError> {
    if ctx.lines.get(i).map(|s| s.as_str()) != Some("<Glyphs>") {
        return Err(bad(ctx.file, graph, &format!("expected its pieces at line {}", i + 1)));
    }
    i += 1;
    i += 5; // length, box min/max, start, end
    let pieces = parse_i32(ctx.lines, i, ctx.file, graph)? as usize;
    i += 1;
    let mut points: Vec<SvgPoint> = Vec::new();
    for p in 0..pieces {
        if !ctx.lines.get(i).map(|s| s.starts_with("####Gly")).unwrap_or(false) {
            let found = ctx.lines.get(i).cloned().unwrap_or_else(|| "the end of the file".into());
            return Err(bad(ctx.file, graph, &format!("expected piece {}, found \"{}\"", p + 1, found)));
        }
        i += 2; // heading + direction
        let ptype = parse_i32(ctx.lines, i, ctx.file, graph)?; i += 1;
        match ptype {
            2 => { // line: two points
                let (x1,y1)=parse_point(ctx.lines,i,ctx.file,graph)?; i+=1;
                let (x2,y2)=parse_point(ctx.lines,i,ctx.file,graph)?; i+=1;
                if points.is_empty() { push_pt(&mut points,x1,y1); }
                push_pt(&mut points,x2,y2);
            }
            3 => { // arc: center, radius, from, to (radians)
                let (cx,cy)=parse_point(ctx.lines,i,ctx.file,graph)?; i+=1;
                let r=parse_f64(ctx.lines,i,ctx.file,graph)?; i+=1;
                let from=parse_f64(ctx.lines,i,ctx.file,graph)?; i+=1;
                let to=parse_f64(ctx.lines,i,ctx.file,graph)?; i+=1;
                let pts = arc_entity_points(cx,cy,r,from,to);
                for (k,pt) in pts.into_iter().enumerate() {
                    if k==0 && !points.is_empty() { /* avoid dup start */ } else if k==0 && points.is_empty() { push_pt(&mut points,pt.x,pt.y); continue; }
                    if k>0 || !points.is_empty() { push_pt(&mut points,pt.x,pt.y); }
                }
            }
            4 => { // circle
                let (cx,cy)=parse_point(ctx.lines,i,ctx.file,graph)?; i+=1;
                let r=parse_f64(ctx.lines,i,ctx.file,graph)?; i+=1;
                let mut pts = Vec::new();
                tessellate_arc(&mut pts,cx,cy,r,0.0,360.0);
                // circle is its own closed contour piece; flush existing? treat as points
                for pt in pts { push_pt(&mut points, pt.x, pt.y); }
            }
            6 => { // polyline: closed flag, count, then vertex+bulge pairs
                let closed = parse_i32(ctx.lines,i,ctx.file,graph)? == 1; i+=1;
                let count = parse_i32(ctx.lines,i,ctx.file,graph)? as usize; i+=1;
                let mut verts: Vec<(f64,f64,f64)> = Vec::with_capacity(count);
                for _ in 0..count {
                    let (x,y)=parse_point(ctx.lines,i,ctx.file,graph)?; i+=1;
                    let b=parse_f64(ctx.lines,i,ctx.file,graph)?; i+=1;
                    verts.push((x,y,b));
                }
                if count > 0 {
                    let segs = if closed { count } else { count.saturating_sub(1) };
                    for s in 0..segs {
                        let (x1,y1,b)=verts[s];
                        let (x2,y2,_)=verts[(s+1)%count];
                        let seg = bulge_arc_points(x1,y1,x2,y2,b);
                        for (k,pt) in seg.into_iter().enumerate() {
                            if k==0 && !points.is_empty() { continue; }
                            if points.is_empty() { points.push(pt); } else { push_pt(&mut points,pt.x,pt.y); }
                        }
                    }
                }
                let _ = closed; // closure handled by contour assembly below
            }
            7 => { // spline: 2 ignored numbers, control points, knots
                i += 2; // two numbers of no meaning
                let controls_n = parse_i32(ctx.lines,i,ctx.file,graph)? as usize; i+=1;
                let mut ctrl = Vec::with_capacity(controls_n);
                for _ in 0..controls_n { let (x,y)=parse_point(ctx.lines,i,ctx.file,graph)?; i+=1; ctrl.push((x,y)); }
                let knots_n = parse_i32(ctx.lines,i,ctx.file,graph)? as usize; i+=1;
                let mut knots = Vec::with_capacity(knots_n);
                for _ in 0..knots_n { knots.push(parse_f64(ctx.lines,i,ctx.file,graph)?); i+=1; }
                let degree = if knots_n >= controls_n + 1 { knots_n - controls_n - 1 } else { 1 };
                let pts = sample_spline_bspline(&ctrl, &knots, degree, (controls_n.max(2)-1)*8);
                for (k,pt) in pts.into_iter().enumerate() {
                    if k==0 && !points.is_empty() { continue; }
                    push_pt(&mut points, pt.x, pt.y);
                }
            }
            other => return Err(bad(ctx.file, graph, &format!("a piece of type {other}, which is not supported yet"))),
        }
    }
    if ctx.lines.get(i).map(|s| s.as_str()) != Some("<End Glyphs>") {
        return Err(bad(ctx.file, graph, "its pieces do not end where they should"));
    }
    let cam_layer = parse_i32(ctx.lines, i + 1, ctx.file, graph)?;
    // i+2 is outer/inner kind - not needed for geometry here
    if let Some(layer) = layer_for(cam_layer) {
        // only CUT contours become nesting geometry; PAINT is bend/engrave (keep if needed later)
        if layer == OutputLayer::Cut && points.len() >= 2 {
            contours.push((layer, points));
        }
    }
    i += 3; // <End Glyphs>, layer, outer/inner
    if ctx.lines.get(i).map(|s| s.as_str()) != Some("<Crafts>") {
        return Err(bad(ctx.file, graph, &format!("expected its machining settings at line {}", i + 1)));
    }
    while i < ctx.lines.len() && ctx.lines[i] != "<End Crafts>" { i += 1; }
    Ok(i + 1)
}

fn read_members(ctx: &ReadCtx, mut i: usize, graph: &str, contours: &mut Vec<(OutputLayer, Vec<SvgPoint>)>) -> Result<usize, IoError> {
    let members = parse_i32(ctx.lines, i, ctx.file, graph)? as usize; i += 1;
    for m in 0..members {
        if !ctx.lines.get(i).map(|s| s.starts_with("####Group elem")).unwrap_or(false) {
            return Err(bad(ctx.file, graph, &format!("expected member {} of {} at line {}", m + 1, members, i + 1)));
        }
        i += 2; // heading and tolerance
        i = read_glyphs(ctx, i, graph, contours)?;
    }
    Ok(i + 2) // group's own layer and kind
}

pub fn parse_chf(text: &str, file: &str) -> Result<RawDetail, IoError> {
    let lines: Vec<String> = text.lines().map(|l| l.trim().to_string()).collect();
    let ctx = ReadCtx { lines: &lines, file };
    let mut i = 0usize;
    if lines.is_empty() || lines[0] != SIGNATURE {
        return Err(IoError::Malformed { file: file.to_string(), reason: "This is not a CHF drawing (it does not open with scFlie).".into() });
    }
    while i < lines.len() && lines[i] != "<Begin Graphs>" { i += 1; }
    if i >= lines.len() { return Err(IoError::Malformed { file: file.to_string(), reason: "missing <Begin Graphs>".into() }); }
    i += 1;
    let graphs = parse_i32(&lines, i, file, "the drawing")? as usize; i += 1;
    let mut contours: Vec<(OutputLayer, Vec<SvgPoint>)> = Vec::new();
    for g in 0..graphs {
        if i >= lines.len() || !lines[i].starts_with("####graph") {
            return Err(bad(file, "the drawing", &format!("expected contour {} of {} at line {}", g + 1, graphs, i + 1)));
        }
        let graph = lines[i].clone(); i += 1;
        let kind = parse_i32(&lines, i, file, &graph)?; i += 1;
        match kind {
            GRAPH_CONTOUR => { i += 1; /* tolerance */ i = read_glyphs(&ctx, i, &graph, &mut contours)?; }
            GRAPH_GROUP => { i += 5; /* length, box min/max, start, end */ i = read_members(&ctx, i, &graph, &mut contours)?; }
            GRAPH_TEXT => {
                i += 5;
                while i < lines.len() && lines[i] != "<End Text para>" { i += 1; }
                if i < lines.len() { i += 1; }
                i += 5;
                i = read_members(&ctx, i, &graph, &mut contours)?;
            }
            other => return Err(bad(file, &graph, &format!("a contour of kind {other}, which is not supported yet"))),
        }
    }

    // Build RawDetail from CUT contours: close rings, pick largest by area as outer, rest as children
    let mut rings: Vec<Vec<SvgPoint>> = Vec::new();
    for (_, mut pts) in contours {
        if pts.len() < 2 { continue; }
        // close if needed
        let first = pts[0]; let last = *pts.last().unwrap();
        let dx = first.x-last.x; let dy = first.y-last.y;
        if (dx*dx+dy*dy).sqrt() > CLOSING_TOL {
            pts.push(first);
        } else if pts.len() > 1 {
            // ensure exact closure point not duplicated oddly
            *pts.last_mut().unwrap() = first;
        }
        if pts.len() >= 4 { // at least 3 unique + closure
            rings.push(pts);
        }
    }
    if rings.is_empty() { return Err(IoError::EmptyDrawing(file.to_string())); }
    // find outer = largest absolute area
    let mut best = 0usize; let mut best_a = -1.0f64;
    for (idx, r) in rings.iter().enumerate() {
        let a = polygon_area(r).abs();
        if a > best_a { best_a = a; best = idx; }
    }
    let outer = rings.remove(best);
    // keep as children those inside outer or significant; simple filter like DXF-ish
    let children: Vec<Vec<SvgPoint>> = rings.into_iter().filter(|r| {
        let a = polygon_area(r).abs();
        if a < 1e-6 { return false; }
        r.iter().any(|p| point_in_polygon(*p, &outer, (0.0, 0.0)).unwrap_or(false))
    }).collect();

    Ok(RawDetail {
        bends: Vec::new(),
        name: std::path::Path::new(file).file_stem().and_then(|s| s.to_str()).unwrap_or("chf").to_string(),
        outer,
        children,
    })
}

pub fn load_chf(path: &std::path::Path) -> Result<RawDetail, IoError> {
    let bytes = std::fs::read(path)?;
    let text = String::from_utf8_lossy(&bytes);
    parse_chf(&text, &path.display().to_string())
}
