// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
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
            var analyzerResult = await analyzer.AnalyzeAsync(document, span, cancellationToken).ConfigureAwait(false); ;

            if (analyzerResult == ExtractMemberAnalysis.InvalidSelection)
            {
                return;
            }

            Contract.ThrowIfNull(analyzerResult.OriginalType);
            Contract.ThrowIfNull(analyzerResult.OriginalTypeDeclarationNode);

            // Currently only member selections are supported
            if (analyzerResult.SelectedMember is null ||
                !MemberAndDestinationValidator.IsMemberValid(analyzerResult.SelectedMember))
            {
                return;
            }

            var allDestinations = FindAllValidDestinations(
                analyzerResult.SelectedMember,
                document.Project.Solution,
                cancellationToken);

            if (allDestinations.Length == 0)
            {
                return;
            }

            var allActions = allDestinations.Select(destination => MembersPuller.TryComputeCodeAction(document, analyzerResult.SelectedMember, destination))
                .WhereNotNull().Concat(new PullMemberUpWithDialogCodeAction(document, analyzerResult.SelectedMember, _service))
                .ToImmutableArray();

            var nestedCodeAction = new CodeActionWithNestedActions(
                string.Format(FeaturesResources.Pull_0_up, analyzerResult.SelectedMember.ToNameDisplayString()),
                allActions, isInlinable: true);
            context.RegisterRefactoring(nestedCodeAction, analyzerResult.SelectedMemberNode.Span);
        }

        private static ImmutableArray<INamedTypeSymbol> FindAllValidDestinations(
            ISymbol selectedMember,
            Solution solution,
            CancellationToken cancellationToken)
        {
            var containingType = selectedMember.ContainingType;
            var allDestinations = selectedMember.IsKind(SymbolKind.Field)
                ? containingType.GetBaseTypes().ToImmutableArray()
                : containingType.AllInterfaces.Concat(containingType.GetBaseTypes()).ToImmutableArray();

            return allDestinations.WhereAsArray(destination => MemberAndDestinationValidator.IsDestinationValid(solution, destination, cancellationToken));
        }
    }
}
