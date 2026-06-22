# AIS-Guided Route Shaping Plan

## Current State

Project 2 already separates routing from display geometry:

1. The CSR graph route is authoritative for topology and cost.
2. Geometry is post-processed after graph search.
3. Land safety remains a hard constraint for any smoothing step.

Current geometry modes:

- `raw`: return the H3 centerline path unchanged.
- `shortcut`: bounded land-safe line-of-sight shortening.
- `funnel`: corridor-aware string pulling through exported edge portals, followed by one more land-safe cleanup pass.

The API now also returns geometry diagnostics:

- `geometryModeRequested`
- `geometryModeUsed`
- `geometryDiagnostics.rawPointCount`
- `geometryDiagnostics.funnelPointCount`
- `geometryDiagnostics.finalPointCount`
- `geometryDiagnostics.fallbackSectionCount`
- `geometryDiagnostics.sections[]`

This makes it possible to benchmark before and after any AIS-guided shaping step.

## Design Rules

These rules are non-negotiable:

- The graph route remains authoritative.
- AIS may guide display geometry only.
- AIS must not silently change route topology or edge cost.
- Port-access geometry must be preserved.
- Land-mask validation remains the final safety gate.
- If AIS shaping fails locally, only that local section falls back.

## Why Project 2 Must Not Consume Raw AIS

Project 2 should not depend on:

- raw AIS CSV
- raw AIS GeoJSONL
- full cleaned AIS trajectories as a shaping input
- full OSM shapefile

Those belong in Project 1.

Project 2 should consume compact compiled artifacts only. This keeps runtime behavior deterministic, bounded, and easy to validate.

## Phase 2 Artifact: AIS Corridor Hints

The first AIS-driven display improvement should use a compact derived artifact exported by Project 1:

```text
ocean-h3-r5.ais-corridor-hints.bin
```

This artifact should contain only high-confidence guide geometries, for example:

- stable port-approach lanes
- narrow-strait corridors
- strong established shipping bundles

Suggested schema per corridor hint:

- `corridor_id`
- `confidence`
- `support_count`
- `direction`
- `centerline polyline`
- `covered_h3_cells`
- `covered_h3_edges`
- `bounding box / region`
- `lane_width_m`
- `heading range`

The critical point is that hints should be graph-aligned, not merely spatially nearby. Project 2 should know which graph corridor or edge span a hint applies to.

## Phase 2 Runtime Pipeline

```text
[1. Graph Route]
      ↓
[2. Funnel Geometry]
      ↓
[3. Select AIS-Eligible Windows]
      ↓
[4. Apply AIS Corridor Shaping Locally]
      ↓
[5. Validate Shaped Sections]
      ↓
[6. Final Land-Safe LOS Cleanup]
      ↓
[7. Return Geometry + Diagnostics]
```

AIS shaping should be attempted only for local windows that satisfy all of the following:

- strong AIS confidence
- enough consecutive covered cells or edges
- acceptable route-heading alignment
- stable graph / port geometry in that area

## Phase 2 Validation Matrix

Any AIS-shaped local section must satisfy all checks below:

- land-safe against `ocean-h3-r5.land-mask.bin`
- remain inside the original routed corridor or a tight buffer
- stay close to funnel geometry
- keep length increase under a small threshold
- avoid self-intersections
- avoid heading flip / oscillation
- preserve anti-meridian correctness
- preserve port-access connectors

Recommended initial thresholds:

- max length increase: `1%`
- port-approach local length increase: at most `2–3%`
- heading alignment: dot product `> 0.7`
- minimum supported window: several consecutive cells / edges
- lateral deviation: capped by local H3 corridor width

If any local section fails, that section falls back to pure funnel output.

## Where to Start

Do not start with unresolved endpoint-geometry problem ports.

Start with:

- stable port approaches
- narrow straits with strong AIS support
- established high-traffic corridors where funnel geometry is already mostly correct

This avoids mixing endpoint-data problems with AIS-shaping behavior.

## Phase 3 Artifact: Multi-Modal AIS Flow Field

Only after corridor hints are stable should Project 1 export a second, richer artifact:

```text
ocean-h3-r5.ais-flow-field.bin
```

This should be multi-modal, not a simple average vector field. Example shape:

```json
{
  "cell_id": "8526850bfffffff",
  "total_support_count": 14200,
  "ambiguity_score": 0.12,
  "bins": [
    {
      "direction_bin": 2,
      "unit_vector": [0.707, -0.707],
      "confidence": 0.85,
      "support_count": 12000,
      "lane_center": [103.8123, 1.2521],
      "lane_width_m": 350
    }
  ]
}
```

Project 2 must choose the AIS bin that matches current route heading. Example rule:

```text
dot(route_heading, ais_bin_vector) > 0.7
```

Skip ambiguous or weak bins.

Direction vectors alone are not enough for lane centering. The artifact must also carry one of:

- lane-center point
- local lateral offset
- density ridge
- corridor centerline reference

## What Not to Do

Avoid these shortcuts:

- do not snap the route to the nearest AIS trajectory
- do not replace graph routing with AIS tracks
- do not use unconstrained spline, Bezier, or Chaikin smoothing
- do not apply AIS shaping globally by default
- do not let AIS override topology or cost
- do not hide fallback decisions

## Recommended Implementation Order

1. Keep the current `raw` / `shortcut` / `funnel` stack stable.
2. Use route-geometry diagnostics to benchmark difficult routes.
3. Export `ocean-h3-r5.ais-corridor-hints.bin` from Project 1.
4. Apply corridor hints first to stable port approaches, then to exact generic route windows in strong AIS-supported straits and corridors.
5. Expand to narrow straits and major shipping corridors.
6. Add `ocean-h3-r5.ais-flow-field.bin` only after Phase 2 behavior is validated.

That progression keeps display shaping useful without compromising routing correctness.
