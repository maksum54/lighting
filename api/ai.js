const DEFAULT_AI_URL = 'https://api.openai.com/v1/chat/completions';

module.exports = async function handler(req, res) {
  if (req.method !== 'POST') {
    res.setHeader('Allow', 'POST');
    return res.status(405).json({ error: 'Method Not Allowed' });
  }

  if (!process.env.AI_API_KEY || !process.env.AI_MODEL) {
    return res.status(503).json({ error: 'AI is not configured on Vercel.' });
  }

  try {
    const body = typeof req.body === 'string' ? JSON.parse(req.body) : (req.body || {});
    const response = await fetch(process.env.AI_API_URL || DEFAULT_AI_URL, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${process.env.AI_API_KEY}`
      },
      body: JSON.stringify({
        model: process.env.AI_MODEL,
        messages: Array.isArray(body.messages) ? body.messages : [],
        temperature: 0.2
      })
    });
    const payload = await response.text();
    res.status(response.status).setHeader('Content-Type', 'application/json').send(payload);
  } catch (error) {
    res.status(502).json({ error: error.message });
  }
};
