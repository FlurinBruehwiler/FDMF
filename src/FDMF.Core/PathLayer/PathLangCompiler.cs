using System.Collections.Generic;

namespace FDMF.Core.PathLayer;

public abstract record AstNode(TextView Range);

public readonly record struct AstIdent(TextView Text)
{
    public override string ToString() => Text.ToString();
}

public sealed record AstPredicate(AstIdent Name, AstIdent InputType, AstExpr Body, TextView Range) : AstNode(Range);

public abstract record AstExpr(TextView Range) : AstNode(Range);

// ---- Error recovery ----

// Inserted by the parser when it encounters invalid syntax.
// Consumers should treat this as an invalid node and rely on diagnostics.
public sealed record AstErrorExpr(TextView Range) : AstExpr(Range);

// ---- Values / identifiers ----

public sealed record AstThisExpr(TextView Range) : AstExpr(Range);

public sealed record AstCurrentExpr(TextView Range) : AstExpr(Range);

// ---- Core path operations ----

public record AstPathStep(
    TextView Range,
    AstFilter? Filter = null
) : AstNode(Range);

public record AstRepeatStep(AstPathStep[] Steps, TextView Range, AstFilter? Filter = null) : AstPathStep(Range, Filter);
public record AstAsoStep(AstIdent AssocName, TextView Range, AstFilter? Filter = null) : AstPathStep(Range, Filter);

// Represents a sequence of traversals starting from a source expression:
//   this->A->B[...]
public sealed record AstPathExpr(
    AstExpr Source,
    IReadOnlyList<AstPathStep> Steps,
    TextView Range
) : AstExpr(Range);

public sealed record AstFilterExpr(AstExpr Source, AstFilter Filter, TextView Range) : AstExpr(Range);

// ---- Composition ----

public enum AstLogicalOp
{
    And,
    Or,
}

public sealed record AstLogicalExpr(AstLogicalOp Op, AstExpr Left, AstExpr Right, TextView Range) : AstExpr(Range);

public sealed record AstPredicateCallExpr(AstIdent PredicateName, AstExpr Argument, TextView Range) : AstExpr(Range);

// ---- Filters / conditions ----

public sealed record AstFilter(AstCondition Condition, TextView Range) : AstNode(Range);

public abstract record AstCondition(TextView Range) : AstNode(Range);

// Inserted by the parser when it encounters invalid syntax.
public sealed record AstErrorCondition(TextView Range) : AstCondition(Range);

public enum AstConditionOp
{
    And,
    Or,
}

public sealed record AstConditionBinary(AstConditionOp Op, AstCondition Left, AstCondition Right, TextView Range) : AstCondition(Range);

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
    AstLiteral Value,
    TextView Range
) : AstCondition(Range);

// Predicate compare:
// - Visible($) = true
// - CanEdit(this) != false
public sealed record AstPredicateCompareCondition(
    AstIdent PredicateName,
    AstExpr Argument,
    AstCompareOp Op,
    AstLiteral Value,
    TextView Range
) : AstCondition(Range);

public abstract record AstLiteral(TextView Range) : AstNode(Range);

// Inserted by the parser when it encounters invalid syntax.
public sealed record AstErrorLiteral(TextView Range) : AstLiteral(Range);

public sealed record AstBoolLiteral(bool Value, TextView Range) : AstLiteral(Range);

// Includes the quotes in source. Parsing/unescaping is a later phase.
public sealed record AstStringLiteral(TextView Raw, TextView Range) : AstLiteral(Range);

public sealed record AstNumberLiteral(TextView Raw, TextView Range) : AstLiteral(Range);
