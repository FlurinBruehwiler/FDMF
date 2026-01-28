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

        return false;
    }
}