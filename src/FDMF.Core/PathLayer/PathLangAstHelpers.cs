// using System.Collections.Generic;
//
// namespace FDMF.Core.PathLayer;
//
// public static class PathLangAstHelpers
// {
//     public static IEnumerable<AstPathStep> EnumeratePathSteps(AstExpr expr)
//     {
//         switch (expr)
//         {
//             case AstPathExpr p:
//                 for (int i = 0; i < p.Steps.Count; i++)
//                     yield return p.Steps[i];
//                 foreach (var s in EnumeratePathSteps(p.Source))
//                     yield return s;
//                 yield break;
//
//             case AstFilterExpr f:
//                 foreach (var s in EnumeratePathSteps(f.Source))
//                     yield return s;
//                 yield break;
//
//             case AstRepeatExpr r:
//                 foreach (var s in EnumeratePathSteps(r.Expr))
//                     yield return s;
//                 yield break;
//
//             case AstLogicalExpr l:
//                 foreach (var s in EnumeratePathSteps(l.Left))
//                     yield return s;
//                 foreach (var s in EnumeratePathSteps(l.Right))
//                     yield return s;
//                 yield break;
//
//             case AstPredicateCallExpr c:
//                 foreach (var s in EnumeratePathSteps(c.Argument))
//                     yield return s;
//                 yield break;
//
//             default:
//                 yield break;
//         }
//     }
// }
