const BASE_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5000';

export interface Port {
  locode: string;
  name: string;
  portLat: number | null;
  portLon: number | null;
  snappedLat: number;
  snappedLon: number;
  marineAccessLat: number | null;
  marineAccessLon: number | null;
  marineAccessDisplayLat: number | null;
  marineAccessDisplayLon: number | null;
  marineAccessDisplayPathCoordinates: number[][] | null;
}

export interface RouteGeometrySectionDiagnostic {
  startNodeOffset: number;
  endNodeOffset: number;
  baseMode: string;
  finalMode: string;
  reason: string;
}

export interface RouteGeometryDiagnostics {
  geometryModeRequested: string;
  geometryModeUsed: string;
  rawPointCount: number;
  funnelPointCount: number | null;
  finalPointCount: number;
  fallbackSectionCount: number;
  aisShapedSectionCount: number;
  sections: RouteGeometrySectionDiagnostic[];
}

export interface RouteResponse {
  success: boolean;
  errorCode: string | null;
  errorMessage: string | null;
  totalDistanceNm: number | null;
  totalCost: number | null;
  nodeCount: number | null;
  searchTimeMs: number | null;
  geometry: GeoJSON.FeatureCollection | null;
  rawGraphGeometry: GeoJSON.FeatureCollection | null;
  geometryModeRequested: string | null;
  geometryModeUsed: string | null;
  geometryDiagnostics: RouteGeometryDiagnostics | null;
}

async function get<T>(path: string): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`);
  if (!res.ok) throw new Error(`API error ${res.status}: ${await res.text()}`);
  return res.json() as Promise<T>;
}

export const api = {
  ports: () => get<Port[]>('/api/ports'),
  routeByLocode: (from: string, to: string) =>
    get<RouteResponse>(`/api/route/by-locode?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`),
  routeByCoordinate: (fromLat: number, fromLon: number, toLat: number, toLon: number) =>
    get<RouteResponse>(
      `/api/route/by-coordinate?fromLat=${fromLat}&fromLon=${fromLon}&toLat=${toLat}&toLon=${toLon}`
    ),
};
