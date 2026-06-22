import { useEffect, useRef } from 'react';
import maplibregl from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';
import { useRouteStore } from '../store/routeStore';
import type { Port } from '../api/client';
import type { Waypoint } from '../store/routeStore';

// ── GeoJSON helpers ──────────────────────────────────────────────────────────

function buildPortsGeoJSON(ports: Port[], from: Waypoint | null, to: Waypoint | null) {
  return {
    type: 'FeatureCollection' as const,
    features: ports.map((p) => {
      const isFrom = from?.locode === p.locode;
      const isTo   = to?.locode   === p.locode;
      return {
        type: 'Feature' as const,
        geometry: {
          type: 'Point' as const,
          coordinates: [p.portLon ?? p.snappedLon, p.portLat ?? p.snappedLat],
        },
        properties: {
          locode: p.locode,
          name: p.name,
          role: isFrom ? 'from' : isTo ? 'to' : 'none',
        },
      };
    }),
  };
}

function buildPicksGeoJSON(from: Waypoint | null, to: Waypoint | null) {
  const features = [];
  if (from) features.push({
    type: 'Feature' as const,
    geometry: { type: 'Point' as const, coordinates: [from.lon, from.lat] },
    properties: { role: 'from', label: from.label },
  });
  if (to) features.push({
    type: 'Feature' as const,
    geometry: { type: 'Point' as const, coordinates: [to.lon, to.lat] },
    properties: { role: 'to', label: to.label },
  });
  return { type: 'FeatureCollection' as const, features };
}

function selectedPort(ports: Port[], waypoint: Waypoint | null) {
  if (!waypoint?.locode) return null;
  return ports.find((port) => port.locode === waypoint.locode) ?? null;
}

function buildMarineAccessGeoJSON(ports: Port[], from: Waypoint | null, to: Waypoint | null) {
  const features: GeoJSON.Feature<GeoJSON.Point>[] = [];
  for (const port of ports) {
    const markerLon = port.marineAccessDisplayLon ?? port.marineAccessLon;
    const markerLat = port.marineAccessDisplayLat ?? port.marineAccessLat;
    if (markerLon == null || markerLat == null) continue;
    const role =
      from?.locode === port.locode ? 'from' :
      to?.locode === port.locode ? 'to' :
      'none';
    features.push({
      type: 'Feature',
      geometry: {
        type: 'Point',
        coordinates: [markerLon, markerLat],
      },
      properties: { role, locode: port.locode, name: port.name },
    });
  }
  return { type: 'FeatureCollection' as const, features };
}

function buildPortAccessConnectorsGeoJSON(ports: Port[], from: Waypoint | null, to: Waypoint | null) {
  const features: GeoJSON.Feature<GeoJSON.LineString>[] = [];
  for (const [role, waypoint] of [['from', from], ['to', to]] as const) {
    const port = selectedPort(ports, waypoint);
    const displayPath = port?.marineAccessDisplayPathCoordinates ?? null;
    if (!port || !displayPath) continue;
    for (const coordinates of splitDatelineLineString(displayPath)) {
      if (coordinates.length < 2) continue;
      features.push({
        type: 'Feature',
        geometry: { type: 'LineString', coordinates },
        properties: { role, locode: port.locode, navigational: false },
      });
    }
  }
  return { type: 'FeatureCollection' as const, features };
}

function splitDatelineLineString(coords: number[][]): number[][][] {
  if (coords.length <= 1) return [coords];
  const segments: number[][][] = [];
  let current: number[][] = [[coords[0][0], coords[0][1]]];

  for (let i = 1; i < coords.length; i++) {
    const prev = coords[i - 1];
    const curr = coords[i];
    const prevLon = prev[0];
    const currLon = curr[0];
    const currLat = curr[1];
    const rawDelta = currLon - prevLon;

    if (Math.abs(rawDelta) > 180) {
      segments.push(current);
      current = [[currLon, currLat]];
    } else {
      current.push([currLon, currLat]);
    }
  }

  if (current.length > 0) segments.push(current);
  return segments.filter((s) => s.length >= 2);
}

function normalizeAndSplitRouteFeatures(features: GeoJSON.Feature[]): GeoJSON.Feature[] {
  const out: GeoJSON.Feature[] = [];

  for (const feature of features) {
    if (!feature.geometry) continue;
    const g = feature.geometry;

    if (g.type === 'LineString') {
      const segments = splitDatelineLineString(g.coordinates as number[][]);
      for (const seg of segments) {
        out.push({
          type: 'Feature',
          properties: feature.properties ?? {},
          geometry: { type: 'LineString', coordinates: seg },
        });
      }
      continue;
    }

    if (g.type === 'MultiLineString') {
      for (const line of g.coordinates as number[][][]) {
        const segments = splitDatelineLineString(line);
        for (const seg of segments) {
          out.push({
            type: 'Feature',
            properties: feature.properties ?? {},
            geometry: { type: 'LineString', coordinates: seg },
          });
        }
      }
      continue;
    }

    out.push(feature);
  }

  return out;
}

function normalizeFeatureCollection(features: GeoJSON.Feature[] | undefined): GeoJSON.FeatureCollection {
  return { type: 'FeatureCollection', features: normalizeAndSplitRouteFeatures(features ?? []) };
}

function buildRouteGeoJSON(route: ReturnType<typeof useRouteStore.getState>['route'], from: Waypoint | null, to: Waypoint | null): GeoJSON.FeatureCollection {
  if (!route?.geometry || !from || !to) return { type: 'FeatureCollection', features: [] };
  const raw = route.geometry as unknown as Record<string, unknown>;
  const type = (raw.type ?? raw.Type) as string | undefined;
  const features = (raw.features ?? raw.Features) as GeoJSON.Feature[] | undefined;
  if (type !== 'FeatureCollection' || !Array.isArray(features)) return { type: 'FeatureCollection', features: [] };
  return normalizeFeatureCollection(features);
}

function coordLabel(lat: number, lon: number) {
  return `${Math.abs(lat).toFixed(2)}°${lat >= 0 ? 'N' : 'S'} ${Math.abs(lon).toFixed(2)}°${lon >= 0 ? 'E' : 'W'}`;
}

// ── Component ────────────────────────────────────────────────────────────────

const PORT_COLOR: maplibregl.ExpressionSpecification = [
  'case',
  ['==', ['get', 'role'], 'from'], '#22c55e',
  ['==', ['get', 'role'], 'to'],   '#ef4444',
  '#f59e0b',
];

const ROLE_COLOR: maplibregl.ExpressionSpecification = [
  'case', ['==', ['get', 'role'], 'from'], '#22c55e', '#ef4444',
];

const MARINE_ACCESS_COLOR: maplibregl.ExpressionSpecification = [
  'case',
  ['==', ['get', 'role'], 'from'], '#22c55e',
  ['==', ['get', 'role'], 'to'], '#ef4444',
  '#06b6d4',
];

export function MapView() {
  const mapRef      = useRef<HTMLDivElement>(null);
  const mapInstance = useRef<maplibregl.Map | null>(null);
  const popupRef    = useRef<maplibregl.Popup | null>(null);

  useEffect(() => {
    if (!mapRef.current || mapInstance.current) return;

    const map = new maplibregl.Map({
      container: mapRef.current,
      style: 'https://basemaps.cartocdn.com/gl/positron-gl-style/style.json',
      center: [115, -25],
      zoom: 3,
    });

    map.addControl(new maplibregl.NavigationControl(), 'top-right');
    popupRef.current = new maplibregl.Popup({
      closeButton: false, closeOnClick: false, offset: 12,
    });

    // Helper to push current store state into all three sources.
    function syncSources(s: ReturnType<typeof useRouteStore.getState>) {
      (map.getSource('ports') as maplibregl.GeoJSONSource)?.setData(
        buildPortsGeoJSON(s.ports, s.from, s.to),
      );
      (map.getSource('picks') as maplibregl.GeoJSONSource)?.setData(
        buildPicksGeoJSON(s.from, s.to),
      );
      (map.getSource('marine-access') as maplibregl.GeoJSONSource)?.setData(
        buildMarineAccessGeoJSON(s.ports, s.from, s.to),
      );
      (map.getSource('port-access-connectors') as maplibregl.GeoJSONSource)?.setData(
        buildPortAccessConnectorsGeoJSON(s.ports, s.from, s.to),
      );
      (map.getSource('route') as maplibregl.GeoJSONSource)?.setData(
        buildRouteGeoJSON(s.route, s.from, s.to),
      );
    }

    map.on('load', () => {
      const s = useRouteStore.getState();

      // ── Sources ───────────────────────────────────────────────────────────
      map.addSource('route', { type: 'geojson', data: buildRouteGeoJSON(s.route, s.from, s.to) });
      map.addSource('ports', { type: 'geojson', data: buildPortsGeoJSON(s.ports, s.from, s.to) });
      map.addSource('picks', { type: 'geojson', data: buildPicksGeoJSON(s.from, s.to) });
      map.addSource('marine-access', {
        type: 'geojson',
        data: buildMarineAccessGeoJSON(s.ports, s.from, s.to),
      });
      map.addSource('port-access-connectors', {
        type: 'geojson',
        data: buildPortAccessConnectorsGeoJSON(s.ports, s.from, s.to),
      });

      // ── Layers ────────────────────────────────────────────────────────────
      map.addLayer({
        id: 'port-access-connectors-line', type: 'line', source: 'port-access-connectors',
        layout: { 'line-join': 'round', 'line-cap': 'round' },
        paint: {
          'line-color': ROLE_COLOR,
          'line-width': 2,
          'line-opacity': 0.75,
          'line-dasharray': [2, 2],
        },
      });
      map.addLayer({
        id: 'route-line', type: 'line', source: 'route',
        layout: { 'line-join': 'round', 'line-cap': 'round' },
        paint: { 'line-color': '#0284c7', 'line-width': 3, 'line-opacity': 0.9 },
      });
      map.addLayer({
        id: 'ports-circle', type: 'circle', source: 'ports',
        paint: {
          'circle-radius': 5,
          'circle-color': PORT_COLOR,
          'circle-stroke-width': 1.5,
          'circle-stroke-color': '#fff',
        },
      });
      map.addLayer({
        id: 'picks-halo', type: 'circle', source: 'picks',
        paint: {
          'circle-radius': 9,
          'circle-color': ROLE_COLOR,
          'circle-opacity': 0.18,
          'circle-stroke-width': 0,
        },
      });
      map.addLayer({
        id: 'picks-circle', type: 'circle', source: 'picks',
        paint: {
          'circle-radius': 5,
          'circle-color': ROLE_COLOR,
          'circle-stroke-width': 1.5,
          'circle-stroke-color': '#fff',
        },
      });
      map.addLayer({
        id: 'marine-access-circle', type: 'circle', source: 'marine-access',
        paint: {
          'circle-radius': 3.5,
          'circle-color': MARINE_ACCESS_COLOR,
          'circle-opacity': 0.92,
          'circle-stroke-width': 2,
          'circle-stroke-color': '#0f172a',
        },
      });

      // ── React to store changes (replaces useEffect sync hooks) ────────────
      const unsubscribe = useRouteStore.subscribe(syncSources);
      map.once('remove', unsubscribe);

      // ── Click: pick S then T ──────────────────────────────────────────────
      map.on('click', (e) => {
        if (map.queryRenderedFeatures(e.point, { layers: ['picks-circle'] }).length > 0) return;

        const portHit = map.queryRenderedFeatures(e.point, { layers: ['ports-circle'] })[0];
        if (portHit) {
          const { locode, name } = portHit.properties as { locode: string; name: string };
          const [lon, lat] = (portHit.geometry as GeoJSON.Point).coordinates as [number, number];
          useRouteStore.getState().selectWaypoint({ label: name, lat, lon, locode });
        } else {
          const { lng, lat } = e.lngLat;
          useRouteStore.getState().selectWaypoint({ label: coordLabel(lat, lng), lat, lon: lng });
        }
      });

      // ── Right-click: remove waypoint ──────────────────────────────────────
      map.on('contextmenu', (e) => {
        const pickHit = map.queryRenderedFeatures(e.point, { layers: ['picks-circle'] })[0];
        if (pickHit) {
          useRouteStore.getState().deselectWaypoint(pickHit.properties!.role as 'from' | 'to');
          return;
        }
        const { from: f, to: t, deselectWaypoint } = useRouteStore.getState();
        if (t) deselectWaypoint('to');
        else if (f) deselectWaypoint('from');
      });

      // ── Port hover popup ──────────────────────────────────────────────────
      map.on('mouseenter', 'ports-circle', (e) => {
        if (!e.features?.[0]) return;
        map.getCanvas().style.cursor = 'pointer';
        const { name, locode } = e.features[0].properties as { name: string; locode: string };
        const [lon, lat] = (e.features[0].geometry as GeoJSON.Point).coordinates as [number, number];
        popupRef.current
          ?.setLngLat([lon, lat])
          .setHTML(`<strong style="font:13px sans-serif">${name}</strong><br><span style="font:11px sans-serif;color:#666">${locode}</span>`)
          .addTo(map);
      });
      map.on('mouseleave', 'ports-circle', () => {
        map.getCanvas().style.cursor = '';
        popupRef.current?.remove();
      });
      map.on('mouseenter', 'marine-access-circle', (e) => {
        if (!e.features?.[0]) return;
        map.getCanvas().style.cursor = 'pointer';
        const { name, locode } = e.features[0].properties as { name: string; locode: string };
        const [lon, lat] = (e.features[0].geometry as GeoJSON.Point).coordinates as [number, number];
        popupRef.current
          ?.setLngLat([lon, lat])
          .setHTML(`<strong style="font:13px sans-serif">${name}</strong><br><span style="font:11px sans-serif;color:#0891b2">Marine access</span><br><span style="font:11px sans-serif;color:#666">${locode}</span>`)
          .addTo(map);
      });
      map.on('mouseleave', 'marine-access-circle', () => {
        map.getCanvas().style.cursor = '';
        popupRef.current?.remove();
      });
      map.on('mouseenter', 'picks-circle', () => { map.getCanvas().style.cursor = 'pointer'; });
      map.on('mouseleave', 'picks-circle', () => { map.getCanvas().style.cursor = ''; });
    });

    mapInstance.current = map;
    return () => { map.remove(); mapInstance.current = null; };
  }, []);

  return (
    <div
      ref={mapRef}
      style={{ width: '100%', height: '100%' }}
      onContextMenu={(e) => e.preventDefault()}
    />
  );
}
