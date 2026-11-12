//! Drawing import/export.
//!
//! Ported from `DeepNestLib.Core/IO` (ACVSheetNest): `RawDetail`, `DxfParser`,
//! `DxfExporter`, `ChfReader` (CHF/SC2000), `PartShapes`, `PartLibrary`.
//!
//! The layers of the exported DXF follow the original:
//! - `0` (cut) — cutting geometry, written first;
//! - `9` (paint) — bend lines and engraved part names;
//! - `Background` — sheet outline (offcuts of any shape).

use nest_geom::SvgPoint;
use thiserror::Error;

/// A flat-pattern detail as parsed: one outer contour plus holes.
/// Ported from `RawDetail`/`IRawDetail`.
#[derive(Debug, Clone, Default, PartialEq, serde::Serialize, serde::Deserialize)]
pub struct RawDetail {
    pub name: String,
    pub outer: Vec<SvgPoint>,
    pub children: Vec<Vec<SvgPoint>>,
    /// Bend lines (open polylines, same millimetres as `outer`). They are not cut:
    /// the layout carries them to the paint layer so the brake operator sees them.
    #[serde(default)]
    pub bends: Vec<Vec<SvgPoint>>,
}

#[derive(Debug, Error)]
pub enum IoError {
    #[error("io: {0}")]
    Io(#[from] std::io::Error),
    #[error("unsupported entity in {file}: {entity}")]
    UnsupportedEntity { file: String, entity: String },
    #[error("no contours found in {0}")]
    EmptyDrawing(String),
    #[error("malformed ASCII DXF in {file}: {reason}")]
    Malformed { file: String, reason: String },
    /// Port of `DxfParser.ConvertDxfToRawDetail` throwing `"Too few points"`.
    #[error("too few points in {0}")]
    TooFewPoints(String),
}

/// Output layers of the exported DXF (`DxfOutputLayers`).
#[derive(Debug, Clone, Copy, PartialEq, Eq, serde::Serialize, serde::Deserialize)]
pub enum OutputLayer {
    Cut,
    Paint,
    Background,
}

impl OutputLayer {
    pub fn as_str(self) -> &'static str {
        match self {
            OutputLayer::Cut => "0",
            OutputLayer::Paint => "9",
            OutputLayer::Background => "Background",
        }
    }
}

pub mod chf;
pub mod dxf;
pub mod export;
pub mod shapes;

pub use chf::{is_chf_bytes, load_chf, parse_chf};
pub use export::{encode_dxf, export_dxf, export_layout, LayoutExport, PlacedPart, SheetOutline};
pub use dxf::{parse_dxf, parse_dxf_bend_lines, parse_dxf_inner_cuts};
pub use shapes::{circle, perforated, perforated_tape, rectangle, square, washer, PerforatedKind};
pub use raw_detail_helpers::{load_detail, load_inner_cuts};

mod raw_detail_helpers {
    use super::{IoError, RawDetail};
    use nest_geom::SvgPoint;
    use std::path::Path;

    /// The inner cuts (`SHEETNEST_INNER_CUT`) of a DXF part drawing; none for other files or on error.
    pub fn load_inner_cuts(path: &Path) -> Vec<Vec<SvgPoint>> {
        let is_dxf = path.extension().and_then(|e| e.to_str()).is_some_and(|e| e.eq_ignore_ascii_case("dxf"));
        if !is_dxf { return Vec::new(); }
        match std::fs::read_to_string(path) {
            Ok(text) => super::parse_dxf_inner_cuts(&text, &path.display().to_string()).unwrap_or_default(),
            Err(_) => Vec::new(),
        }
    }


    /// Load a detail from `.dxf` or `.chf` (detected by content or extension).
    pub fn load_detail(path: &Path) -> Result<RawDetail, IoError> {
        let bytes = std::fs::read(path)?;
        if super::chf::is_chf_bytes(&bytes) {
            let text = String::from_utf8_lossy(&bytes);
            return super::chf::parse_chf(&text, &path.display().to_string());
        }
        match path.extension().and_then(|e| e.to_str()).map(str::to_ascii_lowercase) {
            Some(ext) => match ext.as_str() {
                "dxf" => {
                    let text = String::from_utf8_lossy(&bytes);
                    super::dxf::parse_dxf(&text, &path.display().to_string())
                }
                "chf" => {
                    let text = String::from_utf8_lossy(&bytes);
                    super::chf::parse_chf(&text, &path.display().to_string())
                }
                other => Err(IoError::UnsupportedEntity {
                    file: path.display().to_string(),
                    entity: other.to_string(),
                }),
            },
            None => Err(IoError::UnsupportedEntity {
                file: path.display().to_string(),
                entity: "<no extension>".to_string(),
            }),
        }
    }
}
