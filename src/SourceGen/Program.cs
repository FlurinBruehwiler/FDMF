using SourceGen;

var root = Helper.GetRootDir();

ModelGenerator.Generate(Path.Combine(root, "Shared/Model"));

NetworkingGenerator.Generate(Path.Combine(root, "Shared/IServerProcedures.cs"));
NetworkingGenerator.Generate(Path.Combine(root, "Shared/IClientProcedures.cs"));





