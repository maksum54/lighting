// Vercel serverless function: proxy AI (OpenAI-compatible) dengan pengaman.
// Rate limit & batasan dilakukan in-memory per instance — best-effort di serverless.
const DEFAULT_AI_URL = 'https://api.openai.com/v1/chat/completions';
const AI_MAX_CHARS = Number(process.env.AI_MAX_CHARS) || 800;
const MAX_BODY = 16 * 1024;

const SYSTEM_PROMPT = process.env.AI_SYSTEM_PROMPT || (
  'Kamu adalah konsultan desain pencahayaan (lighting) profesional yang menjawab dalam Bahasa Indonesia, singkat dan praktis. ' +
  'Bahas hanya topik pencahayaan: lumen, lux, watt, efikasi (lm/W), CCT/Kelvin, CRI, UGR, tata letak lampu, standar (SNI/EN), ' +
  'dan hasil kalkulasi pengguna. Di luar topik itu, jawab singkat bahwa kamu hanya membantu soal pencahayaan.'
);

let hits = {};
setInterval(() => { hits = {}; }, 60e3).unref();

function rateLimited(ip) {
  if (hits[ip] && hits[ip] >= 10) return true;
  hits[ip] = (hits[ip] || 0) + 1;
  return false;
}

function ipOf(req) {
  const fwd = req.headers['x-forwarded-for'];
  if (typeof fwd === 'string' && fwd) return fwd.split(',')[0].trim();
  return 'local';
}

function sanitizeMessages(messages) {
  if (!Array.isArray(messages)) return [];
  return messages
    .filter(m => m && (m.role === 'user' || m.role === 'assistant') && typeof m.content === 'string')
    .map(m => ({ role: m.role, content: m.content.slice(0, AI_MAX_CHARS) }))
    .slice(-12);
}

module.exports = async function handler(req, res) {
  if (req.method !== 'POST') {
    res.setHeader('Allow', 'POST');
    return res.status(405).json({ error: 'Method Not Allowed' });
  }

  if (rateLimited(ipOf(req))) {
    return res.status(429).json({ error: 'Too many requests. Coba lagi sebentar lagi.' });
  }

  if (!process.env.AI_API_KEY || !process.env.AI_MODEL) {
    return res.status(503).json({ error: 'AI is not configured on Vercel.' });
  }

  try {
    let body;
    const raw = req.body;
    if (typeof raw === 'string') body = JSON.parse(raw);
    else if (raw && typeof raw === 'object') body = raw;
    else throw new Error('Body tidak valid.');
    if (JSON.stringify(body).length > MAX_BODY) return res.status(413).json({ error: 'Request terlalu besar.' });

    const messages = sanitizeMessages(body.messages);
    if (messages.length === 0) return res.status(400).json({ error: 'messages kosong atau tidak valid.' });

    const response = await fetch(process.env.AI_API_URL || DEFAULT_AI_URL, {
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
    const payload = await response.text();
    res.status(response.status).setHeader('Content-Type', 'application/json').send(payload);
  } catch (error) {
    res.status(502).json({ error: error.message });
  }
};
