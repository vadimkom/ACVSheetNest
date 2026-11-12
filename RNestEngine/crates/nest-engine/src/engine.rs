//! The `nest()` entry point. Port companion of `NestingContext` (load parts/sheets)
//! delegating to the `SvgNest` port for placement.

use crate::{
    Config, DetailLoadInfo, NestError, NestResult, SheetLoadInfo,
};
use nest_io::RawDetail;
use std::collections::HashMap;

/// Catalogue resolves a part name to its flat-pattern geometry. Build one with
/// [`CatalogueBuilder`], or pass any closure.
pub type Catalogue = dyn Fn(&str) -> RawDetail;

/// Takes the job (sheets + parts + geometry catalogue + config) and returns the
/// finished nesting.
///
/// Port companion of `NestingContext.LaunchNest`.
pub fn nest(
    sheets: &[SheetLoadInfo],
    parts: &[DetailLoadInfo],
    catalogue: impl Fn(&str) -> RawDetail,
    config: Config,
) -> Result<NestResult, NestError> {
    crate::svgnest::svgnest_nest(sheets, parts, &catalogue, config)
}

/// Collects geometry for part names from files or memory before a nest starts, so
/// that IO cannot fail mid-run.
pub struct CatalogueBuilder {
    entries: HashMap<String, CatalogueEntry>,
}

enum CatalogueEntry {
    OnDisk(std::path::PathBuf),
    InMemory(RawDetail),
}

impl Default for CatalogueBuilder {
    fn default() -> Self {
        Self::new()
    }
}

impl CatalogueBuilder {
    #[must_use]
    pub fn new() -> Self {
        Self {
            entries: HashMap::new(),
        }
    }

    #[must_use]
    pub fn from_file(mut self, name: impl Into<String>, path: std::path::PathBuf) -> Self {
        self.entries.insert(name.into(), CatalogueEntry::OnDisk(path));
        self
    }

    #[must_use]
    pub fn from_memory(mut self, name: impl Into<String>, detail: RawDetail) -> Self {
        self.entries
            .insert(name.into(), CatalogueEntry::InMemory(detail));
        self
    }

    /// Resolves everything from disk now; `nest` then cannot fail over IO.
    pub fn build(self) -> Result<ResolvedCatalogue, NestError> {
        let mut resolved: HashMap<String, RawDetail> = HashMap::new();
        for (name, entry) in &self.entries {
            resolved.insert(
                name.clone(),
                match entry {
                    CatalogueEntry::InMemory(detail) => detail.clone(),
                    CatalogueEntry::OnDisk(path) => nest_io::load_detail(path)?,
                },
            );
        }
        Ok(ResolvedCatalogue { resolved })
    }
}

/// A catalogue whose geometry is fully resolved in memory.
pub struct ResolvedCatalogue {
    resolved: HashMap<String, RawDetail>,
}

impl ResolvedCatalogue {
    pub fn get(&self, name: &str) -> RawDetail {
        self.resolved[name].clone()
    }

    pub fn as_closure(&self) -> impl Fn(&str) -> RawDetail + '_ {
        move |name: &str| self.get(name)
    }
}

/// Rectangle helper matching the "+ Shape >> Rectangle" ACVSheetNest feature.
pub fn rectangle_detail(width: f64, height: f64) -> RawDetail {
    RawDetail {
        bends: Vec::new(),
        name: "rect".to_owned(),
        outer: vec![
            nest_geom::SvgPoint::new(0.0, 0.0),
            nest_geom::SvgPoint::new(width, 0.0),
            nest_geom::SvgPoint::new(width, height),
            nest_geom::SvgPoint::new(0.0, height),
        ],
        children: Vec::new(),
    }
}
