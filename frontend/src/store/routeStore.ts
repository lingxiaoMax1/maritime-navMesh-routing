import { create } from 'zustand';
import { api } from '../api/client';
import type { Port, RouteResponse } from '../api/client';

let routeRequestSeq = 0;

export interface Waypoint {
  label: string;
  lat: number;
  lon: number;
  locode?: string;
}

interface RouteState {
  ports: Port[];
  portsError: string | null;
  from: Waypoint | null;
  to: Waypoint | null;
  route: RouteResponse | null;
  queryTimeMs: number | null;
  loading: boolean;
  error: string | null;

  loadPorts: () => Promise<void>;
  selectWaypoint: (wp: Waypoint) => void;
  deselectWaypoint: (which: 'from' | 'to') => void;
  computeRoute: () => Promise<void>;
  clearRoute: () => void;
}

export const useRouteStore = create<RouteState>((set, get) => ({
  ports: [],
  portsError: null,
  from: null,
  to: null,
  route: null,
  queryTimeMs: null,
  loading: false,
  error: null,

  loadPorts: async () => {
    try {
      const ports = await api.ports();
      set({ ports, portsError: null });
    } catch (e) {
      set({ portsError: String(e) });
    }
  },

  selectWaypoint: (wp: Waypoint) => {
    const { from, to } = get();
    if (from?.locode && from.locode === wp.locode) return;
    if (to?.locode && to.locode === wp.locode) return;
    if (!from) {
      routeRequestSeq += 1;
      set({ from: wp, route: null, error: null });
      if (to) get().computeRoute();
    } else if (!to) {
      routeRequestSeq += 1;
      set({ to: wp, route: null, error: null });
      get().computeRoute();
    } else {
      routeRequestSeq += 1;
      set({ from: wp, to: null, route: null, error: null, loading: false });
    }
  },

  deselectWaypoint: (which) => {
    routeRequestSeq += 1;
    if (which === 'from') set({ from: null, route: null, queryTimeMs: null, error: null, loading: false });
    else set({ to: null, route: null, queryTimeMs: null, error: null, loading: false });
  },

  computeRoute: async () => {
    const { from, to } = get();
    if (!from || !to) return;
    const requestId = ++routeRequestSeq;
    set({ loading: true, error: null, queryTimeMs: null });
    try {
      const route = (from.locode && to.locode)
        ? await api.routeByLocode(from.locode, to.locode)
        : await api.routeByCoordinate(from.lat, from.lon, to.lat, to.lon);
      if (requestId !== routeRequestSeq) return;
      set({ route, queryTimeMs: route.searchTimeMs ?? null, loading: false });
    } catch (e) {
      if (requestId !== routeRequestSeq) return;
      set({ error: String(e), loading: false });
    }
  },

  clearRoute: () => {
    routeRequestSeq += 1;
    set({ from: null, to: null, route: null, queryTimeMs: null, error: null, loading: false });
  },
}));
