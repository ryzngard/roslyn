// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editor.Options;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.MoveToNamespace
{
    internal partial class VisualStudioMoveToNamespaceOptionsService
    {
        internal class CSharpMoveToNamespaceWorkspace : Workspace
        {
            private readonly string _originalNamespace;

            public CSharpMoveToNamespaceWorkspace(Project project, string @namespace)
                : base(project.Solution.Workspace.Services.HostServices, nameof(CSharpMoveToNamespaceWorkspace))
            {
                var solution = project.Solution;
                _originalNamespace = @namespace;

                // The solution we are handed is still parented by the original workspace. We want to
                // inherit it's "no partial solutions" flag so that way this workspace will also act
                // deterministically if we're in unit tests
                this.TestHookPartialSolutionsDisabled = solution.Workspace.TestHookPartialSolutionsDisabled;

                // Create a new document to hold the temporary code
                NamespaceDocumentId = DocumentId.CreateNewId(project.Id);
                this.SetCurrentSolution(solution.AddDocument(NamespaceDocumentId, Guid.NewGuid().ToString(), GetDocumentText()));

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
