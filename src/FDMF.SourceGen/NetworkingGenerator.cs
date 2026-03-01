using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FDMF.SourceGen;

public static class NetworkingGenerator
{
    public static void Generate(string interfaceFilePath)
    {
        var interfaceFile = File.ReadAllText(interfaceFilePath);

        SyntaxTree tree = CSharpSyntaxTree.ParseText(interfaceFile);

        var interfaces = new List<InterfaceDeclarationSyntax>();

        new InterfaceMemberFinder(interfaces).Visit(tree.GetRoot());

        foreach (var i in interfaces)
        {
            var (genText, className) = GenerateInterfaceImplementation(i);
            var genDir = Path.Combine(Path.GetDirectoryName(interfaceFilePath)!, "Generated");
            Directory.CreateDirectory(genDir);
            var genFilePath = Path.Combine(genDir, className + ".cs");
            File.WriteAllText(genFilePath, genText);
        }
    }

    private static (string sourceText, string className) GenerateInterfaceImplementation(InterfaceDeclarationSyntax interfaceDeclarationSyntax)
    {
        var sb = new SourceBuilder();

        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using FDMF.Core.Rpc;");
        sb.AppendLine();

        var interfaceNamespace = GetContainingNamespace(interfaceDeclarationSyntax) ?? "";
        var generatedNamespace = string.IsNullOrWhiteSpace(interfaceNamespace) ? "Generated" : interfaceNamespace + ".Generated";

        sb.AppendLine($"namespace {generatedNamespace};");
        sb.AppendLine();

        var interfaceName = interfaceDeclarationSyntax.Identifier.Text;

        Debug.Assert(interfaceName.StartsWith("I"));

        var className = "Generated" + interfaceName.Substring(1);

        var ifaceFullName = string.IsNullOrWhiteSpace(interfaceNamespace)
            ? interfaceName
            : $"global::{interfaceNamespace}.{interfaceName}";

        sb.AppendLine($"public sealed class {className}(RpcEndpoint rpc) : {ifaceFullName}");
        sb.AppendLine("{");
        sb.AddIndent();

        foreach (MethodDeclarationSyntax interfaceMember in interfaceDeclarationSyntax.Members.OfType<MethodDeclarationSyntax>())
        {
            var signature = interfaceMember.ToFullString().Trim().TrimEnd(';');
            if (!signature.StartsWith("public ", StringComparison.Ordinal))
                signature = "public " + signature;

            sb.AppendLine(signature);
            sb.AppendLine("{");
            sb.AddIndent();

            var methodName = interfaceMember.Identifier.Text;
            var p = string.Join(", ", interfaceMember.ParameterList.Parameters.Select(x => x.Identifier.Text));

            var isVoid = interfaceMember.ReturnType is PredefinedTypeSyntax pts && pts.Keyword.IsKind(SyntaxKind.VoidKeyword);

            sb.AppendLine($"var guid = rpc.SendRequest(nameof({methodName}), [ {p} ], {isVoid.ToString().ToLower()});");

            if (!isVoid)
            {
                if (TryGetTaskTypeArgumentSyntax(interfaceMember, out var argType))
                {
                    sb.AppendLine($"return rpc.WaitForResponse<{argType.ToString()}>(guid);");
                }
                else
                {
                    //todo handle Tasks without generics
                    Console.WriteLine("Method has to return a Task<T>");
                }
            }

            sb.RemoveIndent();
            sb.AppendLine("}");
        }

        sb.RemoveIndent();
        sb.AppendLine("}");

        return (sb.ToString(), className);
    }

    private static string? GetContainingNamespace(SyntaxNode node)
    {
        for (SyntaxNode? current = node; current != null; current = current.Parent)
        {
            switch (current)
            {
                case NamespaceDeclarationSyntax nd:
                    return nd.Name.ToString();
                case FileScopedNamespaceDeclarationSyntax fd:
                    return fd.Name.ToString();
            }
        }

        return null;
    }

    private static bool TryGetTaskTypeArgumentSyntax(
        MethodDeclarationSyntax method,
        [NotNullWhen(true)] out TypeSyntax? typeArg)
    {
        typeArg = null;

        // The return type might be qualified (System.Threading.Tasks.Task<int>)
        // or unqualified (Task<int>) → either way it's a GenericNameSyntax somewhere.
        var returnType = method.ReturnType;

        // Find the right-most generic name (Task<T>)
        var generic = returnType as GenericNameSyntax;

        if (generic == null)
            return false;

        // Check the identifier textually
        if (generic.Identifier.Text != "Task")
            return false;

        // Must have exactly one type parameter
        if (generic.TypeArgumentList.Arguments.Count != 1)
            return false;



        typeArg = generic.TypeArgumentList.Arguments[0];
        return true;
    }
}

public sealed class InterfaceMemberFinder(List<InterfaceDeclarationSyntax> interfaces) : CSharpSyntaxWalker
{
    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        interfaces.Add(node);
    }

    public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
    {
        Console.WriteLine("");
    }
}
