namespace MaritimeNavMesh.Core.Routing;

public enum RouteStatus { Success, NoPath, SameNode, InvalidNode }

public sealed class RouteResult
{
    public RouteStatus Status { get; init; }
    public string? FailureReason { get; init; }

    // Populated on success
    public int[]? PathNodes { get; init; }
    public int[]? PathEdges { get; init; }
    public double TotalCost { get; init; }
    public double TotalDistanceNm { get; init; }

    // Diagnostics
    public int VisitedNodes { get; init; }
    public int? ForwardVisitedNodes { get; init; }
    public int? ReverseVisitedNodes { get; init; }
    public long ElapsedMs { get; init; }

    public static RouteResult Failure(RouteStatus status, string reason) =>
        new() { Status = status, FailureReason = reason };

    public static RouteResult Ok(
        int[] pathNodes,
        int[] pathEdges,
        double cost,
        double distNm,
        int visited,
        long elapsedMs,
        int? forwardVisitedNodes = null,
        int? reverseVisitedNodes = null) =>
        new()
        {
            Status = RouteStatus.Success,
            PathNodes = pathNodes,
            PathEdges = pathEdges,
            TotalCost = cost,
            TotalDistanceNm = distNm,
            VisitedNodes = visited,
            ForwardVisitedNodes = forwardVisitedNodes,
            ReverseVisitedNodes = reverseVisitedNodes,
            ElapsedMs = elapsedMs,
        };
}
