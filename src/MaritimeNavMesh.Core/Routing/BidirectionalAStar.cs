using System.Diagnostics;
using MaritimeNavMesh.Core.Graph;
using MaritimeNavMesh.Core.Geometry;

namespace MaritimeNavMesh.Core.Routing;

public static class BidirectionalAStar
{
    private const double Infinity = double.PositiveInfinity;
    private const double Epsilon = 1e-9;

    public static RouteResult Search(CsrOceanGraph graph, RouteRequest request)
    {
        var sw = Stopwatch.StartNew();
        int from = request.FromNodeIndex;
        int to = request.ToNodeIndex;
        int n = graph.NodeCount;

        if (from < 0 || from >= n)
            return RouteResult.Failure(RouteStatus.InvalidNode, $"FromNodeIndex {from} out of range");
        if (to < 0 || to >= n)
            return RouteResult.Failure(RouteStatus.InvalidNode, $"ToNodeIndex {to} out of range");
        if (from == to)
        {
            return RouteResult.Ok(
                pathNodes: [from],
                pathEdges: [],
                cost: 0,
                distNm: 0,
                visited: 0,
                elapsedMs: sw.ElapsedMilliseconds);
        }

        var distF = new double[n];
        var distR = new double[n];
        var prevNodeF = new int[n];
        var prevEdgeF = new int[n];
        var nextNodeR = new int[n];
        var nextEdgeR = new int[n];
        var closedF = new bool[n];
        var closedR = new bool[n];
        Array.Fill(distF, Infinity);
        Array.Fill(distR, Infinity);
        Array.Fill(prevNodeF, -1);
        Array.Fill(prevEdgeF, -1);
        Array.Fill(nextNodeR, -1);
        Array.Fill(nextEdgeR, -1);
        distF[from] = 0.0;
        distR[to] = 0.0;

        double startToGoalLowerBound = LowerBoundCost(graph, from, to);
        var pqF = new PriorityQueue<int, double>(256);
        var pqR = new PriorityQueue<int, double>(256);
        pqF.Enqueue(from, ForwardKey(graph, distF[from], from, from, to, startToGoalLowerBound));
        pqR.Enqueue(to, ReverseKey(graph, distR[to], to, from, to, startToGoalLowerBound));

        double bestPathCost = Infinity;
        int bestMeetNode = -1;
        int visitedCount = 0;
        int forwardVisitedCount = 0;
        int reverseVisitedCount = 0;

        while (pqF.Count > 0 && pqR.Count > 0)
        {
            pqF.TryPeek(out _, out double bestForwardKey);
            pqR.TryPeek(out _, out double bestReverseKey);
            if (bestPathCost < Infinity && bestForwardKey + bestReverseKey >= bestPathCost + startToGoalLowerBound - Epsilon)
                break;

            bool expandForward = pqF.Count < pqR.Count || (pqF.Count == pqR.Count && bestForwardKey <= bestReverseKey);
            if (expandForward)
            {
                if (!pqF.TryDequeue(out int node, out double key))
                    break;
                double expectedKey = ForwardKey(graph, distF[node], node, from, to, startToGoalLowerBound);
                if (key > expectedKey + Epsilon || closedF[node])
                    continue;

                closedF[node] = true;
                visitedCount++;
                forwardVisitedCount++;
                if (distR[node] < Infinity)
                {
                    double candidate = distF[node] + distR[node];
                    if (candidate < bestPathCost)
                    {
                        bestPathCost = candidate;
                        bestMeetNode = node;
                    }
                }

                var (edgeStart, edgeEnd) = graph.EdgeRange(node);
                for (int edgeIndex = edgeStart; edgeIndex < edgeEnd; edgeIndex++)
                {
                    int neighbor = checked((int)graph.ColIdx[edgeIndex]);
                    double newDist = distF[node] + graph.EdgeCost[edgeIndex];
                    if (newDist + Epsilon < distF[neighbor])
                    {
                        distF[neighbor] = newDist;
                        prevNodeF[neighbor] = node;
                        prevEdgeF[neighbor] = edgeIndex;
                        pqF.Enqueue(neighbor, ForwardKey(graph, newDist, neighbor, from, to, startToGoalLowerBound));
                    }
                    if (distR[neighbor] < Infinity)
                    {
                        double candidate = newDist + distR[neighbor];
                        if (candidate < bestPathCost)
                        {
                            bestPathCost = candidate;
                            bestMeetNode = neighbor;
                        }
                    }
                }
            }
            else
            {
                if (!pqR.TryDequeue(out int node, out double key))
                    break;
                double expectedKey = ReverseKey(graph, distR[node], node, from, to, startToGoalLowerBound);
                if (key > expectedKey + Epsilon || closedR[node])
                    continue;

                closedR[node] = true;
                visitedCount++;
                reverseVisitedCount++;
                if (distF[node] < Infinity)
                {
                    double candidate = distF[node] + distR[node];
                    if (candidate < bestPathCost)
                    {
                        bestPathCost = candidate;
                        bestMeetNode = node;
                    }
                }

                var (edgeStart, edgeEnd) = graph.ReverseEdgeRange(node);
                for (int reverseSlot = edgeStart; reverseSlot < edgeEnd; reverseSlot++)
                {
                    int predecessor = checked((int)graph.ReverseColIdx[reverseSlot]);
                    int originalEdgeIndex = graph.ReverseEdgeIdx[reverseSlot];
                    double newDist = distR[node] + graph.EdgeCost[originalEdgeIndex];
                    if (newDist + Epsilon < distR[predecessor])
                    {
                        distR[predecessor] = newDist;
                        nextNodeR[predecessor] = node;
                        nextEdgeR[predecessor] = originalEdgeIndex;
                        pqR.Enqueue(predecessor, ReverseKey(graph, newDist, predecessor, from, to, startToGoalLowerBound));
                    }
                    if (distF[predecessor] < Infinity)
                    {
                        double candidate = distF[predecessor] + newDist;
                        if (candidate < bestPathCost)
                        {
                            bestPathCost = candidate;
                            bestMeetNode = predecessor;
                        }
                    }
                }
            }
        }

        sw.Stop();

        if (bestMeetNode < 0 || bestPathCost == Infinity)
            return RouteResult.Failure(RouteStatus.NoPath, "No path found between the given nodes");

        var pathNodes = new List<int>();
        var pathEdges = new List<int>();

        for (int cur = bestMeetNode; cur != from; cur = prevNodeF[cur])
        {
            pathNodes.Add(cur);
            pathEdges.Add(prevEdgeF[cur]);
        }
        pathNodes.Add(from);
        pathNodes.Reverse();
        pathEdges.Reverse();

        for (int cur = bestMeetNode; cur != to; cur = nextNodeR[cur])
        {
            int next = nextNodeR[cur];
            if (next < 0)
                return RouteResult.Failure(RouteStatus.NoPath, "Bidirectional route reconstruction failed.");
            pathEdges.Add(nextEdgeR[cur]);
            pathNodes.Add(next);
        }

        double totalDist = ComputePathDistanceNm(graph, pathNodes);
        return RouteResult.Ok(
            pathNodes: [.. pathNodes],
            pathEdges: [.. pathEdges],
            cost: bestPathCost,
            distNm: totalDist,
            visited: visitedCount,
            elapsedMs: sw.ElapsedMilliseconds,
            forwardVisitedNodes: forwardVisitedCount,
            reverseVisitedNodes: reverseVisitedCount);
    }

    private static double ForwardKey(CsrOceanGraph graph, double g, int node, int start, int goal, double startGoalLowerBound) =>
        g + ForwardPotential(graph, node, start, goal, startGoalLowerBound);

    private static double ReverseKey(CsrOceanGraph graph, double g, int node, int start, int goal, double startGoalLowerBound) =>
        g + ReversePotential(graph, node, start, goal, startGoalLowerBound);

    private static double ForwardPotential(CsrOceanGraph graph, int node, int start, int goal, double startGoalLowerBound) =>
        0.5 * (LowerBoundCost(graph, node, goal) - LowerBoundCost(graph, start, node) + startGoalLowerBound);

    private static double ReversePotential(CsrOceanGraph graph, int node, int start, int goal, double startGoalLowerBound) =>
        0.5 * (LowerBoundCost(graph, start, node) - LowerBoundCost(graph, node, goal) + startGoalLowerBound);

    private static double LowerBoundCost(CsrOceanGraph graph, int fromNode, int toNode)
    {
        if (fromNode == toNode)
            return 0.0;
        double landmarkLowerBound = graph.LandmarkHeuristics.LowerBound(fromNode, toNode);
        if (landmarkLowerBound > 0.0)
            return landmarkLowerBound;
        double lowerBoundRate = graph.MinCostPerNmLowerBound;
        if (lowerBoundRate <= 0.0)
            return 0.0;
        return lowerBoundRate * GeoMath.HaversineNm(
            graph.NodeLat[fromNode],
            graph.NodeLon[fromNode],
            graph.NodeLat[toNode],
            graph.NodeLon[toNode]);
    }

    private static double ComputePathDistanceNm(CsrOceanGraph graph, List<int> pathNodes)
    {
        double totalDist = 0.0;
        for (int i = 1; i < pathNodes.Count; i++)
        {
            int fromNode = pathNodes[i - 1];
            int toNode = pathNodes[i];
            totalDist += GeoMath.HaversineNm(
                graph.NodeLat[fromNode],
                graph.NodeLon[fromNode],
                graph.NodeLat[toNode],
                graph.NodeLon[toNode]);
        }
        return totalDist;
    }
}
