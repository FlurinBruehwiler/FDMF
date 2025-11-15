using SourceGen;

var root = Helper.GetRootDir();

ModelGenerator.Generate(Path.Combine(root, "Model/Model"));

NetworkingGenerator.Generate(Path.Combine(root, "Networking/IServerProcedures.cs"));
NetworkingGenerator.Generate(Path.Combine(root, "Networking/IClientProcedures.cs"));





