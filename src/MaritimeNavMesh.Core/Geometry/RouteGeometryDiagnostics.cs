namespace MaritimeNavMesh.Core.Geometry;

public sealed record RouteGeometrySectionDiagnostic(
    int StartNodeOffset,
    int EndNodeOffset,
    string BaseMode,
    string FinalMode,
    string Reason);

public sealed record RouteGeometryDiagnostics(
    string GeometryModeRequested,
    string GeometryModeUsed,
    int RawPointCount,
    int? FunnelPointCount,
    int FinalPointCount,
    int FallbackSectionCount,
    int AisShapedSectionCount,
    RouteGeometrySectionDiagnostic[] Sections);

public sealed record RouteGeometryBuildResult(
    List<double[]> FinalGraphPoints,
    RouteGeometryDiagnostics Diagnostics);
