using FDMF.Core.PathLayer;

var ast = PathLangParser.Parse("OwnerCanView(Document): this->Business->Owners[$(Person).CurrentUser=true]");

Console.WriteLine(ast.Diagnostics.Count);

var output = PathLangAstPrinter.PrintProgram(ast.Predicates, true);
Console.WriteLine(output);
