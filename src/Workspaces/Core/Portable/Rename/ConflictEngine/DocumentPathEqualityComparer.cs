// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Microsoft.CodeAnalysis.Rename.ConflictEngine
{
    internal sealed class DocumentPathEqualityComparer : IEqualityComparer<Document>
    {
        public static readonly DocumentPathEqualityComparer Instance = new();

        public bool Equals(Document? x, Document? y)
        {
            if (x is null || y is null)
            {
                return ReferenceEquals(x, y);
            }

            return x.FilePath == y.FilePath;
        }

        public int GetHashCode(Document obj) => obj.GetHashCode();
    }
}
