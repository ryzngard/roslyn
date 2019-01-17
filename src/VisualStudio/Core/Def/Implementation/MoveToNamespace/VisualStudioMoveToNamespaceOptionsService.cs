// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editor;
using Microsoft.CodeAnalysis.Editor.Options;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MoveToNamespace;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Text.Projection;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.MoveToNamespace
{
    [ExportWorkspaceService(typeof(IMoveToNamespaceOptionsService), ServiceLayer.Host), Shared]
    internal class VisualStudioMoveToNamespaceOptionsService : IMoveToNamespaceOptionsService
    {
        public const string MoveToNamespaceTextViewRole = "MoveToNamespace";

        private readonly IThreadingContext _threadingContext;
        private readonly IProjectionBufferFactoryService _projectionBufferFactoryService;
        private readonly ITextEditorFactoryService _textEditorFactoryService;
        private readonly ITextBufferFactoryService _textBufferFactoryService;
        private readonly IContentType _contentType;
        private readonly IEditorCommandHandlerServiceFactory _editorCommandHandlerServiceFactory;
        private readonly IVsEditorAdaptersFactoryService _vsEditorAdaptersFactoryService;
        private readonly Microsoft.VisualStudio.OLE.Interop.IServiceProvider _serviceProvider;
        private readonly IEditorOperationsFactoryService _editorOperationsFactoryService;

        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public VisualStudioMoveToNamespaceOptionsService(
            IGlyphService glyphService,
            IThreadingContext threadingContext,
            IProjectionBufferFactoryService projectionBufferFactoryService,
            ITextEditorFactoryService textEditorFactoryService,
            ITextBufferFactoryService textBufferFactoryService,
            IContentTypeRegistryService contentTypeRegistryService,
            IEditorCommandHandlerServiceFactory editorCommandHandlerServiceFactory,
            IVsEditorAdaptersFactoryService editorAdaptersFactoryService,
            IEditorOperationsFactoryService editorOperationsFactoryService,
            [Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider
            )
        {
            _threadingContext = threadingContext;
            _projectionBufferFactoryService = projectionBufferFactoryService;
            _textEditorFactoryService = textEditorFactoryService;
            _textBufferFactoryService = textBufferFactoryService;
            _contentType = contentTypeRegistryService.GetContentType(ContentTypeNames.CSharpContentType);
            _editorCommandHandlerServiceFactory = editorCommandHandlerServiceFactory;
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

            var workspace = new MoveToNamespaceWorkspace(document.Project, defaultNamespace);

            var (textView, textViewHost) = await GetTextViewAsync(workspace, cancellationToken).ConfigureAwait(false);

            var editorControl = new MoveToNamespaceEditorControl(
                textView,
                textViewHost,
                _editorOperationsFactoryService,
                _vsEditorAdaptersFactoryService,
                _serviceProvider);

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

        private async Task<(ITextBuffer, Span)> GetDocumentTextBufferAsync(MoveToNamespaceWorkspace workspace, CancellationToken cancellationToken)
        {
            var moveToNamespaceService = workspace.NamespaceDocument.GetLanguageService<AbstractMoveToNamespaceService>();
            var namespaceDeclaration = moveToNamespaceService.FindNamespaceDeclaration(await workspace.NamespaceDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false));

            var buffer = _textBufferFactoryService.CreateTextBuffer(
                namespaceDeclaration.GetText().ToString(),
                _contentType);

            return (buffer, new Span(namespaceDeclaration.Span.Start, namespaceDeclaration.Span.Length));
        }

        private async Task<(IVsTextView, IWpfTextViewHost)> GetTextViewAsync(MoveToNamespaceWorkspace workspace, CancellationToken cancellationToken)
        {
            var (buffer, namespaceSpan) = await GetDocumentTextBufferAsync(workspace, cancellationToken).ConfigureAwait(false);
            var textContainer = buffer.AsTextContainer();

            var roleSet = _textEditorFactoryService.CreateTextViewRoleSet(
                PredefinedTextViewRoles.Document,
                PredefinedTextViewRoles.Editable,
                PredefinedTextViewRoles.Interactive,
                MoveToNamespaceTextViewRole);

            var textViewAdapter = _vsEditorAdaptersFactoryService.CreateVsTextViewAdapter(_serviceProvider, roleSet);
            var bufferAdapter = _vsEditorAdaptersFactoryService.CreateVsTextBufferAdapter(_serviceProvider, _contentType);
            bufferAdapter.InitializeContent(textContainer.CurrentText.ToString(), textContainer.CurrentText.Length);

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

        internal class MoveToNamespaceWorkspace : Workspace
        {
            private readonly string _originalNamespace;

            public MoveToNamespaceWorkspace(Project project, string @namespace)
                : base(project.Solution.Workspace.Services.HostServices, nameof(MoveToNamespaceWorkspace))
            {
                var solution = project.Solution;
                _originalNamespace = @namespace;

                // The solution we are handed is still parented by the original workspace. We want to
                // inherit it's "no partial solutions" flag so that way this workspace will also act
                // deterministically if we're in unit tests
                this.TestHookPartialSolutionsDisabled = solution.Workspace.TestHookPartialSolutionsDisabled;

                // Create a new document to hold the temporary code
                NamespaceDocumentId = DocumentId.CreateNewId(project.Id);
                this.SetCurrentSolution(solution.AddDocument(NamespaceDocumentId, "namespace_tmp.cs", GetDocumentText()));

                Options = Options.WithChangedOption(EditorCompletionOptions.UseSuggestionMode, true);
            }

            private string GetDocumentText()
            {
                return $@"
namespace {_originalNamespace}
{{
}}
";
            }

            public Document NamespaceDocument => this.CurrentSolution.GetDocument(this.NamespaceDocumentId);
            public DocumentId NamespaceDocumentId { get; }
        }
    }
}
