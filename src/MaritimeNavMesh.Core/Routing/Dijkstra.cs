using System.Diagnostics;
using MaritimeNavMesh.Core.Graph;
using MaritimeNavMesh.Core.Geometry;

namespace MaritimeNavMesh.Core.Routing;

/// <summary>
/// Dijkstra shortest-path search over a CSR ocean graph.
/// All mutable state lives in a per-request RouteWorkspace — thread-safe via allocation.
/// Uses double for distance accumulation to avoid float drift on long routes.
/// </summary>
public static class Dijkstra
{
    private const double Infinity = double.PositiveInfinity;

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
            return RouteResult.Ok(
                pathNodes: [from],
                pathEdges: [],
                cost: 0,
                distNm: 0,
                visited: 0,
                elapsedMs: sw.ElapsedMilliseconds);

        var dist = new double[n];
        var prevNode = new int[n];
        var prevEdge = new int[n];
        Array.Fill(dist, Infinity);
        Array.Fill(prevNode, -1);
        Array.Fill(prevEdge, -1);
        dist[from] = 0.0;

        // PriorityQueue<nodeIndex, priority>
        var pq = new PriorityQueue<int, double>(initialCapacity: 256);
        pq.Enqueue(from, 0.0);
        int visitedCount = 0;

        while (pq.TryDequeue(out int node, out double nodeDist))
        {
            if (nodeDist > dist[node])
                continue; // stale entry

            visitedCount++;

            if (node == to)
                break;

            var (edgeStart, edgeEnd) = graph.EdgeRange(node);
            for (int e = edgeStart; e < edgeEnd; e++)
            {
                int neighbor = (int)graph.ColIdx[e];
                // Use edge cost (friction-weighted) as primary weight; accumulate in double
                double newDist = dist[node] + (double)graph.EdgeCost[e];
                if (newDist < dist[neighbor])
                {
                    dist[neighbor] = newDist;
                    prevNode[neighbor] = node;
                    prevEdge[neighbor] = e;
                    pq.Enqueue(neighbor, newDist);
                }
            }
        }

        sw.Stop();

        if (dist[to] == Infinity)
            return RouteResult.Failure(RouteStatus.NoPath, "No path found between the given nodes");

        // Reconstruct path
        var nodeList = new List<int>();
        var edgeList = new List<int>();

        for (int cur = to; cur != from; cur = prevNode[cur])
        {
            nodeList.Add(cur);
            int pe = prevEdge[cur];
            edgeList.Add(pe);
        }
        nodeList.Add(from);
        nodeList.Reverse();
        edgeList.Reverse();
        double totalDist = ComputePathDistanceNm(graph, nodeList);

        return RouteResult.Ok(
            pathNodes: [.. nodeList],
            pathEdges: [.. edgeList],
            cost: dist[to],
            distNm: totalDist,
            visited: visitedCount,
            elapsedMs: sw.ElapsedMilliseconds);
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
