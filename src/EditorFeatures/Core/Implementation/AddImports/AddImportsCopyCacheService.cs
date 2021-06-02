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

        // TODO: Change this to the actual items that will be returned from the cache
        private readonly Dictionary<object, Task<ImmutableArray<SyntaxNode>>> _cache = new();

        public AddImportsCopyCacheService(Workspace workspace)
        {
            _workspace = workspace;
        }

        public Guid AddSelectionToCache(Document document, ImmutableArray<TextSpan> textSpans, CancellationToken cancellationToken)
        {
            var storageService = _workspace.Services.GetRequiredService<ITemporaryStorageService>();
            var storage = storageService.CreateTemporaryStreamStorage(cancellationToken);

            var guid = Guid.NewGuid();
            var documentId = document.Id;

            _cache[guid] = Task.Run(async () =>
            {
                var document = _workspace.CurrentSolution.GetRequiredDocument(documentId);
                var root = await document.GetRequiredSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

                var nodes = textSpans.SelectAsArray(span => root.FindNode(span));

                // TODO: Actually get the usings here based on the nodes selected

                return nodes;
            }, cancellationToken);

            return guid;
        }

        public async Task<object?> GetDataAsync(Guid identifier)
        {
            if (!_cache.TryGetValue(identifier, out var task))
            {
                return null;
            }

            return await task.ConfigureAwait(false);
        }
    }
}
