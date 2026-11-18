#!/usr/bin/env node
// Проверка ответа движка раскладки по протоколу SheetNest (ENGINE_PROTOCOL.md).
// Без зависимостей: node tools/check.mjs <job.json> <final_job.json> [--svg out.svg] [--json]
//
// Что проверяется:
//   1. каждый item_id существует, положено не больше demand; недоложенное печатается (ошибка);
//   2. rotation равен одному из allowed_orientations (если поле есть) с точностью 1e-6°;
//   3. по Y всё внутри полосы: 0 <= y <= strip_height после сдвига раскладки к minY = 0 (допуск TOL);
//   4. нет наложений: контуры могут касаться, но не заходить друг в друга глубже TOL.
// Что считается: strip_width (по фактическим контурам), плотность, время из solution.run_time_sec.
// Код выхода: 0 — всё верно, 1 — есть нарушения, 2 — не удалось прочитать файлы.

import fs from 'node:fs';

const TOL = 0.01; // единицы чертежа (мм): допустимая глубина «касания»

const args = process.argv.slice(2);
const flag = (name) => {
  const i = args.indexOf(name);
  if (i < 0) return null;
  const v = args[i + 1];
  args.splice(i, 2);
  return v;
};
const svgOut = flag('--svg');
const asJson = args.includes('--json');
const files = args.filter((a) => !a.startsWith('--'));
if (files.length < 2) {
  console.error('usage: node check.mjs <job.json> <final_job.json> [--svg out.svg] [--json]');
  process.exit(2);
}

let job, sol;
try {
  job = JSON.parse(fs.readFileSync(files[0], 'utf8').replace(/^﻿/, ''));
  sol = JSON.parse(fs.readFileSync(files[1], 'utf8').replace(/^﻿/, ''));
} catch (e) {
  console.error('cannot read: ' + e.message);
  process.exit(2);
}

const errors = [];
const H = job.strip_height;
const items = new Map(job.items.map((it) => [it.id, it]));
const placedRaw = sol?.solution?.layout?.placed_items;
if (!Array.isArray(placedRaw)) {
  console.error('solution.layout.placed_items is missing');
  process.exit(2);
}

// --- геометрия -------------------------------------------------------------
const area = (p) => {
  let s = 0;
  for (let i = 0; i < p.length; i++) {
    const [x1, y1] = p[i];
    const [x2, y2] = p[(i + 1) % p.length];
    s += x1 * y2 - x2 * y1;
  }
  return s / 2;
};
const bbox = (p) => {
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
  for (const [x, y] of p) {
    if (x < x0) x0 = x;
    if (y < y0) y0 = y;
    if (x > x1) x1 = x;
    if (y > y1) y1 = y;
  }
  return { x0, y0, x1, y1 };
};
const transform = (pts, deg, [dx, dy]) => {
  const a = (deg * Math.PI) / 180;
  const c = Math.cos(a), s = Math.sin(a);
  return pts.map(([x, y]) => [x * c - y * s + dx, x * s + y * c + dy]);
};
// расстояние от точки до отрезка
const segDist = (px, py, ax, ay, bx, by) => {
  const vx = bx - ax, vy = by - ay;
  const l2 = vx * vx + vy * vy;
  let t = l2 > 0 ? ((px - ax) * vx + (py - ay) * vy) / l2 : 0;
  t = Math.max(0, Math.min(1, t));
  const qx = ax + t * vx - px, qy = ay + t * vy - py;
  return Math.hypot(qx, qy);
};
// точка строго внутри многоугольника: внутри по чётности и дальше TOL от границы
const deepInside = (x, y, poly) => {
  let inside = false;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
    const [xi, yi] = poly[i];
    const [xj, yj] = poly[j];
    if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) inside = !inside;
  }
  if (!inside) return false;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
    if (segDist(x, y, poly[j][0], poly[j][1], poly[i][0], poly[i][1]) <= TOL) return false;
  }
  return true;
};
// отрезки пересекаются «насквозь»: концы каждого дальше TOL по разные стороны другого
const crosses = (a, b, c, d) => {
  const side = (p, q, r) => {
    const len = Math.hypot(q[0] - p[0], q[1] - p[1]) || 1;
    return ((q[0] - p[0]) * (r[1] - p[1]) - (q[1] - p[1]) * (r[0] - p[0])) / len;
  };
  const d1 = side(c, d, a), d2 = side(c, d, b), d3 = side(a, b, c), d4 = side(a, b, d);
  return ((d1 > TOL && d2 < -TOL) || (d1 < -TOL && d2 > TOL)) && ((d3 > TOL && d4 < -TOL) || (d3 < -TOL && d4 > TOL));
};
// пробные точки: вершины, середины сторон и точки на INWARD от середин сторон (внутри самой детали,
// чтобы поймать две совпавшие детали). Среднее вершин не годится: у L-детали оно лежит снаружи.
const INWARD = 5 * TOL;
const insideEvenOdd = (x, y, poly) => {
  let inside = false;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
    const [xi, yi] = poly[i];
    const [xj, yj] = poly[j];
    if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) inside = !inside;
  }
  return inside;
};
const probes = (p) => {
  const out = p.slice();
  const ccw = area(p) > 0 ? 1 : -1;
  for (let i = 0; i < p.length; i++) {
    const q = p[(i + 1) % p.length];
    const mx = (p[i][0] + q[0]) / 2, my = (p[i][1] + q[1]) / 2;
    out.push([mx, my]);
    const len = Math.hypot(q[0] - p[i][0], q[1] - p[i][1]);
    if (len === 0) continue;
    // левая нормаль у обхода против часовой смотрит внутрь
    const nx = (-(q[1] - p[i][1]) / len) * ccw, ny = ((q[0] - p[i][0]) / len) * ccw;
    const ix = mx + nx * INWARD, iy = my + ny * INWARD;
    if (insideEvenOdd(ix, iy, p)) out.push([ix, iy]);
  }
  return out;
};
const overlap = (A, B) => {
  for (let i = 0; i < A.pts.length; i++) {
    const a = A.pts[i], b = A.pts[(i + 1) % A.pts.length];
    const ex0 = Math.min(a[0], b[0]), ex1 = Math.max(a[0], b[0]);
    const ey0 = Math.min(a[1], b[1]), ey1 = Math.max(a[1], b[1]);
    if (ex1 < B.bb.x0 || ex0 > B.bb.x1 || ey1 < B.bb.y0 || ey0 > B.bb.y1) continue;
    for (let j = 0; j < B.pts.length; j++) {
      if (crosses(a, b, B.pts[j], B.pts[(j + 1) % B.pts.length])) return true;
    }
  }
  for (const [x, y] of A.probe) if (deepInside(x, y, B.pts)) return true;
  for (const [x, y] of B.probe) if (deepInside(x, y, A.pts)) return true;
  return false;
};

// --- разбор ответа ---------------------------------------------------------
const counts = new Map();
const placed = [];
placedRaw.forEach((p, k) => {
  const it = items.get(p.item_id);
  if (!it) {
    errors.push(`#${k}: item_id ${p.item_id} нет в задании`);
    return;
  }
  const rot = p.transformation?.rotation ?? 0;
  const tr = p.transformation?.translation ?? [0, 0];
  if (Array.isArray(it.allowed_orientations)) {
    const norm = (v) => ((v % 360) + 360) % 360;
    const ok = it.allowed_orientations.some((o) => {
      const d = Math.abs(norm(o) - norm(rot));
      return Math.min(d, 360 - d) < 1e-6;
    });
    if (!ok) errors.push(`#${k}: item ${it.id} повёрнут на ${rot}°, разрешено [${it.allowed_orientations.join(', ')}]`);
  }
  counts.set(it.id, (counts.get(it.id) ?? 0) + 1);
  const pts = transform(it.shape.data, rot, tr);
  placed.push({ k, id: it.id, rot, pts, bb: bbox(pts), probe: probes(pts), area: Math.abs(area(it.shape.data)) });
});

const missing = [];
for (const it of job.items) {
  const n = counts.get(it.id) ?? 0;
  if (n > it.demand) errors.push(`item ${it.id}: положено ${n}, а demand ${it.demand}`);
  if (n < it.demand) missing.push(`item ${it.id}: ${n}/${it.demand}`);
}
if (missing.length) errors.push('положено не всё: ' + missing.join('; '));

// --- полоса ----------------------------------------------------------------
let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
for (const p of placed) {
  minX = Math.min(minX, p.bb.x0); minY = Math.min(minY, p.bb.y0);
  maxX = Math.max(maxX, p.bb.x1); maxY = Math.max(maxY, p.bb.y1);
}
const height = maxY - minY;
if (placed.length && height > H + TOL) errors.push(`раскладка по Y ${height.toFixed(3)} выше полосы ${H}`);
const width = placed.length ? maxX - minX : 0;
const usedArea = placed.reduce((s, p) => s + p.area, 0);
const density = width > 0 ? usedArea / (width * H) : 0;

// --- наложения (сетка по рамкам) -------------------------------------------
const pairs = [];
const cell = Math.max(1, Math.sqrt((width * Math.max(height, 1)) / Math.max(placed.length, 1)));
const grid = new Map();
placed.forEach((p, i) => {
  for (let gx = Math.floor(p.bb.x0 / cell); gx <= Math.floor(p.bb.x1 / cell); gx++)
    for (let gy = Math.floor(p.bb.y0 / cell); gy <= Math.floor(p.bb.y1 / cell); gy++) {
      const key = gx + ',' + gy;
      if (!grid.has(key)) grid.set(key, []);
      grid.get(key).push(i);
    }
});
const seen = new Set();
const bad = new Set();
for (const list of grid.values()) {
  for (let a = 0; a < list.length; a++)
    for (let b = a + 1; b < list.length; b++) {
      const i = Math.min(list[a], list[b]), j = Math.max(list[a], list[b]);
      const key = i * 1e6 + j;
      if (seen.has(key)) continue;
      seen.add(key);
      const A = placed[i], B = placed[j];
      if (A.bb.x1 <= B.bb.x0 + TOL || B.bb.x1 <= A.bb.x0 + TOL || A.bb.y1 <= B.bb.y0 + TOL || B.bb.y1 <= A.bb.y0 + TOL) continue;
      if (overlap(A, B)) {
        pairs.push([A.k, B.k]);
        bad.add(i); bad.add(j);
      }
    }
}
if (pairs.length) {
  errors.push(`наложений: ${pairs.length} (` + pairs.slice(0, 10).map(([a, b]) => `#${a}×#${b}`).join(', ') + (pairs.length > 10 ? ', …' : '') + ')');
}

// --- SVG -------------------------------------------------------------------
if (svgOut) {
  const pad = 10;
  const W = Math.max(width, 1);
  const hues = job.items.map((_, i) => (i * 137.508) % 360);
  const y = (v) => (H - (v - minY)).toFixed(2); // Y вверх, как в чертеже
  const parts = placed.map((p, i) => {
    const d = p.pts.map(([px, py], n) => `${n ? 'L' : 'M'}${(px - minX).toFixed(2)},${y(py)}`).join('') + 'Z';
    const fill = bad.has(i) ? '#e53935' : `hsl(${hues[p.id]},55%,70%)`;
    return `<path d="${d}" fill="${fill}" fill-opacity="0.8" stroke="#333" stroke-width="${(W / 2000).toFixed(3)}"><title>#${p.k} item ${p.id} rot ${p.rot}</title></path>`;
  });
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${-pad} ${-pad} ${W + 2 * pad} ${H + 2 * pad}" width="${Math.round((W + 2 * pad) / 2)}" height="${Math.round((H + 2 * pad) / 2)}">
<rect x="0" y="0" width="${W}" height="${H}" fill="#fafafa" stroke="#1565c0" stroke-width="${(W / 1000).toFixed(3)}"/>
${parts.join('\n')}
</svg>
`;
  fs.writeFileSync(svgOut, svg);
}

// --- отчёт -----------------------------------------------------------------
const demand = job.items.reduce((s, it) => s + it.demand, 0);
const report = {
  ok: errors.length === 0,
  placed: placed.length,
  demand,
  strip_height: H,
  strip_width: +width.toFixed(3),
  density: +density.toFixed(4),
  reported_density: sol.solution?.layout?.density ?? null,
  run_time_sec: sol.solution?.run_time_sec ?? null,
  overlaps: pairs.length,
  errors,
};
if (asJson) {
  console.log(JSON.stringify(report));
} else {
  console.log(`${report.ok ? 'OK ' : 'BAD'}  placed ${report.placed}/${demand}  width ${report.strip_width}  density ${(density * 100).toFixed(2)}%  overlaps ${pairs.length}`);
  for (const e of errors) console.log('  - ' + e);
}
process.exit(report.ok ? 0 : 1);
