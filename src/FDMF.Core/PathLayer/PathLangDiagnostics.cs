namespace FDMF.Core.PathLayer;

public enum PathLangDiagnosticSeverity : byte
{
    Warning,
    Error,
}

public readonly record struct PathLangDiagnostic(
    PathLangDiagnosticSeverity Severity,
    string Message,
    int Line,
    int Column,
    TextView Span
);

public sealed record PathLangParseResult(
    List<AstPredicate> Predicates,
    List<PathLangDiagnostic> Diagnostics
);

public sealed class PathLangSemanticModel
{
    public Dictionary<AstNode, Guid> PossibleTypesByExpr { get; } = new();

    public Dictionary<AstPredicate, Guid?> InputTypIdByPredicate { get; } = new();

    public Dictionary<AstPathStep, Guid> AssocByPathStep { get; } = new();

    public Dictionary<AstFieldCompareCondition, Guid> FieldByCompare { get; } = new();

    public Dictionary<AstFieldCompareCondition, Guid?> TypeGuardTypIdByCompare { get; } = new();

    public Dictionary<AstTypeTestCondition, Guid?> TypeTestTypIdByCondition { get; } = new();

    public Dictionary<AstPredicateCallExpr, Guid?> TargetInputTypIdByPredicateCall { get; } = new();
    public Dictionary<AstPredicateCompareCondition, Guid?> TargetInputTypIdByPredicateCompare { get; } = new();
}
