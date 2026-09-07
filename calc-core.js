// calc-core.js — mesin kalkulasi pencahayaan bersama (web + add-in Revit).
// Logika ini MENCERMINKAN kalkulasi client index.html (data()/tableCU()/luxAt()).
// Data katalog/CU di bawah harus dijaga SAMA dengan LUM_CATALOG & CU_TABLE di index.html.
// Pure CommonJS — tidak bergantung fs/network, bisa dipakai server.js maupun api/calc.js.

'use strict';

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));

/* ---- katalog luminaire (identik dgn index.html) ---- */
const LUM_CATALOG = [
  { id: '0.75', label: 'Panel LED troffer (direct)' },
  { id: '0.85', label: 'Downlight LED' },
  { id: '0.62', label: 'Lampu gantung / pendant' },
  { id: 'bat',  label: 'Linear batten / tube LED' },
  { id: 'lb',   label: 'Low bay LED' },
  { id: 'hb',   label: 'High bay LED' },
  { id: 'flood',label: 'Floodlight LED (outdoor)' },
  { id: 'spot', label: 'Spotlight / track LED' }
];

/* ---- tabel CU (identik dgn index.html CU_TABLE) ---- */
const CU_TABLE = {
  '0.75': { keys: [[0.80, 0.70], [0.70, 0.50], [0.50, 0.30], [0.30, 0.10]], rows: { 0.6: [0.63, 0.57, 0.51, 0.44], 0.8: [0.69, 0.63, 0.56, 0.49], 1.0: [0.72, 0.66, 0.59, 0.51], 1.25: [0.76, 0.69, 0.62, 0.54], 1.5: [0.79, 0.72, 0.65, 0.56], 2.0: [0.82, 0.75, 0.67, 0.58], 2.5: [0.84, 0.77, 0.69, 0.60], 3.0: [0.86, 0.79, 0.71, 0.62], 4.0: [0.87, 0.80, 0.72, 0.63], 5.0: [0.88, 0.81, 0.73, 0.63] } },
  '0.62': { keys: [[0.80, 0.70], [0.70, 0.50], [0.50, 0.30], [0.30, 0.10]], rows: { 0.6: [0.52, 0.46, 0.41, 0.36], 0.8: [0.58, 0.51, 0.45, 0.39], 1.0: [0.61, 0.54, 0.48, 0.41], 1.25: [0.64, 0.57, 0.50, 0.44], 1.5: [0.66, 0.59, 0.52, 0.45], 2.0: [0.69, 0.61, 0.54, 0.47], 2.5: [0.71, 0.63, 0.56, 0.48], 3.0: [0.72, 0.65, 0.57, 0.50], 4.0: [0.74, 0.66, 0.59, 0.51], 5.0: [0.75, 0.67, 0.59, 0.52] } },
  '0.85': { keys: [[0.80, 0.70], [0.70, 0.50], [0.50, 0.30], [0.30, 0.10]], rows: { 0.6: [0.71, 0.64, 0.57, 0.49], 0.8: [0.78, 0.71, 0.63, 0.55], 1.0: [0.82, 0.75, 0.66, 0.57], 1.25: [0.85, 0.78, 0.70, 0.60], 1.5: [0.87, 0.80, 0.72, 0.62], 2.0: [0.89, 0.82, 0.74, 0.64], 2.5: [0.91, 0.84, 0.76, 0.66], 3.0: [0.92, 0.85, 0.77, 0.67], 4.0: [0.93, 0.86, 0.78, 0.68], 5.0: [0.94, 0.87, 0.79, 0.69] } },
  'bat':  { keys: [[0.80, 0.70], [0.70, 0.50], [0.50, 0.30], [0.30, 0.10]], rows: { 0.6: [0.66, 0.60, 0.53, 0.46], 0.8: [0.72, 0.66, 0.58, 0.50], 1.0: [0.77, 0.70, 0.62, 0.53], 1.25: [0.81, 0.74, 0.65, 0.56], 1.5: [0.84, 0.77, 0.68, 0.59], 2.0: [0.87, 0.80, 0.71, 0.61], 2.5: [0.89, 0.82, 0.73, 0.63], 3.0: [0.90, 0.84, 0.75, 0.65], 4.0: [0.92, 0.86, 0.77, 0.66], 5.0: [0.93, 0.87, 0.78, 0.67] } },
  'lb':   { keys: [[0.80, 0.70], [0.70, 0.50], [0.50, 0.30], [0.30, 0.10]], rows: { 0.6: [0.64, 0.58, 0.52, 0.45], 0.8: [0.71, 0.64, 0.57, 0.50], 1.0: [0.75, 0.68, 0.60, 0.52], 1.25: [0.79, 0.72, 0.63, 0.55], 1.5: [0.82, 0.75, 0.66, 0.57], 2.0: [0.85, 0.78, 0.69, 0.60], 2.5: [0.87, 0.80, 0.71, 0.62], 3.0: [0.88, 0.82, 0.73, 0.63], 4.0: [0.90, 0.84, 0.75, 0.65], 5.0: [0.91, 0.85, 0.76, 0.66] } },
  'hb':   { keys: [[0.80, 0.70], [0.70, 0.50], [0.50, 0.30], [0.30, 0.10]], rows: { 0.6: [0.60, 0.55, 0.50, 0.43], 0.8: [0.68, 0.62, 0.55, 0.47], 1.0: [0.73, 0.66, 0.58, 0.50], 1.25: [0.78, 0.71, 0.62, 0.53], 1.5: [0.82, 0.75, 0.66, 0.56], 2.0: [0.85, 0.79, 0.70, 0.59], 2.5: [0.88, 0.82, 0.73, 0.61], 3.0: [0.90, 0.84, 0.76, 0.64], 4.0: [0.92, 0.87, 0.79, 0.67], 5.0: [0.93, 0.88, 0.80, 0.68] } },
  'flood':{ keys: [[0.80, 0.70], [0.70, 0.50], [0.50, 0.30], [0.30, 0.10]], rows: { 0.6: [0.55, 0.51, 0.47, 0.42], 0.8: [0.62, 0.57, 0.52, 0.46], 1.0: [0.66, 0.60, 0.54, 0.48], 1.25: [0.70, 0.64, 0.57, 0.51], 1.5: [0.72, 0.67, 0.60, 0.53], 2.0: [0.75, 0.70, 0.63, 0.56], 2.5: [0.77, 0.72, 0.65, 0.58], 3.0: [0.79, 0.73, 0.66, 0.59], 4.0: [0.80, 0.75, 0.68, 0.60], 5.0: [0.81, 0.76, 0.69, 0.61] } },
  'spot': { keys: [[0.80, 0.70], [0.70, 0.50], [0.50, 0.30], [0.30, 0.10]], rows: { 0.6: [0.42, 0.38, 0.34, 0.31], 0.8: [0.50, 0.45, 0.40, 0.36], 1.0: [0.56, 0.50, 0.44, 0.40], 1.25: [0.61, 0.55, 0.49, 0.43], 1.5: [0.66, 0.59, 0.52, 0.46], 2.0: [0.72, 0.65, 0.57, 0.50], 2.5: [0.76, 0.69, 0.61, 0.53], 3.0: [0.79, 0.72, 0.64, 0.56], 4.0: [0.83, 0.76, 0.68, 0.60], 5.0: [0.86, 0.79, 0.70, 0.62] } }
};

/* CU dari tabel: interpolasi RI linier; bucket reflektansi = tertinggi yg <= input (konservatif). */
function tableCU(lumType, refC, refW, ri) {
  const T = CU_TABLE[lumType];
  if (!T) return null;
  const ris = Object.keys(T.rows).map(Number).sort((a, b) => a - b);
  let key = 0;
  for (let i = 0; i < T.keys.length; i++) {
    const k = T.keys[i];
    if (refC >= k[0] && refW >= k[1]) { key = i; break; }
  }
  let cu;
  if (ri <= ris[0]) cu = T.rows[ris[0]][key];
  else if (ri >= ris[ris.length - 1]) cu = T.rows[ris[ris.length - 1]][key];
  else {
    let lo = ris[0], hi = ris[ris.length - 1];
    for (let i = 0; i < ris.length - 1; i++) { if (ri >= ris[i] && ri <= ris[i + 1]) { lo = ris[i]; hi = ris[i + 1]; break; } }
    cu = T.rows[lo][key] + (T.rows[hi][key] - T.rows[lo][key]) * ((ri - lo) / (hi - lo));
  }
  return cu;
}

/* Iluminasi titik Lambertian, identik luxAt() client. */
function luxAt(grid, x, y) {
  const h = grid.hm, h2 = h * h;
  let acc = 0;
  for (let i = 0; i < grid.cols; i++) {
    for (let j = 0; j < grid.rows; j++) {
      const fx = (i + 0.5) * grid.L / grid.cols, fy = (j + 0.5) * grid.W / grid.rows;
      const dx = x - fx, dy = y - fy, r2 = dx * dx + dy * dy;
      acc += grid.F * grid.llf * h2 / (Math.PI * (h2 + r2) * (h2 + r2));
    }
  }
  return acc;
}

function num(v, dflt) { const x = Number(v); return Number.isFinite(x) ? x : dflt; }

/* Input sanitasi + validasi. Mengembalikan error ber-status 400 utk nilai tak masuk akal. */
function normalizeInput(raw) {
  const o = (raw && typeof raw === 'object') ? raw : {};
  const L = num(o.L, NaN), W = num(o.W, NaN), H = num(o.H, NaN);
  const wp = num(o.wp, NaN), F = num(o.F, NaN), P = num(o.P, NaN), E = num(o.E, NaN);
  const llf = num(o.llf, NaN), refC = num(o.refC, NaN), refW = num(o.refW, NaN);
  const lumType = String(o.lumType || '');
  const manual = !!o.cuManual;
  const cu = num(o.cu, NaN);
  const bad = [];
  if (!Number.isFinite(L) || L < 1 || L > 200) bad.push('L (1–200)');
  if (!Number.isFinite(W) || W < 1 || W > 200) bad.push('W (1–200)');
  if (!Number.isFinite(H) || H < 1.5 || H > 20) bad.push('H (1.5–20)');
  if (!Number.isFinite(wp) || wp < 0.2 || wp >= H - 0.3) bad.push('wp (0.2 s/d H-0.3)');
  if (!Number.isFinite(F) || F < 100 || F > 100000) bad.push('F/lumen (100–100000)');
  if (!Number.isFinite(P) || P < 1 || P > 2000) bad.push('P/watt (1–2000)');
  if (!Number.isFinite(E) || E < 10 || E > 100000) bad.push('E/lux (10–100000)');
  if (!Number.isFinite(llf) || llf < 0.5 || llf > 1) bad.push('LLF (0.5–1)');
  if (!Number.isFinite(refC) || refC < 0.1 || refC > 0.9) bad.push('refC (0.1–0.9)');
  if (!Number.isFinite(refW) || refW < 0.1 || refW > 0.9) bad.push('refW (0.1–0.9)');
  if (!CU_TABLE[lumType]) bad.push('lumType tidak dikenal');
  if (manual && (!Number.isFinite(cu) || cu < 0.2 || cu > 1)) bad.push('cu manual (0.2–1) saat cuManual aktif');
  if (bad.length) {
    const err = new Error('Parameter tidak valid: ' + bad.join(', '));
    err.status = 400;
    throw err;
  }
  return { L, W, H, wp, F, P, E, llf, refC, refW, lumType, manual, cu };
}

/* Kalkulasi utama — hasil identik data() di client + daftar posisi grid (meter). */
function calcLuminaire(raw) {
  const in0 = normalizeInput(raw);
  const L = in0.L, W = in0.W, H = in0.H, wp = in0.wp;
  const hrc = Math.max(0.3, H - wp);
  const A = L * W;
  const ri = A / (hrc * (L + W));
  const tcu = tableCU(in0.lumType, in0.refC, in0.refW, ri);
  const cuUsed = in0.manual ? clamp(in0.cu, 0.2, 1) : (tcu !== null ? tcu : clamp(in0.cu, 0.2, 1));
  const rawN = in0.E * A / (in0.llf * cuUsed * in0.F);
  // Layout lampu dihitung langsung dari kebutuhan mentah (rawN), lalu kolom & baris dibulatkan
  // ke INTEGER TERDEKAT secara simetris (bukan "ceil" ganda). Pembulatan ceil(n) lalu ceil(cols)
  // lalu ceil(rows) membuat kebutuhan riil 16.2 melonjak jadi 20 (5×4). Dengan round: 16.2 → 4×4,
  // sama seperti hasil DIALux. rasio kolom/baris mengikuti L/W agar jarak grid tetap persegi.
  const cols = Math.max(1, Math.round(Math.sqrt(rawN * L / W)));
  const rows = Math.max(1, Math.round(Math.sqrt(rawN * W / L)));
  const n = cols * rows;
  const actual = n * in0.llf * cuUsed * in0.F / A;
  const power = n * in0.P;
  const lpd = power / A;
  const eff = in0.F / in0.P;
  const lumens = n * in0.F;
  const sx = cols > 1 ? L / (cols - 1) : L;
  const sy = rows > 1 ? W / (rows - 1) : W;
  const hm = hrc;

  // keseragaman & titik tengah pd area tugas (pinggir dibuang) — identik data()
  const border = Math.min(0.5, Math.max(0.1, L / 8, W / 8));
  const NX = 8, NY = 8;
  let minL = Infinity, sum = 0;
  for (let i = 0; i < NX; i++) for (let j = 0; j < NY; j++) {
    const x = border + (L - 2 * border) * (i + 0.5) / NX;
    const y = border + (W - 2 * border) * (j + 0.5) / NY;
    const v = luxAt({ cols, rows, L, W, F: in0.F, llf: in0.llf, hm }, x, y);
    if (v < minL) minL = v; sum += v;
  }
  const eAvg = sum / (NX * NY);
  const u0 = minL > 0 ? minL / eAvg : 0;

  // posisi grid (pusat sel) dalam meter — dipakai add-in Revit utk penempatan
  const positionsM = [];
  for (let i = 0; i < cols; i++) for (let j = 0; j < rows; j++) {
    positionsM.push({
      x: +( (i + 0.5) * L / cols ).toFixed(4),
      y: +( (j + 0.5) * W / rows ).toFixed(4)
    });
  }

  return {
    L, W, H, wp, hrc, F: in0.F, P: in0.P, E: in0.E, refC: in0.refC, refW: in0.refW,
    lumType: in0.lumType, lumLabel: (LUM_CATALOG.find(l => l.id === in0.lumType) || {}).label || in0.lumType,
    llf: in0.llf, manual: in0.manual, cuUsed: +cuUsed.toFixed(4), tcu,
    ri: +ri.toFixed(3), A: +A.toFixed(2), raw: +rawN.toFixed(1), n, cols, rows, installed: n,
    actual: +actual.toFixed(2), power, lpd: +lpd.toFixed(3), eff: +eff.toFixed(1), lumens,
    sx: +sx.toFixed(3), sy: +sy.toFixed(3), u0: +u0.toFixed(4), emin: +minL.toFixed(2),
    hm, positionsM
  };
}

module.exports = { calcLuminaire, tableCU, CU_TABLE, LUM_CATALOG, normalizeInput };
