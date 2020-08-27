// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

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
        protected abstract Task<SyntaxNode?> GetSelectedMemberNodeAsync(Document document, TextSpan span, CancellationToken cancellationToken);
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
                return null;
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
            var selectedMemberNode = await GetSelectedMemberNodeAsync(document, span, cancellationToken).ConfigureAwait(false);
            if (selectedMemberNode is null)
            {
                return null;
            }

            var semanticModel = await document.GetRequiredSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var selectedMember = semanticModel.GetDeclaredSymbol(selectedMemberNode, cancellationToken);
            if (selectedMember is null || selectedMember.ContainingType is null)
            {
                return null;
            }

            // Use same logic as pull members up for determining if a selected member
            // is valid to be moved into a base
            if (!MemberAndDestinationValidator.IsMemberValid(selectedMember))
            {
                return null;
            }

            var containingType = selectedMember.ContainingType;

            // Can't extract to a new type if there's already a base. Maybe
            // in the future we could inject a new type inbetween base and
            // current
            if (containingType.BaseType?.SpecialType != SpecialType.System_Object)
            {
                return null;
            }

            var syntaxFacts = document.GetRequiredLanguageService<ISyntaxFactsService>();
            var containingTypeDeclarationNode = selectedMemberNode.FirstAncestorOrSelf<SyntaxNode>(syntaxFacts.IsTypeDeclaration);

            return new ExtractMemberAnalysis(containingType, containingTypeDeclarationNode, selectedMember);
        }
    }

    internal class ExtractMemberAnalysis
    {
        public static readonly ExtractMemberAnalysis InvalidSelection = new ExtractMemberAnalysis();

        public INamedTypeSymbol? OriginalType { get; }
        public SyntaxNode? OriginalTypeDeclarationNode { get; }
        public ISymbol? SelectedMember { get; }

        public ExtractMemberAnalysis(
            INamedTypeSymbol originalType,
            SyntaxNode? containingTypeDeclarationNode,
            ISymbol? selectedMember = null)
        {
            OriginalType = originalType;
            OriginalTypeDeclarationNode = containingTypeDeclarationNode;
            SelectedMember = selectedMember;
        }

        private ExtractMemberAnalysis()
        { }
    }
}
