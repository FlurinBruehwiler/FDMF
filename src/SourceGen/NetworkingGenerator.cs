using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SourceGen;

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
            var genText = GenerateInterfaceImplementation(i);
            var genFilePath = Path.Combine(Path.GetDirectoryName(interfaceFilePath)!, Path.GetFileName(interfaceFilePath).Substring(1));
            File.WriteAllText(genFilePath, genText);
        }
    }

    private static string GenerateInterfaceImplementation(InterfaceDeclarationSyntax interfaceDeclarationSyntax)
    {
        var sb = new SourceBuilder();

        sb.AppendLine("using System.Net.WebSockets;");
        sb.AppendLine();

        sb.AppendLine("namespace Networking;");
        sb.AppendLine();

        var interfaceName = interfaceDeclarationSyntax.Identifier.Text;

        Debug.Assert(interfaceName.StartsWith("I"));

        var className = interfaceName.Substring(1);

        sb.AppendLine($"public class {className}(Action<Stream> sendMessage, Dictionary<Guid, PendingRequest> callbacks) : {interfaceName}");
        sb.AppendLine("{");
        sb.AddIndent();

        foreach (MethodDeclarationSyntax interfaceMember in interfaceDeclarationSyntax.Members.OfType<MethodDeclarationSyntax>())
        {
            sb.AppendLine(interfaceMember.ToFullString().Trim().TrimEnd(';'));
            sb.AppendLine("{");
            sb.AddIndent();

            var methodName = interfaceMember.Identifier.Text;
            var p = string.Join(", ", interfaceMember.ParameterList.Parameters.Select(x => x.Identifier.Text));

            var isVoid = interfaceMember.ReturnType is PredefinedTypeSyntax pts && pts.Keyword.IsKind(SyntaxKind.VoidKeyword);

            sb.AppendLine($"var guid = NetworkingClient.SendRequest(sendMessage, nameof({methodName}), [ {p} ], {isVoid.ToString().ToLower()});");

            if (!isVoid)
            {
                if (TryGetTaskTypeArgumentSyntax(interfaceMember, out var argType))
                {
                    sb.AppendLine($"return NetworkingClient.WaitForResponse<{argType.ToString()}>(callbacks, guid);");
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

        return sb.ToString();
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

public class InterfaceMemberFinder(List<InterfaceDeclarationSyntax> interfaces) : CSharpSyntaxWalker
{
    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        interfaces.Add(node);
    }
}