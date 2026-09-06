# LuxCalc — Lighting Design Studio

A browser-based lighting calculation workspace for room sizing, luminaire counts, 2D plans, 3D room visualization, and printable reports.

## Included

- Zonal cavity method calculation with LLF, CU, target lux, power density, and energy efficiency.
- Interactive 2D plan with luminaire grid and illuminance heatmap.
- Canvas-based 3D room preview with drag rotation.
- PDF print report plus PNG, DXF, and JSON exports.
- Optional server-side AI assistant configuration; API keys stay on the server.

## Run locally

```bash
node server.js
```

Open <http://localhost:8787>.

## Optional AI environment

Copy `.env.example` to `.env` and provide `AI_API_KEY` and `AI_MODEL`. The server exposes `POST /api/ai` as an OpenAI-compatible proxy. The frontend does not receive the secret key.

On Vercel, `api/ai.js` is deployed as a serverless function. Add `AI_API_KEY`, `AI_MODEL`, and optionally `AI_API_URL` under Project Settings → Environment Variables, then redeploy. Keep the variables enabled for the Production environment.

The app remains fully usable without AI configuration.

## Notes

Results are design estimates based on the entered assumptions. Validate final designs against the applicable project standard and photometric data.
