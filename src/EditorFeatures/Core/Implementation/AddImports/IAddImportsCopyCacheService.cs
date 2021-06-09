// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.Implementation.AddImports
{
    internal interface IAddImportsCopyCacheService : IWorkspaceService
    {
        AddImportsCacheIdentifier AddSelectionToCache(Document document, ImmutableArray<TextSpan> textSpans, CancellationToken cancellationToken);

        Task<ImmutableArray<SymbolKey>> GetDataAsync(AddImportsCacheIdentifier identifier);
    }

    internal readonly struct AddImportsCacheIdentifier
    {
        public readonly DocumentId DocumentId;
        private readonly Guid _guid;

        public AddImportsCacheIdentifier(Document document)
        {
            DocumentId = document.Id;
            _guid = Guid.NewGuid();
        }

        public override bool Equals(object? obj)
        {
            return obj is AddImportsCacheIdentifier identifier &&
                   EqualityComparer<DocumentId>.Default.Equals(DocumentId, identifier.DocumentId) &&
                   _guid.Equals(identifier._guid);
        }

        public override int GetHashCode()
        {
            return Hash.Combine(
                DocumentId.GetHashCode()
                , _guid.GetHashCode());
        }
    }
}
