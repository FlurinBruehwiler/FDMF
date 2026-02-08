using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BaseModel.Generated;
using FDMF.Core.Database;

namespace FDMF.Core.PathLayer;

public static class PathEvaluation
{
    public static bool Evaluate(DbSession session, Guid thisObj, AstPredicate predicate, PathLangSemanticModel semanticModel)
    {
        var type = semanticModel.InputTypIdByPredicate[predicate]; //todo error handling

        if (session.GetTypId(thisObj) != type) //todo inheritance
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
                //we do the repeat n times until the repeat doesn't match anymore
                //after each step, we evaluate what comes after the repeat
                while (true)
                {
                    //check if the repeat step matches
                    if (EvalSteps(repeatStep.Steps, obj, out obj))
                    {
                        //check exit condition (can only exit the loop if the exit condition matches)
                        if (repeatStep.Filter != null && !CheckCondition(repeatStep.Filter.Condition, obj, default)) //todo what entity?
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
                    var fld = type.FieldDefinitions.First(x => astFieldCompareCondition.FieldName.Text.Span.SequenceEqual(x.Key));
                    var actualValue = session.GetFldValue(obj, fld.ObjId);

                    bool r = true;
                    switch (astFieldCompareCondition.Value)
                    {
                        case AstBoolLiteral astBoolLiteral:
                            r = MemoryMarshal.Read<bool>(actualValue) == astBoolLiteral.Value;
                            break;
                        case AstNumberLiteral astNumberLiteral:
                            break;
                        case AstStringLiteral astStringLiteral:
                            r = Encoding.Unicode.GetString(actualValue).SequenceEqual(astStringLiteral.Raw.Span);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    return astFieldCompareCondition.Op == AstCompareOp.Equals ? r : !r;
                case AstPredicateCompareCondition astPredicateCompareCondition:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(condition));
            }

            return true;
        }
    }



    /* is it depth-first or breadth-first? I think depth-first makes sense
     *
     * pseudo code:
     *
     * obj->AssocA->AssocB->AssocC
     *
     * foreach(var objA in obj.AssocA){
     *   foreach(var objB in objA.AssocB){
     *      foreach(var objC in objB.AssocC){
     *
     *      }
     *   }
     * }
     */
}
