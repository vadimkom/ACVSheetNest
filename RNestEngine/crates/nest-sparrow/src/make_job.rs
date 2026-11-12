//! `rnest-make-job` — builds a jagua-rs strip-packing job from drawings, to run
//! the same job through sparrow.exe and rnest-sparrow.exe and compare them.
//!
//! usage: rnest-make-job <out.json> <strip_height> <copies> <rot: 0|2|4|free> <drawing>...

fn main() {
    let a: Vec<String> = std::env::args().skip(1).collect();
    if a.len() < 5 {
        eprintln!("usage: rnest-make-job <out.json> <strip_height> <copies> <rot: 0|2|4|free> <drawing>...");
        std::process::exit(2);
    }
    let strip_height: f64 = a[1].parse().expect("strip height");
    let copies: u32 = a[2].parse().expect("copies");
    let orientations: Option<Vec<f64>> = match a[3].as_str() {
        "0" => Some(vec![0.0]),
        "2" => Some(vec![0.0, 180.0]),
        "4" => Some(vec![0.0, 90.0, 180.0, 270.0]),
        _ => None,
    };
    let mut items = Vec::new();
    for path in &a[4..] {
        let detail = match nest_io::load_detail(std::path::Path::new(path)) {
            Ok(d) => d,
            Err(e) => {
                eprintln!("skip {path}: {e}");
                continue;
            }
        };
        let mut data: Vec<[f64; 2]> = detail.outer.iter().map(|p| [p.x, p.y]).collect();
        if data.len() > 1 && data[0] == data[data.len() - 1] {
            data.pop();
        }
        let mut item = serde_json::json!({
            "id": items.len(),
            "demand": copies,
            "shape": { "type": "simple_polygon", "data": data },
        });
        if let Some(o) = &orientations {
            item["allowed_orientations"] = serde_json::json!(o);
        }
        items.push(item);
    }
    let job = serde_json::json!({ "name": "job", "strip_height": strip_height, "items": items });
    std::fs::write(&a[0], serde_json::to_string(&job).unwrap()).expect("write job");
    eprintln!("{} item(s) x {copies} -> {}", items.len(), a[0]);
}
