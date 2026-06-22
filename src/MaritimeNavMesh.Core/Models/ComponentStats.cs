namespace MaritimeNavMesh.Core.Models;

public sealed class ComponentStats
{
    public int ComponentId { get; init; }
    public int NodeCount { get; init; }
    public bool IsDominant { get; init; }
}
