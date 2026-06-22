using MaritimeNavMesh.Core.Graph;

namespace MaritimeNavMesh.Core.Geometry;

/// <summary>
/// Builds GeoJSON-compatible route geometry from a path node list.
/// Splits segments crossing the anti-meridian into a MultiLineString.
/// </summary>
public static class RouteGeometryBuilder
{
    private const double AntiMeridianThreshold = 180.0;
    private const int MaxShortcutLookaheadNodes = 128;

    /// <summary>
    /// Analyzes and builds graph-only route geometry points for the requested mode.
    /// </summary>
    public static RouteGeometryBuildResult BuildGraphGeometry(
        CsrOceanGraph graph,
        int[] pathNodes,
        RouteGeometryMode mode = RouteGeometryMode.Raw,
        RasterLandMask? landMask = null,
        EdgePortalSet? edgePortals = null,
        string? startLocode = null,
        string? endLocode = null,
        AisCorridorHintSet? aisCorridorHints = null)
    {
        var rawPoints = BuildRawGraphPoints(graph, pathNodes);
        string requestedMode = ModeName(mode);

        if (mode == RouteGeometryMode.Raw)
        {
            return new RouteGeometryBuildResult(
                rawPoints,
                new RouteGeometryDiagnostics(
                    requestedMode,
                    GeometryModeUsed: "raw",
                    RawPointCount: rawPoints.Count,
                    FunnelPointCount: null,
                    FinalPointCount: rawPoints.Count,
                    FallbackSectionCount: 0,
                    AisShapedSectionCount: 0,
                    Sections: []));
        }

        if (mode == RouteGeometryMode.Shortcut)
        {
            var shortcutPoints = landMask is null ? rawPoints : SmoothGraphPoints(rawPoints, landMask);
            return new RouteGeometryBuildResult(
                shortcutPoints,
                new RouteGeometryDiagnostics(
                    requestedMode,
                    GeometryModeUsed: landMask is null ? "raw" : "shortcut",
                    RawPointCount: rawPoints.Count,
                    FunnelPointCount: null,
                    FinalPointCount: shortcutPoints.Count,
                    FallbackSectionCount: 0,
                    AisShapedSectionCount: 0,
                    Sections: []));
        }

        if (landMask is null)
        {
            return new RouteGeometryBuildResult(
                rawPoints,
                new RouteGeometryDiagnostics(
                    requestedMode,
                    GeometryModeUsed: "raw",
                    RawPointCount: rawPoints.Count,
                    FunnelPointCount: null,
                    FinalPointCount: rawPoints.Count,
                    FallbackSectionCount: 0,
                    AisShapedSectionCount: 0,
                    Sections: []));
        }

        if (edgePortals is null)
        {
            var shortcutPoints = SmoothGraphPoints(rawPoints, landMask);
            return new RouteGeometryBuildResult(
                shortcutPoints,
                new RouteGeometryDiagnostics(
                    requestedMode,
                    GeometryModeUsed: "shortcut",
                    RawPointCount: rawPoints.Count,
                    FunnelPointCount: null,
                    FinalPointCount: shortcutPoints.Count,
                    FallbackSectionCount: 0,
                    AisShapedSectionCount: 0,
                    Sections: []));
        }

        var funnel = BuildFunnelOrFallback(graph, pathNodes, rawPoints, landMask, edgePortals);
        var finalPoints = funnel.FinalPoints;
        int aisShapedSectionCount = 0;

        if (aisCorridorHints is not null && pathNodes.Length >= 2)
        {
            var shaped = TryApplyAisHints(
                graph,
                pathNodes,
                landMask,
                edgePortals,
                aisCorridorHints,
                startLocode,
                endLocode);
            if (shaped.AppliedSectionCount > 0)
            {
                finalPoints = shaped.Points;
                aisShapedSectionCount = shaped.AppliedSectionCount;
            }
        }

        return new RouteGeometryBuildResult(
            finalPoints,
            new RouteGeometryDiagnostics(
                requestedMode,
                GeometryModeUsed: "funnel",
                RawPointCount: rawPoints.Count,
                FunnelPointCount: funnel.FunnelPointCount,
                FinalPointCount: finalPoints.Count,
                FallbackSectionCount: funnel.Sections.Length,
                AisShapedSectionCount: aisShapedSectionCount,
                Sections: funnel.Sections));
    }

    public static List<List<double[]>> BuildSegments(
        CsrOceanGraph graph,
        int[] pathNodes,
        RouteGeometryMode mode = RouteGeometryMode.Raw,
        RasterLandMask? landMask = null,
        EdgePortalSet? edgePortals = null)
    {
        if (pathNodes.Length == 0)
            return [];

        var geometry = BuildGraphGeometry(graph, pathNodes, mode, landMask, edgePortals);
        return BuildSegmentsFromPoints(geometry.FinalGraphPoints);
    }

    public static List<List<double[]>> BuildSegmentsWithEndpoints(
        CsrOceanGraph graph,
        int[] pathNodes,
        double[]? rawStartLonLat,
        double[]? rawEndLonLat,
        RouteGeometryMode mode = RouteGeometryMode.Raw,
        RasterLandMask? landMask = null,
        EdgePortalSet? edgePortals = null)
    {
        if (pathNodes.Length == 0)
            return [];

        var geometry = BuildGraphGeometry(graph, pathNodes, mode, landMask, edgePortals);
        return BuildSegmentsWithEndpointPoints(geometry.FinalGraphPoints, rawStartLonLat, rawEndLonLat);
    }

    public static List<List<double[]>> BuildSegmentsWithEndpointPoints(
        List<double[]> graphPoints,
        double[]? rawStartLonLat,
        double[]? rawEndLonLat)
    {
        var points = new List<double[]>(graphPoints.Count + 2);
        if (rawStartLonLat is not null && rawStartLonLat.Length == 2)
            points.Add(new double[] { rawStartLonLat[0], rawStartLonLat[1] });

        points.AddRange(graphPoints.Select(CopyPoint));

        if (rawEndLonLat is not null && rawEndLonLat.Length == 2)
            points.Add(new double[] { rawEndLonLat[0], rawEndLonLat[1] });

        return BuildSegmentsFromPoints(points);
    }

    public static List<List<double[]>> BuildSegmentsWithPortAccessPaths(
        CsrOceanGraph graph,
        int[] pathNodes,
        double[][]? startAccessPath,
        double[][]? endAccessPath,
        RouteGeometryMode mode = RouteGeometryMode.Raw,
        RasterLandMask? landMask = null,
        EdgePortalSet? edgePortals = null)
    {
        var geometry = BuildGraphGeometry(graph, pathNodes, mode, landMask, edgePortals);
        return BuildSegmentsWithPortAccessPoints(geometry.FinalGraphPoints, startAccessPath, endAccessPath);
    }

    public static List<List<double[]>> BuildSegmentsWithPortAccessPoints(
        List<double[]> graphPoints,
        double[][]? startAccessPath,
        double[][]? endAccessPath)
    {
        var points = new List<double[]>();

        AppendStartAccess(points, startAccessPath);
        points.AddRange(graphPoints.Select(CopyPoint));
        AppendEndAccess(points, endAccessPath);

        return BuildSegmentsFromPoints(DedupeSequentialPoints(points));
    }

    private static List<double[]> BuildRawGraphPoints(CsrOceanGraph graph, int[] pathNodes)
    {
        var rawPoints = new List<double[]>(pathNodes.Length);
        for (int i = 0; i < pathNodes.Length; i++)
        {
            int node = pathNodes[i];
            rawPoints.Add(new double[] { graph.NodeLon[node], graph.NodeLat[node] });
        }
        return rawPoints;
    }

    private static FunnelGeometryBuildResult BuildFunnelOrFallback(
        CsrOceanGraph graph,
        int[] pathNodes,
        List<double[]> rawPoints,
        RasterLandMask landMask,
        EdgePortalSet edgePortals)
    {
        var funnelPoints = new List<double[]>();
        var finalPoints = new List<double[]>();
        var sections = new List<RouteGeometrySectionDiagnostic>();

        int runStart = 0;
        for (int i = 1; i <= rawPoints.Count; i++)
        {
            bool runEnded = i == rawPoints.Count || Math.Abs(rawPoints[i][0] - rawPoints[i - 1][0]) > AntiMeridianThreshold;
            if (!runEnded)
                continue;

            int length = i - runStart;
            var runPoints = rawPoints.GetRange(runStart, length);
            var runNodes = pathNodes[runStart..i];
            var attempt = TryFunnelContinuousRun(graph, runNodes, runPoints, landMask, edgePortals);

            if (attempt.Success)
            {
                funnelPoints.AddRange(attempt.Points!);
                finalPoints.AddRange(PostProcessFunneledPoints(attempt.Points!, landMask));
            }
            else
            {
                var fallbackPoints = SmoothGraphPoints(runPoints, landMask);
                funnelPoints.AddRange(fallbackPoints);
                finalPoints.AddRange(fallbackPoints);
                sections.Add(new RouteGeometrySectionDiagnostic(
                    StartNodeOffset: runStart,
                    EndNodeOffset: i - 1,
                    BaseMode: "funnel",
                    FinalMode: "shortcut",
                    Reason: attempt.FailureReason ?? "unknown"));
            }

            runStart = i;
        }

        return new FunnelGeometryBuildResult(
            FinalPoints: DedupeSequentialPoints(finalPoints),
            FunnelPointCount: DedupeSequentialPoints(funnelPoints).Count,
            Sections: [.. sections]);
    }

    private static AisShapingResult TryApplyAisHints(
        CsrOceanGraph graph,
        int[] pathNodes,
        RasterLandMask landMask,
        EdgePortalSet edgePortals,
        AisCorridorHintSet aisCorridorHints,
        string? startLocode,
        string? endLocode)
    {
        var candidates = new List<HintWindow>();

        var startWindow = TryResolvePortHintWindow(pathNodes, startLocode, aisCorridorHints, isStart: true, landMask);
        if (startWindow is not null)
            candidates.Add(startWindow);

        var endWindow = TryResolvePortHintWindow(pathNodes, endLocode, aisCorridorHints, isStart: false, landMask);
        if (endWindow is not null)
            candidates.Add(endWindow);

        candidates.AddRange(FindGenericHintWindows(pathNodes, aisCorridorHints, landMask));
        var selected = SelectNonOverlappingWindows(candidates);
        if (selected.Count == 0)
            return new AisShapingResult(BuildFunnelOrFallback(graph, pathNodes, BuildRawGraphPoints(graph, pathNodes), landMask, edgePortals).FinalPoints, 0);

        var combined = new List<double[]>();
        int cursorNodeOffset = 0;

        foreach (var window in selected)
        {
            if (window.StartNodeOffset > cursorNodeOffset)
            {
                AppendCombined(
                    combined,
                    BuildSubpathGeometry(graph, pathNodes[cursorNodeOffset..(window.StartNodeOffset + 1)], landMask, edgePortals));
            }
            else if (window.StartNodeOffset < cursorNodeOffset)
            {
                continue;
            }

            AppendCombined(combined, window.Coordinates);
            cursorNodeOffset = window.EndNodeOffset;
        }

        if (cursorNodeOffset < pathNodes.Length - 1)
        {
            AppendCombined(
                combined,
                BuildSubpathGeometry(graph, pathNodes[cursorNodeOffset..pathNodes.Length], landMask, edgePortals));
        }

        if (combined.Count == 0)
            combined = BuildFunnelOrFallback(graph, pathNodes, BuildRawGraphPoints(graph, pathNodes), landMask, edgePortals).FinalPoints;

        return new AisShapingResult(DedupeSequentialPoints(combined), selected.Count);
    }

    private static HintWindow? TryResolvePortHintWindow(
        int[] pathNodes,
        string? locode,
        AisCorridorHintSet hintSet,
        bool isStart,
        RasterLandMask landMask)
    {
        if (string.IsNullOrWhiteSpace(locode) || !hintSet.TryGetPortApproach(locode, out var hint))
            return null;
        if (hint.EdgeSpans.Length == 0 || hint.Coordinates.Length < 2)
            return null;

        if (isStart)
        {
            return TryMatchHintAt(pathNodes, hint, 0, landMask, reversed: false, source: "port");
        }

        int startOffset = pathNodes.Length - hint.EdgeSpans.Length - 1;
        if (startOffset < 0)
            return null;
        return TryMatchHintAt(pathNodes, hint, startOffset, landMask, reversed: true, source: "port");
    }

    private static List<HintWindow> FindGenericHintWindows(
        int[] pathNodes,
        AisCorridorHintSet hintSet,
        RasterLandMask landMask)
    {
        var windows = new List<HintWindow>();
        if (pathNodes.Length < 2)
            return windows;

        foreach (var hint in hintSet.RouteWindows)
        {
            int edgeCount = hint.EdgeSpans.Length;
            if (edgeCount == 0 || edgeCount >= pathNodes.Length)
                continue;

            for (int offset = 0; offset <= pathNodes.Length - edgeCount - 1; offset++)
            {
                var forward = TryMatchHintAt(pathNodes, hint, offset, landMask, reversed: false, source: "corridor");
                if (forward is not null)
                {
                    windows.Add(forward);
                    continue;
                }

                if (AisCorridorHintSet.IsBidirectional(hint))
                {
                    var reverse = TryMatchHintAt(pathNodes, hint, offset, landMask, reversed: true, source: "corridor");
                    if (reverse is not null)
                        windows.Add(reverse);
                }
            }
        }

        return windows;
    }

    private static HintWindow? TryMatchHintAt(
        int[] pathNodes,
        AisCorridorHint hint,
        int startNodeOffset,
        RasterLandMask landMask,
        bool reversed,
        string source)
    {
        int edgeCount = hint.EdgeSpans.Length;
        if (startNodeOffset < 0 || startNodeOffset + edgeCount >= pathNodes.Length)
            return null;

        for (int i = 0; i < edgeCount; i++)
        {
            var expected = reversed ? hint.EdgeSpans[edgeCount - 1 - i] : hint.EdgeSpans[i];
            int fromNode = reversed ? expected.ToNode : expected.FromNode;
            int toNode = reversed ? expected.FromNode : expected.ToNode;
            if (pathNodes[startNodeOffset + i] != fromNode || pathNodes[startNodeOffset + i + 1] != toNode)
                return null;
        }

        var sourceCoords = reversed
            ? hint.Coordinates.Reverse().Select(CopyPoint).ToList()
            : hint.Coordinates.Select(CopyPoint).ToList();
        var prepared = PrepareHintCoordinates(sourceCoords, landMask);
        if (prepared is null)
            return null;

        return new HintWindow(
            StartNodeOffset: startNodeOffset,
            EndNodeOffset: startNodeOffset + edgeCount,
            Coordinates: prepared,
            Kind: hint.Kind,
            Confidence: hint.Confidence,
            Source: source);
    }

    private static List<HintWindow> SelectNonOverlappingWindows(List<HintWindow> candidates)
    {
        if (candidates.Count == 0)
            return [];

        var selected = new List<HintWindow>();
        foreach (var candidate in candidates
                     .OrderByDescending(WindowPriority)
                     .ThenBy(c => c.StartNodeOffset)
                     .ThenByDescending(c => c.EdgeCount))
        {
            bool overlaps = selected.Any(existing =>
                !(candidate.EndNodeOffset <= existing.StartNodeOffset || candidate.StartNodeOffset >= existing.EndNodeOffset));
            if (!overlaps)
                selected.Add(candidate);
        }

        return [.. selected.OrderBy(c => c.StartNodeOffset).ThenBy(c => c.EndNodeOffset)];
    }

    private static (int KindPriority, int EdgeCount, float Confidence, int StartBias) WindowPriority(HintWindow window) =>
        (window.Kind == AisCorridorHintSet.PortApproachKind ? 2 : 1, window.EdgeCount, window.Confidence, -window.StartNodeOffset);

    private static void AppendCombined(List<double[]> combined, IEnumerable<double[]> points)
    {
        bool hasExisting = combined.Count > 0;
        foreach (var point in points)
        {
            if (hasExisting && combined.Count > 0)
            {
                var previous = combined[^1];
                if (Math.Abs(previous[0] - point[0]) <= 1e-12 && Math.Abs(previous[1] - point[1]) <= 1e-12)
                    continue;
            }
            combined.Add(CopyPoint(point));
        }
    }

    private static List<double[]> BuildSubpathGeometry(
        CsrOceanGraph graph,
        int[] pathNodes,
        RasterLandMask landMask,
        EdgePortalSet edgePortals)
    {
        if (pathNodes.Length == 0)
            return [];
        if (pathNodes.Length == 1)
            return BuildRawGraphPoints(graph, pathNodes);

        var rawPoints = BuildRawGraphPoints(graph, pathNodes);
        return BuildFunnelOrFallback(graph, pathNodes, rawPoints, landMask, edgePortals).FinalPoints;
    }

    private static List<double[]>? PrepareHintCoordinates(IEnumerable<double[]> coordinates, RasterLandMask landMask)
    {
        var raw = coordinates.Select(CopyPoint).ToList();
        if (raw.Count < 2)
            return null;
        for (int i = 1; i < raw.Count; i++)
        {
            if (!landMask.IsSegmentLandSafe(raw[i - 1][0], raw[i - 1][1], raw[i][0], raw[i][1]))
                return null;
        }
        var smoothed = SmoothGraphPoints(raw, landMask);
        if (smoothed.Count < 2)
            return null;
        for (int i = 1; i < smoothed.Count; i++)
        {
            if (!landMask.IsSegmentLandSafe(smoothed[i - 1][0], smoothed[i - 1][1], smoothed[i][0], smoothed[i][1]))
                return null;
        }
        return smoothed;
    }

    private static void AppendStartAccess(List<double[]> points, double[][]? accessPath)
    {
        if (accessPath is null || accessPath.Length < 2)
            return;

        for (int i = 0; i < accessPath.Length - 1; i++)
            points.Add(new double[] { accessPath[i][0], accessPath[i][1] });
    }

    private static void AppendEndAccess(List<double[]> points, double[][]? accessPath)
    {
        if (accessPath is null || accessPath.Length < 2)
            return;

        for (int i = accessPath.Length - 2; i >= 0; i--)
            points.Add(new double[] { accessPath[i][0], accessPath[i][1] });
    }

    private static List<double[]> DedupeSequentialPoints(List<double[]> points)
    {
        if (points.Count <= 1)
            return points;

        var deduped = new List<double[]> { CopyPoint(points[0]) };
        for (int i = 1; i < points.Count; i++)
        {
            var prev = deduped[^1];
            var current = points[i];
            if (Math.Abs(prev[0] - current[0]) <= 1e-12 && Math.Abs(prev[1] - current[1]) <= 1e-12)
                continue;
            deduped.Add(CopyPoint(current));
        }
        return deduped;
    }

    private static List<double[]> SmoothGraphPoints(List<double[]> points, RasterLandMask? landMask)
    {
        if (landMask is null || points.Count < 3)
            return DedupeSequentialPoints(points);

        var result = new List<double[]>();
        int runStart = 0;
        for (int i = 1; i <= points.Count; i++)
        {
            bool runEnded = i == points.Count || Math.Abs(points[i][0] - points[i - 1][0]) > AntiMeridianThreshold;
            if (!runEnded)
                continue;

            result.AddRange(SmoothContinuousRun(points.GetRange(runStart, i - runStart), landMask));
            runStart = i;
        }
        return DedupeSequentialPoints(result);
    }

    private static List<double[]> PostProcessFunneledPoints(List<double[]> points, RasterLandMask landMask)
    {
        if (points.Count < 3)
            return DedupeSequentialPoints(points);

        return DedupeSequentialPoints(SmoothGraphPoints(points, landMask));
    }

    private static List<double[]> SmoothContinuousRun(List<double[]> points, RasterLandMask landMask)
    {
        if (points.Count < 3)
            return DedupeSequentialPoints(points);

        var smoothed = new List<double[]> { CopyPoint(points[0]) };
        int current = 0;
        while (current < points.Count - 1)
        {
            int next = current + 1;
            int farthestCandidate = Math.Min(points.Count - 1, current + MaxShortcutLookaheadNodes);
            for (int candidate = farthestCandidate; candidate > current + 1; candidate--)
            {
                if (!landMask.IsSegmentLandSafe(
                        points[current][0], points[current][1],
                        points[candidate][0], points[candidate][1]))
                    continue;
                next = candidate;
                break;
            }
            smoothed.Add(CopyPoint(points[next]));
            current = next;
        }
        return smoothed;
    }

    private static FunnelRunAttempt TryFunnelContinuousRun(
        CsrOceanGraph graph,
        int[] pathNodes,
        List<double[]> points,
        RasterLandMask landMask,
        EdgePortalSet edgePortals)
    {
        if (points.Count < 3)
            return FunnelRunAttempt.SuccessResult(DedupeSequentialPoints(points));

        var portals = new List<(double[] Left, double[] Right)> { (points[0], points[0]) };
        for (int i = 0; i < pathNodes.Length - 1; i++)
        {
            if (!TryFindEdgeIndex(graph, pathNodes[i], pathNodes[i + 1], out int edgeIndex))
                return FunnelRunAttempt.Failure("edge_not_found");
            if (!edgePortals.TryGetPortal(edgeIndex, out var segment))
                return FunnelRunAttempt.Failure("missing_portal");
            portals.Add(OrderPortal(points[i], points[i + 1], segment));
        }
        portals.Add((points[^1], points[^1]));

        var funneled = RunFunnel(portals);
        if (funneled.Count < 2)
            return FunnelRunAttempt.Failure("degenerate_funnel");
        for (int i = 1; i < funneled.Count; i++)
        {
            if (!landMask.IsSegmentLandSafe(funneled[i - 1][0], funneled[i - 1][1], funneled[i][0], funneled[i][1]))
                return FunnelRunAttempt.Failure("unsafe_funnel_segment");
        }
        return FunnelRunAttempt.SuccessResult(DedupeSequentialPoints(funneled));
    }

    private static (double[] Left, double[] Right) OrderPortal(double[] from, double[] to, PortalSegment segment)
    {
        var a = new double[] { segment.Ax, segment.Ay };
        var b = new double[] { segment.Bx, segment.By };
        return TriArea2(from, to, a) >= 0 ? (a, b) : (b, a);
    }

    private static bool TryFindEdgeIndex(CsrOceanGraph graph, int fromNode, int toNode, out int edgeIndex)
    {
        for (int edge = (int)graph.RowPtr[fromNode]; edge < graph.RowPtr[fromNode + 1]; edge++)
        {
            if (graph.ColIdx[edge] == toNode)
            {
                edgeIndex = edge;
                return true;
            }
        }
        edgeIndex = -1;
        return false;
    }

    private static List<double[]> RunFunnel(List<(double[] Left, double[] Right)> portals)
    {
        var path = new List<double[]>();
        var apex = CopyPoint(portals[0].Left);
        var left = CopyPoint(portals[0].Left);
        var right = CopyPoint(portals[0].Right);
        int apexIndex = 0;
        int leftIndex = 0;
        int rightIndex = 0;
        path.Add(CopyPoint(apex));

        for (int i = 1; i < portals.Count; i++)
        {
            var nextLeft = portals[i].Left;
            var nextRight = portals[i].Right;

            if (TriArea2(apex, right, nextRight) <= 0.0)
            {
                if (PointsEqual(apex, right) || TriArea2(apex, left, nextRight) > 0.0)
                {
                    right = CopyPoint(nextRight);
                    rightIndex = i;
                }
                else
                {
                    path.Add(CopyPoint(left));
                    apex = CopyPoint(left);
                    apexIndex = leftIndex;
                    left = CopyPoint(apex);
                    right = CopyPoint(apex);
                    leftIndex = apexIndex;
                    rightIndex = apexIndex;
                    i = apexIndex;
                    continue;
                }
            }

            if (TriArea2(apex, left, nextLeft) >= 0.0)
            {
                if (PointsEqual(apex, left) || TriArea2(apex, right, nextLeft) < 0.0)
                {
                    left = CopyPoint(nextLeft);
                    leftIndex = i;
                }
                else
                {
                    path.Add(CopyPoint(right));
                    apex = CopyPoint(right);
                    apexIndex = rightIndex;
                    left = CopyPoint(apex);
                    right = CopyPoint(apex);
                    leftIndex = apexIndex;
                    rightIndex = apexIndex;
                    i = apexIndex;
                    continue;
                }
            }
        }

        path.Add(CopyPoint(portals[^1].Left));
        return path;
    }

    private static double TriArea2(double[] a, double[] b, double[] c) =>
        (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);

    private static bool PointsEqual(double[] a, double[] b) =>
        Math.Abs(a[0] - b[0]) <= 1e-12 && Math.Abs(a[1] - b[1]) <= 1e-12;

    private static double[] CopyPoint(double[] point) => [point[0], point[1]];

    private static List<List<double[]>> BuildSegmentsFromPoints(List<double[]> points)
    {
        var segments = new List<List<double[]>>();
        if (points.Count == 0)
            return segments;

        var current = new List<double[]> { CopyPoint(points[0]) };
        for (int i = 1; i < points.Count; i++)
        {
            double prevLon = points[i - 1][0];
            double curLon = points[i][0];
            double curLat = points[i][1];

            double rawLonDelta = curLon - prevLon;
            if (Math.Abs(rawLonDelta) > AntiMeridianThreshold)
            {
                segments.Add(current);
                current = new List<double[]> { new double[] { curLon, curLat } };
            }
            else
            {
                current.Add(new double[] { curLon, curLat });
            }
        }

        segments.Add(current);
        return segments;
    }

    private static string ModeName(RouteGeometryMode mode) => mode.ToString().ToLowerInvariant();

    private sealed record FunnelGeometryBuildResult(
        List<double[]> FinalPoints,
        int FunnelPointCount,
        RouteGeometrySectionDiagnostic[] Sections);

    private sealed record FunnelRunAttempt(
        bool Success,
        List<double[]>? Points,
        string? FailureReason)
    {
        public static FunnelRunAttempt SuccessResult(List<double[]> points) => new(true, points, null);
        public static FunnelRunAttempt Failure(string reason) => new(false, null, reason);
    }

    private sealed record HintWindow(
        int StartNodeOffset,
        int EndNodeOffset,
        List<double[]> Coordinates,
        byte Kind,
        float Confidence,
        string Source)
    {
        public int EdgeCount => EndNodeOffset - StartNodeOffset;
    }

    private sealed record AisShapingResult(List<double[]> Points, int AppliedSectionCount);

    public static List<List<double[]>> BuildMultiLineString(CsrOceanGraph graph, int[] pathNodes) =>
        BuildSegments(graph, pathNodes);
}
