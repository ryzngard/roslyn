// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.PullMemberUp;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.ExtractMembers
{
    internal abstract class AbstractExtractMemberAnalyzer : ILanguageService
    {
        /// <summary>
        /// Gets all of the selected member nodes in the span that are part of the same type declaration. If the span goes 
        /// across multiple type declarations, the first declaration found is used. Anything outside of that declaration 
        /// is not returned
        /// </summary>
        protected abstract Task<ImmutableArray<SyntaxNode>> GetSelectedMemberNodesAsync(Document document, TextSpan span, CancellationToken cancellationToken);
        protected abstract Task<SyntaxNode?> GetSelectedClassDeclarationAsync(Document document, TextSpan span, CancellationToken cancellationToken);

        public async Task<ExtractMemberAnalysis> AnalyzeAsync(Document document, TextSpan span, CancellationToken cancellationToken)
        {
            var memberNodeAnalysis = await GetMemberNodeAnalysisAsync(document, span, cancellationToken).ConfigureAwait(false);
            if (memberNodeAnalysis is not null)
            {
                return memberNodeAnalysis;
            }

            var classNodeAnalysis = await GetClassNodeAnalysisAsync(document, span, cancellationToken).ConfigureAwait(false);
            return classNodeAnalysis ?? ExtractMemberAnalysis.InvalidSelection;
        }

        private async Task<ExtractMemberAnalysis?> GetClassNodeAnalysisAsync(Document document, TextSpan span, CancellationToken cancellationToken)
        {
            var selectedClassNode = await GetSelectedClassDeclarationAsync(document, span, cancellationToken).ConfigureAwait(false);
            if (selectedClassNode is null)
            {
                // Check to see if the span is at the member level of a class
                var root = await document.GetRequiredSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var selectedNode = root.FindNode(span, findInsideTrivia: true);
                var syntaxFacts = document.GetRequiredLanguageService<ISyntaxFactsService>();

                selectedClassNode = selectedNode.FirstAncestorOrSelf<SyntaxNode>(syntaxFacts.IsTypeDeclaration);
                if (selectedClassNode is null)
                {
                    return null;
                }

                var membersOfClass = syntaxFacts.GetMembersOfTypeDeclaration(selectedClassNode);
                if (!membersOfClass.Contains(selectedNode))
                {
                    // The selected node is not member level for the class
                    return null;
                }
            }

            var semanticModel = await document.GetRequiredSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var originalType = semanticModel.GetDeclaredSymbol(selectedClassNode, cancellationToken) as INamedTypeSymbol;

            if (originalType is null)
            {
                return null;
            }

            return new ExtractMemberAnalysis(originalType, selectedClassNode);
        }

        private async Task<ExtractMemberAnalysis?> GetMemberNodeAnalysisAsync(Document document, TextSpan span, CancellationToken cancellationToken)
        {
            var selectedMemberNodes = await GetSelectedMemberNodesAsync(document, span, cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetRequiredSemanticModelAsync(cancellationToken).ConfigureAwait(false);

            var selectedMemberNodeSymbolPairs = selectedMemberNodes
                .Select(node => (node: node, symbol: semanticModel.GetDeclaredSymbol(node, cancellationToken)))
                .Where((pair) => pair.symbol != null && MemberAndDestinationValidator.IsMemberValid(pair.symbol))
                .ToImmutableArray();

            if (selectedMemberNodeSymbolPairs.Length == 0)
            {
                return null;
            }

            var syntaxFacts = document.GetRequiredLanguageService<ISyntaxFactsService>();

            var firstPair = selectedMemberNodeSymbolPairs.First();
            var containingTypeDeclarationNode = firstPair.node.FirstAncestorOrSelf<SyntaxNode>(syntaxFacts.IsTypeDeclaration);
            var containingType = firstPair.symbol.ContainingType;

            return new ExtractMemberAnalysis(containingType, containingTypeDeclarationNode, selectedMemberNodeSymbolPairs);
        }
    }

    internal class ExtractMemberAnalysis
    {
        public static readonly ExtractMemberAnalysis InvalidSelection = new ExtractMemberAnalysis();

        public INamedTypeSymbol? OriginalType { get; }
        public SyntaxNode? OriginalTypeDeclarationNode { get; }
        public ImmutableArray<(SyntaxNode node, ISymbol symbol)> SelectedMembers { get; }

        public ExtractMemberAnalysis(
            INamedTypeSymbol originalType,
            SyntaxNode? containingTypeDeclarationNode,
            ImmutableArray<(SyntaxNode, ISymbol)> selectedMembers = default)
        {
            OriginalType = originalType;
            OriginalTypeDeclarationNode = containingTypeDeclarationNode;
            SelectedMembers = selectedMembers;
        }

        private ExtractMemberAnalysis()
        { }
    }
}
