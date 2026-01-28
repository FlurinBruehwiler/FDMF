namespace FDMF.Core.PathLayer;

public abstract record AstNode;

public readonly record struct AstIdent(TextView Text)
{
    public override string ToString() => Text.ToString();
}

public sealed record AstPredicate(AstIdent Name, AstIdent InputType, AstExpr Body) : AstNode;

public abstract record AstExpr : AstNode;

// ---- Error recovery ----

// Inserted by the parser when it encounters invalid syntax.
// Consumers should treat this as an invalid node and rely on diagnostics.
public sealed record AstErrorExpr : AstExpr;

// ---- Values / identifiers ----

public sealed record AstThisExpr : AstExpr;

public sealed record AstCurrentExpr : AstExpr;

// ---- Core path operations ----

public sealed record AstTraverseExpr(
    AstExpr Source,
    AstIdent AssocName,
    AstFilter? Filter = null
) : AstExpr;

public sealed record AstFilterExpr(AstExpr Source, AstFilter Filter) : AstExpr;

public sealed record AstRepeatExpr(AstExpr Expr) : AstExpr;

// ---- Composition ----

public enum AstLogicalOp
{
    And,
    Or,
}

public sealed record AstLogicalExpr(AstLogicalOp Op, AstExpr Left, AstExpr Right) : AstExpr;

public sealed record AstPredicateCallExpr(AstIdent PredicateName, AstExpr Argument) : AstExpr;

// ---- Filters / conditions ----

public sealed record AstFilter(AstCondition Condition) : AstNode;

public abstract record AstCondition : AstNode;

// Inserted by the parser when it encounters invalid syntax.
public sealed record AstErrorCondition : AstCondition;

public enum AstConditionOp
{
    And,
    Or,
}

public sealed record AstConditionBinary(AstConditionOp Op, AstCondition Left, AstCondition Right) : AstCondition;

public enum AstCompareOp
{
    Equals,
    NotEquals,
}

// Field compare with optional type guard:
// - $.Field = literal
// - $(Type).Field = literal
public sealed record AstFieldCompareCondition(
    AstIdent? TypeGuard,
    AstIdent FieldName,
    AstCompareOp Op,
    AstLiteral Value
) : AstCondition;

// Predicate compare:
// - Visible($) = true
// - CanEdit(this) != false
public sealed record AstPredicateCompareCondition(
    AstIdent PredicateName,
    AstExpr Argument,
    AstCompareOp Op,
    AstLiteral Value
) : AstCondition;

public abstract record AstLiteral : AstNode;

// Inserted by the parser when it encounters invalid syntax.
public sealed record AstErrorLiteral : AstLiteral;

public sealed record AstBoolLiteral(bool Value) : AstLiteral;

// Includes the quotes in source. Parsing/unescaping is a later phase.
public sealed record AstStringLiteral(TextView Raw) : AstLiteral;

public sealed record AstNumberLiteral(TextView Raw) : AstLiteral;
