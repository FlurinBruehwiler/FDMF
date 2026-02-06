using System.Diagnostics;
using System.Runtime.InteropServices;
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

        // For now, '$' at top-level is treated as 'this'.
        return EvalBool(predicate.Body, thisObj, thisObj);

        bool EvalBool(AstExpr expr, Guid thisId, Guid currentId)
        {
            switch (expr)
            {
                case AstLogicalExpr log:
                    return log.Op == AstLogicalOp.And
                        ? EvalBool(log.Left, thisId, currentId) && EvalBool(log.Right, thisId, currentId)
                        : EvalBool(log.Left, thisId, currentId) || EvalBool(log.Right, thisId, currentId);

                case AstPredicateCallExpr:
                    throw new NotImplementedException("Predicate calls are not implemented in PathEvaluation");

                case AstErrorExpr:
                    return false;

                default:
                    // Node-set expressions succeed if they yield at least one node.
                    return VisitNodes(expr, thisId, currentId, _ => true);
            }
        }

        bool VisitNodes(AstExpr expr, Guid thisId, Guid currentId, Func<Guid, bool> visitor)
        {
            switch (expr)
            {
                case AstThisExpr:
                    return visitor(thisId);

                case AstCurrentExpr:
                    return visitor(currentId);

                case AstErrorExpr:
                    return false;

                case AstLogicalExpr:
                    // Logical expressions are boolean-only.
                    return EvalBool(expr, thisId, currentId) && visitor(thisId); // never reached; keeps compiler happy

                case AstPredicateCallExpr:
                    throw new NotImplementedException("Predicate calls are not implemented in PathEvaluation");

                case AstFilterExpr fe:
                    return VisitNodes(fe.Source, thisId, currentId, node =>
                    {
                        if (!CheckCondition(fe.Filter.Condition, node))
                            return false;
                        return visitor(node);
                    });

                case AstPathExpr path:
                    return VisitNodes(path.Source, thisId, currentId, srcNode => VisitPathSteps(srcNode, path.Steps, 0, thisId, currentId, visitor));

                case AstRepeatExpr re:
                    return VisitRepeat(re, thisId, currentId, visitor);

                default:
                    throw new ArgumentOutOfRangeException(nameof(expr));
            }
        }

        bool VisitPathSteps(Guid start, IReadOnlyList<AstPathStep> steps, int stepIndex, Guid thisId, Guid currentId, Func<Guid, bool> visitor)
        {
            if (stepIndex >= steps.Count)
                return visitor(start);

            var step = steps[stepIndex];
            var assocId = semanticModel.AssocByPathStep[step];

            foreach (var asoObj in session.EnumerateAso(start, assocId))
            {
                var nextId = asoObj.ObjId;

                if (step.Filter is not null && !CheckCondition(step.Filter.Condition, nextId))
                    continue;

                if (VisitPathSteps(nextId, steps, stepIndex + 1, thisId, currentId, visitor))
                    return true;
            }

            return false;
        }

        bool VisitRepeat(AstRepeatExpr re, Guid thisId, Guid currentId, Func<Guid, bool> visitor)
        {
            // v1 restriction (also enforced by binder): repeat must be a single traversal like repeat(this->Parent)
            if (re.Expr is not AstPathExpr p || p.Steps.Count != 1 || p.Steps[0].Filter is not null)
                throw new NotSupportedException("repeat(...) must contain a single traversal like repeat(this->Parent)");

            var step = p.Steps[0];
            var assocId = semanticModel.AssocByPathStep[step];

            // Repeat over each start node produced by the inner source (normally just 'this' or '$').
            return VisitNodes(p.Source, thisId, currentId, startNode =>
            {
                var visited = new HashSet<Guid>();
                var stack = new Stack<Guid>();

                if (visited.Add(startNode))
                    stack.Push(startNode);

                while (stack.Count > 0)
                {
                    var node = stack.Pop();

                    // zero-or-more: include the start node itself
                    if (visitor(node))
                        return true;

                    foreach (var asoObj in session.EnumerateAso(node, assocId))
                    {
                        var next = asoObj.ObjId;
                        if (visited.Add(next))
                            stack.Push(next);
                    }
                }

                return false;
            });
        }

        bool CheckCondition(AstCondition condition, Guid objId)
        {
            switch (condition)
            {
                case AstErrorCondition:
                    return false;

                case AstConditionBinary bin:
                    return bin.Op == AstConditionOp.And
                        ? CheckCondition(bin.Left, objId) && CheckCondition(bin.Right, objId)
                        : CheckCondition(bin.Left, objId) || CheckCondition(bin.Right, objId);

                case AstFieldCompareCondition fc:
                {
                    if (fc.TypeGuard is not null)
                    {
                        if (!semanticModel.TypeGuardTypIdByCompare.TryGetValue(fc, out var guardTypId) || guardTypId is null)
                            return false;
                        if (session.GetTypId(objId) != guardTypId.Value)
                            return false;
                    }

                    if (!semanticModel.FieldByCompare.TryGetValue(fc, out var fieldId))
                        return false;

                    var actualValue = session.GetFldValue(objId, fieldId);
                    if (actualValue.Length == 0)
                        return false;

                    bool eq;
                    switch (fc.Value)
                    {
                        case AstBoolLiteral b:
                            eq = actualValue.Length >= 1 && MemoryMarshal.Read<bool>(actualValue) == b.Value;
                            break;
                        case AstStringLiteral s:
                            eq = actualValue.SequenceEqual(MemoryMarshal.AsBytes(s.Raw.Span));
                            break;
                        case AstNumberLiteral:
                            // TODO: parse and compare numbers; not needed for current tests.
                            return false;
                        default:
                            return false;
                    }

                    return fc.Op == AstCompareOp.Equals ? eq : !eq;
                }

                case AstPredicateCompareCondition:
                    throw new NotImplementedException("Predicate comparisons are not implemented in PathEvaluation");

                default:
                    throw new ArgumentOutOfRangeException(nameof(condition));
            }
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
