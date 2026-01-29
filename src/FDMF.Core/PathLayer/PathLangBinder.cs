using BaseModel.Generated;
using FDMF.Core.Database;

namespace FDMF.Core.PathLayer;

public sealed record PathLangSemanticResult(
    PathLangSemanticModel SemanticModel,
    List<PathLangDiagnostic> Diagnostics
);

public static class PathLangBinder
{
    public static PathLangSemanticResult Bind(Model model, IReadOnlyList<AstPredicate> predicates)
    {
        PathLangModelIndex index = PathLangModelIndex.Create(model);
        List<PathLangDiagnostic> diagnostics = new();

        //do we even need this if we already have the semanticModel?
        Dictionary<string, Guid?> predicateInputTypIdByNameD = new(StringComparer.Ordinal);
        Dictionary<string, Guid?>.AlternateLookup<ReadOnlySpan<char>> predicateInputTypIdByName = predicateInputTypIdByNameD.GetAlternateLookup<ReadOnlySpan<char>>();

        PathLangSemanticModel semantic = new();

        // Predeclare predicates for call resolution.
        foreach (var p in predicates)
        {
            var name = p.Name.Text.Span;

            predicateInputTypIdByName[name] = ResolveTypeName(p.InputType.Text);
        }

        foreach (var p in predicates)
        {
            var inputTypId = predicateInputTypIdByName[p.Name.Text.Span];
            if (inputTypId == null)
                Report(PathLangDiagnosticSeverity.Error, $"Unknown type '{p.InputType.Text}'", p.InputType.Text);

            semantic.InputTypIdByPredicate[p] = inputTypId;

            var rootTypes = new HashSet<Guid>();
            if (inputTypId.HasValue && inputTypId.Value != Guid.Empty)
                rootTypes.Add(inputTypId.Value);

            // At the start of evaluation, '$' refers to the same node as 'this'.
            BindExpr(p.Body, rootTypes, rootTypes);
        }

        return new PathLangSemanticResult(semantic, new List<PathLangDiagnostic>(diagnostics));

        Guid? ResolveTypeName(TextView typeName)
        {
            //todo make better, allow for allocation free search if we only expect a single result and a simple criterion
            var r = Searcher.Search<EntityDefinition>(model.DbSession, new StringCriterion
            {
                FieldId = EntityDefinition.Fields.Key,
                Type = StringCriterion.MatchType.Exact,
                Value = typeName.ToString()
            });

            if (r.Count != 1)
                return null;

            return r[0].ObjId;
        }

        void BindExpr(AstExpr expr, HashSet<Guid> thisTypes, HashSet<Guid> currentTypes)
        {
            switch (expr)
            {
                case AstLogicalExpr log:
                    BindExpr(log.Left, thisTypes, currentTypes);
                    BindExpr(log.Right, thisTypes, currentTypes);
                    return;

                case AstPredicateCallExpr call:
                {
                    var argTypes = BindNodeSetExpr(call.Argument, thisTypes, currentTypes);
                    var name = call.PredicateName.Text.ToString();
                    predicateInputTypIdByName.TryGetValue(name, out var targetInput);
                    if (!predicateInputTypIdByName.ContainsKey(name))
                        Report(PathLangDiagnosticSeverity.Error, $"Unknown predicate '{name}'", call.PredicateName.Text);

                    semantic.TargetInputTypIdByPredicateCall[call] = targetInput;
                    ValidatePredicateArgType(name, targetInput, argTypes, call.PredicateName.Text);
                    return;
                }

                case AstThisExpr or AstCurrentExpr or AstTraverseExpr or AstFilterExpr or AstRepeatExpr:
                    BindNodeSetExpr(expr, thisTypes, currentTypes);
                    return;

                case AstErrorExpr:
                    return;

                default:
                    Report(PathLangDiagnosticSeverity.Error, $"Unsupported expression node '{expr.GetType().Name}'", TextView.Empty(string.Empty));
                    return;
            }
        }

        HashSet<Guid> BindNodeSetExpr(AstExpr expr, HashSet<Guid> thisTypes, HashSet<Guid> currentTypes)
        {
            switch (expr)
            {
                case AstThisExpr t:
                {
                    var types = new HashSet<Guid>(thisTypes);
                    semantic.PossibleTypesByExpr[t] = types;
                    return types;
                }

                case AstCurrentExpr c:
                {
                    var types = new HashSet<Guid>(currentTypes);
                    semantic.PossibleTypesByExpr[c] = types;
                    return types;
                }

                case AstTraverseExpr tr:
                {
                    var srcTypes = BindNodeSetExpr(tr.Source, thisTypes, currentTypes);
                    var assocKey = tr.AssocName.Text.ToString();

                    var assocByType = new Dictionary<Guid, PathLangResolvedAssoc>();
                    var outTypes = new HashSet<Guid>();
                    foreach (var typ in srcTypes)
                    {
                        if (!index.TryResolveAssoc(typ, assocKey, out var assoc))
                        {
                            Report(PathLangDiagnosticSeverity.Error, $"Unknown association '{assocKey}' on type {FormatType(typ)}", tr.AssocName.Text);
                            continue;
                        }

                        assocByType[typ] = new PathLangResolvedAssoc(assoc.FldId, assoc.TargetTypId);
                        if (assoc.TargetTypId != Guid.Empty)
                            outTypes.Add(assoc.TargetTypId);
                    }

                    semantic.AssocByTraverse[tr] = assocByType;

                    if (tr.Filter is not null)
                        BindFilter(tr.Filter, thisTypes, outTypes);

                    semantic.PossibleTypesByExpr[tr] = outTypes;
                    return outTypes;
                }

                case AstFilterExpr fe:
                {
                    var srcTypes = BindNodeSetExpr(fe.Source, thisTypes, currentTypes);
                    BindFilter(fe.Filter, thisTypes, new HashSet<Guid>(srcTypes));
                    var outTypes = new HashSet<Guid>(srcTypes);
                    semantic.PossibleTypesByExpr[fe] = outTypes;
                    return outTypes;
                }

                case AstRepeatExpr re:
                {
                    ValidateRepeatShape(re);
                    var innerTypes = BindNodeSetExpr(re.Expr, thisTypes, currentTypes);

                    // Type-wise approximation: 0+ repetitions includes the starting nodes.
                    var types = new HashSet<Guid>(innerTypes);
                    foreach (var t in currentTypes)
                        types.Add(t);
                    foreach (var t in thisTypes)
                        types.Add(t);

                    semantic.PossibleTypesByExpr[re] = types;
                    return types;
                }

                case AstErrorExpr e:
                {
                    var types = new HashSet<Guid>();
                    semantic.PossibleTypesByExpr[e] = types;
                    return types;
                }

                default:
                    Report(PathLangDiagnosticSeverity.Error, $"Expected path expression, got '{expr.GetType().Name}'", TextView.Empty(string.Empty));
                    return new HashSet<Guid>();
            }
        }

        void BindFilter(AstFilter filter, HashSet<Guid> thisTypes, HashSet<Guid> currentTypes)
        {
            BindCondition(filter.Condition, thisTypes, currentTypes);
        }

        void BindCondition(AstCondition condition, HashSet<Guid> thisTypes, HashSet<Guid> currentTypes)
        {
            switch (condition)
            {
                case AstConditionBinary bin:
                    BindCondition(bin.Left, thisTypes, currentTypes);
                    BindCondition(bin.Right, thisTypes, currentTypes);
                    return;

                case AstFieldCompareCondition fc:
                {
                    Guid? guardTypId = null;
                    if (fc.TypeGuard is not null)
                    {
                        guardTypId = ResolveTypeName(fc.TypeGuard.Value.Text);
                        if (guardTypId == null)
                            Report(PathLangDiagnosticSeverity.Error, $"Unknown type '{fc.TypeGuard.Value.Text}'", fc.TypeGuard.Value.Text);
                    }

                    semantic.TypeGuardTypIdByCompare[fc] = guardTypId;

                    var fieldKey = fc.FieldName.Text.ToString();
                    IEnumerable<Guid> typesToCheck = guardTypId.HasValue ? new[] { guardTypId.Value } : currentTypes;

                    var fieldByTyp = new Dictionary<Guid, PathLangResolvedField>();
                    foreach (var typ in typesToCheck)
                    {
                        if (typ == Guid.Empty)
                            continue;

                        if (!index.TryResolveScalar(typ, fieldKey, out var fld))
                        {
                            Report(PathLangDiagnosticSeverity.Error, $"Unknown field '{fieldKey}' on type {FormatType(typ)}", fc.FieldName.Text);
                            continue;
                        }

                        fieldByTyp[typ] = new PathLangResolvedField(fld.FldId, fld.DataType);
                        ValidateLiteralType(fld.DataType, fc.Value, fc.FieldName.Text);
                    }

                    semantic.FieldByCompare[fc] = fieldByTyp;
                    return;
                }

                case AstPredicateCompareCondition pc:
                {
                    var name = pc.PredicateName.Text.ToString();
                    predicateInputTypIdByName.TryGetValue(name, out var targetInput);
                    if (!predicateInputTypIdByName.ContainsKey(name))
                        Report(PathLangDiagnosticSeverity.Error, $"Unknown predicate '{name}'", pc.PredicateName.Text);

                    semantic.TargetInputTypIdByPredicateCompare[pc] = targetInput;

                    var argTypes = BindNodeSetExpr(pc.Argument, thisTypes, currentTypes);
                    ValidatePredicateArgType(name, targetInput, argTypes, pc.PredicateName.Text);

                    if (pc.Value is not AstBoolLiteral)
                        Report(PathLangDiagnosticSeverity.Error, "Predicate comparisons must compare against a boolean literal", GetSpan(pc.Value));

                    return;
                }

                default:
                    return;
            }
        }

        void ValidateRepeatShape(AstRepeatExpr re)
        {
            // v1 restriction: repeat must contain a path expression whose last step yields a single association traversal.
            // Enforce: repeat(<single traversal>) where inner is exactly: (this|$) -> Ident [no filter]
            if (re.Expr is AstTraverseExpr tr && tr.Source is (AstThisExpr or AstCurrentExpr) && tr.Filter is null && tr.Source is not AstTraverseExpr)
                return;

            Report(PathLangDiagnosticSeverity.Error, "repeat(...) must contain a single traversal like repeat(this->Parent)", GetSpan(re.Expr));
        }

        void ValidatePredicateArgType(string predName, Guid? targetInputTypId, IReadOnlySet<Guid> argTypes, TextView at)
        {
            if (!targetInputTypId.HasValue || targetInputTypId.Value == Guid.Empty)
                return;
            if (argTypes.Count == 0)
                return;

            foreach (var t in argTypes)
            {
                if (t == targetInputTypId.Value)
                    return;
            }

            Report(PathLangDiagnosticSeverity.Warning, $"Predicate '{predName}' expects {FormatType(targetInputTypId.Value)} but argument may be {FormatTypes(argTypes)}", at);
        }

        void ValidateLiteralType(string dataType, AstLiteral lit, TextView at)
        {
            if (lit is AstErrorLiteral)
                return;

            var lower = dataType.Trim().ToLowerInvariant();
            bool ok = lower switch
            {
                "bool" or "boolean" => lit is AstBoolLiteral,
                "string" => lit is AstStringLiteral,
                "int" or "int32" or "int64" or "long" or "double" or "float" or "decimal" or "number" => lit is AstNumberLiteral,
                "datetime" or "date" => lit is AstStringLiteral,
                _ => true,
            };

            if (!ok)
                Report(PathLangDiagnosticSeverity.Error, $"Literal type does not match field type '{dataType}'", at);
        }

        void Report(PathLangDiagnosticSeverity severity, string message, TextView span)
        {
            var (line, col) = ComputeLineCol(span);
            diagnostics.Add(new PathLangDiagnostic(severity, message, line, col, span));
        }

        (int line, int col) ComputeLineCol(TextView span)
        {
            if (span.Source is null)
                return (0, 0);

            int line = 1;
            int col = 1;
            int max = Math.Clamp(span.Start, 0, span.Source.Length);
            for (int i = 0; i < max; i++)
            {
                if (span.Source[i] == '\n')
                {
                    line++;
                    col = 1;
                }
                else
                {
                    col++;
                }
            }

            return (line, col);
        }

        TextView GetSpan(AstNode node)
        {
            return node switch
            {
                AstPredicate p => p.Name.Text,
                AstTraverseExpr t => t.AssocName.Text,
                AstPredicateCallExpr c => c.PredicateName.Text,
                AstLogicalExpr l => GetSpan(l.Left),
                AstFilterExpr f => GetSpan(f.Source),
                AstRepeatExpr r => GetSpan(r.Expr),
                AstFilter flt => GetSpan(flt.Condition),
                AstConditionBinary b => GetSpan(b.Left),
                AstFieldCompareCondition fc => fc.FieldName.Text,
                AstPredicateCompareCondition pc => pc.PredicateName.Text,
                AstStringLiteral s => s.Raw,
                AstNumberLiteral n => n.Raw,
                _ => TextView.Empty(string.Empty),
            };
        }

        string FormatType(Guid typId)
        {
            return index.TryGetEntityByTypId(typId, out var info) ? $"'{info.Key}'" : $"'{typId}'";
        }

        string FormatTypes(IReadOnlySet<Guid> typIds)
        {
            if (typIds.Count == 0)
                return "<unknown>";
            return string.Join(" | ", typIds.Select(FormatType));
        }
    }
}
