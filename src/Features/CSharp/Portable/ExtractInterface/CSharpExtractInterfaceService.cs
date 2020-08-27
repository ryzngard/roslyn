// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.ExtractInterface;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Shared.Extensions;

namespace Microsoft.CodeAnalysis.CSharp.ExtractInterface
{
    [ExportLanguageService(typeof(AbstractExtractInterfaceService), LanguageNames.CSharp), Shared]
    internal sealed class CSharpExtractInterfaceService : AbstractExtractInterfaceService
    {
        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public CSharpExtractInterfaceService()
        {
        }

        internal override string GetContainingNamespaceDisplay(INamedTypeSymbol typeSymbol, CompilationOptions compilationOptions)
        {
            return typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : typeSymbol.ContainingNamespace.ToDisplayString();
        }

        internal override bool IsExtractableMember(ISymbol m)
            => base.IsExtractableMember(m) && !m.ExplicitInterfaceImplementations().Any();

        internal override bool ShouldIncludeAccessibilityModifier(SyntaxNode typeNode)
        {
            var typeDeclaration = typeNode as TypeDeclarationSyntax;
            return typeDeclaration.Modifiers.Any(m => SyntaxFacts.IsAccessibilityModifier(m.Kind()));
        }

        protected override Task<Solution> UpdateMembersWithExplicitImplementationsAsync(
            Solution unformattedSolution, IReadOnlyList<DocumentId> documentIds,
            INamedTypeSymbol extractedInterface, INamedTypeSymbol typeToExtractFrom,
            IEnumerable<ISymbol> includedMembers, ImmutableDictionary<ISymbol, SyntaxAnnotation> symbolToDeclarationMap,
            CancellationToken cancellationToken)
        {
            // In C#, member implementations do not always need
            // to be explicitly added. It's safe enough to return
            // the passed in solution
            return Task.FromResult(unformattedSolution);
        }
    }
}
