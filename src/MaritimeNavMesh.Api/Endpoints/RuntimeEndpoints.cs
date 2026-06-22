using System.Diagnostics;
using MaritimeNavMesh.Api.Models;
using MaritimeNavMesh.Api.Services;
using MaritimeNavMesh.Core.Geometry;
using MaritimeNavMesh.Core.Routing;
using MaritimeNavMesh.Core.Runtime;
using Microsoft.Extensions.Options;

namespace MaritimeNavMesh.Api.Endpoints;

public static class RuntimeEndpoints
{
    private static readonly (string Name, string From, string To)[] PresetScenarios =
    [
        ("west_aus_to_java", "AUFRE", "IDJKT"),
        ("east_aus_to_java", "AUSYD", "IDJKT"),
        ("melbourne_to_singapore", "AUMEL", "SGSIN"),
    ];

    public static void MapRuntimeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/runtime/profile", (GraphService graphService) =>
        {
            var rt = graphService.Runtime;
            var profile = graphService.StartupProfile;
            return Results.Ok(new RuntimeProfileResponse(
                StartupTotalMs: profile.StartupTotalMs,
                GraphLoadMs: profile.GraphLoadMs,
                PortsLoadMs: profile.PortsLoadMs,
                GraphRuntimeBuildMs: profile.GraphRuntimeBuildMs,
                EdgePortalsLoadMs: profile.EdgePortalsLoadMs,
                AisCorridorHintsLoadMs: profile.AisCorridorHintsLoadMs,
                LandMaskLoadMs: profile.LandMaskLoadMs,
                ManagedHeapBytes: profile.ManagedHeapBytes,
                WorkingSetBytes: profile.WorkingSetBytes,
                NodeCount: rt.Graph.NodeCount,
                EdgeCount: rt.Graph.EdgeCount,
                PortCount: rt.AllPorts.Count,
                ComponentCount: rt.ComponentIndex.Stats.Count,
                RouteSmoothingEnabled: profile.RouteSmoothingEnabled));
        })
        .WithName("RuntimeProfile")
        .WithTags("Runtime")
        .Produces<RuntimeProfileResponse>();

        app.MapGet("/api/runtime/benchmark/by-locode", (
            string from,
            string to,
            int? iterations,
            string? geometryMode,
            string? algorithm,
            GraphService graphService,
            IOptions<GraphOptions> options) =>
        {
            var rt = graphService.Runtime;
            if (!rt.PortsByLocode.TryGetValue(from.ToUpperInvariant(), out var fromPort))
                return Results.NotFound(new { Error = $"From-port not found: {from}" });
            if (!rt.PortsByLocode.TryGetValue(to.ToUpperInvariant(), out var toPort))
                return Results.NotFound(new { Error = $"To-port not found: {to}" });

            int fromNode = rt.SnapPortToNode(from);
            int toNode = rt.SnapPortToNode(to);
            if (fromNode < 0 || toNode < 0)
                return Results.NotFound(new { Error = "Port is present but not routable in the loaded graph." });

            int iterationCount = Math.Clamp(iterations ?? 5, 1, 50);
            var resolvedMode = ResolveGeometryMode(geometryMode, options.Value.DefaultRouteGeometryMode);
            var resolvedAlgorithm = ResolveRouteAlgorithm(algorithm, options.Value.DefaultRouteAlgorithm);
            var searchSamples = new List<double>(iterationCount);
            var totalSamples = new List<double>(iterationCount);
            var visitedSamples = new List<double>(iterationCount);
            var forwardVisitedSamples = new List<double>(iterationCount);
            var reverseVisitedSamples = new List<double>(iterationCount);
            int successCount = 0;
            int failureCount = 0;
            int? lastNodeCount = null;
            double? lastDistanceNm = null;
            int? lastVisitedNodes = null;
            int? lastForwardVisitedNodes = null;
            int? lastReverseVisitedNodes = null;
            string? lastFailure = null;

            for (int i = 0; i < iterationCount; i++)
            {
                var totalSw = Stopwatch.StartNew();
                var result = RoutePlanner.Search(rt.Graph, new RouteRequest
                {
                    FromNodeIndex = fromNode,
                    ToNodeIndex = toNode,
                    Algorithm = resolvedAlgorithm,
                });
                if (result.Status != RouteStatus.Success)
                {
                    totalSw.Stop();
                    failureCount += 1;
                    lastFailure = result.FailureReason;
                    continue;
                }

                _ = RouteGeometryBuilder.BuildGraphGeometry(
                    rt.Graph,
                    result.PathNodes!,
                    resolvedMode,
                    graphService.LandMask,
                    graphService.EdgePortals,
                    fromPort.Locode,
                    toPort.Locode,
                    graphService.AisCorridorHints);
                totalSw.Stop();

                successCount += 1;
                searchSamples.Add(result.ElapsedMs);
                totalSamples.Add(totalSw.Elapsed.TotalMilliseconds);
                visitedSamples.Add(result.VisitedNodes);
                if (result.ForwardVisitedNodes.HasValue) forwardVisitedSamples.Add(result.ForwardVisitedNodes.Value);
                if (result.ReverseVisitedNodes.HasValue) reverseVisitedSamples.Add(result.ReverseVisitedNodes.Value);
                lastNodeCount = result.PathNodes!.Length;
                lastDistanceNm = result.TotalDistanceNm;
                lastVisitedNodes = result.VisitedNodes;
                lastForwardVisitedNodes = result.ForwardVisitedNodes;
                lastReverseVisitedNodes = result.ReverseVisitedNodes;
            }

            return Results.Ok(new RouteBenchmarkResponse(
                Algorithm: resolvedAlgorithm.ToString().ToLowerInvariant(),
                Mode: resolvedMode.ToString().ToLowerInvariant(),
                From: fromPort.Locode,
                To: toPort.Locode,
                Iterations: iterationCount,
                SuccessCount: successCount,
                FailureCount: failureCount,
                AvgSearchMs: Average(searchSamples),
                P95SearchMs: Percentile95(searchSamples),
                AvgTotalMs: Average(totalSamples),
                P95TotalMs: Percentile95(totalSamples),
                AvgVisitedNodes: Average(visitedSamples),
                P95VisitedNodes: Percentile95(visitedSamples),
                AvgForwardVisitedNodes: Average(forwardVisitedSamples),
                AvgReverseVisitedNodes: Average(reverseVisitedSamples),
                LastNodeCount: lastNodeCount,
                LastDistanceNm: lastDistanceNm,
                LastVisitedNodes: lastVisitedNodes,
                LastForwardVisitedNodes: lastForwardVisitedNodes,
                LastReverseVisitedNodes: lastReverseVisitedNodes,
                LastFailure: lastFailure));
        })
        .WithName("BenchmarkRouteByLocode")
        .WithTags("Runtime")
        .Produces<RouteBenchmarkResponse>()
        .ProducesProblem(404);

        app.MapGet("/api/runtime/benchmark/preset-suite", (
            int? iterations,
            string? geometryMode,
            string? algorithm,
            GraphService graphService,
            IOptions<GraphOptions> options) =>
        {
            var rt = graphService.Runtime;
            int iterationCount = Math.Clamp(iterations ?? 1, 1, 50);
            var resolvedMode = ResolveGeometryMode(geometryMode, options.Value.DefaultRouteGeometryMode);
            var resolvedAlgorithm = ResolveRouteAlgorithm(algorithm, options.Value.DefaultRouteAlgorithm);
            var scenarios = new List<RouteBenchmarkScenarioResponse>(PresetScenarios.Length);

            foreach (var scenario in PresetScenarios)
            {
                if (!rt.PortsByLocode.TryGetValue(scenario.From, out var fromPort))
                    return Results.NotFound(new { Error = $"Preset from-port not found: {scenario.From}" });
                if (!rt.PortsByLocode.TryGetValue(scenario.To, out var toPort))
                    return Results.NotFound(new { Error = $"Preset to-port not found: {scenario.To}" });

                int fromNode = rt.SnapPortToNode(scenario.From);
                int toNode = rt.SnapPortToNode(scenario.To);
                if (fromNode < 0 || toNode < 0)
                    return Results.NotFound(new { Error = $"Preset route is not routable in the loaded graph: {scenario.From} -> {scenario.To}" });

                scenarios.Add(RunRouteBenchmarkScenario(
                    scenario.Name,
                    fromPort.Locode,
                    toPort.Locode,
                    fromNode,
                    toNode,
                    iterationCount,
                    resolvedAlgorithm,
                    resolvedMode,
                    rt,
                    graphService.LandMask,
                    graphService.EdgePortals,
                    graphService.AisCorridorHints));
            }

            return Results.Ok(new RouteBenchmarkSuiteResponse(
                Algorithm: resolvedAlgorithm.ToString().ToLowerInvariant(),
                Mode: resolvedMode.ToString().ToLowerInvariant(),
                Scenarios: scenarios.ToArray()));
        })
        .WithName("BenchmarkPresetSuite")
        .WithTags("Runtime")
        .Produces<RouteBenchmarkSuiteResponse>()
        .ProducesProblem(404);
    }

    private static RouteGeometryMode ResolveGeometryMode(string? requested, string? configuredDefault)
    {
        if (TryParseGeometryMode(requested, out var explicitMode))
            return explicitMode;
        if (TryParseGeometryMode(configuredDefault, out var defaultMode))
            return defaultMode;
        return RouteGeometryMode.Funnel;
    }

    private static bool TryParseGeometryMode(string? value, out RouteGeometryMode mode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            mode = RouteGeometryMode.Raw;
            return false;
        }
        return Enum.TryParse(value, ignoreCase: true, out mode);
    }

    private static RouteAlgorithm ResolveRouteAlgorithm(string? requested, string? configuredDefault)
    {
        if (TryParseRouteAlgorithm(requested, out var explicitAlgorithm))
            return explicitAlgorithm;
        if (TryParseRouteAlgorithm(configuredDefault, out var defaultAlgorithm))
            return defaultAlgorithm;
        return RouteAlgorithm.BidirectionalAStar;
    }

    private static bool TryParseRouteAlgorithm(string? value, out RouteAlgorithm algorithm)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            algorithm = RouteAlgorithm.Dijkstra;
            return false;
        }
        return Enum.TryParse(value.Replace("-", string.Empty), ignoreCase: true, out algorithm);
    }

    private static double Average(List<double> samples) => samples.Count == 0 ? 0.0 : samples.Average();

    private static RouteBenchmarkScenarioResponse RunRouteBenchmarkScenario(
        string name,
        string fromLocode,
        string toLocode,
        int fromNode,
        int toNode,
        int iterationCount,
        RouteAlgorithm resolvedAlgorithm,
        RouteGeometryMode resolvedMode,
        GraphRuntime rt,
        RasterLandMask? landMask,
        EdgePortalSet? edgePortals,
        AisCorridorHintSet? aisCorridorHints)
    {
        var searchSamples = new List<double>(iterationCount);
        var totalSamples = new List<double>(iterationCount);
        var visitedSamples = new List<double>(iterationCount);
        var forwardVisitedSamples = new List<double>(iterationCount);
        var reverseVisitedSamples = new List<double>(iterationCount);
        int successCount = 0;
        int failureCount = 0;
        int? lastNodeCount = null;
        double? lastDistanceNm = null;
        int? lastVisitedNodes = null;
        int? lastForwardVisitedNodes = null;
        int? lastReverseVisitedNodes = null;
        string? lastFailure = null;

        for (int i = 0; i < iterationCount; i++)
        {
            var totalSw = Stopwatch.StartNew();
            var result = RoutePlanner.Search(rt.Graph, new RouteRequest
            {
                FromNodeIndex = fromNode,
                ToNodeIndex = toNode,
                Algorithm = resolvedAlgorithm,
            });
            if (result.Status != RouteStatus.Success)
            {
                totalSw.Stop();
                failureCount += 1;
                lastFailure = result.FailureReason;
                continue;
            }

            _ = RouteGeometryBuilder.BuildGraphGeometry(
                rt.Graph,
                result.PathNodes!,
                resolvedMode,
                landMask,
                edgePortals,
                fromLocode,
                toLocode,
                aisCorridorHints);
            totalSw.Stop();

            successCount += 1;
            searchSamples.Add(result.ElapsedMs);
            totalSamples.Add(totalSw.Elapsed.TotalMilliseconds);
            visitedSamples.Add(result.VisitedNodes);
            if (result.ForwardVisitedNodes.HasValue) forwardVisitedSamples.Add(result.ForwardVisitedNodes.Value);
            if (result.ReverseVisitedNodes.HasValue) reverseVisitedSamples.Add(result.ReverseVisitedNodes.Value);
            lastNodeCount = result.PathNodes!.Length;
            lastDistanceNm = result.TotalDistanceNm;
            lastVisitedNodes = result.VisitedNodes;
            lastForwardVisitedNodes = result.ForwardVisitedNodes;
            lastReverseVisitedNodes = result.ReverseVisitedNodes;
        }

        return new RouteBenchmarkScenarioResponse(
            Name: name,
            Algorithm: resolvedAlgorithm.ToString().ToLowerInvariant(),
            From: fromLocode,
            To: toLocode,
            Iterations: iterationCount,
            SuccessCount: successCount,
            FailureCount: failureCount,
            AvgSearchMs: Average(searchSamples),
            P95SearchMs: Percentile95(searchSamples),
            AvgTotalMs: Average(totalSamples),
            P95TotalMs: Percentile95(totalSamples),
            AvgVisitedNodes: Average(visitedSamples),
            P95VisitedNodes: Percentile95(visitedSamples),
            AvgForwardVisitedNodes: Average(forwardVisitedSamples),
            AvgReverseVisitedNodes: Average(reverseVisitedSamples),
            LastNodeCount: lastNodeCount,
            LastDistanceNm: lastDistanceNm,
            LastVisitedNodes: lastVisitedNodes,
            LastForwardVisitedNodes: lastForwardVisitedNodes,
            LastReverseVisitedNodes: lastReverseVisitedNodes,
            LastFailure: lastFailure);
    }

    private static double Percentile95(List<double> samples)
    {
        if (samples.Count == 0)
            return 0.0;
        var ordered = samples.OrderBy(v => v).ToArray();
        int index = (int)Math.Ceiling(ordered.Length * 0.95) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }
}
