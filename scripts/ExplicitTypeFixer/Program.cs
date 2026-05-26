using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Simplification;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: ExplicitTypeFixer <path-to.sln>");
    return 1;
}

string solutionPath = Path.GetFullPath(args[0]);
if (!File.Exists(solutionPath))
{
    Console.Error.WriteLine($"Solution not found: {solutionPath}");
    return 1;
}

MSBuildLocator.RegisterDefaults();
using MSBuildWorkspace workspace = MSBuildWorkspace.Create();
Solution solution = await workspace.OpenSolutionAsync(solutionPath).ConfigureAwait(false);

int filesChanged = 0;
int declarationsFixed = 0;

foreach (ProjectId projectId in solution.ProjectIds)
{
    Project? project = solution.GetProject(projectId);
    if (project is null || !project.Language.Equals(LanguageNames.CSharp, StringComparison.OrdinalIgnoreCase))
        continue;

    foreach (DocumentId documentId in project.DocumentIds)
    {
        Document? document = solution.GetDocument(documentId);
        if (document?.FilePath is null || !document.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            continue;

        DocumentEditor editor = await DocumentEditor.CreateAsync(document).ConfigureAwait(false);
        SemanticModel semanticModel = await document.GetSemanticModelAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No semantic model for {document.FilePath}");
        SyntaxNode root = await document.GetSyntaxRootAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No syntax root for {document.FilePath}");

        int fileFixes = 0;

        foreach (ForEachStatementSyntax forEach in root.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            if (forEach.Type is not IdentifierNameSyntax { Identifier.Text: "var" })
                continue;

            ForEachStatementInfo info = semanticModel.GetForEachStatementInfo(forEach);
            ITypeSymbol? elementType = info.ElementType;
            if (elementType is null || elementType.SpecialType == SpecialType.System_Void)
                continue;

            TypeSyntax typeSyntax = ParseType(elementType);
            ForEachStatementSyntax updated = forEach
                .WithType(typeSyntax)
                .WithAdditionalAnnotations(Formatter.Annotation);

            editor.ReplaceNode(forEach, updated);
            fileFixes++;
        }

        foreach (LocalDeclarationStatementSyntax localDecl in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            if (!UsesVar(localDecl))
                continue;

            if (localDecl.Declaration.Variables.Count != 1)
                continue;

            VariableDeclaratorSyntax? declarator = localDecl.Declaration.Variables.FirstOrDefault();
            if (declarator?.Initializer?.Value is null)
                continue;

            if (declarator.Initializer.Value is ParenthesizedLambdaExpressionSyntax
                or SimpleLambdaExpressionSyntax)
            {
                ITypeSymbol? delegateType = semanticModel.GetTypeInfo(declarator.Initializer.Value).Type;
                if (delegateType is null)
                    continue;

                TypeSyntax delegateTypeSyntax = ParseType(delegateType);
                LocalDeclarationStatementSyntax lambdaUpdate = localDecl
                    .WithDeclaration(localDecl.Declaration.WithType(delegateTypeSyntax))
                    .WithAdditionalAnnotations(Formatter.Annotation);

                editor.ReplaceNode(localDecl, lambdaUpdate);
                fileFixes++;
                continue;
            }

            ITypeSymbol? type = semanticModel.GetTypeInfo(declarator.Initializer.Value).Type;
            if (type is null || type.SpecialType == SpecialType.System_Void)
                continue;

            if (type is INamedTypeSymbol { IsAnonymousType: true })
                continue;

            TypeSyntax typeSyntax = ParseType(type);
            LocalDeclarationStatementSyntax updated = localDecl
                .WithDeclaration(localDecl.Declaration.WithType(typeSyntax))
                .WithAdditionalAnnotations(Formatter.Annotation);

            editor.ReplaceNode(localDecl, updated);
            fileFixes++;
        }

        if (fileFixes == 0)
            continue;

        Document changedDocument = editor.GetChangedDocument();
        solution = changedDocument.Project.Solution;
        filesChanged++;
        declarationsFixed += fileFixes;
    }
}

if (filesChanged == 0)
{
    Console.WriteLine("No var declarations with inferable types found.");
    return 0;
}

Console.WriteLine($"Applying changes to {filesChanged} file(s), {declarationsFixed} declaration(s)...");
bool applied = workspace.TryApplyChanges(solution);
if (!applied)
{
    Console.Error.WriteLine("Failed to apply workspace changes.");
    return 1;
}

Console.WriteLine("Done.");
return 0;

static bool UsesVar(LocalDeclarationStatementSyntax node) =>
    node.Declaration.Type is IdentifierNameSyntax { Identifier.Text: "var" };

static TypeSyntax ParseType(ITypeSymbol type) =>
    SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
        .WithTrailingTrivia(SyntaxFactory.Space)
        .WithAdditionalAnnotations(Simplifier.Annotation);
