# Commands — Maritime NavMesh Routing (Project 2)

## Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 10.0.x (`dotnet --version`) |
| Node.js | 22+ (`node --version`) |
| npm | 10+ (`npm --version`) |

---

## Setup

### 1. Copy graph artifacts from Project 1

The API expects artifacts under `src/MaritimeNavMesh.Api/artifacts/`.
Copy the six Project 1 runtime files there:

```bash
mkdir -p src/MaritimeNavMesh.Api/artifacts

# Adjust source path to match your Project 1 runtime folder
cp ../maritime-navMesh-builder/outputs/melbourne-singapore/h3_r5/ocean-h3-r5.graph.bin src/MaritimeNavMesh.Api/artifacts/
cp ../maritime-navMesh-builder/outputs/melbourne-singapore/h3_r5/ocean-h3-r5.manifest.json src/MaritimeNavMesh.Api/artifacts/
cp ../maritime-navMesh-builder/outputs/melbourne-singapore/h3_r5/ocean-h3-r5.ports.json src/MaritimeNavMesh.Api/artifacts/
cp ../maritime-navMesh-builder/outputs/melbourne-singapore/h3_r5/ocean-h3-r5.land-mask.bin src/MaritimeNavMesh.Api/artifacts/
cp ../maritime-navMesh-builder/outputs/melbourne-singapore/h3_r5/ocean-h3-r5.edge-portals.bin src/MaritimeNavMesh.Api/artifacts/
cp ../maritime-navMesh-builder/outputs/melbourne-singapore/h3_r5/ocean-h3-r5.ais-corridor-hints.bin src/MaritimeNavMesh.Api/artifacts/
```

This is an explicit manual handoff. Project 2 has no runtime link to the Project 1
workspace. The current ports.json includes raw terminal coordinates, snapped graph anchors, nearest local water-side access points (`r10` first, `r9` fallback), water-following graph-connector paths, and non-navigational display-connector metadata.
The API loads the 500 m conservative land mask once at startup. Route geometry execution is explicit: `raw` returns the H3 centerline unchanged; `shortcut` removes H3-center zig-zags only when the replacement segment remains entirely in water; `funnel` uses CSR-aligned edge portals first, validates the result against the land mask, then applies one more land-safe shortcut cleanup pass to remove small residual kinks. Route responses now also include `geometryModeRequested`, `geometryModeUsed`, and `geometryDiagnostics` for benchmark and fallback inspection. Routes can now apply conservative AIS corridor hints on top of funnel geometry when an exported hint exactly matches a routed edge window. Named-port port-approach hints still anchor start/end windows, and generic route-window hints can also shape matching interior or coordinate-route sections. Every shaped segment remains land-safe.
The runtime graph loader now memory-maps the immutable graph arrays from `graph.bin` and reconstructs only the derived runtime structures (`row_ptr`, `col_idx`, startup coordinate cache) in managed memory.
For the next AIS-guided display-shaping phase and the `ais-corridor-hints` artifact contract, see `AIS_ROUTE_SHAPING_PLAN.md`.

### 2. Restore .NET dependencies

```bash
dotnet restore
```

### 3. Install frontend dependencies

```bash
cd frontend && npm install
```

---

## Development

### Run the API (backend)

```bash
cd src/MaritimeNavMesh.Api
dotnet run
```

API listens on:
- HTTP: `http://localhost:5000`
- HTTPS: `https://localhost:7073`

The graph is loaded and validated at startup. Watch the logs — any port
validation warnings will appear there.

### Run the frontend

In a separate terminal:

```bash
cd frontend
npm run dev
```

Frontend runs at `http://localhost:5173`.
Set API base URL explicitly to avoid port mismatch:

```bash
cd frontend
VITE_API_URL=http://localhost:5000 npm run dev
```

If `VITE_API_URL` is not set, client/proxy defaults currently point to `http://localhost:5000`.
You can also update `frontend/vite.config.ts` proxy targets.

---

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/health` | Liveness + graph stats |
| `GET` | `/api/graph/stats` | Node/edge/component counts |
| `GET` | `/api/graph/snap?lat=&lon=&maxDistNm=` | Nearest node(s) to a coordinate |
| `GET` | `/api/runtime/profile` | Startup/load/memory profile for the loaded runtime |
| `GET` | `/api/runtime/benchmark/by-locode?from=&to=&iterations=&geometryMode=&algorithm=` | Repeated route benchmark on the real routing path |
| `GET` | `/api/runtime/benchmark/preset-suite?iterations=&geometryMode=&algorithm=` | Fixed three-route benchmark surface for mmap profiling |
| `GET` | `/api/ports` | List all snapped ports |
| `GET` | `/api/ports/{locode}` | Get a single port by LOCODE |
| `GET` | `/api/route/by-locode?from=AUMEL&to=SGSIN` | Route between two LOCODEs |
| `GET` | `/api/route/by-coordinate?fromLat=&fromLon=&toLat=&toLon=` | Route between two coordinates |

### Quick smoke-test (curl)

```bash
# Health check
curl http://localhost:5000/health

# Graph stats
curl http://localhost:5000/api/graph/stats

# Runtime startup / memory profile
curl http://localhost:5000/api/runtime/profile

# Repeated named-port route benchmark
curl "http://localhost:5000/api/runtime/benchmark/by-locode?from=AUMEL&to=SGSIN&iterations=3&geometryMode=funnel"

# Fixed preset benchmark surface
curl "http://localhost:5000/api/runtime/benchmark/preset-suite?iterations=1&geometryMode=funnel"

# Compare the optional bidirectional A* path against Dijkstra
curl "http://localhost:5000/api/runtime/benchmark/by-locode?from=AUMEL&to=SGSIN&iterations=3&geometryMode=funnel&algorithm=dijkstra"
curl "http://localhost:5000/api/runtime/benchmark/by-locode?from=AUMEL&to=SGSIN&iterations=3&geometryMode=funnel&algorithm=bidirectionalastar"
curl "http://localhost:5000/api/runtime/benchmark/preset-suite?iterations=1&geometryMode=funnel&algorithm=bidirectionalastar"

# All ports (truncated)
curl http://localhost:5000/api/ports | python3 -m json.tool | head -30

# Port by LOCODE
curl http://localhost:5000/api/ports/AUMEL

# Route by LOCODE
curl "http://localhost:5000/api/route/by-locode?from=AUMEL&to=SGSIN"
curl "http://localhost:5000/api/route/by-locode?from=AUMEL&to=SGSIN&algorithm=bidirectionalastar"

# Route by coordinates (Melbourne → Singapore)
curl "http://localhost:5000/api/route/by-coordinate?fromLat=-37.81&fromLon=144.96&toLat=1.35&toLon=103.82"
curl "http://localhost:5000/api/route/by-coordinate?fromLat=-37.81&fromLon=144.96&toLat=1.35&toLon=103.82&algorithm=bidirectionalastar"

# Nearest-node snap
curl "http://localhost:5000/api/graph/snap?lat=-37.81&lon=144.96&maxDistNm=50"
```

Current regional baseline after the first memory-mapped graph-loading cut:

- `/api/runtime/profile`
  - `startupTotalMs`: `746`
  - `graphLoadMs`: `248`
  - `graphRuntimeBuildMs`: `473`
  - `managedHeapBytes`: `33967432`
  - `workingSetBytes`: `200359936`
- `/api/runtime/benchmark/by-locode?from=AUMEL&to=SGSIN&iterations=3&geometryMode=funnel`
  - `avgSearchMs`: `102.0`
  - `p95SearchMs`: `113.0`
  - `avgTotalMs`: `124.9`
  - `p95TotalMs`: `144.1`

Cold-process first-query check:

```bash
# start a fresh API process without launchSettings port overrides
dotnet run --no-launch-profile --project src/MaritimeNavMesh.Api/MaritimeNavMesh.Api.csproj --urls http://127.0.0.1:5001

# in a second terminal, hit the fresh process once
curl "http://127.0.0.1:5001/api/runtime/profile"
curl "http://127.0.0.1:5001/api/runtime/benchmark/by-locode?from=AUFRE&to=IDJKT&iterations=1&geometryMode=funnel"
```

Current observed cold-process first-query result:

- fresh `/api/runtime/profile`
  - `startupTotalMs`: `820`
  - `graphLoadMs`: `312`
  - `graphRuntimeBuildMs`: `484`
- first route query `AUFRE -> IDJKT`
  - `avgSearchMs`: `67`
  - `avgTotalMs`: `77.0`
- warm repeat of the same query
  - `avgSearchMs`: `65`
  - `avgTotalMs`: `68.3`

This is a cold-process test, not a forced OS page-cache flush.

Current preset benchmark suite sample (`iterations=1`, `geometryMode=funnel`):

- `west_aus_to_java` (`AUFRE -> IDJKT`)
  - `avgSearchMs`: `68`
  - `avgTotalMs`: `78.3`
- `east_aus_to_java` (`AUSYD -> IDJKT`)
  - `avgSearchMs`: `58`
  - `avgTotalMs`: `81.9`
- `melbourne_to_singapore` (`AUMEL -> SGSIN`)
  - `avgSearchMs`: `106`
  - `avgTotalMs`: `130.1`

Current algorithm stance:

- default route algorithm: `bidirectionalastar`
- optional comparison algorithm: `dijkstra`
- reason: queue-balanced bidirectional A* with ALT-style landmark lower bounds now wins
  clearly on both search scope and steady-state query latency; the tradeoff is higher
  startup cost because landmark tables are built eagerly

Benchmark contract notes:

- track both latency and search scope
- `/api/runtime/benchmark/by-locode` and `/api/runtime/benchmark/preset-suite` now return:
  - `avgVisitedNodes`
  - `p95VisitedNodes`
  - `lastVisitedNodes`

Current visited-node comparison from the preset suite (`iterations=1`, `geometryMode=funnel`):

- Dijkstra
  - `AUFRE -> IDJKT`: `129500`
  - `AUSYD -> IDJKT`: `127462`
  - `AUMEL -> SGSIN`: `215973`
- Bidirectional A* (queue-balanced + landmarks)
  - `AUFRE -> IDJKT`: `12581`
  - `AUSYD -> IDJKT`: `25522`
  - `AUMEL -> SGSIN`: `23007`
  - `AUSYD -> IDJKT` split:
    - forward: `4667`
    - reverse: `20855`

This is the main search-scope signal to compare before changing the default algorithm.

---

## Build

```bash
# Build entire solution
dotnet build MaritimeNavMeshRouting.slnx

# Build release
dotnet build -c Release
```

---

## Test

```bash
# Run all tests
dotnet test MaritimeNavMeshRouting.slnx

# Run with output
dotnet test --logger "console;verbosity=detailed"

# Run a specific test class
dotnet test --filter "FullyQualifiedName~DijkstraTests"
dotnet test --filter "FullyQualifiedName~CsrGraphLoaderTests"
dotnet test --filter "FullyQualifiedName~GeoMathTests"
dotnet test --filter "FullyQualifiedName~KdTreeTests"
```

---

## Frontend build

```bash
cd frontend

# Type-check
npx tsc -p tsconfig.app.json --noEmit

# Production build
npm run build

# Preview production build
npm run preview
```

---

## OpenAPI / Swagger

When running in Development mode, the OpenAPI spec is available at:

```
http://localhost:5000/openapi/v1.json
```

---

## Configuration

Graph artifact paths and options are configured in
`src/MaritimeNavMesh.Api/appsettings.json`:

```json
{
  "Graph": {
    "BinaryPath":           "artifacts/ocean-h3-r5.graph.bin",
    "ManifestPath":         "artifacts/ocean-h3-r5.manifest.json",
    "PortsPath":            "artifacts/ocean-h3-r5.ports.json",
    "LandMaskPath":         "artifacts/ocean-h3-r5.land-mask.bin",
    "EdgePortalsPath":      "artifacts/ocean-h3-r5.edge-portals.bin",
    "AisCorridorHintsPath": "artifacts/ocean-h3-r5.ais-corridor-hints.bin",
    "DefaultRouteGeometryMode": "funnel",
    "DefaultRouteAlgorithm": "dijkstra",
    "EnableRouteSmoothing": true,
    "ValidateManifestHash": true,
    "DefaultMaxSnapDistanceNm": 50.0
  }
}
```

To override for a specific environment, use `appsettings.Development.json` or
environment variables:

```bash
Graph__BinaryPath=/abs/path/to/ocean-h3-r5.graph.bin dotnet run
```


Named-port route geometry now includes the exported marine-access connector polyline at both ends when available. This keeps the visible route aligned with the local water-side port access path rather than starting and ending only at the coarse graph node.
