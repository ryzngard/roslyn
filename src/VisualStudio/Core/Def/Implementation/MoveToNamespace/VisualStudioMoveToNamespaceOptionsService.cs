// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editor;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MoveToNamespace;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.MoveToNamespace
{
    [ExportWorkspaceService(typeof(IMoveToNamespaceOptionsService), ServiceLayer.Host), Shared]
    internal partial class VisualStudioMoveToNamespaceOptionsService : IMoveToNamespaceOptionsService
    {
        public const string MoveToNamespaceTextViewRole = "MoveToNamespace";

        private readonly IThreadingContext _threadingContext;
        private readonly ITextEditorFactoryService _textEditorFactoryService;
        private readonly IContentType _contentType;
        private readonly IVsEditorAdaptersFactoryService _vsEditorAdaptersFactoryService;
        private readonly OLE.Interop.IServiceProvider _serviceProvider;
        private readonly IEditorOperationsFactoryService _editorOperationsFactoryService;

        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public VisualStudioMoveToNamespaceOptionsService(
            IThreadingContext threadingContext,
            ITextEditorFactoryService textEditorFactoryService,
            IContentTypeRegistryService contentTypeRegistryService,
            IVsEditorAdaptersFactoryService editorAdaptersFactoryService,
            IEditorOperationsFactoryService editorOperationsFactoryService,
            [Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider
            )
        {
            _threadingContext = threadingContext;
            _textEditorFactoryService = textEditorFactoryService;
            _contentType = contentTypeRegistryService.GetContentType(ContentTypeNames.CSharpContentType);
            _vsEditorAdaptersFactoryService = editorAdaptersFactoryService;
            _editorOperationsFactoryService = editorOperationsFactoryService;
            _serviceProvider = (OLE.Interop.IServiceProvider)serviceProvider.GetService(typeof(OLE.Interop.IServiceProvider));
        }

        public async Task<MoveToNamespaceOptionsResult> GetChangeNamespaceOptionsAsync(
            Document document,
            string defaultNamespace,
            CancellationToken cancellationToken)
        {
            await _threadingContext.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var workspace = new CSharpMoveToNamespaceWorkspace(document.Project, defaultNamespace);

            var (textView, textViewHost) = await GetTextViewAsync(workspace, cancellationToken).ConfigureAwait(false);

            var editorControl = new MoveToNamespaceEditorControl(
                textView,
                textViewHost,
                _editorOperationsFactoryService);

            var viewModel = new MoveToNamespaceDialogViewModel(
                editorControl,
                defaultNamespace);

            var dialog = new MoveToNamespaceDialog(viewModel);
            var result = dialog.ShowModal();

            if (result == true)
            {
                return new MoveToNamespaceOptionsResult(viewModel.NamespaceName);
            }
            else
            {
                return MoveToNamespaceOptionsResult.Cancelled;
            }
        }

        private async Task<string> GetDocumentTextAsync(CSharpMoveToNamespaceWorkspace workspace, CancellationToken cancellationToken)
        {
            var syntaxTree = await workspace.NamespaceDocument.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            var sourceText = await syntaxTree.GetTextAsync(cancellationToken).ConfigureAwait(false);

            return sourceText.ToString();
        }

        private async Task<(IVsTextView, IWpfTextViewHost)> GetTextViewAsync(CSharpMoveToNamespaceWorkspace workspace, CancellationToken cancellationToken)
        {
            var documentText = await GetDocumentTextAsync(workspace, cancellationToken).ConfigureAwait(false);

            var roleSet = _textEditorFactoryService.CreateTextViewRoleSet(
                PredefinedTextViewRoles.Document,
                PredefinedTextViewRoles.Editable,
                PredefinedTextViewRoles.Interactive,
                MoveToNamespaceTextViewRole);

            var textViewAdapter = _vsEditorAdaptersFactoryService.CreateVsTextViewAdapter(_serviceProvider, roleSet);
            var bufferAdapter = _vsEditorAdaptersFactoryService.CreateVsTextBufferAdapter(_serviceProvider, _contentType);
            bufferAdapter.InitializeContent(documentText, documentText.Length);

            var textBuffer = _vsEditorAdaptersFactoryService.GetDataBuffer(bufferAdapter);
            workspace.OnDocumentOpened(workspace.NamespaceDocument.Id, textBuffer.AsTextContainer());

            var initView = new[] {
                new INITVIEW()
                {
                    fSelectionMargin = 0,
                    fWidgetMargin = 0,
                    fDragDropMove = 0,
                    IndentStyle = vsIndentStyle.vsIndentStyleNone
                }
            };

            textViewAdapter.Initialize(
                bufferAdapter as IVsTextLines,
                IntPtr.Zero,
                (uint)TextViewInitFlags3.VIF_NO_HWND_SUPPORT,
                initView);

            var textViewHost = _vsEditorAdaptersFactoryService.GetWpfTextViewHost(textViewAdapter);


            return (textViewAdapter, textViewHost);
        }
    }
}
