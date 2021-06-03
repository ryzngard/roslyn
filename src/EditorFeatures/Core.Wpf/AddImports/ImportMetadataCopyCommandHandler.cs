// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel.Composition;
using Microsoft.CodeAnalysis.Editor.Shared.Options;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using System.Linq;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Experiments;
using System.Windows;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Host.Mef;

namespace Microsoft.CodeAnalysis.Editor.Implementation.AddImports
{
    [Export]
    [Export(typeof(ICommandHandler))]
    [ContentType(ContentTypeNames.CSharpContentType)]
    [Name(PredefinedCommandHandlerNames.AddImportsPaste)]
    internal sealed partial class ImportMetadataCopyCommandHandler : ICommandHandler<CopyCommandArgs>
    {
        public const string ClipboardDataFormat = "add usings clipboard data";

        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public ImportMetadataCopyCommandHandler()
        {
        }

        public string DisplayName => "Copy import metadata";

        public bool ExecuteCommand(CopyCommandArgs args, CommandExecutionContext executionContext)
        {
            // Check that the feature is enabled before doing any work
            var optionValue = args.SubjectBuffer.GetOptionalFeatureOnOffOption(FeatureOnOffOptions.AddImportsOnPaste);

            // If the feature is explicitly disabled we can exit early
            if (optionValue.HasValue && !optionValue.Value)
            {
                return false;
            }

            var selection = args.TextView.Selection;
            if (selection.IsEmpty)
            {
                return false;
            }

            var sourceTextContainer = args.SubjectBuffer.AsTextContainer();
            if (!Workspace.TryGetWorkspace(sourceTextContainer, out var workspace))
            {
                return false;
            }

            var document = sourceTextContainer.GetOpenDocumentInCurrentContext();
            if (document is null)
            {
                return false;
            }

            var experimentationService = document.Project.Solution.Workspace.Services.GetRequiredService<IExperimentationService>();
            var enabled = optionValue.HasValue && optionValue.Value
                || experimentationService.IsExperimentEnabled(WellKnownExperimentNames.ImportsOnPasteDefaultEnabled);

            //if (!enabled)
            //{
            //    return false;
            //}

            var addImportsCopyCacheService = document.Project.Solution.Workspace.Services.GetService<IAddImportsCopyCacheService>();
            if (addImportsCopyCacheService is null)
            {
                return false;
            }

            var snapshotSpans = selection.GetSnapshotSpansOnBuffer(args.SubjectBuffer);
            var textSpans = snapshotSpans.Select(s => s.Span.ToTextSpan()).ToImmutableArray();

            // Add an object to the cache computing the necessary usings for the selection
            var token = addImportsCopyCacheService.AddSelectionToCache(document, textSpans, executionContext.OperationContext.UserCancellationToken);

            var dataObject = new LazyDataObject(Clipboard.GetDataObject(), token, addImportsCopyCacheService);
            Clipboard.Clear();
            Clipboard.SetDataObject(dataObject);

            return true;
        }

        public CommandState GetCommandState(CopyCommandArgs args)
            => CommandState.Available;
    }
}
