// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis.Host;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace Microsoft.CodeAnalysis.CopyData
{
    internal record CopyData(int SequenceNumber, ITextView TextView, SnapshotPoint? CaretPosition);

    internal interface ICopyDataService : IWorkspaceService
    {
        /// <summary>
        /// Given a clipboard sequence number, tries to get
        /// the related CopyData. This may be null because
        /// the clipboard data was external or in another instance
        /// of Visual Studio
        /// </summary>
        CopyData? TryGetData(int sequenceNumber);

        /// <summary>
        /// Same as calling <see cref="TryGetData(int)"/> with
        /// current clipboard sequence number
        /// </summary>
        CopyData? TryGetData();
    }
}
