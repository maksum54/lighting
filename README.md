# Luxora — Lighting Design Studio

A browser-based lighting calculation workspace for room sizing, luminaire counts, 2D illuminance heatmaps, 3D room preview, design checks, and printable reports.

## Included

- **Zonal cavity method** with CU taken from Room Index + ceiling/wall reflectance tables (3 luminaire types), LLF, target lux, power density (LPD, W/m²), and luminaire efficacy (lm/W).
- **Input validation & sanity clamping** — extreme or empty inputs never produce NaN/blank results.
- **Room presets** (office, classroom, retail, home, commercial kitchen, warehouse, clinic) that fill target lux, reflectance, CCT recommendation, and reference LPD / uniformity limits.
- **Design checks tab**: lux target, uniformity U₀ (Emin/Eav over the task area), spacing vs. mounting height, LPD vs. reference limit, luminaire efficacy.
- **Point-by-point illuminance heatmap** over the plan and **uniformity estimate** derived from it (not just an average).
- Interactive **2D plan** with luminaire grid and heatmap; canvas **3D room preview** with drag rotation.
- **Exports**: printable report (print CSS), PNG plan, JSON project, and DXF floor plan.
- **Save/load project** in the browser (localStorage).
- Optional server-side **AI assistant**; API keys stay on the server. The AI chat includes recent context and an offline fallback so the app stays usable with no AI configured.

## Run locally

```bash
node server.js
```

Open <http://localhost:8787>.

## Optional AI environment

Copy `.env.example` to `.env` and provide `AI_API_KEY` and `AI_MODEL`. The server exposes `POST /api/ai` as an OpenAI-compatible proxy. The frontend never receives the secret key.

The endpoint is guarded: per-IP rate limit, small request body cap, message length cap, only `user`/`assistant` roles accepted, and a server-side system prompt that keeps answers on lighting topics (override with `AI_SYSTEM_PROMPT`).

On Vercel, `api/ai.js` is deployed as a serverless function. Add `AI_API_KEY`, `AI_MODEL`, and optionally `AI_API_URL` under Project Settings → Environment Variables, then redeploy. Keep the variables enabled for the Production environment. (Rate limiting on Vercel is in-memory per function instance — best effort.)

The app remains fully usable without AI configuration.

## Notes

Results are design estimates based on the entered assumptions and simplified photometry. The heatmap treats each luminaire as a Lambertian source; validate final designs against the applicable project standard and real photometric data.
