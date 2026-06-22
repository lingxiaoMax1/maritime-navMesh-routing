using MaritimeNavMesh.Core.Graph;
using MaritimeNavMesh.Core.Models;

namespace MaritimeNavMesh.Core.Indices;

/// <summary>
/// Component membership index. Answers: are two nodes in the same component?
/// Provides component stats including which component is the dominant ocean component.
/// </summary>
public sealed class ComponentIndex
{
    private readonly IIndexedArray<int> _nodeComponent;
    public IReadOnlyDictionary<int, ComponentStats> Stats { get; }
    public int DominantComponentId { get; }

    public ComponentIndex(CsrOceanGraph graph)
    {
        _nodeComponent = graph.NodeComponent;

        var counts = new Dictionary<int, int>();
        for (int i = 0; i < graph.NodeCount; i++)
        {
            int c = graph.NodeComponent[i];
            if (c < 0) continue;
            counts.TryGetValue(c, out int existing);
            counts[c] = existing + 1;
        }

        // Dominant = largest component
        int dominantId = -1;
        int dominantCount = 0;
        foreach (var (id, count) in counts)
        {
            if (count > dominantCount)
            {
                dominantCount = count;
                dominantId = id;
            }
        }
        DominantComponentId = dominantId;

        var stats = new Dictionary<int, ComponentStats>(counts.Count);
        foreach (var (id, count) in counts)
            stats[id] = new ComponentStats { ComponentId = id, NodeCount = count, IsDominant = id == dominantId };
        Stats = stats;
    }

    public int GetComponent(int nodeIndex) => _nodeComponent[nodeIndex];

    public bool SameComponent(int nodeA, int nodeB) =>
        _nodeComponent[nodeA] == _nodeComponent[nodeB];

    public bool IsRoutable(int nodeA, int nodeB)
    {
        int ca = _nodeComponent[nodeA];
        int cb = _nodeComponent[nodeB];
        return ca >= 0 && cb >= 0 && ca == cb;
    }
}
