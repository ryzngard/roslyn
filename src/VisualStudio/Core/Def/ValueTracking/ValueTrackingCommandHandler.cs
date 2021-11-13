// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Editor;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Internal.Log;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.ValueTracking;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.LanguageServices.Setup;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Roslyn.Utilities;
using IAsyncServiceProvider = Microsoft.VisualStudio.Shell.IAsyncServiceProvider;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.VisualStudio.LanguageServices.ValueTracking
{
    [Export(typeof(ICommandHandler))]
    [ContentType(ContentTypeNames.RoslynContentType)]
    [Name(PredefinedCommandHandlerNames.ShowValueTracking)]
    internal class ValueTrackingCommandHandler : ICommandHandler<ValueTrackingEditorCommandArgs>
    {
        private readonly IAsyncServiceProvider _serviceProvider;
        private readonly IThreadingContext _threadingContext;
        private readonly ClassificationTypeMap _typeMap;
        private readonly IClassificationFormatMapService _classificationFormatMapService;
        private readonly IGlyphService _glyphService;
        private readonly IEditorFormatMapService _formatMapService;

        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public ValueTrackingCommandHandler(
            SVsServiceProvider serviceProvider,
            IThreadingContext threadingContext,
            ClassificationTypeMap typeMap,
            IClassificationFormatMapService classificationFormatMapService,
            IGlyphService glyphService,
            IEditorFormatMapService formatMapService)
        {
            _serviceProvider = (IAsyncServiceProvider)serviceProvider;
            _threadingContext = threadingContext;
            _typeMap = typeMap;
            _classificationFormatMapService = classificationFormatMapService;
            _glyphService = glyphService;
            _formatMapService = formatMapService;
        }

        public string DisplayName => "Go to value tracking";

        public CommandState GetCommandState(ValueTrackingEditorCommandArgs args)
            => CommandState.Available;

        public bool ExecuteCommand(ValueTrackingEditorCommandArgs args, CommandExecutionContext executionContext)
        {
            using var logger = Logger.LogBlock(FunctionId.ValueTracking_Command, CancellationToken.None, LogLevel.Information);

            var cancellationToken = executionContext.OperationContext.UserCancellationToken;
            var caretPosition = args.TextView.GetCaretPoint(args.SubjectBuffer);
            if (!caretPosition.HasValue)
            {
                return false;
            }

            var textSpan = new TextSpan(caretPosition.Value.Position, 0);
            var sourceTextContainer = args.SubjectBuffer.AsTextContainer();
            var document = sourceTextContainer.GetOpenDocumentInCurrentContext();
            if (document is null)
            {
                return false;
            }

            _threadingContext.JoinableTaskFactory.Run(async () =>
            {
                await ShowToolWindowAsync(args.TextView, textSpan, document, cancellationToken).ConfigureAwait(false);
            });

            return true;
        }

        private async Task ShowToolWindowAsync(ITextView textView, TextSpan textSpan, Document document, CancellationToken cancellationToken)
        {
            var toolWindow = await GetOrCreateToolWindowAsync(textView, cancellationToken).ConfigureAwait(false);
            if (toolWindow?.ViewModel is null)
            {
                return;
            }

            await _threadingContext.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            await ShowToolWindowAsync(cancellationToken).ConfigureAwait(true);

            toolWindow.ViewModel.OnShow(textView, textSpan, document);
        }

        private async Task ShowToolWindowAsync(CancellationToken cancellationToken)
        {
            var roslynPackage = await RoslynPackage.GetOrLoadAsync(_threadingContext, _serviceProvider, cancellationToken).ConfigureAwait(false);
            Contract.ThrowIfNull(roslynPackage);

            await roslynPackage.ShowToolWindowAsync(
                    typeof(ValueTrackingToolWindow),
                    0,
                    true,
                    roslynPackage.DisposalToken).ConfigureAwait(false);
        }

        private async Task<ValueTrackingToolWindow?> GetOrCreateToolWindowAsync(ITextView textView, CancellationToken cancellationToken)
        {
            var roslynPackage = await RoslynPackage.GetOrLoadAsync(_threadingContext, _serviceProvider, cancellationToken).ConfigureAwait(false);
            if (roslynPackage is null)
            {
                return null;
            }

            await _threadingContext.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            if (ValueTrackingToolWindow.Instance is null)
            {
                var factory = roslynPackage.GetAsyncToolWindowFactory(Guids.ValueTrackingToolWindowId);

                var viewModel = new ValueTrackingTreeViewModel(
                    _classificationFormatMapService,
                    _formatMapService,
                    _typeMap,
                    _glyphService,
                    _threadingContext);

                factory.CreateToolWindow(Guids.ValueTrackingToolWindowId, 0, viewModel);
                await factory.InitializeToolWindowAsync(Guids.ValueTrackingToolWindowId, 0);

                // FindWindowPaneAsync creates an instance if it does not exist
                ValueTrackingToolWindow.Instance = (ValueTrackingToolWindow)await roslynPackage.FindWindowPaneAsync(
                    typeof(ValueTrackingToolWindow),
                    0,
                    true,
                    roslynPackage.DisposalToken).ConfigureAwait(false);
            }

            // This can happen if the tool window was initialized outside of this command handler. The ViewModel 
            // still needs to be initialized but had no necessary context. Provide that context now in the command handler.
            if (ValueTrackingToolWindow.Instance.ViewModel is null)
            {
                ValueTrackingToolWindow.Instance.ViewModel = new ValueTrackingTreeViewModel(
                    _classificationFormatMapService,
                    _formatMapService,
                    _typeMap,
                    _glyphService,
                    _threadingContext);
            }

            return ValueTrackingToolWindow.Instance;
        }
    }
}
