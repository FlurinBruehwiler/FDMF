using FDMF.Core.Database;
using FDMF.SourceGen;
using Environment = FDMF.Core.Environment;
using Helper = FDMF.SourceGen.Helper;

var root = Helper.GetRootDir();

var env = Environment.CreateDatabase("temp", Path.Combine(root, "FDMF.Tests/testdata/TestModelDump.json"));

using (var session = new DbSession(env))
{
    var model = session.GetObjFromGuid<Model.Generated.Model>(env.ModelGuid);
    ModelGenerator.Generate(model!.Value, Path.Combine(root, "FDMF.Tests/Generated"));
}

NetworkingGenerator.Generate(Path.Combine(root, "Core/IServerProcedures.cs"));
NetworkingGenerator.Generate(Path.Combine(root, "Core/IClientProcedures.cs"));





