using System.Diagnostics;
using FDMF.Core.Database;

namespace FDMF.Core.PathLayer;

public static class PathEvaluation
{
    public static bool Evaluate(DbSession session, Guid objId, AstPredicate predicate, PathLangSemanticModel semanticModel)
    {
        var type = semanticModel.InputTypIdByPredicate[predicate]; //todo error handling

        if (session.GetTypId(objId) != type) //todo inheritance
        {
            throw new Exception("error"); //todo error handling
        }
        //
        // if (predicate.Body is AstPathExpr astPathExpr)
        // {
        //     Debug.Assert(astPathExpr.Source is AstThisExpr);
        //
        //     var steps = astPathExpr.Steps.ToArray();
        //
        //     session.EnumerateAso(objId, semanticModel.AssocByPathStep[steps[0]])
        // }

        return false;
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
