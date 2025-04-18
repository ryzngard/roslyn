﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Extensions.ContextQuery;
using Microsoft.CodeAnalysis.CSharp.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Completion.KeywordRecommenders;

internal sealed class ClassKeywordRecommender() : AbstractSyntacticSingleKeywordRecommender(SyntaxKind.ClassKeyword)
{
    private static readonly ISet<SyntaxKind> s_validModifiers = new HashSet<SyntaxKind>(SyntaxFacts.EqualityComparer)
        {
            SyntaxKind.NewKeyword,
            SyntaxKind.PublicKeyword,
            SyntaxKind.ProtectedKeyword,
            SyntaxKind.InternalKeyword,
            SyntaxKind.PrivateKeyword,
            SyntaxKind.AbstractKeyword,
            SyntaxKind.SealedKeyword,
            SyntaxKind.StaticKeyword,
            SyntaxKind.UnsafeKeyword,
            SyntaxKind.FileKeyword,
        };

    protected override bool IsValidContext(int position, CSharpSyntaxContext context, CancellationToken cancellationToken)
    {
        var syntaxTree = context.SyntaxTree;
        return
            context.IsGlobalStatementContext ||
            context.IsTypeDeclarationContext(
                validModifiers: s_validModifiers,
                validTypeDeclarations: SyntaxKindSet.NonEnumTypeDeclarations,
                canBePartial: true,
                cancellationToken: cancellationToken) ||
            context.IsRecordDeclarationContext(s_validModifiers, cancellationToken) ||
            syntaxTree.IsTypeParameterConstraintStartContext(position, context.LeftToken);
    }
}
