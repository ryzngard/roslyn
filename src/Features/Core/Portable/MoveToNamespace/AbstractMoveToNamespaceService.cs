// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.ChangeNamespace;
using Microsoft.CodeAnalysis.CodeRefactorings.MoveType;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.LanguageServices;
using System.Text;
using System.Diagnostics;
using Microsoft.CodeAnalysis.Editing;

namespace Microsoft.CodeAnalysis.MoveToNamespace
{
    internal interface IMoveToNamespaceService : ILanguageService
    {
        Task<ImmutableArray<AbstractMoveToNamespaceCodeAction>> GetCodeActionsAsync(Document document, TextSpan span, CancellationToken cancellationToken);
        Task<MoveToNamespaceAnalysisResult> AnalyzeTypeAtSelectionAsync(Document document, TextSpan selection, CancellationToken cancellationToken);
        Task<MoveToNamespaceResult> MoveToNamespaceAsync(MoveToNamespaceAnalysisResult analysisResult, string targetNamespace, CancellationToken cancellationToken);
        MoveToNamespaceOptionsResult GetChangeNamespaceOptions(Document document, string defaultNamespace, ImmutableArray<string> namespaces);
    }

    internal abstract class AbstractMoveToNamespaceService<TNamespaceDeclarationSyntax, TNamedTypeDeclarationSyntax>
        : IMoveToNamespaceService
        where TNamespaceDeclarationSyntax : SyntaxNode
        where TNamedTypeDeclarationSyntax : SyntaxNode

    {
        protected abstract string GetNamespaceName(TNamespaceDeclarationSyntax namespaceSyntax);
        protected abstract string GetNamespaceName(TNamedTypeDeclarationSyntax namedTypeSyntax);
        protected abstract bool IsContainedInNamespaceDeclaration(TNamespaceDeclarationSyntax namespaceSyntax, TextSpan span);

        public async Task<ImmutableArray<AbstractMoveToNamespaceCodeAction>> GetCodeActionsAsync(
            Document document,
            TextSpan span,
            CancellationToken cancellationToken)
        {
            var typeAnalysisResult = await AnalyzeTypeAtSelectionAsync(document, span, cancellationToken).ConfigureAwait(false);

            if (typeAnalysisResult.CanPerform)
            {
                return ImmutableArray.Create(AbstractMoveToNamespaceCodeAction.Generate(this, typeAnalysisResult));
            }

            return ImmutableArray<AbstractMoveToNamespaceCodeAction>.Empty;
        }

        public async Task<MoveToNamespaceAnalysisResult> AnalyzeTypeAtSelectionAsync(
            Document document,
            TextSpan span,
            CancellationToken cancellationToken)
        {
            var analysis = await SelectionAnalyzer.AnalyzeAsync(document, span, cancellationToken).ConfigureAwait(false);

            var analysisResult = analysis.Context switch
            {
                TNamedTypeDeclarationSyntax _ => await TryAnalyzeNamedTypeAsync(analysis, cancellationToken).ConfigureAwait(false),
                _ => await TryAnalyzeNamespaceAsync(analysis, cancellationToken).ConfigureAwait(false),
            };

            return analysisResult;
        }

        private async Task<MoveToNamespaceAnalysisResult> TryAnalyzeNamespaceAsync(
            SelectionAnalyzer.SelectionAnalysisResult analysis, CancellationToken cancellationToken)
        {
            var document = analysis.Document;
            var intersectedTypeNodes = analysis.IntersectedNodes.OfType<TNamedTypeDeclarationSyntax>().ToImmutableArray();

            if (analysis.Context is TNamespaceDeclarationSyntax namespaceDeclarationSyntax && intersectedTypeNodes.Any())
            {
                return new MoveToNamespaceAnalysisResult(
                    analysis,
                    GetNamespaceName(namespaceDeclarationSyntax),
                    await GetNamespacesAsync(document, cancellationToken).ConfigureAwait(false),
                    MoveToNamespaceAnalysisResult.ContainerType.MultipleNamedTypes);
            }

            var declarationSyntax = analysis.Context.FirstAncestorOrSelf<TNamespaceDeclarationSyntax>();

            if (declarationSyntax == default)
            {
                // There are no namespace declarations at or above the context
                return MoveToNamespaceAnalysisResult.Invalid;
            }

            if (analysis.Selection.IsEmpty && !IsContainedInNamespaceDeclaration(declarationSyntax, analysis.Selection))
            {
                // The selection is a single cursor position but not contained within the declaration syntax
                // that encompasses or is the context
                return MoveToNamespaceAnalysisResult.Invalid;
            }

            if (ContainsNamespaceDeclaration(declarationSyntax) || ContainsMultipleNamespaceInSpine(declarationSyntax))
            {
                // The namespace declaration has namespace declarations inside it or is 
                // contained inside another namespace declaration
                return MoveToNamespaceAnalysisResult.Invalid;
            }

            return new MoveToNamespaceAnalysisResult(
                analysis.WithContext(declarationSyntax),
                GetNamespaceName(declarationSyntax),
                await GetNamespacesAsync(document, cancellationToken).ConfigureAwait(false), 
                MoveToNamespaceAnalysisResult.ContainerType.Namespace);
        }

        private async Task<MoveToNamespaceAnalysisResult> TryAnalyzeNamedTypeAsync(
            SelectionAnalyzer.SelectionAnalysisResult analysis, CancellationToken cancellationToken)
        {
            var node = analysis.Context;

            // Multiple nested namespaces are currently not supported
            if (ContainsMultipleNamespaceInSpine(node) || ContainsMultipleTypesInSpine(node))
            {
                return MoveToNamespaceAnalysisResult.Invalid;
            }

            if (node is TNamedTypeDeclarationSyntax namedTypeDeclarationSyntax)
            {
                var namespaceName = GetNamespaceName(namedTypeDeclarationSyntax);
                var namespaces = await GetNamespacesAsync(analysis.Document, cancellationToken).ConfigureAwait(false);
                return new MoveToNamespaceAnalysisResult(analysis, namespaceName, namespaces, MoveToNamespaceAnalysisResult.ContainerType.NamedType);
            }

            return null;
        }

        private bool ContainsNamespaceDeclaration(SyntaxNode node)
            => node.DescendantNodes().OfType<TNamespaceDeclarationSyntax>().Any();

        private static bool ContainsMultipleNamespaceInSpine(SyntaxNode node)
            => node.AncestorsAndSelf().OfType<TNamespaceDeclarationSyntax>().Count() > 1;

        private static bool ContainsMultipleTypesInSpine(SyntaxNode node)
            => node.AncestorsAndSelf().OfType<TNamedTypeDeclarationSyntax>().Count() > 1;

        public Task<MoveToNamespaceResult> MoveToNamespaceAsync(
            MoveToNamespaceAnalysisResult analysisResult,
            string targetNamespace,
            CancellationToken cancellationToken)
        {
            if (!analysisResult.CanPerform)
            {
                return Task.FromResult(MoveToNamespaceResult.Failed);
            }

            switch (analysisResult.Container)
            {
                case MoveToNamespaceAnalysisResult.ContainerType.Namespace:
                    return MoveItemsInNamespaceAsync(analysisResult.SelectionAnalysis.Document, (TNamespaceDeclarationSyntax)analysisResult.SelectionAnalysis.Context, targetNamespace, cancellationToken);
                case MoveToNamespaceAnalysisResult.ContainerType.NamedType:
                    return MoveTypeToNamespaceAsync(analysisResult.SelectionAnalysis, targetNamespace, cancellationToken);
                case MoveToNamespaceAnalysisResult.ContainerType.MultipleNamedTypes:
                    return MoveMultipleTypesToNamespaceAsync(analysisResult.SelectionAnalysis, targetNamespace, cancellationToken);
                default:
                    throw new InvalidOperationException();
            }
        }

        private static async Task<MoveToNamespaceResult> MoveItemsInNamespaceAsync(
            Document document,
            TNamespaceDeclarationSyntax namespaceDecl,
            string targetNamespace,
            CancellationToken cancellationToken)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var containerSymbol = (INamespaceSymbol)semanticModel.GetDeclaredSymbol(namespaceDecl);
            var members = containerSymbol.GetMembers();
            var newNameOriginalSymbolMapping = members
                .ToImmutableDictionary(symbol => GetNewSymbolName(symbol, targetNamespace), symbol => (ISymbol)symbol);

            var changeNamespaceService = document.GetLanguageService<IChangeNamespaceService>();
            if (changeNamespaceService == null)
            {
                return MoveToNamespaceResult.Failed;
            }

            var originalSolution = document.Project.Solution;
            var typeDeclarationsInContainer = namespaceDecl.DescendantNodes(syntaxNode => syntaxNode is TNamedTypeDeclarationSyntax).ToImmutableArray();

            var changedSolution = await changeNamespaceService.ChangeNamespaceAsync(
                document,
                namespaceDecl,
                targetNamespace,
                cancellationToken).ConfigureAwait(false);

            return new MoveToNamespaceResult(originalSolution, changedSolution, document.Id, newNameOriginalSymbolMapping);
        }

        private static async Task<MoveToNamespaceResult> MoveMultipleTypesToNamespaceAsync(
            SelectionAnalyzer.SelectionAnalysisResult selectionAnalysis,
            string targetNamespace,
            CancellationToken cancellationToken)
        {
            var intersectionAnnotation = new SyntaxAnnotation();

            // Mark all of the nodes, except the first, so they can be tracked across all of the edits.
            // First is ignored because it's already moved to the right place
            var annotationEditor = await DocumentEditor.CreateAsync(selectionAnalysis.Document, cancellationToken).ConfigureAwait(false);
            foreach (var node in selectionAnalysis.IntersectedNodes.Skip(1))
            {
                annotationEditor.ReplaceNode(node, node.WithAdditionalAnnotations(intersectionAnnotation));
            }

            var (modifiedDocument, namespaceNode) = await SplitNodeIntoOwnNamespace(annotationEditor.GetChangedDocument(), selectionAnalysis.IntersectedNodes.First(), cancellationToken).ConfigureAwait(false);

            if (modifiedDocument == null || namespaceNode == null)
            {
                return MoveToNamespaceResult.Failed;
            }

            // Add the remaining nodes into the new namespace
            var editor = await DocumentEditor.CreateAsync(modifiedDocument, cancellationToken).ConfigureAwait(false);
            editor.TrackNode(namespaceNode);

            var syntaxRoot = editor.OriginalRoot;
            var annotatedNodes = syntaxRoot.GetAnnotatedNodes(intersectionAnnotation);

            if (annotatedNodes.Any())
            {
                var namespaceContainer = annotatedNodes.First().FirstAncestorOrSelf<TNamespaceDeclarationSyntax>();

                editor.TrackNode(namespaceContainer);

                foreach (var node in annotatedNodes)
                {
                    editor.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia);
                    editor.AddMember(namespaceNode, node);
                }

                modifiedDocument = editor.GetChangedDocument();

                var newRoot = await modifiedDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

                namespaceNode = newRoot.GetCurrentNode(namespaceNode);
                namespaceContainer = newRoot.GetCurrentNode(namespaceContainer);

                // Remove an empty namespace declaration
                if (!namespaceContainer.ChildNodes().OfType<TNamedTypeDeclarationSyntax>().Any())
                {
                    editor = await DocumentEditor.CreateAsync(modifiedDocument, cancellationToken).ConfigureAwait(false);
                    editor.TrackNode(namespaceNode);

                    editor.RemoveNode(namespaceContainer);

                    modifiedDocument = editor.GetChangedDocument();

                    newRoot = await modifiedDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                    namespaceNode = newRoot.GetCurrentNode(namespaceNode);
                }
            }

            return await MoveItemsInNamespaceAsync(
                modifiedDocument,
                namespaceNode,
                targetNamespace,
                cancellationToken).ConfigureAwait(false);
        }

        private static async Task<MoveToNamespaceResult> MoveTypeToNamespaceAsync(
            SelectionAnalyzer.SelectionAnalysisResult selectionAnalysis,
            string targetNamespace,
            CancellationToken cancellationToken)
        {
            var (modifiedDocument, namespaceNode) = await SplitNodeIntoOwnNamespace(selectionAnalysis.Document, selectionAnalysis.Context, cancellationToken).ConfigureAwait(false);

            if (modifiedDocument == null || namespaceNode == null)
            {
                return MoveToNamespaceResult.Failed;
            }

            return await MoveItemsInNamespaceAsync(
                modifiedDocument,
                namespaceNode,
                targetNamespace,
                cancellationToken).ConfigureAwait(false);
        }

        private static async Task<(Document, TNamespaceDeclarationSyntax)> SplitNodeIntoOwnNamespace(
            Document document, SyntaxNode container, CancellationToken cancellationToken)
        {
            var moveTypeService = document.GetLanguageService<IMoveTypeService>();
            if (moveTypeService == null)
            {
                return (null, null);
            }

            // The move service expects a single position, not a full selection
            // See https://github.com/dotnet/roslyn/issues/34643
            var moveSpan = new TextSpan(container.FullSpan.Start, 0);

            var modifiedSolution = await moveTypeService.GetModifiedSolutionAsync(
                document,
                moveSpan,
                MoveTypeOperationKind.MoveTypeNamespaceScope,
                cancellationToken).ConfigureAwait(false);

            var modifiedDocument = modifiedSolution.GetDocument(document.Id);
            var syntaxRoot = await modifiedDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            var syntaxNode = syntaxRoot.GetAnnotatedNodes(AbstractMoveTypeService.NamespaceScopeMovedAnnotation).SingleOrDefault();
            if (syntaxNode == null)
            {
                syntaxNode = container.FirstAncestorOrSelf<TNamespaceDeclarationSyntax>();
            }

            return (modifiedDocument, syntaxNode as TNamespaceDeclarationSyntax);
        }

        private static string GetNewSymbolName(ISymbol symbol, string targetNamespace)
        {
            Debug.Assert(symbol != null);
            return targetNamespace + symbol.ToDisplayString().Substring(symbol.ContainingNamespace.ToDisplayString().Length);
        }

        private static SymbolDisplayFormat QualifiedNamespaceFormat = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

        protected static string GetQualifiedName(INamespaceSymbol namespaceSymbol)
            => namespaceSymbol.ToDisplayString(QualifiedNamespaceFormat);

        private static async Task<ImmutableArray<string>> GetNamespacesAsync(Document document, CancellationToken cancellationToken)
        {
            var compilation = await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

            return compilation.GlobalNamespace.GetAllNamespaces(cancellationToken)
                .Where(n => n.NamespaceKind == NamespaceKind.Module && n.ContainingAssembly == compilation.Assembly)
                .Select(GetQualifiedName)
                .ToImmutableArray();
        }

        public MoveToNamespaceOptionsResult GetChangeNamespaceOptions(
            Document document,
            string defaultNamespace,
            ImmutableArray<string> namespaces)
        {
            var syntaxFactsService = document.GetLanguageService<ISyntaxFactsService>();
            var moveToNamespaceOptionsService = document.Project.Solution.Workspace.Services.GetService<IMoveToNamespaceOptionsService>();

            if (moveToNamespaceOptionsService == null)
            {
                return MoveToNamespaceOptionsResult.Cancelled;
            }

            return moveToNamespaceOptionsService.GetChangeNamespaceOptions(
                defaultNamespace,
                namespaces,
                syntaxFactsService);
        }
    }
}
