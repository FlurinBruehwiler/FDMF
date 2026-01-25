namespace FDMF.Core.PathLayer;

public sealed class PathLangSemanticModel
{
    // Node-set expression -> possible runtime TypIds.
    public Dictionary<AstExpr, IReadOnlySet<Guid>> PossibleTypesByExpr { get; } = new();

    // Predicate definition -> resolved input TypId (if known).
    public Dictionary<AstPredicate, Guid?> InputTypIdByPredicate { get; } = new();

    // Traverse expression -> (source TypId -> resolved assoc field + target type).
    public Dictionary<AstTraverseExpr, Dictionary<Guid, PathLangResolvedAssoc>> AssocByTraverse { get; } = new();

    // Field compare -> (TypId -> resolved scalar field).
    public Dictionary<AstFieldCompareCondition, Dictionary<Guid, PathLangResolvedField>> FieldByCompare { get; } = new();

    // Type guard -> resolved TypId (if present).
    public Dictionary<AstFieldCompareCondition, Guid?> TypeGuardTypIdByCompare { get; } = new();

    // Predicate calls / predicate compares -> resolved predicate input type (if known).
    public Dictionary<AstPredicateCallExpr, Guid?> TargetInputTypIdByPredicateCall { get; } = new();
    public Dictionary<AstPredicateCompareCondition, Guid?> TargetInputTypIdByPredicateCompare { get; } = new();
}

public readonly record struct PathLangResolvedAssoc(Guid AssocFldId, Guid TargetTypId);

public readonly record struct PathLangResolvedField(Guid FldId, string DataType);
