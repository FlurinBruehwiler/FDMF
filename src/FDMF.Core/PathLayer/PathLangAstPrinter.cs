using System;
using System.Collections.Generic;
using System.Text;

namespace FDMF.Core.PathLayer;

public static class PathLangAstPrinter
{
    public static string PrintProgram(IReadOnlyList<AstPredicate> predicates, bool includeSpans = false)
    {
        var sb = new StringBuilder();

        if (predicates is null)
            return "<null>";

        if (predicates.Count == 0)
            return "<empty program>";

        for (int i = 0; i < predicates.Count; i++)
        {
            if (i != 0)
                sb.AppendLine();
            WritePredicate(sb, predicates[i], 0, includeSpans);
        }

        return sb.ToString();
    }

    public static string Print(AstNode node, bool includeSpans = false)
    {
        if (node is null)
            return "<null>";

        var sb = new StringBuilder();
        WriteNode(sb, node, 0, includeSpans);
        return sb.ToString();
    }

    private static void WriteNode(StringBuilder sb, AstNode node, int indent, bool includeSpans)
    {
        switch (node)
        {
            case AstPredicate p:
                WritePredicate(sb, p, indent, includeSpans);
                return;
            case AstExpr e:
                WriteExpr(sb, e, indent, includeSpans);
                return;
            case AstFilter f:
                WriteFilter(sb, f, indent, includeSpans);
                return;
            case AstCondition c:
                WriteCondition(sb, c, indent, includeSpans);
                return;
            case AstLiteral l:
                WriteLiteral(sb, l, indent, includeSpans);
                return;
            default:
                Line(sb, indent, node.GetType().Name);
                return;
        }
    }

    private static void WritePredicate(StringBuilder sb, AstPredicate p, int indent, bool includeSpans)
    {
        Line(sb, indent, $"Predicate {FormatIdent(p.Name, includeSpans)}({FormatIdent(p.InputType, includeSpans)})");
        Line(sb, indent + 1, "Body:");
        WriteExpr(sb, p.Body, indent + 2, includeSpans);
    }

    private static void WriteExpr(StringBuilder sb, AstExpr e, int indent, bool includeSpans)
    {
        switch (e)
        {
            case AstThisExpr:
                Line(sb, indent, "This");
                return;
            case AstCurrentExpr:
                Line(sb, indent, "Current ($)");
                return;
            case AstErrorExpr:
                Line(sb, indent, "<error expr>");
                return;

            case AstPathExpr p:
                Line(sb, indent, "PathExpr");
                Line(sb, indent + 1, "Source:");
                WriteExpr(sb, p.Source, indent + 2, includeSpans);
                Line(sb, indent + 1, "Steps:");
                for (int i = 0; i < p.Steps.Count; i++)
                {
                    var step = p.Steps[i];
                    Line(sb, indent + 2, $"-> {FormatIdent(step.AssocName, includeSpans)}");
                    if (step.Filter is not null)
                    {
                        Line(sb, indent + 3, "Filter:");
                        WriteFilter(sb, step.Filter, indent + 4, includeSpans);
                    }
                }
                return;

            case AstFilterExpr fe:
                Line(sb, indent, "FilterExpr");
                Line(sb, indent + 1, "Source:");
                WriteExpr(sb, fe.Source, indent + 2, includeSpans);
                Line(sb, indent + 1, "Filter:");
                WriteFilter(sb, fe.Filter, indent + 2, includeSpans);
                return;

            case AstRepeatExpr re:
                Line(sb, indent, "Repeat");
                WriteExpr(sb, re.Expr, indent + 1, includeSpans);
                return;

            case AstLogicalExpr log:
                Line(sb, indent, $"Logical {log.Op}");
                Line(sb, indent + 1, "Left:");
                WriteExpr(sb, log.Left, indent + 2, includeSpans);
                Line(sb, indent + 1, "Right:");
                WriteExpr(sb, log.Right, indent + 2, includeSpans);
                return;

            case AstPredicateCallExpr call:
                Line(sb, indent, $"PredicateCall {FormatIdent(call.PredicateName, includeSpans)}");
                Line(sb, indent + 1, "Arg:");
                WriteExpr(sb, call.Argument, indent + 2, includeSpans);
                return;

            default:
                Line(sb, indent, e.GetType().Name);
                return;
        }
    }

    private static void WriteFilter(StringBuilder sb, AstFilter f, int indent, bool includeSpans)
    {
        Line(sb, indent, "[");
        WriteCondition(sb, f.Condition, indent + 1, includeSpans);
        Line(sb, indent, "]");
    }

    private static void WriteCondition(StringBuilder sb, AstCondition c, int indent, bool includeSpans)
    {
        switch (c)
        {
            case AstErrorCondition:
                Line(sb, indent, "<error condition>");
                return;

            case AstConditionBinary bin:
                Line(sb, indent, $"Condition {bin.Op}");
                Line(sb, indent + 1, "Left:");
                WriteCondition(sb, bin.Left, indent + 2, includeSpans);
                Line(sb, indent + 1, "Right:");
                WriteCondition(sb, bin.Right, indent + 2, includeSpans);
                return;

            case AstFieldCompareCondition fc:
                Line(sb, indent, $"FieldCompare {fc.Op}");
                if (fc.TypeGuard is not null)
                    Line(sb, indent + 1, $"TypeGuard: {FormatIdent(fc.TypeGuard.Value, includeSpans)}");
                Line(sb, indent + 1, $"Field: {FormatIdent(fc.FieldName, includeSpans)}");
                Line(sb, indent + 1, "Value:");
                WriteLiteral(sb, fc.Value, indent + 2, includeSpans);
                return;

            case AstPredicateCompareCondition pc:
                Line(sb, indent, $"PredicateCompare {pc.Op} {FormatIdent(pc.PredicateName, includeSpans)}");
                Line(sb, indent + 1, "Arg:");
                WriteExpr(sb, pc.Argument, indent + 2, includeSpans);
                Line(sb, indent + 1, "Value:");
                WriteLiteral(sb, pc.Value, indent + 2, includeSpans);
                return;

            default:
                Line(sb, indent, c.GetType().Name);
                return;
        }
    }

    private static void WriteLiteral(StringBuilder sb, AstLiteral l, int indent, bool includeSpans)
    {
        switch (l)
        {
            case AstErrorLiteral:
                Line(sb, indent, "<error literal>");
                return;
            case AstBoolLiteral b:
                Line(sb, indent, $"Bool {b.Value.ToString().ToLowerInvariant()}");
                return;
            case AstNumberLiteral n:
                Line(sb, indent, $"Number {FormatTextView(n.Raw, includeSpans)}");
                return;
            case AstStringLiteral s:
                Line(sb, indent, $"String {FormatTextView(s.Raw, includeSpans)}");
                return;
            default:
                Line(sb, indent, l.GetType().Name);
                return;
        }
    }

    private static void Line(StringBuilder sb, int indent, string text)
    {
        sb.Append(' ', indent * 2);
        sb.AppendLine(text);
    }

    private static string FormatIdent(AstIdent ident, bool includeSpans)
    {
        return FormatTextView(ident.Text, includeSpans);
    }

    private static string FormatTextView(TextView tv, bool includeSpans)
    {
        // Use ToString() so empty/missing tokens still show as empty.
        var s = tv.ToString();

        if (!includeSpans)
            return s.Length == 0 ? "<missing>" : s;

        var shown = s.Length == 0 ? "<missing>" : s;
        return $"{shown} @ {tv.Start}+{tv.Length}";
    }
}
