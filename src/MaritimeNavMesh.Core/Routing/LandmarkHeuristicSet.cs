using MaritimeNavMesh.Core.Graph;

namespace MaritimeNavMesh.Core.Routing;

public sealed class LandmarkHeuristicSet
{
    private readonly int[] _landmarkNodes;
    private readonly float[][] _fromLandmark;
    private readonly float[][] _toLandmark;

    private LandmarkHeuristicSet(int[] landmarkNodes, float[][] fromLandmark, float[][] toLandmark)
    {
        _landmarkNodes = landmarkNodes;
        _fromLandmark = fromLandmark;
        _toLandmark = toLandmark;
    }

    public int LandmarkCount => _landmarkNodes.Length;
    public IReadOnlyList<int> LandmarkNodes => _landmarkNodes;

    public double LowerBound(int fromNode, int toNode)
    {
        if (fromNode == toNode)
            return 0.0;

        double best = 0.0;
        for (int i = 0; i < _landmarkNodes.Length; i++)
        {
            float lToTarget = _fromLandmark[i][toNode];
            float lToSource = _fromLandmark[i][fromNode];
            if (IsFinite(lToTarget) && IsFinite(lToSource))
            {
                double candidate = lToTarget - lToSource;
                if (candidate > best)
                    best = candidate;
            }

            float sourceToL = _toLandmark[i][fromNode];
            float targetToL = _toLandmark[i][toNode];
            if (IsFinite(sourceToL) && IsFinite(targetToL))
            {
                double candidate = sourceToL - targetToL;
                if (candidate > best)
                    best = candidate;
            }
        }

        return best > 0.0 ? best : 0.0;
    }

    public static LandmarkHeuristicSet Build(CsrOceanGraph graph)
    {
        int dominantComponent = FindDominantComponent(graph);
        var landmarkNodes = SelectLandmarks(graph, dominantComponent);
        var fromLandmark = new float[landmarkNodes.Length][];
        var toLandmark = new float[landmarkNodes.Length][];
        for (int i = 0; i < landmarkNodes.Length; i++)
        {
            fromLandmark[i] = ComputeSingleSourceDistances(graph, landmarkNodes[i], reverse: false);
            toLandmark[i] = ComputeSingleSourceDistances(graph, landmarkNodes[i], reverse: true);
        }

        return new LandmarkHeuristicSet(landmarkNodes, fromLandmark, toLandmark);
    }

    private static int FindDominantComponent(CsrOceanGraph graph)
    {
        var counts = new Dictionary<int, int>();
        int dominantComponent = -1;
        int dominantCount = 0;
        for (int i = 0; i < graph.NodeCount; i++)
        {
            int component = graph.NodeComponent[i];
            if (component < 0)
                continue;
            counts.TryGetValue(component, out int count);
            count += 1;
            counts[component] = count;
            if (count > dominantCount)
            {
                dominantCount = count;
                dominantComponent = component;
            }
        }
        return dominantComponent;
    }

    private static int[] SelectLandmarks(CsrOceanGraph graph, int componentId)
    {
        int? minLon = null;
        int? maxLon = null;
        int? minLat = null;
        int? maxLat = null;

        for (int i = 0; i < graph.NodeCount; i++)
        {
            if (graph.NodeComponent[i] != componentId)
                continue;
            if (!minLon.HasValue || graph.NodeLon[i] < graph.NodeLon[minLon.Value]) minLon = i;
            if (!maxLon.HasValue || graph.NodeLon[i] > graph.NodeLon[maxLon.Value]) maxLon = i;
            if (!minLat.HasValue || graph.NodeLat[i] < graph.NodeLat[minLat.Value]) minLat = i;
            if (!maxLat.HasValue || graph.NodeLat[i] > graph.NodeLat[maxLat.Value]) maxLat = i;
        }

        var unique = new List<int>(4);
        void AddIfUnique(int? node)
        {
            if (!node.HasValue) return;
            if (!unique.Contains(node.Value)) unique.Add(node.Value);
        }

        AddIfUnique(minLon);
        AddIfUnique(maxLon);
        AddIfUnique(minLat);
        AddIfUnique(maxLat);

        if (unique.Count == 0)
            throw new InvalidOperationException("Could not select any landmark nodes from the graph.");

        return [.. unique];
    }

    private static float[] ComputeSingleSourceDistances(CsrOceanGraph graph, int sourceNode, bool reverse)
    {
        int nodeCount = graph.NodeCount;
        var dist = new float[nodeCount];
        Array.Fill(dist, float.PositiveInfinity);
        dist[sourceNode] = 0f;

        var pq = new PriorityQueue<int, float>(256);
        pq.Enqueue(sourceNode, 0f);

        while (pq.TryDequeue(out int node, out float nodeDist))
        {
            if (nodeDist > dist[node])
                continue;

            if (!reverse)
            {
                var (edgeStart, edgeEnd) = graph.EdgeRange(node);
                for (int edgeIndex = edgeStart; edgeIndex < edgeEnd; edgeIndex++)
                {
                    int neighbor = checked((int)graph.ColIdx[edgeIndex]);
                    float newDist = dist[node] + graph.EdgeCost[edgeIndex];
                    if (newDist < dist[neighbor])
                    {
                        dist[neighbor] = newDist;
                        pq.Enqueue(neighbor, newDist);
                    }
                }
            }
            else
            {
                var (edgeStart, edgeEnd) = graph.ReverseEdgeRange(node);
                for (int reverseIndex = edgeStart; reverseIndex < edgeEnd; reverseIndex++)
                {
                    int predecessor = checked((int)graph.ReverseColIdx[reverseIndex]);
                    int originalEdgeIndex = graph.ReverseEdgeIdx[reverseIndex];
                    float newDist = dist[node] + graph.EdgeCost[originalEdgeIndex];
                    if (newDist < dist[predecessor])
                    {
                        dist[predecessor] = newDist;
                        pq.Enqueue(predecessor, newDist);
                    }
                }
            }
        }

        return dist;
    }

    private static bool IsFinite(float value) => !float.IsPositiveInfinity(value) && !float.IsNaN(value);
}
