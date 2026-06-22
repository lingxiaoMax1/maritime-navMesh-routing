# Maritime NavMesh Routing

Project 2 is the runtime side of the maritime routing system.
It consumes the compiled artifacts exported by Project 1 and serves:

- a C# routing API
- a frontend map client
- local runtime/benchmark endpoints for profiling

The runtime does not preprocess source geography itself. Project 1 owns graph compilation, land-mask generation, port snapping, portal export, and AIS shaping hints. Project 2 loads those outputs, validates them, and answers route queries.

## What is implemented

- compact CSR graph loading from `ocean-h3-r5.graph.bin`
- manifest hash and schema validation
- ports lookup from `ocean-h3-r5.ports.json`
- coordinate snapping via KD-tree
- named-port routing and coordinate routing
- Dijkstra and bidirectional A*
- queue-balanced bidirectional A* with landmark lower bounds
- route geometry modes: `raw`, `shortcut`, `funnel`
- land-safe smoothing using Project 1 land-mask + edge portals
- conservative AIS corridor shaping on top of funnel geometry
- runtime profiling and benchmark endpoints
- React + MapLibre frontend for interactive routing

## Repository layout

```text
MaritimeNavMeshRouting.slnx
src/
  MaritimeNavMesh.Api/      ASP.NET Core API
  MaritimeNavMesh.Core/     graph, routing, geometry, runtime indices
  MaritimeNavMesh.IO/       binary/json artifact loaders and manifest validation
frontend/                  React + Vite + MapLibre client
tests/MaritimeNavMesh.Tests/
benchmarks/MaritimeNavMesh.Benchmarks/
COMMANDS.md
IMPLEMENTATION.md
```

## Runtime artifact contract

The API expects Project 1 runtime artifacts under:

```text
src/MaritimeNavMesh.Api/artifacts/
```

Current required files:

- `ocean-h3-r5.graph.bin`
- `ocean-h3-r5.manifest.json`
- `ocean-h3-r5.ports.json`
- `ocean-h3-r5.land-mask.bin`
- `ocean-h3-r5.edge-portals.bin`
- `ocean-h3-r5.ais-corridor-hints.bin`

Current sample regional artifact sizes in this repo:

| Artifact | Old Size | New Size | Reduction |
|---|---:|---:|---:|
| `graph.bin` | 46 MB | 18.5 MB | 27.5 MB |
| `edge-portals.bin` | 41 MB | 0.54 MB | 40.46 MB |
| `land-mask.bin` | 15 MB | 0.19 MB | 14.81 MB |
| `ports.json` | 0.245 MB | 0.085 MB | 0.160 MB |
| `manifest.json` | n/a | 0.0073 MB | n/a |
| `ais-corridor-hints.bin` | n/a | 0.0059 MB | n/a |
| **Core runtime total** | **102.245 MB** | **19.315 MB** | **82.93 MB** |

Those files are copied in from Project 1. They are generated data, not hand-authored source.

### Why the runtime bundle is compact

Project 2 consumes a compact runtime contract rather than a verbose export.
The goal is to keep the shipped artifact set small while preserving the exact routing graph, edge costs, and land-safety behavior.

What stays lossless:

- same routable H3 cells
- same directed edges
- same `edge_cost` values
- same land-mask raster resolution and dilation

What gets reconstructed at load time instead of stored verbosely on disk:

- `row_ptr` is rebuilt from per-node `degree`
- `col_idx` is rebuilt from `int16` target deltas plus overflow arrays
- node center coordinates are derived from `node_h3_int`
- standard H3-neighbor portal geometry is reconstructed implicitly
- tiled land-mask payloads are decompressed on demand

Short artifact summary:

- `graph.bin`: compact CSR runtime graph with H3 identity, `degree`, delta-compressed targets, and exact `float32` edge costs
- `edge-portals.bin`: sparse implicit/explicit portal contract instead of one geometry record per directed edge
- `land-mask.bin`: tiled Web Mercator bitmask with empty-tile elimination and zlib-compressed non-empty tiles
- `ports.json`: compact field-table schema instead of repeated JSON objects

This is why Project 2 can load a regional runtime bundle of about `19.3 MB` instead of carrying a `100+ MB` verbose artifact set for the same graph.

## How the runtime works

### Graph loading

`MaritimeNavMesh.IO` memory-maps the immutable graph artifact and validates:

- magic/version
- array offsets and lengths
- SHA-256 against the manifest when enabled
- node/edge counts and schema consistency

The current graph format is a compact runtime layout:

- node identity stored as `node_h3_int`
- `degree` stored on disk instead of `row_ptr`
- edge targets stored as `int16` deltas plus overflow arrays
- coordinates reconstructed at startup from H3 IDs

At load time Project 2 rebuilds:

- `row_ptr`
- `col_idx`
- startup coordinate cache
- reverse adjacency
- KD-tree
- H3 index
- component index
- landmark heuristic tables

### Routing

Project 2 currently supports two search algorithms:

- `dijkstra`
- `bidirectionalastar`

Default is `bidirectionalastar`.

Routing entrypoints:

- LOCODE to LOCODE
- coordinate to coordinate

Ports route through their exported routing H3 / marine-access metadata rather than inventing port geometry at runtime.

### Geometry modes

The route topology stays fixed. Geometry post-processing is separate.

- `raw`: H3 graph centerline only
- `shortcut`: land-safe line-of-sight cleanup over the routed graph path
- `funnel`: portal-based smoothing, land-mask validation, final shortcut cleanup, optional AIS hint shaping

If a funnel section cannot be completed safely, that section falls back to shortcut mode and diagnostics are returned in the API response.

### Port handling

`ports.json` carries more than just snapped nodes. It includes:

- raw terminal coordinates
- snapped graph anchor
- routing H3
- marine access point
- display-only local access point
- local display access path
- marine access connector path

For named-port routes, the API stitches port access geometry onto the graph route before returning GeoJSON.

## API

Default backend config is in [appsettings.json](/Users/lingxiao/Projects/csharp/maritime-navMesh-routing/src/MaritimeNavMesh.Api/appsettings.json).

Current graph defaults:

- `EnableRouteSmoothing = true`
- `DefaultRouteGeometryMode = funnel`
- `DefaultRouteAlgorithm = bidirectionalastar`
- `ValidateManifestHash = true`
- `DefaultMaxSnapDistanceNm = 50.0`

### Endpoints

- `GET /health`
- `GET /api/graph/stats`
- `GET /api/graph/snap?lat=&lon=&maxDistNm=`
- `GET /api/runtime/profile`
- `GET /api/runtime/benchmark/by-locode?from=&to=&iterations=&geometryMode=&algorithm=`
- `GET /api/runtime/benchmark/preset-suite?iterations=&geometryMode=&algorithm=`
- `GET /api/ports`
- `GET /api/ports/{locode}`
- `GET /api/route/by-locode?from=AUMEL&to=SGSIN`
- `GET /api/route/by-coordinate?fromLat=&fromLon=&toLat=&toLon=`

### Route response shape

Successful route responses include:

- `totalDistanceNm`
- `totalCost`
- `nodeCount`
- `searchTimeMs`
- `geometry`
- `rawGraphGeometry`
- `geometryModeRequested`
- `geometryModeUsed`
- `geometryDiagnostics`

The diagnostics payload reports raw/funnel/final point counts, fallback section count, and AIS-shaped section count.

## Frontend

The frontend is a React + Vite + MapLibre app under `frontend/`.

Current behavior:

- loads ports from `/api/ports`
- click a port or map point to set origin and destination
- auto-runs route query after both waypoints are set
- supports both LOCODE routing and coordinate routing automatically
- shows:
  - raw port markers
  - selected points
  - marine access points
  - local display access connectors
  - final route geometry
- splits dateline-crossing linework before rendering

The API base URL defaults to:

```text
http://localhost:5000
```

and can be overridden with `VITE_API_URL`.

## Setup

### Prerequisites

- .NET SDK 10.x
- Node.js 22+
- npm 10+

### 1. Copy Project 1 artifacts

```bash
mkdir -p src/MaritimeNavMesh.Api/artifacts

cp ../maritime-navMesh-builder/outputs/melbourne-singapore/h3_r5/ocean-h3-r5.graph.bin src/MaritimeNavMesh.Api/artifacts/
cp ../maritime-navMesh-builder/outputs/melbourne-singapore/h3_r5/ocean-h3-r5.manifest.json src/MaritimeNavMesh.Api/artifacts/
cp ../maritime-navMesh-builder/outputs/melbourne-singapore/h3_r5/ocean-h3-r5.ports.json src/MaritimeNavMesh.Api/artifacts/
cp ../maritime-navMesh-builder/outputs/melbourne-singapore/h3_r5/ocean-h3-r5.land-mask.bin src/MaritimeNavMesh.Api/artifacts/
cp ../maritime-navMesh-builder/outputs/melbourne-singapore/h3_r5/ocean-h3-r5.edge-portals.bin src/MaritimeNavMesh.Api/artifacts/
cp ../maritime-navMesh-builder/outputs/melbourne-singapore/h3_r5/ocean-h3-r5.ais-corridor-hints.bin src/MaritimeNavMesh.Api/artifacts/
```

### 2. Restore and build

```bash
dotnet restore
cd frontend && npm install
```

## Running locally

### Backend

```bash
cd src/MaritimeNavMesh.Api
dotnet run
```

Default dev URLs:

- `http://localhost:5000`
- `https://localhost:7073`

### Frontend

```bash
cd frontend
VITE_API_URL=http://localhost:5000 npm run dev
```

Frontend dev server:

- `http://localhost:5173`

## Validation

Current repo-local validation commands:

```bash
dotnet test MaritimeNavMeshRouting.slnx
cd frontend && npm run build
```

Current state validated locally while writing this README:

- `.NET tests`: `63 passed`
- `frontend build`: passed

## Benchmarking and profiling

Use the runtime endpoints for actual routing-path measurements rather than synthetic microbenchmarks:

```bash
curl http://localhost:5000/api/runtime/profile
curl "http://localhost:5000/api/runtime/benchmark/by-locode?from=AUMEL&to=SGSIN&iterations=3&geometryMode=funnel"
curl "http://localhost:5000/api/runtime/benchmark/preset-suite?iterations=1&geometryMode=funnel"
```

Those endpoints expose both latency and search-scope metrics, including visited-node counts for algorithm comparison.

## Notes and scope boundaries

- Project 2 is a runtime engine, not a preprocessing pipeline.
- Project 2 should not ingest raw OSM, GEBCO, or AIS source datasets directly.
- Project 1 owns all heavy preprocessing and artifact generation.
- The sample artifacts in this repo are regional, not global.
- `src/MaritimeNavMesh.Api/artifacts/` is ignored in git and should be treated as copied runtime input.

## Related docs

- [COMMANDS.md](/Users/lingxiao/Projects/csharp/maritime-navMesh-routing/COMMANDS.md)
- [IMPLEMENTATION.md](/Users/lingxiao/Projects/csharp/maritime-navMesh-routing/IMPLEMENTATION.md)
- [frontend/README.md](/Users/lingxiao/Projects/csharp/maritime-navMesh-routing/frontend/README.md)
