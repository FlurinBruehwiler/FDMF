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

            return EvalSteps(steps, thisObj);
        }

        return false;

        bool EvalSteps(Span<AstPathStep> steps, Guid obj)
        {
            if (steps.Length == 0)
                return true;

            var thisStep = steps[0];

            var otherType = session.GetObjFromGuid<EntityDefinition>(semanticModel.PossibleTypesByExpr[thisStep])!.Value;
            foreach (var asoObj in session.EnumerateAso(obj, semanticModel.AssocByPathStep[thisStep]))
            {
                //check condition
                if (thisStep.Filter != null)
                {
                    if(!CheckCondition(thisStep.Filter.Condition, asoObj.ObjId, otherType))
                        continue;
                }

                if (EvalSteps(steps.Slice(1), asoObj.ObjId))
                {
                    return true;
                }
            }

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
