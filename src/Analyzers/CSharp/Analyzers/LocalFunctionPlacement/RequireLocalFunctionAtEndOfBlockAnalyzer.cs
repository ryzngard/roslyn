using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.CSharp.LanguageServices;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.LanguageServices;

namespace Microsoft.CodeAnalysis.CSharp.Analyzers.LocalFunctionPlacement
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal sealed class RequireLocalFunctionAtEndOfBlockAnalyzer : AbstractBuiltInCodeStyleDiagnosticAnalyzer
    {
        private static readonly LocalizableString s_localizableTitle = new LocalizableResourceString(nameof(AnalyzersResources.Naming_Styles), AnalyzersResources.ResourceManager, typeof(AnalyzersResources));

        public RequireLocalFunctionAtEndOfBlockAnalyzer()
            : base(
                  IDEDiagnosticIds.LocalFunctionShouldNotBeFollowedByExpressionsId,
                  EnforceOnBuildValues.LocalFunctionShouldNotBeFollowedByExpressions,
                  option: null,
                  s_localizableTitle)
        { }

        public override DiagnosticAnalyzerCategory GetAnalyzerCategory() => DiagnosticAnalyzerCategory.SyntaxTreeWithoutSemanticsAnalysis;

        protected override void InitializeWorker(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                context =>
                {
                    var localFunction = (LocalFunctionStatementSyntax)context.Node;
                    var syntaxFacts = CSharpSyntaxFacts.Instance;
                    var parent = localFunction.Parent;

                    if (parent is null)
                        return;

                    var siblings = parent.ChildNodes().ToImmutableArray();
                    var location = siblings.IndexOf(localFunction);
                    var isFollowedByStatements = false;

                    for (var i = location + 1; i < siblings.Length; i++)
                    {
                        var sibling = siblings[i];
                        if (sibling is LocalFunctionStatementSyntax)
                        {
                            continue;
                        }

                        if (syntaxFacts.IsExpressionStatement(sibling))
                        {
                            isFollowedByStatements = true;
                            break;
                        }
                    }

                    if (isFollowedByStatements)
                    {
                        var diagnostic = DiagnosticHelper.Create(
                            Descriptor,
                            localFunction.GetLocation(),
                            ReportDiagnostic.Info,
                            additionalLocations: null,
                            properties: ImmutableDictionary<string, string>.Empty);

                        context.ReportDiagnostic(diagnostic);
                    }
                }, SyntaxKind.LocalFunctionStatement);
        }
    }
}
