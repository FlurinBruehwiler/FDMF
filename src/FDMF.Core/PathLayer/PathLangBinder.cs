using BaseModel.Generated;
using FDMF.Core.Database;

namespace FDMF.Core.PathLayer;

public sealed record PathLangSemanticResult(
    PathLangSemanticModel SemanticModel,
    List<PathLangDiagnostic> Diagnostics
);

public static class PathLangBinder
{
    public static PathLangSemanticResult Bind(Model model, DbSession dbSession, IReadOnlyList<AstPredicate> predicates)
    {
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

            inputTypId ??= Guid.Empty;

            //todo $ doesn't really exist yet, maybe we should unify both concepts
            BindExpr(p.Body, inputTypId.Value, inputTypId.Value);
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

        void BindExpr(AstExpr expr, Guid thisTypes, Guid currentTypes)
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

                case AstThisExpr or AstCurrentExpr or AstPathExpr or AstFilterExpr or AstRepeatExpr:
                    BindNodeSetExpr(expr, thisTypes, currentTypes);
                    return;

                case AstErrorExpr:
                    return;

                default:
                    Report(PathLangDiagnosticSeverity.Error, $"Unsupported expression node '{expr.GetType().Name}'", TextView.Empty(string.Empty));
                    return;
            }
        }

        Guid BindNodeSetExpr(AstExpr expr, Guid thisType, Guid currentType)
        {
            switch (expr)
            {
                case AstThisExpr t:
                {
                    semantic.PossibleTypesByExpr[t] = thisType;
                    return thisType;
                }

                case AstCurrentExpr c:
                {
                    semantic.PossibleTypesByExpr[c] = currentType;
                    return currentType;
                }

                case AstPathExpr path:
                {
                    var type = BindNodeSetExpr(path.Source, thisType, currentType);

                    foreach (var step in path.Steps)
                    {
                        var assocKey = step.AssocName.Text.ToString();

                        if (GetAso(type, assocKey) is not {} assoc)
                        {
                            Report(PathLangDiagnosticSeverity.Error, $"Unknown association '{assocKey}' on type {FormatType(type)}", step.AssocName.Text);
                            break;
                        }

                        type = assoc.OtherReferenceFields.OwningEntity.ObjId;

                        if (step.Filter is not null)
                            BindFilter(step.Filter, thisType, type);
                    }

                    semantic.PossibleTypesByExpr[path] = type;
                    return type;
                }

                case AstFilterExpr fe:
                {
                    var srcTypes = BindNodeSetExpr(fe.Source, thisType, currentType);
                    BindFilter(fe.Filter, thisType, srcTypes);

                    semantic.PossibleTypesByExpr[fe] = srcTypes;
                    return srcTypes;
                }

                case AstRepeatExpr re:
                {
                    ValidateRepeatShape(re);
                    var innerTypes = BindNodeSetExpr(re.Expr, thisType, currentType);

                    semantic.PossibleTypesByExpr[re] = innerTypes;
                    return innerTypes;
                }

                case AstErrorExpr e:
                {
                    semantic.PossibleTypesByExpr[e] = Guid.Empty;
                    return Guid.Empty;
                }

                default:
                    Report(PathLangDiagnosticSeverity.Error, $"Expected path expression, got '{expr.GetType().Name}'", TextView.Empty(string.Empty));
                    return Guid.Empty;
            }
        }

        void BindFilter(AstFilter filter, Guid thisTypes, Guid currentTypes)
        {
            BindCondition(filter.Condition, thisTypes, currentTypes);
        }

        void BindCondition(AstCondition condition, Guid thisTypes, Guid currentTypes)
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
                    Guid typesToCheck = guardTypId ?? currentTypes;

                    if (typesToCheck != Guid.Empty)
                    {
                        if (GetFld(typesToCheck, fieldKey) is not {} fld)
                        {
                            Report(PathLangDiagnosticSeverity.Error, $"Unknown field '{fieldKey}' on type {FormatType(typesToCheck)}", fc.FieldName.Text);
                        }
                        else
                        {
                            ValidateLiteralType(fld.DataType, fc.Value, fc.FieldName.Text);

                            semantic.FieldByCompare[fc] = new PathLangResolvedField(fld.ObjId, fld.DataType);;
                        }
                    }

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

        ReferenceFieldDefinition? GetAso(Guid ed, ReadOnlySpan<char> key)
        {
            if (dbSession.GetObjFromGuid<EntityDefinition>(ed) is {} entityDefinition)
            {
                foreach (var fld in entityDefinition.ReferenceFieldDefinitions)
                {
                    if (fld.Key.AsSpan().SequenceEqual(key))
                    {
                        return fld;
                    }
                }
            }

            return null;
        }

        FieldDefinition? GetFld(Guid ed, ReadOnlySpan<char> key)
        {
            if (dbSession.GetObjFromGuid<EntityDefinition>(ed) is {} entityDefinition)
            {
                foreach (var fld in entityDefinition.FieldDefinitions)
                {
                    if (fld.Key.AsSpan().SequenceEqual(key))
                    {
                        return fld;
                    }
                }
            }

            return null;
        }

        void ValidateRepeatShape(AstRepeatExpr re)
        {
            // v1 restriction: repeat must contain a path expression whose last step yields a single association traversal.
            // Enforce: repeat(<single traversal>) where inner is exactly: (this|$) -> Ident [no filter]
            if (re.Expr is AstPathExpr p && p.Source is (AstThisExpr or AstCurrentExpr) && p.Steps.Count == 1 && p.Steps[0].Filter is null)
                return;

            Report(PathLangDiagnosticSeverity.Error, "repeat(...) must contain a single traversal like repeat(this->Parent)", GetSpan(re.Expr));
        }

        void ValidatePredicateArgType(string predName, Guid? targetInputTypId, Guid argTypes, TextView at)
        {
            if (!targetInputTypId.HasValue || targetInputTypId.Value == Guid.Empty)
                return;

            if (argTypes == targetInputTypId.Value)
                return;

            Report(PathLangDiagnosticSeverity.Warning, $"Predicate '{predName}' expects {FormatType(targetInputTypId.Value)} but argument may be {FormatType(argTypes)}", at);
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
                AstPathExpr p when p.Steps.Count > 0 => p.Steps[0].AssocName.Text,
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
            if (typId == Guid.Empty)
                return "<unknown>";

            if (dbSession.GetObjFromGuid<EntityDefinition>(typId) is {} entityDefinition)
            {
                return $"'{entityDefinition.Key}'";
            }

            return $"'{typId}'";
        }
    }
}
