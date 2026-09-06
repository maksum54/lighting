// Vercel serverless: POST /api/calc — kalkulasi pencahayaan bersama (web + add-in Revit).
// Logika ada di calc-core.js (root) supaya hasil persis sama dgn server.js & client.
const { calcLuminaire } = require('../calc-core');

module.exports = async function handler(req, res) {
  if (req.method !== 'POST') {
    res.setHeader('Allow', 'POST');
    return res.status(405).json({ error: 'Method Not Allowed' });
  }
  try {
    let body;
    const raw = req.body;
    if (typeof raw === 'string') body = JSON.parse(raw);
    else if (raw && typeof raw === 'object') body = raw;
    else throw Object.assign(new Error('Body tidak valid.'), { status: 400 });
    const out = calcLuminaire(body);
    res.status(200).json(out);
  } catch (error) {
    res.status((error && error.status) || 400).json({ error: (error && error.message) || 'Invalid request' });
  }
};
