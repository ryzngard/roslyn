// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable 

using System;
using System.Composition;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.ExtractMembers;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.ExtractClass
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(PredefinedCodeRefactoringProviderNames.ExtractClass)), Shared]
    internal class ExtractClassRefactoringProvider : CodeRefactoringProvider
    {
        private readonly IExtractClassOptionsService? _optionsService;

        [ImportingConstructor]
        [SuppressMessage("RoslynDiagnosticsReliability", "RS0033:Importing constructor should be marked with 'ObsoleteAttribute'", Justification = "Used in tests")]
        public ExtractClassRefactoringProvider(
            [Import(AllowDefault = true)] IExtractClassOptionsService? service)
        {
            _optionsService = service;
        }

        public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var optionsService = _optionsService ?? context.Document.Project.Solution.Workspace.Services.GetService<IExtractClassOptionsService>();
            if (optionsService is null)
            {
                return;
            }

            var (document, span, cancellationToken) = context;

            var analyzer = context.Document.GetRequiredLanguageService<AbstractExtractMemberAnalyzer>();
            var analysis = await analyzer.AnalyzeAsync(document, span, cancellationToken).ConfigureAwait(false);
            if (analysis == ExtractMemberAnalysis.InvalidSelection)
            {
                return;
            }

            Contract.ThrowIfNull(analysis.OriginalType);
            Contract.ThrowIfNull(analysis.OriginalTypeDeclarationNode);

            // Can't extract to a new type if there's already a base. Maybe
            // in the future we could inject a new type inbetween base and
            // current
            if (analysis.OriginalType.SpecialType != SpecialType.System_Object)
            {
                return;
            }

            var action = new ExtractClassWithDialogCodeAction(document, span, optionsService, analysis.OriginalType, analysis.OriginalTypeDeclarationNode, analysis.SelectedMember);
            context.RegisterRefactoring(action, action.Span);
        }
    }
}
