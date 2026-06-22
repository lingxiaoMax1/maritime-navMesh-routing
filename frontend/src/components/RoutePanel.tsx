import type { RouteResponse } from '../api/client';
import { useRouteStore } from '../store/routeStore';
import type { Waypoint } from '../store/routeStore';

const NM_TO_KM = 1.852;
const AVG_SPEED_KT = 15; // typical cargo vessel

export function RoutePanel() {
  const { ports, portsError, from, to, route, queryTimeMs, loading, error, clearRoute } = useRouteStore();
  const hasAnything = !!(from || to);

  return (
    <aside className="panel">
      <div className="panel-header">
        <div className="panel-header__left">
          <span className="panel-anchor">⚓</span>
          <div>
            <div className="panel-title">Ocean Router</div>
            <div className="panel-subtitle">Maritime nav mesh</div>
          </div>
        </div>
        <span className="panel-badge">{ports.length} ports</span>
      </div>

      {portsError && (
        <div className="panel-alert panel-alert--warn">
          <span className="panel-alert__icon">⚠</span>
          <span>Failed to load ports</span>
        </div>
      )}

      <div className="panel-divider" />

      <div className="panel-picks">
        <PickRow label="Origin" color="#22c55e" wp={from} hint="Click map or port" />
        <PickRow label="Destination" color="#ef4444" wp={to} hint={from ? 'Click map or port' : '—'} />
      </div>

      {loading && (
        <div className="panel-computing">
          <span className="panel-spinner" />
          Computing route…
        </div>
      )}

      {!loading && error && (
        <div className="panel-alert">
          <span className="panel-alert__icon">✕</span>
          <span>{error}</span>
        </div>
      )}

      {!loading && !error && route && !route.success && (
        <div className="panel-alert">
          <span className="panel-alert__icon">✕</span>
          <span>{route.errorMessage ?? route.errorCode ?? 'No route found'}</span>
        </div>
      )}

      {!loading && route?.success && <RouteStats route={route} queryTimeMs={queryTimeMs} />}

      {hasAnything && (
        <button className="panel-clear" onClick={clearRoute}>
          Clear route
        </button>
      )}
    </aside>
  );
}

function PickRow({
  label, color, wp, hint,
}: { label: string; color: string; wp: Waypoint | null; hint: string }) {
  return (
    <div className={`pick-row${wp ? ' pick-row--set' : ''}`}>
      <span className="pick-dot" style={{ background: wp ? color : '#d1d5db' }} />
      <div className="pick-body">
        <span className="pick-label-head">{label}</span>
        {wp ? (
          <span className="pick-value">
            {wp.locode
              ? <><strong>{wp.locode}</strong> · {wp.label}</>
              : <em className="pick-coord">{wp.label}</em>}
          </span>
        ) : (
          <span className="pick-placeholder">{hint}</span>
        )}
      </div>
    </div>
  );
}

function RouteStats({ route, queryTimeMs }: { route: RouteResponse; queryTimeMs: number | null }) {
  const nm = route.totalDistanceNm ?? 0;
  const km = (nm * NM_TO_KM).toFixed(0);
  const hrs = nm / AVG_SPEED_KT;
  const days = Math.floor(hrs / 24);
  const remHrs = Math.round(hrs % 24);
  const eta = days > 0 ? `~${days}d ${remHrs}h` : `~${Math.round(hrs)}h`;
  const queryLabel = queryTimeMs != null
    ? queryTimeMs >= 1000 ? `${(queryTimeMs / 1000).toFixed(2)} s` : `${queryTimeMs} ms`
    : null;
  const diagnostics = route.geometryDiagnostics;

  return (
    <div className="panel-result">
      <div className="panel-result__line">
        <span className="panel-result__key">Distance</span>
        <span className="panel-result__vals">
          <span>{nm.toFixed(0)} <em>nm</em></span>
          <span>{km} <em>km</em></span>
        </span>
      </div>
      <div className="panel-result__line">
        <span className="panel-result__key">Est. time</span>
        <span className="panel-result__vals">
          <span>{eta}</span>
          <span><em>@ 15 kt</em></span>
        </span>
      </div>
      <div className="panel-result__meta">
        <span>{route.nodeCount} waypoints</span>
        <span>{route.geometryModeUsed ?? 'raw'} geometry</span>
        {queryLabel && <span className="panel-result__query-time">⚡ {queryLabel}</span>}
      </div>
      {diagnostics && (
        <>
          <div className="panel-result__meta">
            <span>{diagnostics.rawPointCount} raw pts</span>
            {diagnostics.funnelPointCount != null && <span>{diagnostics.funnelPointCount} pre-cleanup pts</span>}
            <span>{diagnostics.finalPointCount} final pts</span>
            {diagnostics.fallbackSectionCount > 0 && <span>{diagnostics.fallbackSectionCount} fallback sections</span>}
          </div>
          {diagnostics.aisShapedSectionCount > 0 && (
            <div className="panel-result__meta">
              <span>{diagnostics.aisShapedSectionCount} AIS-shaped sections</span>
            </div>
          )}
        </>
      )}
    </div>
  );
}
