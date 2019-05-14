using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis
{
    internal static class SelectionAnalyzer
    {
        public static async ValueTask<SelectionAnalysisResult> AnalyzeAsync(
            Document document,
            TextSpan selection,
            CancellationToken cancellationToken)
        {
            var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);

            var context = root.FindNode(selection);

            return new SelectionAnalysisResult(
                document,
                selection,
                context);
        }

        public struct SelectionAnalysisResult
        {
            internal SelectionAnalysisResult(Document document,
                                             TextSpan selection,
                                             SyntaxNode context)
            {
                Document = document;
                Selection = selection;
                Context = context;

                IntersectedNodes = context
                    .ChildNodes()
                    .Where(n => n.Span.IntersectsWith(selection))
                    .ToImmutableArray();

                IsSelectionExactOnContext = context.FullSpan.OverlapsWith(selection);
            }

            public Document Document { get; }
            public TextSpan Selection { get; }
            public SyntaxNode Context { get; }
            public ImmutableArray<SyntaxNode> IntersectedNodes { get; }
            public bool IsSelectionExactOnContext { get; }
        }
    }
}
