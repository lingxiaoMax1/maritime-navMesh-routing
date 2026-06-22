namespace MaritimeNavMesh.Core.Geometry;

public readonly record struct AisCorridorEdgeSpan(int FromNode, int ToNode);

public sealed record AisCorridorHint(
    int CorridorId,
    string Locode,
    byte Kind,
    byte Flags,
    float Confidence,
    int SupportCount,
    double[][] Coordinates,
    AisCorridorEdgeSpan[] EdgeSpans,
    double MinLon,
    double MinLat,
    double MaxLon,
    double MaxLat);

public sealed class AisCorridorHintSet
{
    public const byte PortApproachKind = 1;
    public const byte RouteWindowKind = 2;
    public const byte BidirectionalFlag = 1;

    private readonly Dictionary<string, AisCorridorHint> _portApproachByLocode;
    private readonly AisCorridorHint[] _routeWindows;

    public int HintCount => _portApproachByLocode.Count + _routeWindows.Length;
    public IReadOnlyList<AisCorridorHint> RouteWindows => _routeWindows;

    public AisCorridorHintSet(IEnumerable<AisCorridorHint> hints)
    {
        var materialized = hints.ToArray();
        _portApproachByLocode = materialized
            .Where(h => h.Kind == PortApproachKind && !string.IsNullOrWhiteSpace(h.Locode))
            .GroupBy(h => h.Locode.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.Confidence).First());
        _routeWindows = materialized
            .Where(h => h.Kind == RouteWindowKind && h.EdgeSpans.Length > 0 && h.Coordinates.Length >= 2)
            .OrderByDescending(h => h.Confidence)
            .ThenByDescending(h => h.EdgeSpans.Length)
            .ToArray();
    }

    public bool TryGetPortApproach(string locode, out AisCorridorHint hint)
    {
        if (string.IsNullOrWhiteSpace(locode))
        {
            hint = default!;
            return false;
        }

        return _portApproachByLocode.TryGetValue(locode.ToUpperInvariant(), out hint!);
    }

    public static bool IsBidirectional(AisCorridorHint hint) => (hint.Flags & BidirectionalFlag) != 0;
}
