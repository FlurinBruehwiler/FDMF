using FDMF.Core.DatabaseLayer;
using FDMF.SourceGen;
using Environment = FDMF.Core.Environment;
using Helper = FDMF.SourceGen.Helper;

var root = Helper.GetRootDir();

//Main
using var env2 = Environment.CreateDatabase("temp2");

using (var session = new DbSession(env2))
{
    var model = session.GetObjFromGuid<Model>(env2.ModelGuid);
    ModelGenerator.Generate(model!.Value, Path.Combine(root, "FDMF.Core/Generated"), "FDMF.Core.DatabaseLayer");
}

//Test Data
GenerateModel(Path.Combine(root, "FDMF.Tests/testdata/TestModelDump.json"));
GenerateModel(Path.Combine(root, "FDMF.Tests/testdata/BusinessModelDump.json"));

void GenerateModel(string path)
{
    using var env = Environment.CreateDatabase("temp", path);

    using (var session = new DbSession(env))
    {
        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;
        ModelGenerator.Generate(model, Path.Combine(root, $"FDMF.Tests/Generated/{model.Name}"), $"FDMF.Tests.{model.Name}Model");
    }
}

//Networking
NetworkingGenerator.Generate(Path.Combine(root, "FDMF.Core/IServerProcedures.cs"));
NetworkingGenerator.Generate(Path.Combine(root, "FDMF.Core/IClientProcedures.cs"));






