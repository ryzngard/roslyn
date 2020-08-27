using System;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.ExtractMembers;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.CSharp.CodeRefactorings.Extractmembers
{
    [ExportLanguageService(typeof(AbstractExtractMemberAnalyzer), LanguageNames.CSharp), Shared]
    internal class CSharpExtractMembersAnalyzer : AbstractExtractMemberAnalyzer
    {
        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public CSharpExtractMembersAnalyzer()
        {
        }

        protected override async Task<SyntaxNode?> GetSelectedClassDeclarationAsync(Document document, TextSpan span, CancellationToken cancellationToken)
        {
            var relaventNodes = await document.GetRelevantNodesAsync<ClassDeclarationSyntax>(span, cancellationToken).ConfigureAwait(false);
            return relaventNodes.FirstOrDefault();
        }

        protected override async Task<SyntaxNode?> GetSelectedMemberNodeAsync(Document document, TextSpan span, CancellationToken cancellationToken)
        {
            // Consider:
            // MemberDeclaration: member that can be declared in type (those are the ones we can pull up) 
            // VariableDeclaratorSyntax: for fields the MemberDeclaration can actually represent multiple declarations, e.g. `int a = 0, b = 1;`.
            // ..Since the user might want to select & pull up only one of them (e.g. `int a = 0, [|b = 1|];` we also look for closest VariableDeclaratorSyntax.
            return await document.TryGetRelevantNodeAsync<MemberDeclarationSyntax>(span, cancellationToken).ConfigureAwait(false) as SyntaxNode ??
                await document.TryGetRelevantNodeAsync<VariableDeclaratorSyntax>(span, cancellationToken).ConfigureAwait(false);
        }
    }
}
