// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeRefactorings.PullMemberUp.Dialog;
using Microsoft.CodeAnalysis.ExtractMembers;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.PullMemberUp;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Roslyn.Utilities;
using static Microsoft.CodeAnalysis.CodeActions.CodeAction;

namespace Microsoft.CodeAnalysis.CodeRefactorings.PullMemberUp
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(PredefinedCodeRefactoringProviderNames.PullMemberUp)), Shared]
    internal partial class PullMemberUpRefactoringProvider : CodeRefactoringProvider
    {
        private readonly IPullMemberUpOptionsService _service;

        [SuppressMessage("RoslynDiagnosticsReliability", "RS0034:Exported parts should have [ImportingConstructor]", Justification = "Used incorrectly by tests")]
        public PullMemberUpRefactoringProvider(IPullMemberUpOptionsService service)
            => _service = service;

        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public PullMemberUpRefactoringProvider() : this(service: null)
        {
        }

        public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var (document, span, cancellationToken) = context;

            var analyzer = document.GetRequiredLanguageService<AbstractExtractMemberAnalyzer>();
            var analyzerResult = await analyzer.AnalyzeAsync(document, span, cancellationToken).ConfigureAwait(false);

            if (analyzerResult == ExtractMemberAnalysis.InvalidSelection || analyzerResult.SelectedMembers.IsDefaultOrEmpty)
            {
                return;
            }

            Contract.ThrowIfNull(analyzerResult.OriginalType);
            Contract.ThrowIfNull(analyzerResult.OriginalTypeDeclarationNode);

            var validSelectedMembers = analyzerResult
                .SelectedMembers
                .WhereAsArray(pair => MemberAndDestinationValidator.IsMemberValid(pair.symbol));

            // Currently only member selections are supported
            if (validSelectedMembers.IsEmpty)
            {
                return;
            }

            var allDestinations = validSelectedMembers.All(pair => pair.symbol.IsKind(SymbolKind.Field))
                ? analyzerResult.OriginalType.GetBaseTypes()
                : analyzerResult.OriginalType.AllInterfaces.Concat(analyzerResult.OriginalType.GetBaseTypes());

            var validDestinations = allDestinations
                .Where(destination => MemberAndDestinationValidator.IsDestinationValid(document.Project.Solution, destination, cancellationToken))
                .ToImmutableArray();

            if (validDestinations.Length == 0)
            {
                return;
            }

            if (validSelectedMembers.Length == 1)
            {
                // If only one member is being pulled up provide shortcuts as quick actions to do only that member 
                // that won't show a dialog to the user.
                // e.x: Pull "x" up to "IBase" 
                var allActions = validDestinations.Select(destination => MembersPuller.TryComputeCodeAction(document, validSelectedMembers, destination))
                   .WhereNotNull().Concat(new PullMemberUpWithDialogCodeAction(document, analyzerResult.OriginalType, validSelectedMembers, _service))
                   .ToImmutableArray();

                var selectedMember = validSelectedMembers.Single();

                var nestedCodeAction = new CodeActionWithNestedActions(
                    string.Format(FeaturesResources.Pull_0_up, selectedMember.symbol.ToNameDisplayString()),
                    allActions, isInlinable: true);

                context.RegisterRefactoring(nestedCodeAction, selectedMember.node.Span);
            }
            else
            {
                context.RegisterRefactoring(new PullMemberUpWithDialogCodeAction(document, analyzerResult.OriginalType, validSelectedMembers, _service));
            }
        }
    }
}
