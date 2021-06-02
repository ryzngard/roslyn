// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.Editor.Implementation.AddImports
{
    internal interface IAddImportsCopyCacheService : IWorkspaceService
    {
        Guid AddSelectionToCache(Document document, ImmutableArray<TextSpan> textSpans, CancellationToken cancellationToken);

        // TODO: Use something other than just object here
        Task<object?> GetDataAsync(Guid identifier);
    }
}
