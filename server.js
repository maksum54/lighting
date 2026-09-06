const http = require('http');
const fs = require('fs');
const path = require('path');

const root = __dirname;
const port = Number(process.env.PORT) || 8787;
const aiUrl = process.env.AI_API_URL || 'https://api.openai.com/v1/chat/completions';

function send(res, status, body, contentType = 'text/plain; charset=utf-8') {
  res.writeHead(status, { 'Content-Type': contentType });
  res.end(body);
}

function readJson(req) {
  return new Promise((resolve, reject) => {
    let body = '';
    req.on('data', chunk => {
      body += chunk;
      if (body.length > 64 * 1024) reject(new Error('Request too large'));
    });
    req.on('end', () => {
      try { resolve(JSON.parse(body || '{}')); }
      catch { reject(new Error('Invalid JSON')); }
    });
    req.on('error', reject);
  });
}

async function handleAi(req, res) {
  if (!process.env.AI_API_KEY || !process.env.AI_MODEL) {
    return send(res, 503, JSON.stringify({ error: 'AI is not configured. Add AI_API_KEY and AI_MODEL to .env.' }), 'application/json');
  }
  try {
    const input = await readJson(req);
    const response = await fetch(aiUrl, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${process.env.AI_API_KEY}`
      },
      body: JSON.stringify({
        model: process.env.AI_MODEL,
        messages: Array.isArray(input.messages) ? input.messages : [],
        temperature: 0.2
      })
    });
    const text = await response.text();
    send(res, response.status, text, 'application/json');
  } catch (error) {
    send(res, 502, JSON.stringify({ error: error.message }), 'application/json');
  }
}

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, `http://${req.headers.host || 'localhost'}`);
  if (url.pathname === '/api/ai' && req.method === 'POST') return handleAi(req, res);
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
