# Project 2 Implementation Reference

This document describes the **current, as-built** implementation of Maritime NavMesh Routing
(Project 2). For the next AIS-guided display-shaping phase, see `AIS_ROUTE_SHAPING_PLAN.md`.

---

## Status

Phases 1–4B are **complete and passing** (`dotnet test`: `63/63`). The first
global-runtime loading cut is also in place: Project 2 now memory-maps the immutable
on-disk graph arrays and reconstructs only the derived/runtime-owned structures in
managed memory.

| Phase | Scope | Status |
|-------|-------|--------|
| 1 — Loader | Solution scaffold, CSR binary loader, manifest validation, port loader | ✅ Done |
| 2 — Routing | Dijkstra, path reconstruction, geometry builder | ✅ Done |
| 3 — Indices | H3Index, KdTree2D, ComponentIndex | ✅ Done |
| 4 — API | ASP.NET Core minimal API, all endpoints | ✅ Done |
| 4B — Frontend | Vite + React + MapLibre GL, route panel, source/target picking + rendering | ✅ Done |
| 5 — A* / BiDi | Bidirectional A* implemented and benchmarkable beside Dijkstra | 🟡 In Progress |
| 6 — Global | Memory-mapped graph loading baseline complete; ALT / CH remain future work | 🟡 In Progress |

---

## Solution Structure

```
MaritimeNavMeshRouting.slnx

src/
  MaritimeNavMesh.Core/           # Pure graph logic — no IO, no ASP.NET
    Graph/
      CsrOceanGraph.cs            # Immutable CSR graph data class
    Routing/
      Dijkstra.cs                 # Per-request allocation, double accumulation
      RouteRequest.cs             # Input DTO (fromNode, toNode)
      RouteResult.cs              # Output (status, path arrays, cost, distNm, diagnostics)
    Indices/
      H3Index.cs                  # Dictionary<long,int> — H3 int → node index
      ComponentIndex.cs           # Component stats, same-component checks
      KdTree2D.cs                 # 2D KD-tree over lon/lat for coord snap
    Geometry/
      GeoMath.cs                  # Haversine NM, longitude delta normalisation
      RouteGeometryBuilder.cs     # raw/shortcut/funnel geometry + conservative AIS hint shaping
    Models/
      PortSnap.cs                 # Port record (locode, name, snappedH3Hex, …)
      ComponentStats.cs           # Component metadata
    Runtime/
      GraphRuntime.cs             # Builds all indices; port validation; snap + route entry points

  MaritimeNavMesh.IO/             # Binary loading only; returns Core models
    Loaders/
      CsrGraphLoader.cs           # Full binary validation (magic, header, CSR invariants, SHA-256)
      PortLookupLoader.cs         # Reads ports.json flat array → PortSnap[]
      ManifestValidator.cs        # Cross-validates manifest JSON vs loaded graph + ports
    Models/
      GraphManifest.cs            # JSON deserialization model for manifest and optional AIS hint metadata

  MaritimeNavMesh.Api/            # ASP.NET Core minimal API
    Program.cs                    # DI wiring, CORS, hosted loader, endpoint registration
    Services/
      GraphService.cs             # Singleton runtime holder
      GraphLoaderHostedService    # IHostedService — loads graph before first request
    Endpoints/
      HealthEndpoints.cs          # GET /health
      GraphEndpoints.cs           # GET /api/graph/stats, GET /api/graph/snap
      PortEndpoints.cs            # GET /api/ports, GET /api/ports/{locode}
      RouteEndpoints.cs           # GET /api/route/by-locode, GET /api/route/by-coordinate
    Models/
      ApiModels.cs                # Request/response records + GeoJSON types
      GraphOptions.cs             # Config binding for artifact paths (Graph: section)
    appsettings.json              # Default artifact paths under artifacts/

benchmarks/
  MaritimeNavMesh.Benchmarks/
    Program.cs                    # BenchmarkDotNet entry point (no benchmarks written yet)

tests/
  MaritimeNavMesh.Tests/
    Fixtures/
      SyntheticGraphBuilder.cs    # 4-node diamond graph: 0→1→3 (cost 2) vs 0→2→3 (cost 3)
    IO/
      CsrGraphLoaderTests.cs      # Happy path, hash mismatch, bad magic, file too small
    Core/
      DijkstraTests.cs            # Shortest path, same node, directed no-path, invalid nodes
      BidirectionalAStarTests.cs  # parity with Dijkstra on the directed synthetic graph
      GeoMathTests.cs             # Haversine spot checks, anti-meridian wrap
      KdTreeTests.cs              # Exact snap, nearest sort, empty result

frontend/                         # Vite + React + TypeScript
  src/
    api/
      client.ts                   # Typed fetch wrapper; VITE_API_URL env var
    store/
      routeStore.ts               # Zustand store (ports, from/to, route, loading, error)
    components/
      MapView.tsx                 # MapLibre GL map + route LineString layer
      RoutePanel.tsx              # Port-pair selector, route button, result stats
    App.tsx                       # Root: loads ports on mount, renders MapView + RoutePanel
  vite.config.ts                  # Dev proxy: /api → http://localhost:5000
```

---

## Graph Runtime Design

```
Project 1 artifacts
   ocean-h3-r5.graph.bin        (18 MB, 399,587 nodes, 2,149,348 directed edges)
   ocean-h3-r5.manifest.json    (7.1 KB, sha256 + exact offsets/dtypes)
   ocean-h3-r5.ports.json       (~129 KB, compact runtime schema for 112 ports + marine access metadata and connector paths)
   ocean-h3-r5.land-mask.bin    (~189 KB, 500 m conservative tiled land mask with compressed tile payloads for route smoothing)
   ocean-h3-r5.edge-portals.bin (~532 KB, implicit/explicit portal bundle for funnel smoothing; 648 explicit directed edges, 324 unique explicit payloads)
   ocean-h3-r5.ais-corridor-hints.bin (~5.9 KB, 36 conservative AIS shaping hints: 12 port-approach + 24 generic route-window)
            │
            │  CsrGraphLoader (IO)          PortLookupLoader (IO)
            │    • validate magic             • compact runtime port schema
            │    • verify SHA-256             • null snapped_h3 skipped
            │    • parse 8 runtime arrays → PortSnap[]
            │    → CsrOceanGraph
            │
            └──────────────── GraphRuntime (Core) ──────────────────
                                  • H3Index (built from NodeH3Int)
                                  • ComponentIndex (largest component = dominant)
                                  • KdTree2D (built from NodeLat/NodeLon)
                                  • PortsByLocode Dictionary
                                  • AIS corridor hints loaded at startup for conservative funnel shaping: named-port approach windows plus generic exact route-window matches
                                  • Port validation warnings (logged at startup)
                                  • SnapPortToNode(locode) → marine-access nodeIndex
                                  • SnapCoordinateToNode(lat, lon) → nodeIndex
                                  • Route(from, to) → RouteResult
```

### Layer rules

| Layer | May use | May NOT use |
|-------|---------|-------------|
| Core | Core only | IO, ASP.NET |
| IO | Core (models) | Core (indices), ASP.NET |
| API | Core + IO | Direct binary reads |

---

## Binary Format Reference

```
ocean-h3-r5.graph.bin — little_endian_csr_runtime

Header (96 bytes):
  [0..7]   magic       = "OCNCSR1\0"  (8 bytes, ASCII)
  [8..11]  version     = 1             (uint32)
  [12..15] resolution  = 5             (uint32)
  [16..23] nodeCount   = 399587        (uint64)
  [24..31] edgeCount   = 2149348       (uint64)
  [32..95] offsets[8]                 (8 × uint64)

Arrays (immediately after header, in offset order):
  node_h3_int           int64   [nodeCount]
  node_component        int32   [nodeCount]
  node_class            uint8   [nodeCount]
  degree                uint8   [nodeCount]
  edge_target_delta_i16 int16   [edgeCount]
  edge_cost             float32 [edgeCount]
  overflow_edge_pos     uint32  [overflowCount]
  overflow_col_idx      uint32  [overflowCount]

Project 2 memory-maps the immutable on-disk arrays (`node_h3_int`, `node_component`,
`node_class`, `degree`, `edge_target_delta_i16`, `edge_cost`, `overflow_edge_pos`,
`overflow_col_idx`) and reconstructs the runtime-owned structures in memory:
`row_ptr` from `degree`, `col_idx` from the delta stream and overflow tables, and
`NodeLat` / `NodeLon` once at startup from `node_h3_int` via `pocketken.H3`.

The mmap lifecycle is now hardened in the live runtime:

- `CsrGraphLoader` disposes the mapped backing store if binary validation fails part-way.
- `GraphRuntime` owns the mapped graph and disposes it explicitly.
- `GraphService` only swaps in the new runtime after the full startup load succeeds.
- host shutdown disposes the runtime instead of leaving the mapping to finalizer/process exit behavior.
```

**H3 integer parsing (C#):**
H3 cell IDs are `uint64` values stored as `int64` (many have the high bit set).
Parse hex strings with `ulong.TryParse(hex, NumberStyles.HexNumber, …)` then cast to `long`.
Never use `Convert.ToInt64(hex, 16)` — it throws on values > `long.MaxValue`.

---

## Dijkstra Implementation Notes

- **Per-request allocation**: `dist[]`, `prevNode[]`, `prevEdge[]` are `new` each call.
  Thread-safe — no shared mutable state.
- **Double accumulation**: `dist[]` and priority queue use `double`, not `float`.
  Avoids drift on routes with thousands of hops.
- **Stale entry filter**: `if (nodeDist > dist[node]) continue;` — safe lazy deletion.
- **Bathymetry ownership**: Project 2 no longer does runtime edge-depth filtering. Project 1 owns bathymetry and compiles a cargo-safe graph ahead of time.
- **Stop condition**: break when target node is popped from queue.
- Returns `RouteStatus.NoPath` if `dist[to] == double.PositiveInfinity`.

---

## API Endpoints

All routes return JSON. Route failures return HTTP 200 with `success: false` and an
`errorCode` string. HTTP-level errors (e.g. 503 before graph is ready) use standard
status codes.

### `GET /health`

```json
{
  "status": "ok",
  "message": "Graph loaded and ready",
  "nodeCount": 399587,
  "edgeCount": 2149348,
  "portCount": 112
}
```

Returns `503` with `{ "Status": "loading" }` if called before the graph is ready.

### `GET /api/graph/stats`

```json
{
  "nodeCount": 399587,
  "edgeCount": 2149348,
  "resolution": 5,
  "componentCount": 22270,
  "portCount": 112
}
```

### `GET /api/graph/snap?lat=&lon=&maxDistNm=`

Returns up to 5 nearest nodes sorted by haversine distance.

```json
[
  {
    "nodeIndex": 12345,
    "lat": -37.81,
    "lon": 144.96,
    "snapDistanceNm": 0.42,
    "componentId": 0,
    "nodeClass": 1
  }
]
```

`maxDistNm` defaults to the `Graph:DefaultMaxSnapDistanceNm` config value (50 nm).

### `GET /api/ports`

Returns all 112 snapped ports as a flat array:

```json
[
  {
    "locode": "AUMEL",
    "name": "Melbourne",
    "snappedH3Hex": "85a7268bfffffff",
    "snappedLat": -37.81,
    "snappedLon": 144.96,
    "snapDistanceNm": 2.1,
    "marineAccessH3Hex": "87be63192ffffff",
    "marineAccessLat": -37.8610,
    "marineAccessLon": 144.9420,
    "marineAccessSource": "local_graph_entry_search",
    "marineAccessPathIsLandSafe": true,
    "componentId": 0
  }
]
```

### `GET /api/ports/{locode}`

Single port by LOCODE. `404` if not found.

### `GET /api/route/by-locode?from=AUMEL&to=SGSIN`

Route between two LOCODEs. Both must be present in ports.json. The runtime uses
marine_access_h3 when present and falls back to snapped_h3 when connector data is absent.
The map displays `marine_access_display_*` when exported by Project 1. Those fields describe the nearest local water-side access point (`r10` first, `r9` fallback) and the short dashed raw-port -> access-point display connector. The actual named-port route geometry is stitched from `marine_access_path_coordinates`, which carries the water-following connector from that local access point into the routing graph anchor. Raw inland terminal coordinates are not inserted into navigational route geometry.

Project 2 also loads `ocean-h3-r5.land-mask.bin`, a 500 m conservative Web Mercator land bitmask exported by Project 1, together with `ocean-h3-r5.edge-portals.bin`, the current portal file. The land mask is a tiled binary with compressed per-tile payloads: Project 2 reads the global metadata and tile index at startup, then loads and inflates tile payloads lazily as route-geometry segment checks touch them. The portal file stores two CSR-edge-order bitsets plus sparse explicit payloads only; canonical same-resolution H3-neighbor portals are reconstructed lazily from `node_h3_int` and cached on first use, and only mixed-resolution/special portals remain explicit on disk. Route geometry execution is explicit: `raw` returns the H3 centerline unchanged; `shortcut` performs bounded land-safe line-of-sight shortening; `funnel` pulls the route through exported portals, validates the result against the land mask, then applies one more land-safe shortcut cleanup pass. If a funnel run is incomplete or unsafe, only that run falls back to `shortcut`. Port-access paths are preserved, route cost and topology are unchanged, and `rawGraphGeometry` remains available in the API response for debugging.

The API now also returns `geometryModeRequested`, `geometryModeUsed`, and `geometryDiagnostics` so benchmark routes can report point-count reduction and per-section fallback behavior explicitly.

```json
{
  "success": true,
  "errorCode": null,
  "errorMessage": null,
  "totalDistanceNm": 3274.1,
  "totalCost": 3412.7,
  "nodeCount": 892,
  "geometryModeRequested": "funnel",
  "geometryModeUsed": "funnel",
  "geometryDiagnostics": {
    "geometryModeRequested": "funnel",
    "geometryModeUsed": "funnel",
    "rawPointCount": 184,
    "funnelPointCount": 27,
    "finalPointCount": 19,
    "fallbackSectionCount": 1,
    "sections": [
      {
        "startNodeOffset": 120,
        "endNodeOffset": 141,
        "baseMode": "funnel",
        "finalMode": "shortcut",
        "reason": "missing_portal"
      }
    ]
  },
  "geometry": {
    "type": "FeatureCollection",
    "features": [
      {
        "type": "Feature",
        "geometry": { "type": "LineString", "coordinates": [[144.96, -37.81], …] },
        "properties": {}
      }
    ]
  }
}
```

On failure:

```json
{
  "success": false,
  "errorCode": "NO_PATH",
  "errorMessage": "No path found between the given nodes",
  …
}
```

Error codes: `PORT_NOT_FOUND`, `NO_PATH`, `INVALID_NODE`.

### `GET /api/route/by-coordinate?fromLat=&fromLon=&toLat=&toLon=&maxSnapDistNm=`

Same response shape as by-locode. Error code `SNAP_FAILED` if no node within snap distance.

---

## Frontend Architecture

```
App.tsx
  useEffect → loadPorts() (on mount)
  renders:
    MapView.tsx         full-screen MapLibre GL map
    RoutePanel.tsx      absolute-positioned left panel
```

### State (Zustand — `routeStore.ts`)

```ts
{
  ports: Port[]              // loaded once at startup from GET /api/ports
  portsError: string | null  // API load failure (separate from route errors)
  from: Waypoint | null      // { label, lat, lon, locode? }
  to: Waypoint | null
  route: RouteResponse | null
  queryTimeMs: number | null // Dijkstra ElapsedMs from backend
  loading: boolean
  error: string | null

  loadPorts()                // GET /api/ports
  selectWaypoint(wp)         // state machine: sets from→to→restart cycle
  deselectWaypoint('from'|'to')
  computeRoute()             // by-locode (port↔port) or by-coordinate (otherwise)
  clearRoute()
}
```

`Waypoint` is `{ label: string; lat: number; lon: number; locode?: string }`.
`locode` is only set for port waypoints; coordinate picks omit it.
Port-to-port routing uses `by-locode`; any other combination uses `by-coordinate`.

### Map layers

| Layer id | Source | Style |
|----------|--------|-------|
| `route-line` | `route` (GeoJSON) | sky-blue `#0284c7`, 3 px line, 0.9 opacity |
| `ports-circle` | `ports` (GeoJSON) | amber dots, 4 px radius, white stroke |
| `picks-halo` | `picks` (GeoJSON) | green (from) / red (to) halo ring, low opacity |
| `picks-circle` | `picks` (GeoJSON) | green (from) / red (to) dot, 5 px radius, white stroke |

Base map: CartoCDN Positron (`positron-gl-style`, light theme).

The route GeoJSON `FeatureCollection` is rendered as one or more `LineString` features.
Multiple features occur only when the route crosses the anti-meridian.

Port dots change color to green or red when selected as source/target.
Coordinate picks (non-port) appear only in `picks` layers (not in `ports` source).

### API client (`client.ts`)

Base URL is controlled by `VITE_API_URL` environment variable (default: `http://localhost:5000`).
In development the Vite proxy rewrites `/api/*` calls to avoid CORS issues.

---

## Configuration

`src/MaritimeNavMesh.Api/appsettings.json`:

```json
{
  "Graph": {
    "BinaryPath":               "artifacts/ocean-h3-r5.graph.bin",
    "ManifestPath":             "artifacts/ocean-h3-r5.manifest.json",
    "PortsPath":                "artifacts/ocean-h3-r5.ports.json",
    "LandMaskPath":             "artifacts/ocean-h3-r5.land-mask.bin",
    "EdgePortalsPath":          "artifacts/ocean-h3-r5.edge-portals.bin",
    "DefaultRouteGeometryMode": "funnel",
    "EnableRouteSmoothing":     true,
    "ValidateManifestHash":     true,
    "DefaultMaxSnapDistanceNm": 50.0
  }
}
```

Set `ValidateManifestHash: false` to skip SHA-256 verification (e.g. for development
builds where the binary may be regenerated frequently).

---

## KdTree2D Notes

- Built once at startup by `GraphRuntime` from `graph.NodeLat` / `graph.NodeLon`.
- Split axis alternates: depth-even → split on lat, depth-odd → split on lon.
- Leaf size 16 — brute-forces leaves; avoids tree overhead for small batches.
- Axial pruning: latitude splits use `1° ≈ 60 nm`; longitude splits use
  `1° ≈ 60 × cos(lat) nm` (implemented). This is important at high latitudes —
  at 60°N (Rotterdam, Oslo) the longitude factor is ≈ 30 nm/°, so a flat 60 nm
  constant would over-prune valid branches.
- `QueryNearest` returns up to `maxK` results sorted by **haversine** distance (KD-tree
  used only for candidate selection).

---

## Port Validation at Startup

`GraphRuntime` validates every port against the loaded graph:

1. `snapped_h3` hex must resolve to a node via `H3Index`.
2. If `component_id` is set, it must match `graph.NodeComponent[nodeIndex]`.

Failures are **warnings** (not exceptions) because the current graph is a regional
subset — some Project 1 ports may have been snapped to cells outside the bbox.
Warnings are logged at startup and retrievable via `/health`.

---

## What is Not Yet Implemented

| Feature | Notes |
|---------|-------|
| ALT landmarks / Contraction Hierarchies | Phase 6 |
| Benchmarks | `MaritimeNavMesh.Benchmarks` project exists but contains no benchmarks yet |
| Integration tests against real artifact | Planned — requires artifact in test fixtures or env var path |
| Port autocomplete / search | Frontend Phase 5B |
| Route diagnostics drawer | Frontend Phase 5B |
| PMTiles graph/cell visualization | Future — after Project 1 exports PMTiles |

---

## Test Coverage Summary

Current passing test count:

- `63/63` total

Coverage areas include:

- CSR graph loading and manifest/hash validation
- compressed adjacency reconstruction
- H3-derived startup coordinate cache
- routing and no-path behavior
- KD-tree snapping
- route geometry building and fallback behavior
- edge portal loading
- land-mask loading
- AIS corridor-hint loading
- graph runtime port-access routing behavior

---

## Known Limitations (Current Regional Build)

- Graph covers Melbourne-Singapore bbox only — not global.
- 29,442 edges have direct/map-matched AIS weighting evidence before smoothing; k2 diffusion informs 307,540 edges.
- `edge_flags` is no longer part of the runtime graph. If edge-level flags become query-time behavior later, they should return as an explicit optional runtime array or sidecar artifact.
- Some port snaps may lie outside the regional bbox → logged as startup warnings.


Named-port route geometry now includes the exported marine-access connector polyline at both ends when available. This keeps the visible route aligned with the local water-side port access path rather than starting and ending only at the coarse graph node.
### Runtime profiling

Project 2 now exposes two runtime-observability endpoints:

- `/api/runtime/profile`
  - startup total time
  - graph load time
  - ports load time
  - graph-runtime build time
  - edge-portals load time
  - AIS hint load time
  - land-mask load time
  - managed heap bytes after startup
  - process working set bytes after startup
- `/api/runtime/benchmark/by-locode`
  - repeated named-port route benchmark on the real routing path
  - reports average/p95 search time and average/p95 end-to-end route time
  - reports average/p95 visited nodes so search scope is tracked with latency
  - accepts `algorithm=dijkstra|bidirectionalastar`
  - intended to compare route performance across runtime-loading strategies
- `/api/runtime/benchmark/preset-suite`
  - runs a small fixed benchmark surface over three named regional routes
  - reports average/p95 visited nodes per scenario
  - accepts `algorithm=dijkstra|bidirectionalastar`
  - intended to keep mmap profiling repeatable without ad hoc manual route selection

Current regional baseline after the first memory-mapped graph-loading cut:

- startup total: `746 ms`
- graph load: `248 ms`
- ports load: `7 ms`
- graph-runtime build: `473 ms`
- edge-portals load: `11 ms`
- AIS hints load: `2 ms`
- land-mask load: `0 ms` at startup because tile payloads remain lazy
- managed heap after startup: `33,967,432` bytes
- working set after startup: `200,359,936` bytes

Sample route benchmark (`AUMEL -> SGSIN`, `iterations=3`, `geometryMode=funnel`):

- average search time: `102.0 ms`
- p95 search time: `113.0 ms`
- average total route time: `124.9 ms`
- p95 total route time: `144.1 ms`
- last route node count: `720`
- last visited nodes: `215,973`

Cold-process first-query validation (fresh API process, first route on a different long
regional path, without forcing an OS page-cache flush):

- startup profile on the fresh process:
  - `startupTotalMs`: `820`
  - `graphLoadMs`: `312`
  - `graphRuntimeBuildMs`: `484`
  - `managedHeapBytes`: `33,816,608`
  - `workingSetBytes`: `200,540,160`
- first route query (`AUFRE -> IDJKT`, `iterations=1`, `geometryMode=funnel`):
  - search: `67 ms`
  - total route time: `77.0 ms`
  - HTTP total time: `83.9 ms`
- warm repeat of the same query:
  - search: `65 ms`
  - total route time: `68.3 ms`
  - HTTP total time: `69.7 ms`

This is a cold-process / first-query validation, not a true kernel page-cache flush.
The result is still useful: first-touch latency did not spike into seconds, so the
current memory-mapped graph layout is stable enough to continue with the global-scale
runtime work before introducing more aggressive graph-loading changes.

Current preset benchmark suite (`iterations=1`, `geometryMode=funnel`) on a fresh
runtime process:

- `west_aus_to_java` (`AUFRE -> IDJKT`)
  - search: `68 ms`
  - total route time: `78.3 ms`
  - visited nodes: `129,500`
- `east_aus_to_java` (`AUSYD -> IDJKT`)
  - search: `58 ms`
  - total route time: `81.9 ms`
  - visited nodes: `127,462`
- `melbourne_to_singapore` (`AUMEL -> SGSIN`)
  - search: `106 ms`
  - total route time: `130.1 ms`
  - visited nodes: `215,973`

Bidirectional A* is now implemented as an optional routing algorithm. It uses:

- the current directed CSR graph
- reverse adjacency built once from the immutable graph
- a symmetric lower-bound heuristic derived from haversine distance multiplied by the
  graph-wide minimum observed `edge_cost / distance_nm` ratio

Current regional comparison on the preset suite after queue-balanced frontier expansion,
forward/reverse visited-node instrumentation, and ALT-style landmark lower bounds:

- Bidirectional A* now shows a major search-scope improvement:
  - `AUFRE -> IDJKT`: `12,581` visited vs Dijkstra `129,500`
  - `AUSYD -> IDJKT`: `25,522` visited vs Dijkstra `127,462`
  - `AUMEL -> SGSIN`: `23,007` visited vs Dijkstra `215,973`
- The forward/reverse split is now visible in the benchmark response:
  - `AUSYD -> IDJKT`: forward `4,667`, reverse `20,855`

However, query time is still the primary operational metric, and on the current fresh
regional suite bidirectional A* is now faster end-to-end:

- `AUFRE -> IDJKT`
  - Dijkstra total: `78.0 ms`
  - Bidirectional A* total: `30.7 ms`
- `AUSYD -> IDJKT`
  - Dijkstra total: `100.6 ms`
  - Bidirectional A* total: `60.5 ms`
- `AUMEL -> SGSIN`
  - Dijkstra total: `131.9 ms`
  - Bidirectional A* total: `75.1 ms`

So the current conclusion is:

- bidirectional A* is materially better in both search scope and steady-state latency
- the tradeoff is startup cost: landmark tables are built eagerly at startup
- startup profile increased to approximately:
  - `startupTotalMs`: `1561`
  - `graphRuntimeBuildMs`: `1228`
  - `managedHeapBytes`: `65,401,904`
  - `workingSetBytes`: `243,630,080`
- because query time is the higher-priority operational metric, bidirectional A* is now
  the configured default route algorithm

Current visited-node comparison is now part of the runtime benchmark contract.
Using the preset suite (`iterations=1`, `geometryMode=funnel`):

- Dijkstra
  - `AUFRE -> IDJKT`: `129,500` visited
  - `AUSYD -> IDJKT`: `127,462` visited
  - `AUMEL -> SGSIN`: `215,973` visited
- Bidirectional A* (queue-balanced + landmarks)
  - `AUFRE -> IDJKT`: `12,581` visited
  - `AUSYD -> IDJKT`: `25,522` visited
  - `AUMEL -> SGSIN`: `23,007` visited

That is the signal to watch going forward: if bidirectional A* reduces visited nodes
consistently, query time usually follows; if visited nodes rise, the heuristic or stop
condition is not doing enough useful work for that route family.
