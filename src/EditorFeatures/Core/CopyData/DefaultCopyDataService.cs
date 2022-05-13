// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.CodeAnalysis.Editor;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.CodeAnalysis.CopyData
{
    [ExportWorkspaceService(typeof(ICopyDataService)), Shared]
    [System.ComponentModel.Composition.Export(typeof(ICommandHandler))]
    [ContentType(ContentTypeNames.RoslynContentType)]
    [Name(PredefinedCommandHandlerNames.GoToDefinition)]
    internal class DefaultCopyDataService : ICopyDataService, ICommandHandler<CopyCommandArgs>
    {
        private readonly Queue<CopyData> _cache = new();
        private const int MaximumCacheSize = 50;

        public string DisplayName => "Roslyn Copy Data Handler";

        [ImportingConstructor]
        [System.ComponentModel.Composition.ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public DefaultCopyDataService()
        {
        }

        public CopyData? TryGetData(int sequenceNumber)
            => _cache.FirstOrDefault(d => d.SequenceNumber == sequenceNumber);

        private void Store(CopyData data)
        {
            if (_cache.Count >= MaximumCacheSize)
            {
                _cache.Dequeue();
            }

            _cache.Enqueue(data);
        }

        public CommandState GetCommandState(CopyCommandArgs args)
        {
            throw new NotImplementedException();
        }

        [DllImport("User32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClipboardSequenceNumber();

        public bool ExecuteCommand(CopyCommandArgs args, CommandExecutionContext executionContext)
        {
            var textView = args.TextView;
            var caretPoint = textView.GetCaretPoint(args.SubjectBuffer);
            var sequenceNumber = GetClipboardSequenceNumber();

            Store(new CopyData(sequenceNumber, textView, caretPoint));

            return false;
        }

        public CopyData? TryGetData()
             => TryGetData(GetClipboardSequenceNumber());
    }
}
