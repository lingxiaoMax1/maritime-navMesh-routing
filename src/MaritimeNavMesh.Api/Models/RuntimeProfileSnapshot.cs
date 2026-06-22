namespace MaritimeNavMesh.Api.Models;

public sealed record RuntimeProfileSnapshot(
    long StartupTotalMs,
    long GraphLoadMs,
    long PortsLoadMs,
    long GraphRuntimeBuildMs,
    long EdgePortalsLoadMs,
    long AisCorridorHintsLoadMs,
    long LandMaskLoadMs,
    long ManagedHeapBytes,
    long WorkingSetBytes,
    bool RouteSmoothingEnabled);
