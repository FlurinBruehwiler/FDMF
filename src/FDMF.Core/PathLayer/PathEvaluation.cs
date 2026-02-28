using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FDMF.Core.DatabaseLayer;

namespace FDMF.Core.PathLayer;

//todo, the current implementation is just a proof of concept, it is *very* inefficient...
public static class PathEvaluation
{
    public static bool Evaluate(DbSession session, Guid thisObj, AstPredicate predicate, PathLangSemanticModel semanticModel, Guid currentUser)
    {
        var type = semanticModel.InputTypIdByPredicate[predicate]; //todo error handling

        if (!GeneratedCodeHelper.IsAssignableFrom(session, type ?? Guid.Empty, session.GetTypId(thisObj)))
        {
            throw new Exception("error"); //todo error handling
        }

        if (predicate.Body is AstPathExpr astPathExpr)
        {
            Debug.Assert(astPathExpr.Source is AstThisExpr);

            var steps = astPathExpr.Steps.ToArray();

            return EvalSteps(steps, thisObj, out _);
        }

        return false;

        bool EvalSteps(Span<AstPathStep> steps, Guid obj, out Guid targetObj)
        {
            if (steps.Length == 0)
            {
                targetObj = obj;
                return true;
            }

            var thisStep = steps[0];

            if (thisStep is AstAsoStep asoStep)
            {
                var otherType = session.GetObjFromGuid<EntityDefinition>(semanticModel.PossibleTypesByExpr[asoStep])!.Value;

                //depth first
                foreach (var asoObj in session.EnumerateAso(obj, semanticModel.AssocByPathStep[asoStep]))
                {
                    //check condition
                    if (asoStep.Filter != null)
                    {
                        if(!CheckCondition(asoStep.Filter.Condition, asoObj.ObjId, otherType))
                            continue;
                    }

                    if (EvalSteps(steps.Slice(1), asoObj.ObjId, out targetObj))
                    {
                        return true;
                    }
                }
            }
            else if (thisStep is AstRepeatStep repeatStep)
            {
                var otherType = session.GetObjFromGuid<EntityDefinition>(semanticModel.PossibleTypesByExpr[repeatStep.Steps.Last()])!.Value;

                //we do the repeat n times until the repeat doesn't match anymore
                //after each step, we evaluate what comes after the repeat
                while (true)
                {
                    //check if the repeat step matches
                    if (EvalSteps(repeatStep.Steps, obj, out obj))
                    {
                        //check exit condition (can only exit the loop if the exit condition matches)
                        if (repeatStep.Filter != null && !CheckCondition(repeatStep.Filter.Condition, obj, otherType))
                        {
                            continue;
                        }

                        //try to exit the loop
                        if (EvalSteps(steps.Slice(1), obj, out targetObj))
                        {
                            return true;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }

            targetObj = Guid.Empty;
            return false;
        }

        bool CheckCondition(AstCondition condition, Guid obj, EntityDefinition type)
        {
            switch (condition)
            {
                case AstConditionBinary astConditionBinary:
                    if (astConditionBinary.Op == AstConditionOp.And)
                        return CheckCondition(astConditionBinary.Left, obj, type) && CheckCondition(astConditionBinary.Right, obj, type);
                    if (astConditionBinary.Op == AstConditionOp.Or)
                        return CheckCondition(astConditionBinary.Left, obj, type) || CheckCondition(astConditionBinary.Right, obj, type);
                    break;
                case AstFieldCompareCondition astFieldCompareCondition:
                    if (semanticModel.TypeGuardTypIdByCompare.TryGetValue(astFieldCompareCondition, out var tg) && tg.HasValue)
                    {
                        if (!GeneratedCodeHelper.IsAssignableFrom(session, tg.Value, session.GetTypId(obj)))
                            return false;
                    }

                    var fld = semanticModel.FieldByCompare[astFieldCompareCondition];

                    ReadOnlySpan<byte> actualValue;

                    bool r = true;

                    if (fld == PathLangBinder.CurrentUserFieldGuid)
                    {
                        r = obj == currentUser;
                    }
                    else if (fld == PathLangBinder.CanViewFieldGuid)
                    {
                        //todo
                    }
                    else if (fld == PathLangBinder.CanEditFieldGuid)
                    {
                        //todo
                    }
                    else
                    {
                        actualValue = session.GetFldValue(obj, fld);

                        switch (astFieldCompareCondition.Value)
                        {
                            case AstBoolLiteral astBoolLiteral:
                                r = MemoryMarshal.Read<bool>(actualValue) == astBoolLiteral.Value;
                                break;
                            case AstNumberLiteral astNumberLiteral:
                                //todo
                                break;
                            case AstStringLiteral astStringLiteral:
                                r = Encoding.Unicode.GetString(actualValue).AsSpan().SequenceEqual(astStringLiteral.Raw.Span);
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }

                    return astFieldCompareCondition.Op == AstCompareOp.Equals ? r : !r;

                case AstTypeTestCondition tt:
                    if (!semanticModel.TypeTestTypIdByCondition.TryGetValue(tt, out var guardTypId) || !guardTypId.HasValue)
                        return false;

                    return GeneratedCodeHelper.IsAssignableFrom(session, guardTypId.Value, session.GetTypId(obj));
                case AstPredicateCompareCondition astPredicateCompareCondition:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(condition));
            }

            return true;
        }
    }
}
