using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.CSharp.CodeFixes.AddNullableEnabled
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = PredefinedCodeFixProviderNames.AddNullableEnable), Shared]
    internal class AddNullableEnabledContextCodeFixProvider : SyntaxEditorBasedCodeFixProvider
    {
#pragma warning disable RS0033 // Importing constructor should be marked with 'ObsoleteAttribute'
        [ImportingConstructor]
#pragma warning restore RS0033 // Importing constructor should be marked with 'ObsoleteAttribute'
        public AddNullableEnabledContextCodeFixProvider()
        {
        }

        public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("CS8632");

        internal override CodeFixCategory CodeFixCategory => CodeFixCategory.Compile;

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            // TODO: 
            // Add nullable enable for project that doesn't have
            // Add fix for whole project
            if (context.Project.CompilationOptions is null)
            {
                return Task.CompletedTask;
            }

            var projectEnabledNullable = context.Project.CompilationOptions.NullableContextOptions != NullableContextOptions.Disable;

            if (projectEnabledNullable)
            {
                var codeFix = new CodeFix(
                    "Fix nullable context",
                    (cancellationToken) => AddNullableEnabledContextCodeFixProvider.FixForNullableEnabledProjectAsync(context.Document, context.Span, cancellationToken),
                    equivalenceKey: null);

                context.RegisterCodeFix(codeFix, context.Diagnostics.First());
            }

            return Task.CompletedTask;
        }

        private static async Task<Document> FixForNullableEnabledProjectAsync(Document document, TextSpan span, CancellationToken cancellationToken)
        {
            var documentEditor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

            var nullablePragmas = documentEditor.OriginalRoot.DescendantNodesAndTokens(descendIntoTrivia: true).Where(nodeOrToken => nodeOrToken.IsKind(SyntaxKind.NullableDirectiveTrivia));
            var precedingDisablePragmas = nullablePragmas
                .OrderBy(n => n.SpanStart)
                .Where(n => n.SpanStart < span.Start)
                .Reverse()
                .TakeWhile(t => t.ChildNodesAndTokens().Any(t => t.IsKind(SyntaxKind.DisableKeyword)));

            foreach (var nullablePragma in precedingDisablePragmas)
            {
                if (nullablePragma.AsNode() is not SyntaxNode nodeToRemove)
                {
                    continue;
                }

                documentEditor.RemoveNode(nodeToRemove);
            }

            return documentEditor.GetChangedDocument();
        }

        protected override Task FixAllAsync(Document document, ImmutableArray<Diagnostic> diagnostics, SyntaxEditor editor, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }


        private class CodeFix : CustomCodeActions.DocumentChangeAction
        {
            public CodeFix(string title, Func<CancellationToken, Task<Document>> createChangedDocument, string? equivalenceKey) : base(title, createChangedDocument, equivalenceKey)
            {
            }
        }
    }
}
