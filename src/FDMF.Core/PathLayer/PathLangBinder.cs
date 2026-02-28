using FDMF.Core.DatabaseLayer;

namespace FDMF.Core.PathLayer;

public sealed record PathLangSemanticResult(
    PathLangSemanticModel SemanticModel,
    List<PathLangDiagnostic> Diagnostics
);

public static class PathLangBinder
{
    public static Guid CurrentUserFieldGuid = Guid.Parse("7E5BA146-B27E-4F12-8D1B-511BB840EB8D");
    public static Guid CanViewFieldGuid = Guid.Parse("7E5BA146-B27E-4F12-8D1B-511BB840EB8D");
    public static Guid CanEditFieldGuid = Guid.Parse("7E5BA146-B27E-4F12-8D1B-511BB840EB8D");

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

            //in case there are multiple
            if (r.Count > 1)
            {
                Report(PathLangDiagnosticSeverity.Error, $"There are multiple types with the key '{typeName.Span}': {string.Join(", ", r.Select(x => x.Id))}", typeName);
                return null;
            }

            if (r.Count == 0)
            {
                Report(PathLangDiagnosticSeverity.Error, $"Unknown type '{typeName}'", typeName);
                return null;
            }

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
                    var targetInput = ResolvePredicate(call.PredicateName.Text);

                    semantic.TargetInputTypIdByPredicateCall[call] = targetInput;
                    ValidatePredicateArgType(call.PredicateName.Text, targetInput, argTypes);
                    return;
                }

                case AstThisExpr or AstCurrentExpr or AstPathExpr or AstFilterExpr:
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

                    BindSteps(path.Steps, ref type, thisType);

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

        void BindSteps(IReadOnlyList<AstPathStep> steps, ref Guid type, Guid thisType)
        {
            foreach (var step in steps)
            {
                if (step is AstAsoStep asoStep)
                {
                    var assocKey = asoStep.AssocName.Text.ToString();

                    if (GetAso(type, assocKey) is not {} assoc)
                    {
                        Report(PathLangDiagnosticSeverity.Error, $"Unknown association '{assocKey}' on type {FormatType(type)}", asoStep.AssocName.Text);
                        break;
                    }

                    semantic.AssocByPathStep[step] = assoc.ObjId;
                    type = assoc.OtherReferenceFields.OwningEntity.ObjId;

                    if (step.Filter is not null)
                        type = BindFilter(step.Filter, thisType, type);

                    semantic.PossibleTypesByExpr[step] = type;
                }
                else if (step is AstRepeatStep repeatStep)
                {
                    var startType = type;
                    BindSteps(repeatStep.Steps, ref type, thisType);
                    if (startType != type)
                    {
                        Report(PathLangDiagnosticSeverity.Error, $"Start and end of steps in repeat expr need to have the same type", repeatStep.Range);
                    }

                    if (step.Filter is not null)
                        type = BindFilter(step.Filter, thisType, type);

                    semantic.PossibleTypesByExpr[step] = type;
                }
            }
        }

        Guid BindFilter(AstFilter filter, Guid thisTypes, Guid currentTypes)
        {
            var narrowed = ComputeNarrowedType(filter.Condition, currentTypes);
            BindCondition(filter.Condition, thisTypes, narrowed);
            return narrowed;
        }

        void BindCondition(AstCondition condition, Guid thisTypes, Guid currentType)
        {
            switch (condition)
            {
                case AstConditionBinary bin:
                    BindCondition(bin.Left, thisTypes, currentType);
                    BindCondition(bin.Right, thisTypes, currentType);
                    return;

                case AstTypeTestCondition tt:
                {
                    var guardTypId = ResolveTypeName(tt.TypeName.Text);
                    semantic.TypeTestTypIdByCondition[tt] = guardTypId;
                    return;
                }

                case AstFieldCompareCondition fc:
                {
                    Guid? guardTypId = null;
                    if (fc.TypeGuard is not null)
                    {
                        guardTypId = ResolveTypeName(fc.TypeGuard.Value.Text);
                    }

                    semantic.TypeGuardTypIdByCompare[fc] = guardTypId;

                    var fieldKey = fc.FieldName.Text;
                    Guid type = guardTypId ?? currentType;

                    if (type != Guid.Empty)
                    {
                        if (GetFld(type, fieldKey.Span) is not {} fld)
                        {
                            if (currentType == User.TypId && fieldKey.Span is "CurrentUser") //special case
                            {
                                ValidateLiteralType(FieldDataType.Boolean, fc.Value, fc.FieldName.Text);
                                semantic.FieldByCompare[fc] = CurrentUserFieldGuid;
                            }
                            else if (fieldKey.Span is "CanView")
                            {
                                ValidateLiteralType(FieldDataType.Boolean, fc.Value, fc.FieldName.Text);
                                semantic.FieldByCompare[fc] = CanViewFieldGuid;
                            }
                            else if (fieldKey.Span is "CanEdit")
                            {
                                ValidateLiteralType(FieldDataType.Boolean, fc.Value, fc.FieldName.Text);
                                semantic.FieldByCompare[fc] = CanEditFieldGuid;
                            }
                            else
                            {
                                Report(PathLangDiagnosticSeverity.Error, $"Unknown field '{fieldKey}' on type {FormatType(type)}", fc.FieldName.Text);
                            }
                        }
                        else
                        {
                            ValidateLiteralType(fld.DataType, fc.Value, fc.FieldName.Text);

                            semantic.FieldByCompare[fc] = fld.ObjId;
                        }
                    }

                    return;
                }

                case AstPredicateCompareCondition pc:
                {
                    var targetInput = ResolvePredicate(pc.PredicateName.Text);

                    semantic.TargetInputTypIdByPredicateCompare[pc] = targetInput;

                    var argTypes = BindNodeSetExpr(pc.Argument, thisTypes, currentType);
                    ValidatePredicateArgType(pc.PredicateName.Text, targetInput, argTypes);

                    if (pc.Value is not AstBoolLiteral)
                        Report(PathLangDiagnosticSeverity.Error, "Predicate comparisons must compare against a boolean literal", pc.Value.Range);

                    return;
                }

                default:
                    return;
            }
        }

        Guid? ResolvePredicate(TextView name)
        {
            predicateInputTypIdByName.TryGetValue(name.Span, out var targetInput);
            if (!predicateInputTypIdByName.ContainsKey(name.Span))
            {
                Report(PathLangDiagnosticSeverity.Error, $"Unknown predicate '{name}'", name);
                return null;
            }

            return targetInput;
        }

        ReferenceFieldDefinition? GetAso(Guid ed, ReadOnlySpan<char> key)
        {
            if (dbSession.GetObjFromGuid<EntityDefinition>(ed) is not {} entityDefinition)
                return null;

            var seen = new HashSet<Guid>();
            while (true)
            {
                if (!seen.Add(entityDefinition.ObjId))
                    return null;

                foreach (var fld in entityDefinition.ReferenceFieldDefinitions)
                {
                    if (fld.Key.AsSpan().SequenceEqual(key))
                        return fld;
                }

                if (!entityDefinition.Parent.HasValue)
                    return null;

                entityDefinition = entityDefinition.Parent.Value;
            }
        }

        FieldDefinition? GetFld(Guid ed, ReadOnlySpan<char> key)
        {
            if (dbSession.GetObjFromGuid<EntityDefinition>(ed) is not {} entityDefinition)
                return null;

            var seen = new HashSet<Guid>();
            while (true)
            {
                if (!seen.Add(entityDefinition.ObjId))
                    return null;

                foreach (var fld in entityDefinition.FieldDefinitions)
                {
                    if (fld.Key.AsSpan().SequenceEqual(key))
                        return fld;
                }

                if (!entityDefinition.Parent.HasValue)
                    return null;

                entityDefinition = entityDefinition.Parent.Value;
            }
        }

        Guid ComputeNarrowedType(AstCondition condition, Guid currentType)
        {
            if (currentType == Guid.Empty)
                return currentType;

            if (ContainsOr(condition))
                return currentType;

            var narrowed = currentType;

            foreach (var guard in EnumerateGuards(condition))
            {
                var guardTypId = ResolveTypeName(guard);
                if (!guardTypId.HasValue || guardTypId.Value == Guid.Empty)
                    continue;

                // guardTypId must be compatible with current type.
                if (FDMF.Core.GeneratedCodeHelper.IsAssignableFrom(dbSession, narrowed, guardTypId.Value))
                {
                    // guard is more derived (or same)
                    narrowed = guardTypId.Value;
                }
                else if (FDMF.Core.GeneratedCodeHelper.IsAssignableFrom(dbSession, guardTypId.Value, narrowed))
                {
                    // guard is a base type (no narrowing)
                }
                else
                {
                    Report(PathLangDiagnosticSeverity.Error, $"Type guard '{guard.Span}' is not compatible with current type {FormatType(narrowed)}", guard);
                }
            }

            return narrowed;

            bool ContainsOr(AstCondition c)
            {
                return c switch
                {
                    AstConditionBinary b when b.Op == AstConditionOp.Or => true,
                    AstConditionBinary b => ContainsOr(b.Left) || ContainsOr(b.Right),
                    _ => false
                };
            }

            IEnumerable<TextView> EnumerateGuards(AstCondition c)
            {
                switch (c)
                {
                    case AstConditionBinary b:
                        foreach (var x in EnumerateGuards(b.Left))
                            yield return x;
                        foreach (var x in EnumerateGuards(b.Right))
                            yield return x;
                        yield break;
                    case AstTypeTestCondition tt:
                        yield return tt.TypeName.Text;
                        yield break;
                    case AstFieldCompareCondition fc when fc.TypeGuard is not null:
                        yield return fc.TypeGuard.Value.Text;
                        yield break;
                    default:
                        yield break;
                }
            }
        }

        void ValidatePredicateArgType(TextView predName, Guid? targetInputTypId, Guid argTypes)
        {
            if (!targetInputTypId.HasValue || targetInputTypId.Value == Guid.Empty)
                return;

            if (argTypes == targetInputTypId.Value)
                return;

            Report(PathLangDiagnosticSeverity.Warning, $"Predicate '{predName}' expects {FormatType(targetInputTypId.Value)} but argument may be {FormatType(argTypes)}", predName);
        }

        void ValidateLiteralType(FieldDataType dataType, AstLiteral lit, TextView at)
        {
            if (lit is AstErrorLiteral)
                return;

            bool ok = dataType switch
            {
                FieldDataType.Boolean => lit is AstBoolLiteral,
                FieldDataType.String => lit is AstStringLiteral,
                FieldDataType.Integer or FieldDataType.Decimal => lit is AstNumberLiteral,
                FieldDataType.DateTime => lit is AstStringLiteral,
                FieldDataType.Guid => throw new NotImplementedException(),
                FieldDataType.Enum => throw new NotImplementedException(),
                _ => throw new NotImplementedException(),
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
