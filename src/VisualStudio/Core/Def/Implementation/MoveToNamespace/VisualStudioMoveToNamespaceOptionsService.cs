// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editor;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.MoveToNamespace;
using Microsoft.CodeAnalysis.Notification;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding;
using Microsoft.VisualStudio.Text.Projection;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.MoveToNamespace
{
    [ExportWorkspaceService(typeof(IMoveToNamespaceOptionsService), ServiceLayer.Host), Shared]
    internal class VisualStudioMoveToNamespaceOptionsService : IMoveToNamespaceOptionsService
    {
        private readonly IThreadingContext _threadingContext;
        private readonly IProjectionBufferFactoryService _projectionBufferFactoryService;
        private readonly ITextEditorFactoryService _textEditorFactoryService;
        private readonly ITextBufferFactoryService _textBufferFactoryService;
        private readonly IContentType _contentType;
        private readonly IEditorCommandHandlerServiceFactory _editorCommandHandlerServiceFactory;

        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public VisualStudioMoveToNamespaceOptionsService(
            IGlyphService glyphService,
            IThreadingContext threadingContext,
            IProjectionBufferFactoryService projectionBufferFactoryService,
            ITextEditorFactoryService textEditorFactoryService,
            ITextBufferFactoryService textBufferFactoryService,
            IContentTypeRegistryService contentTypeRegistryService,
            IEditorCommandHandlerServiceFactory editorCommandHandlerServiceFactory)
        {
            _threadingContext = threadingContext;
            _projectionBufferFactoryService = projectionBufferFactoryService;
            _textEditorFactoryService = textEditorFactoryService;
            _textBufferFactoryService = textBufferFactoryService;
            _contentType = contentTypeRegistryService.GetContentType(ContentTypeNames.CSharpContentType);
            _editorCommandHandlerServiceFactory = editorCommandHandlerServiceFactory;
        }

        public async Task<MoveToNamespaceOptionsResult> GetChangeNamespaceOptionsAsync(
            Document document,
            string defaultNamespace,
            CancellationToken cancellationToken)
        {
            await _threadingContext.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var workspace = new MoveToNamespaceWorkspace(document.Project, defaultNamespace);

            var textView = await GetTextViewAsync(workspace, cancellationToken).ConfigureAwait(false);
            var viewHost = _textEditorFactoryService.CreateTextViewHost(textView, setFocus: true);

            var commanding = _editorCommandHandlerServiceFactory.GetService(textView);

            var viewModel = new MoveToNamespaceDialogViewModel(
                viewHost,
                defaultNamespace,
                commanding);

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

        private async Task<(ITextBuffer, Span)> GetDocumentTextBofferAsync(MoveToNamespaceWorkspace workspace, CancellationToken cancellationToken)
        {
            var moveToNamespaceService = workspace.NamespaceDocument.GetLanguageService<AbstractMoveToNamespaceService>();
            var namespaceDeclaration = moveToNamespaceService.FindNamespaceDeclaration(await workspace.NamespaceDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false));

            var buffer = _textBufferFactoryService.CreateTextBuffer(
                namespaceDeclaration.GetText().ToString(),
                _contentType);

            return (buffer, new Span(namespaceDeclaration.Span.Start, namespaceDeclaration.Span.Length));
        }

        private async Task<IWpfTextView> GetTextViewAsync(MoveToNamespaceWorkspace workspace, CancellationToken cancellationToken)
        {
            var (buffer, namespaceSpan) = await GetDocumentTextBofferAsync(workspace, cancellationToken).ConfigureAwait(false);

            var textContainer = buffer.AsTextContainer();
            workspace.OnDocumentOpened(workspace.NamespaceDocument.Id, textContainer);

            var elisionBuffer = _projectionBufferFactoryService.CreateElisionBuffer(
                null,
                new Text.NormalizedSnapshotSpanCollection(buffer.CurrentSnapshot, namespaceSpan),
                ElisionBufferOptions.None,
                _projectionBufferFactoryService.ProjectionContentType);

            var roleSet = _textEditorFactoryService.CreateTextViewRoleSet(
                PredefinedTextViewRoles.Document,
                PredefinedTextViewRoles.Editable,
                PredefinedTextViewRoles.Interactive);

            return _textEditorFactoryService.CreateTextView(elisionBuffer, roleSet);
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
            }

            private string GetDocumentText()
            {
                return $@"
namespace {_originalNamespace}
{{
}}
";
            }

            public TextSpan NamespaceSpan { get; }
            public Document NamespaceDocument => this.CurrentSolution.GetDocument(this.NamespaceDocumentId);
            public DocumentId NamespaceDocumentId { get; }
        }
    }
}
