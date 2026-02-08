// using BaseModel.Generated;
// using FDMF.Core.Database;
// using FDMF.Core.PathLayer;
// using TestModel.Generated;
// using Environment = FDMF.Core.Environment;
//
// namespace FDMF.Tests;
//
// [Collection(DatabaseCollection.DatabaseCollectionName)]
// public sealed class PathLangBinderTests
// {
//     [Fact]
//     public void Bind_Resolves_Field_And_Association_Ids_From_Model()
//     {
//         using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());
//         using var session = new DbSession(env, readOnly: true);
//
//         var model = session.GetObjFromGuid<Model>(env.ModelGuid);
//         Assert.NotNull(model);
//
//         var src = "P(TestingFolder): this->Parent[$.Name=\"Parent\"]";
//         var parse = PathLangParser.Parse(src);
//         Assert.DoesNotContain(parse.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
//
//         var bind = PathLangBinder.Bind(model.Value, session, parse.Predicates);
//         Assert.DoesNotContain(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
//
//         var pred = Assert.Single(parse.Predicates);
//         Assert.Equal(TestingFolder.TypId, bind.SemanticModel.InputTypIdByPredicate[pred]);
//
//         var path = Assert.IsType<AstPathExpr>(pred.Body);
//         var step = Assert.Single(path.Steps);
//         Assert.Equal(TestingFolder.Fields.Parent, bind.SemanticModel.AssocByPathStep[step]);
//
//         var cond = Assert.IsType<AstFieldCompareCondition>(step.Filter!.Condition);
//         Assert.Equal(TestingFolder.Fields.Name, bind.SemanticModel.FieldByCompare[cond]);
//     }
//
//     [Fact]
//     public void Bind_Unknown_Field_Reports_Error_But_Does_Not_Throw()
//     {
//         using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());
//         using var session = new DbSession(env, readOnly: true);
//
//         var model = session.GetObjFromGuid<Model>(env.ModelGuid);
//         Assert.NotNull(model);
//
//         var src = "P(TestingFolder): this[$.DoesNotExist=\"x\"]";
//         var parse = PathLangParser.Parse(src);
//         var bind = PathLangBinder.Bind(model.Value, session, parse.Predicates);
//
//         Assert.Contains(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
//     }
//
//     private static Model LoadBusinessModel(Environment env, DbSession session)
//     {
//         var model = session.GetObjFromGuid<Model>(env.ModelGuid);
//         Assert.NotNull(model);
//         return model!.Value;
//     }
//
//     //todo, I think a standard visitor pattern would be better....
//     private static IEnumerable<AstPathStep> EnumeratePathSteps(AstExpr expr)
//     {
//         switch (expr)
//         {
//             case AstPathExpr p:
//                 foreach (var s in p.Steps)
//                     yield return s;
//                 foreach (var x in EnumeratePathSteps(p.Source))
//                     yield return x;
//                 break;
//             case AstFilterExpr f:
//                 foreach (var x in EnumeratePathSteps(f.Source))
//                     yield return x;
//                 break;
//             case AstRepeatExpr r:
//                 foreach (var x in EnumeratePathSteps(r.Expr))
//                     yield return x;
//                 break;
//             case AstLogicalExpr l:
//                 foreach (var x in EnumeratePathSteps(l.Left))
//                     yield return x;
//                 foreach (var x in EnumeratePathSteps(l.Right))
//                     yield return x;
//                 break;
//             default:
//                 yield break;
//         }
//     }
//
//     private static IEnumerable<AstFieldCompareCondition> EnumerateFieldCompares(AstExpr expr)
//     {
//         foreach (var s in EnumeratePathSteps(expr))
//         {
//             if (s.Filter?.Condition is AstFieldCompareCondition fc)
//                 yield return fc;
//         }
//
//         if (expr is AstFilterExpr fe && fe.Filter.Condition is AstFieldCompareCondition fc2)
//             yield return fc2;
//     }
//
//     [Fact]
//     public void Bind_ComplexPredicate_ResolvesNestedTraversals_AndFields()
//     {
//         using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetBusinessModelDumpFile());
//         using var session = new DbSession(env, readOnly: true);
//         var model = LoadBusinessModel(env, session);
//
//         var src =
//             "CanEdit(Document): this->BusinessCase[$.Locked=false] AND (this->OwnerUnit->Members[$(User).CurrentUser=true] OR this->ExplicitViewers[$.CurrentUser=true])";
//
//         var parse = PathLangParser.Parse(src);
//         Assert.DoesNotContain(parse.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
//
//         var bind = PathLangBinder.Bind(model, session, parse.Predicates);
//         Assert.DoesNotContain(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
//
//         var pred = Assert.Single(parse.Predicates);
//         Assert.Equal(Guid.Parse("40d51f0a-31f0-4a04-9d16-77f454fd914e"), bind.SemanticModel.InputTypIdByPredicate[pred]);
//
//         // Assoc ids we expect to be resolved.
//         var doc_BusinessCase = Guid.Parse("2df0f6aa-582d-4daf-89a1-def70bf75aed");
//         var doc_OwnerUnit = Guid.Parse("35cfa966-9d9f-434f-95a7-507bb313f26f");
//         var ou_Members = Guid.Parse("f86ce70e-57df-4056-8ff2-9cc0f6af444e");
//         var doc_ExplicitViewers = Guid.Parse("3f0771ed-7629-495c-bff2-5b8d3939f169");
//
//         var steps = EnumeratePathSteps(pred.Body).ToList();
//         Assert.Contains(steps, s => s.AssocName.Text.ToString() == "BusinessCase" && bind.SemanticModel.AssocByPathStep[s] == doc_BusinessCase);
//         Assert.Contains(steps, s => s.AssocName.Text.ToString() == "OwnerUnit" && bind.SemanticModel.AssocByPathStep[s] == doc_OwnerUnit);
//         Assert.Contains(steps, s => s.AssocName.Text.ToString() == "Members" && bind.SemanticModel.AssocByPathStep[s] == ou_Members);
//         Assert.Contains(steps, s => s.AssocName.Text.ToString() == "ExplicitViewers" && bind.SemanticModel.AssocByPathStep[s] == doc_ExplicitViewers);
//
//         // Field ids we expect to be resolved.
//         var bc_Locked = Guid.Parse("0c52f663-a159-4e40-91e4-f7327b13e793");
//         var user_CurrentUser = Guid.Parse("9b76cfbf-e9b5-41db-b655-b496fca80732");
//         var user_TypId = Guid.Parse("3777d451-b036-4772-9358-5a67ab44763b");
//
//         var fieldCompares = EnumerateFieldCompares(pred.Body).ToList();
//         Assert.Contains(fieldCompares, fc => fc.FieldName.Text.ToString() == "Locked" && bind.SemanticModel.FieldByCompare[fc] == bc_Locked);
//         Assert.Contains(fieldCompares, fc => fc.FieldName.Text.ToString() == "CurrentUser" && bind.SemanticModel.FieldByCompare[fc] == user_CurrentUser);
//
//         // Ensure the type-guard was resolved for $(User).CurrentUser.
//         var guarded = fieldCompares.Single(fc => fc.TypeGuard is not null);
//         Assert.Equal(user_TypId, bind.SemanticModel.TypeGuardTypIdByCompare[guarded]);
//     }
//
//     [Fact]
//     public void Bind_UnknownAssociation_ReturnsHelpfulError()
//     {
//         using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetBusinessModelDumpFile());
//         using var session = new DbSession(env, readOnly: true);
//         var model = LoadBusinessModel(env, session);
//
//         var src = "Bad(Document): this->DoesNotExist->Members[$.CurrentUser=true]";
//         var parse = PathLangParser.Parse(src);
//         var bind = PathLangBinder.Bind(model, session, parse.Predicates);
//
//         Assert.Contains(bind.Diagnostics, d =>
//             d.Severity == PathLangDiagnosticSeverity.Error &&
//             d.Message == "Unknown association 'DoesNotExist' on type 'Document'");
//     }
//
//     [Fact]
//     public void Bind_UnknownField_ReturnsHelpfulError()
//     {
//         using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetBusinessModelDumpFile());
//         using var session = new DbSession(env, readOnly: true);
//         var model = LoadBusinessModel(env, session);
//
//         var src = "Bad(Document): this->OwnerUnit[$.DoesNotExist=true]";
//         var parse = PathLangParser.Parse(src);
//         var bind = PathLangBinder.Bind(model, session, parse.Predicates);
//
//         Assert.Contains(bind.Diagnostics, d =>
//             d.Severity == PathLangDiagnosticSeverity.Error &&
//             d.Message == "Unknown field 'DoesNotExist' on type 'OrganizationalUnit'");
//     }
//
//     [Fact]
//     public void Bind_UnknownType_ReturnsHelpfulError()
//     {
//         using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetBusinessModelDumpFile());
//         using var session = new DbSession(env, readOnly: true);
//         var model = LoadBusinessModel(env, session);
//
//         var src = "Bad(DoesNotExist): this";
//         var parse = PathLangParser.Parse(src);
//         var bind = PathLangBinder.Bind(model, session, parse.Predicates);
//
//         Assert.Contains(bind.Diagnostics, d =>
//             d.Severity == PathLangDiagnosticSeverity.Error &&
//             d.Message == "Unknown type 'DoesNotExist'");
//     }
// }
