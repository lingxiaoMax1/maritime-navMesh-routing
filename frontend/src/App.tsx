import { useEffect } from 'react';
import { MapView } from './components/MapView';
import { RoutePanel } from './components/RoutePanel';
import { useRouteStore } from './store/routeStore';
import './App.css';

function App() {
  const loadPorts = useRouteStore((s) => s.loadPorts);

  useEffect(() => {
    loadPorts();
  }, [loadPorts]);

  return (
    <div style={{ width: '100vw', height: '100vh', position: 'relative', background: '#0d1117' }}>
      <MapView />
      <RoutePanel />
    </div>
  );
}

export default App;
