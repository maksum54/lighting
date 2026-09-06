const http = require('http');
const fs = require('fs');
const path = require('path');
const { calcLuminaire } = require('./calc-core');

const root = __dirname;
const port = Number(process.env.PORT) || 8787;
const aiUrl = process.env.AI_API_URL || 'https://api.openai.com/v1/chat/completions';
const AI_MAX_CHARS = Number(process.env.AI_MAX_CHARS) || 800;
const MAX_BODY = 16 * 1024; // 16 KB cukup untuk riwayat pendek; cegah penyalahgunaan kuota API

const SYSTEM_PROMPT = process.env.AI_SYSTEM_PROMPT || (
  'Kamu adalah konsultan desain pencahayaan (lighting) profesional yang menjawab dalam Bahasa Indonesia, singkat dan praktis. ' +
  'Bahas hanya topik pencahayaan: lumen, lux, watt, efikasi (lm/W), CCT/Kelvin, CRI, UGR, tata letak lampu, standar (SNI/EN), ' +
  'dan hasil kalkulasi pengguna. Di luar topik itu, jawab singkat bahwa kamu hanya membantu soal pencahayaan.'
);

function send(res, status, body, contentType = 'text/plain; charset=utf-8') {
  res.writeHead(status, { 'Content-Type': contentType });
  res.end(body);
}

function readJson(req) {
  return new Promise((resolve, reject) => {
    let body = '';
    let over = false;
    req.on('data', chunk => {
      if (over) return;
      body += chunk;
      if (body.length > MAX_BODY) {
        over = true;
        body = '';
        reject(Object.assign(new Error('Request terlalu besar (maks 16 KB).'), { status: 413 }));
      }
    });
    req.on('end', () => {
      if (over) return;
      try { resolve(JSON.parse(body || '{}')); }
      catch (e) { reject(Object.assign(e, { status: 400 })); }
    });
    req.on('error', reject);
  });
}

function sanitizeMessages(messages) {
  if (!Array.isArray(messages)) return [];
  return messages
    .filter(m => m && (m.role === 'user' || m.role === 'assistant') && typeof m.content === 'string')
    .map(m => ({ role: m.role, content: m.content.slice(0, AI_MAX_CHARS) }))
    .slice(-12);
}

function limiters() {
  const map = new Map();
  setInterval(() => { for (const [k, v] of map) if (Date.now() - v.ts > 60e3) map.delete(k); }, 60e3).unref();
  return (key) => {
    const now = Date.now();
    const cur = map.get(key);
    if (!cur || now - cur.ts > 60e3) { map.set(key, { n: 1, ts: now }); return true; }
    if (cur.n >= 10) return false;
    cur.n++; return true;
  };
}
const allow = limiters();

function ipOf(req) {
  const fwd = req.headers['x-forwarded-for'];
  if (typeof fwd === 'string' && fwd) return fwd.split(',')[0].trim();
  return req.socket.remoteAddress || 'local';
}

async function handleAi(req, res) {
  if (!process.env.AI_API_KEY || !process.env.AI_MODEL) {
    return send(res, 503, JSON.stringify({ error: 'AI is not configured. Add AI_API_KEY and AI_MODEL to .env.' }), 'application/json');
  }
  if (!allow(ipOf(req))) {
    return send(res, 429, JSON.stringify({ error: 'Too many requests. Coba lagi sebentar lagi.' }), 'application/json');
  }
  try {
    const input = await readJson(req);
    const messages = sanitizeMessages(input.messages);
    if (messages.length === 0) return send(res, 400, JSON.stringify({ error: 'messages kosong atau tidak valid.' }), 'application/json');
    const response = await fetch(aiUrl, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${process.env.AI_API_KEY}`
      },
      body: JSON.stringify({
        model: process.env.AI_MODEL,
        messages: [{ role: 'system', content: SYSTEM_PROMPT }, ...messages],
        temperature: 0.2
      })
    });
    const text = await response.text();
    send(res, response.status, text, 'application/json');
  } catch (error) {
    const status = error && error.status;
    send(res, status || 502, JSON.stringify({ error: error.message }), 'application/json');
  }
}

/* POST /api/calc — kalkulasi pencahayaan bersama (dipakai web & add-in Revit). Tanpa AI/key. */
async function handleCalc(req, res) {
  try {
    const input = await readJson(req);
    const out = calcLuminaire(input);
    send(res, 200, JSON.stringify(out), 'application/json; charset=utf-8');
  } catch (error) {
    const status = error && error.status;
    send(res, status || 400, JSON.stringify({ error: (error && error.message) || 'Invalid request' }), 'application/json');
  }
}

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, `http://${req.headers.host || 'localhost'}`);
  if (url.pathname === '/api/ai' && req.method === 'POST') return handleAi(req, res);
  if (url.pathname === '/api/calc' && req.method === 'POST') return handleCalc(req, res);
  if (req.method !== 'GET' && req.method !== 'HEAD') return send(res, 405, 'Method Not Allowed');

  const requested = decodeURIComponent(url.pathname === '/' ? '/index.html' : url.pathname);
  const filePath = path.resolve(root, `.${requested}`);
  if (!filePath.startsWith(root + path.sep)) return send(res, 403, 'Forbidden');

  try {
    const data = fs.readFileSync(filePath);
    const ext = path.extname(filePath);
    const types = { '.html': 'text/html; charset=utf-8', '.js': 'application/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8' };
    res.writeHead(200, { 'Content-Type': types[ext] || 'application/octet-stream' });
    if (req.method === 'HEAD') return res.end();
    res.end(data);
  } catch {
    send(res, 404, 'Not found');
  }
});

server.listen(port, () => console.log(`LuxCalc running on http://localhost:${port}`));
