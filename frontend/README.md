# Maritime NavMesh Frontend (Project 2)

Frontend for Project 2 route testing and visualization.

## Stack

- Vite + React + TypeScript
- MapLibre GL JS
- Zustand

## What it does

- Loads snapped ports from `GET /api/ports`
- Lets you pick source and target by:
  - left-clicking a port, or
  - left-clicking any ocean coordinate
- Computes routes via:
  - `GET /api/route/by-locode` (port→port)
  - `GET /api/route/by-coordinate` (any non-port endpoint)
- Renders:
  - amber raw port dots
  - cyan marine-access display dots only for ports whose raw point is not already water-reachable; these are the nearest local water-side access points (`r10` first, `r9` fallback)
  - dashed cyan raw-port -> marine-access display connector lines from Project 1; these are short display-only offsets, not the graph route itself
  - picked source/target dots (`S`/`T`)
  - route polyline, stitched from Project 1's water-following `marine_access_path_coordinates` connector plus the main graph route for named-port routes

Interaction rules:

- Left-click picks `from` then `to`.
- If both are already set, the next left-click starts a new route (`from` resets, `to` cleared).
- Right-click on `S` or `T` removes that endpoint and clears route.
- Right-click elsewhere removes most recent endpoint (`to` then `from`).

Anti-meridian handling:

- Frontend route rendering splits line features at date-line crossings to avoid world-wrap artifacts.

## Run

From `frontend/`:

```bash
npm install
npm run dev
```

Default dev URL: `http://localhost:5173`

## API base URL

The client defaults to:

```text
http://localhost:5000
```

You can override with:

```bash
VITE_API_URL=http://localhost:5260 npm run dev
```

`vite.config.ts` also contains a `/api` and `/health` proxy target (currently `http://localhost:5000`).

## Build

```bash
npm run build
```
