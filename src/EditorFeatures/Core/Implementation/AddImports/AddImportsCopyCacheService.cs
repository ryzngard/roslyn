// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.Editor.Implementation.AddImports
{
    internal sealed class AddImportsCopyCacheService : IAddImportsCopyCacheService
    {
        [ExportWorkspaceServiceFactory(typeof(IAddImportsCopyCacheService), ServiceLayer.Editor), Shared]
        internal sealed class Factory : IWorkspaceServiceFactory
        {
            [ImportingConstructor]
            [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
            public Factory() { }

            public IWorkspaceService CreateService(HostWorkspaceServices workspaceServices)
                => new AddImportsCopyCacheService(workspaceServices.Workspace);
        }

        private readonly Workspace _workspace;

        private readonly Dictionary<AddImportsCacheIdentifier, Task<ImmutableArray<SyntaxNode>>> _cache = new();

        public AddImportsCopyCacheService(Workspace workspace)
        {
            _workspace = workspace;
        }

        public AddImportsCacheIdentifier AddSelectionToCache(Document document, ImmutableArray<TextSpan> textSpans, CancellationToken cancellationToken)
        {
            var storageService = _workspace.Services.GetRequiredService<ITemporaryStorageService>();
            var storage = storageService.CreateTemporaryStreamStorage(cancellationToken);

            var documentId = document.Id;
            var identifier = new AddImportsCacheIdentifier(document);

            _cache[identifier] = Task.Run(async () =>
            {
                var document = _workspace.CurrentSolution.GetRequiredDocument(documentId);
                var root = await document.GetRequiredSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

                var nodes = textSpans.SelectAsArray(span => root.FindNode(span));

                // TODO: Actually get the usings here based on the nodes selected

                return nodes;
            }, cancellationToken);

            return identifier;
        }

        public async Task<object?> GetDataAsync(AddImportsCacheIdentifier identifier)
        {
            if (!_cache.TryGetValue(identifier, out var task))
            {
                return null;
            }

            return await task.ConfigureAwait(false);
        }
    }
}
