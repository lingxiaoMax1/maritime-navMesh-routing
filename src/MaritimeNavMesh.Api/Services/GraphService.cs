using MaritimeNavMesh.Core.Runtime;
using MaritimeNavMesh.IO.Loaders;
using Microsoft.Extensions.Options;
using MaritimeNavMesh.Api.Models;
using MaritimeNavMesh.Core.Geometry;
using MaritimeNavMesh.IO.Models;
using System.Diagnostics;

namespace MaritimeNavMesh.Api.Services;

/// <summary>
/// Singleton service that holds the loaded graph runtime for the lifetime of the application.
/// Loaded at startup via IHostedService; all routing calls are read-only and thread-safe.
/// </summary>
public sealed class GraphService : IDisposable
{
    private volatile GraphRuntime? _runtime;
    private RasterLandMask? _landMask;
    private EdgePortalSet? _edgePortals;
    private AisCorridorHintSet? _aisCorridorHints;
    private RuntimeProfileSnapshot? _startupProfile;

    public GraphRuntime Runtime => _runtime
        ?? throw new InvalidOperationException("Graph runtime is not yet loaded. Check startup logs.");

    public bool IsLoaded => _runtime is not null;
    public RasterLandMask? LandMask => _landMask;
    public EdgePortalSet? EdgePortals => _edgePortals;
    public AisCorridorHintSet? AisCorridorHints => _aisCorridorHints;
    public RuntimeProfileSnapshot StartupProfile => _startupProfile
        ?? throw new InvalidOperationException("Runtime profile is not yet available. Check startup logs.");

    public void Load(GraphOptions options, ILogger<GraphService> logger)
    {
        var totalSw = Stopwatch.StartNew();
        long graphLoadMs = 0;
        long portsLoadMs = 0;
        long graphRuntimeBuildMs = 0;
        long edgePortalsLoadMs = 0;
        long aisCorridorHintsLoadMs = 0;
        long landMaskLoadMs = 0;
        GraphRuntime? runtime = null;
        RasterLandMask? landMask = null;
        EdgePortalSet? edgePortals = null;
        AisCorridorHintSet? aisCorridorHints = null;
        Core.Graph.CsrOceanGraph? graph = null;

        try
        {
            logger.LogInformation("Loading CSR graph from: {Path}", options.BinaryPath);

            var phaseSw = Stopwatch.StartNew();
            graph = options.ValidateManifestHash && !string.IsNullOrWhiteSpace(options.ManifestPath)
                ? LoadWithManifest(options, logger)
                : CsrGraphLoader.Load(options.BinaryPath);
            phaseSw.Stop();
            graphLoadMs = phaseSw.ElapsedMilliseconds;

            logger.LogInformation("CSR graph loaded: {Nodes} nodes, {Edges} edges, resolution {Res}",
                graph.NodeCount, graph.EdgeCount, graph.Resolution);

            logger.LogInformation("Loading ports from: {Path}", options.PortsPath);
            phaseSw.Restart();
            var ports = PortLookupLoader.Load(options.PortsPath);
            phaseSw.Stop();
            portsLoadMs = phaseSw.ElapsedMilliseconds;
            logger.LogInformation("Loaded {Count} ports", ports.Length);

            phaseSw.Restart();
            runtime = new GraphRuntime(graph, ports);
            graph = null;
            phaseSw.Stop();
            graphRuntimeBuildMs = phaseSw.ElapsedMilliseconds;

            GraphManifest? manifest = null;
            if (options.ValidateManifestHash && !string.IsNullOrWhiteSpace(options.ManifestPath))
                manifest = ManifestValidator.LoadManifest(options.ManifestPath);

            if (options.EnableRouteSmoothing && !string.IsNullOrWhiteSpace(options.EdgePortalsPath))
            {
                if (!File.Exists(options.EdgePortalsPath))
                {
                    logger.LogWarning("Funnel geometry disabled because edge portals are missing: {Path}", options.EdgePortalsPath);
                }
                else
                {
                    phaseSw.Restart();
                    edgePortals = EdgePortalLoader.Load(options.EdgePortalsPath, runtime.Graph);
                    phaseSw.Stop();
                    edgePortalsLoadMs = phaseSw.ElapsedMilliseconds;
                    if (manifest?.EdgePortals is not null)
                    {
                        string actualHash = CsrGraphLoader.ComputeSha256(options.EdgePortalsPath);
                        if (!string.Equals(actualHash, manifest.EdgePortals.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Edge-portal SHA-256 does not match the Project 1 manifest.");
                        if (manifest.EdgePortals.EdgeCount != edgePortals.EdgeCount)
                            throw new InvalidDataException("Edge-portal edge count does not match the Project 1 manifest.");
                    }
                }
            }

            if (options.EnableRouteSmoothing && !string.IsNullOrWhiteSpace(options.AisCorridorHintsPath))
            {
                if (!File.Exists(options.AisCorridorHintsPath))
                {
                    logger.LogWarning("AIS route shaping disabled because corridor hints are missing: {Path}", options.AisCorridorHintsPath);
                }
                else
                {
                    phaseSw.Restart();
                    aisCorridorHints = AisCorridorHintLoader.Load(options.AisCorridorHintsPath);
                    phaseSw.Stop();
                    aisCorridorHintsLoadMs = phaseSw.ElapsedMilliseconds;
                    if (manifest?.AisCorridorHints is not null)
                    {
                        string actualHash = CsrGraphLoader.ComputeSha256(options.AisCorridorHintsPath);
                        if (!string.Equals(actualHash, manifest.AisCorridorHints.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("AIS corridor-hint SHA-256 does not match the Project 1 manifest.");
                        if (manifest.AisCorridorHints.HintCount != aisCorridorHints.HintCount)
                            throw new InvalidDataException("AIS corridor-hint count does not match the Project 1 manifest.");
                    }
                    logger.LogInformation("AIS corridor hints loaded: {Count}", aisCorridorHints.HintCount);
                }
            }

            if (options.EnableRouteSmoothing && !string.IsNullOrWhiteSpace(options.LandMaskPath))
            {
                if (!File.Exists(options.LandMaskPath))
                {
                    logger.LogWarning("Route smoothing disabled because land mask is missing: {Path}", options.LandMaskPath);
                }
                else
                {
                    phaseSw.Restart();
                    landMask = LandMaskLoader.Load(options.LandMaskPath);
                    phaseSw.Stop();
                    landMaskLoadMs = phaseSw.ElapsedMilliseconds;
                    if (manifest?.LandMask is not null)
                    {
                        string actualHash = CsrGraphLoader.ComputeSha256(options.LandMaskPath);
                        if (!string.Equals(actualHash, manifest.LandMask.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Land-mask SHA-256 does not match the Project 1 manifest.");
                        if (manifest.LandMask.Width != landMask.Width || manifest.LandMask.Height != landMask.Height)
                            throw new InvalidDataException("Land-mask dimensions do not match the Project 1 manifest.");
                    }
                    logger.LogInformation(
                        "Land mask loaded: {Width}x{Height} at {PixelSizeM} m, dilation {DilationPixels} px",
                        landMask.Width,
                        landMask.Height,
                        landMask.PixelSizeM,
                        landMask.DilationPixels);
                }
            }

            foreach (var warning in runtime.StartupWarnings)
                logger.LogWarning("[Port validation] {Warning}", warning);

            if (runtime.StartupWarnings.Count > 0)
                logger.LogWarning("{Count} port validation warning(s). Check ports.json against graph artifact.", runtime.StartupWarnings.Count);

            UnloadCurrent();
            _runtime = runtime;
            _landMask = landMask;
            _edgePortals = edgePortals;
            _aisCorridorHints = aisCorridorHints;
            totalSw.Stop();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            _startupProfile = new RuntimeProfileSnapshot(
                StartupTotalMs: totalSw.ElapsedMilliseconds,
                GraphLoadMs: graphLoadMs,
                PortsLoadMs: portsLoadMs,
                GraphRuntimeBuildMs: graphRuntimeBuildMs,
                EdgePortalsLoadMs: edgePortalsLoadMs,
                AisCorridorHintsLoadMs: aisCorridorHintsLoadMs,
                LandMaskLoadMs: landMaskLoadMs,
                ManagedHeapBytes: GC.GetTotalMemory(forceFullCollection: false),
                WorkingSetBytes: Process.GetCurrentProcess().WorkingSet64,
                RouteSmoothingEnabled: options.EnableRouteSmoothing);
            logger.LogInformation("Graph runtime ready. Components: {ComponentCount}",
                runtime.ComponentIndex.Stats.Count);
        }
        catch
        {
            runtime?.Dispose();
            graph?.Dispose();
            throw;
        }
    }

    public void Dispose() => UnloadCurrent();

    private void UnloadCurrent()
    {
        _runtime?.Dispose();
        _runtime = null;
        _landMask = null;
        _edgePortals = null;
        _aisCorridorHints = null;
        _startupProfile = null;
    }

    private static Core.Graph.CsrOceanGraph LoadWithManifest(GraphOptions options, ILogger<GraphService> logger)
    {
        var manifest = ManifestValidator.LoadManifest(options.ManifestPath);
        var graph = CsrGraphLoader.LoadAndVerifyHash(options.BinaryPath, manifest.Binary?.Sha256 ?? "");
        ManifestValidator.ValidateAgainstGraph(manifest, graph, options.BinaryPath);
        logger.LogInformation("Manifest validated successfully (schema version {V})", manifest.SchemaVersion);
        return graph;
    }
}

/// <summary>Loads the graph at startup before the app begins accepting requests.</summary>
public sealed class GraphLoaderHostedService(
    GraphService graphService,
    IOptions<GraphOptions> graphOptions,
    ILogger<GraphService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        graphService.Load(graphOptions.Value, logger);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        graphService.Dispose();
        return Task.CompletedTask;
    }
}
