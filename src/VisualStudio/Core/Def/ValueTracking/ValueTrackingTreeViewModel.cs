// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Editor.ReferenceHighlighting;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using EnvDTE;
using Microsoft.CodeAnalysis.ValueTracking;
using EnvDTE80;
using Microsoft.VisualStudio.Language.Intellisense;
using System.Collections.Immutable;
using Roslyn.Utilities;
using Microsoft.CodeAnalysis.Classification;

namespace Microsoft.VisualStudio.LanguageServices.ValueTracking
{
    internal class ValueTrackingTreeViewModel : INotifyPropertyChanged
    {
        private readonly IClassificationFormatMapService _classificationFormatMapService;
        private readonly IGlyphService _glyphService;
        private readonly IThreadingContext _threadingContext;
        private readonly IEditorFormatMapService _formatMapService;
        private readonly ClassificationTypeMap _typeMap;
        private Brush? _highlightBrush;
        public Brush? HighlightBrush
        {
            get => _highlightBrush;
            set => SetProperty(ref _highlightBrush, value);
        }

        public ObservableCollection<TreeItemViewModel> Roots { get; } = new();
        public string AutomationName => ServicesVSResources.Value_Tracking;

        private TreeViewItemBase? _selectedItem;
        public TreeViewItemBase? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        private string _selectedItemFile = "";
        public string SelectedItemFile
        {
            get => _selectedItemFile;
            set => SetProperty(ref _selectedItemFile, value);
        }

        private int _selectedItemLine;
        public int SelectedItemLine
        {
            get => _selectedItemLine;
            set => SetProperty(ref _selectedItemLine, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        private int _loadingCount;
        public int LoadingCount
        {
            get => _loadingCount;
            set => SetProperty(ref _loadingCount, value);
        }

        public bool ShowDetails => SelectedItem is TreeItemViewModel;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ValueTrackingTreeViewModel(
            IClassificationFormatMapService classificationFormatMapService,
            IEditorFormatMapService formatMapService,
            ClassificationTypeMap typeMap,
            IGlyphService glyphService,
            IThreadingContext threadingContext)
        {
            _classificationFormatMapService = classificationFormatMapService;
            _formatMapService = formatMapService;
            _typeMap = typeMap;
            _glyphService = glyphService;
            _threadingContext = threadingContext;
            var editorMap = _formatMapService.GetEditorFormatMap("text");
            SetHighlightBrush(editorMap);

            editorMap.FormatMappingChanged += (s, e) =>
            {
                SetHighlightBrush(editorMap);
            };

            PropertyChanged += Self_PropertyChanged;
        }

        private void SetHighlightBrush(IEditorFormatMap editorMap)
        {
            var properties = editorMap.GetProperties(ReferenceHighlightTag.TagId);
            HighlightBrush = properties["Background"] as Brush;
        }

        private void Self_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectedItem))
            {
                if (SelectedItem is not null)
                {
                    SelectedItem.IsNodeSelected = true;

                    if (SelectedItem is TreeItemViewModel itemWithInfo)
                    {
                        SelectedItemFile = itemWithInfo?.FileName ?? "";
                        SelectedItemLine = itemWithInfo?.LineNumber ?? 0;
                    }
                    else
                    {
                        SelectedItemFile = string.Empty;
                        SelectedItemLine = 0;
                    }
                }

                NotifyPropertyChanged(nameof(ShowDetails));
            }

            if (e.PropertyName == nameof(LoadingCount))
            {
                IsLoading = LoadingCount > 0;
            }
        }

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string name = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            NotifyPropertyChanged(name);
        }

        private void NotifyPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        internal void OnShow(ITextView textView, TextSpan textSpan, CodeAnalysis.Document document)
        {
            Task.Run(async () =>
            {
                try
                {
                    LoadingCount++;
                    await OnShowAsync(textView, textSpan, document, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    LoadingCount--;
                }
            });
        }

        private async Task OnShowAsync(ITextView textView, TextSpan textSpan, CodeAnalysis.Document document, CancellationToken cancellationToken)
        {
            var solution = document.Project.Solution;
            await ShowMessageAsync("Calculating...", cancellationToken).ConfigureAwait(false);

            var symbol = await GetSelectedSymbolAsync(textSpan, document, cancellationToken).ConfigureAwait(false);
            if (symbol is null)
            {
                await ShowMessageAsync("Invalid Symbol", cancellationToken).ConfigureAwait(false);
                return;
            }

            var syntaxTree = document.GetRequiredSyntaxTreeSynchronously(cancellationToken);
            var location = Location.Create(syntaxTree, textSpan);

            var item = await ValueTrackedItem.TryCreateAsync(solution, location, symbol, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (item is null)
            {
                await ShowMessageAsync("Invalid Symbol", cancellationToken).ConfigureAwait(false);
                return;
            }

            var classificationFormatMap = _classificationFormatMapService.GetClassificationFormatMap(textView);
            var valueTrackingService = solution.Workspace.Services.GetRequiredService<IValueTrackingService>();
            var childItems = await valueTrackingService.TrackValueSourceAsync(solution, item, cancellationToken).ConfigureAwait(false);
            var childViewModels = childItems.SelectAsArray(child => CreateViewModel(child));

            RoslynDebug.AssertNotNull(location.SourceTree);

            var sourceText = await location.SourceTree.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var documentSpan = await ClassifiedSpansAndHighlightSpanFactory.GetClassifiedDocumentSpanAsync(document, location.SourceSpan, cancellationToken).ConfigureAwait(false);
            var classificationResult = await ClassifiedSpansAndHighlightSpanFactory.ClassifyAsync(documentSpan, cancellationToken).ConfigureAwait(false);

            var root = new TreeItemViewModel(
                location.SourceSpan,
                sourceText,
                document.Id,
                document.FilePath ?? document.Name,
                symbol.GetGlyph(),
                classificationResult.ClassifiedSpans,
                this,
                _glyphService,
                _threadingContext,
                classificationFormatMap,
                 _typeMap,
                solution.Workspace,
                children: childViewModels);

            await _threadingContext.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Roots.Clear();
            Roots.Add(root);

            TreeItemViewModel CreateViewModel(ValueTrackedItem valueTrackedItem, ImmutableArray<TreeItemViewModel> children = default)
            {
                var document = solution.GetRequiredDocument(valueTrackedItem.DocumentId);
                var fileName = document.FilePath ?? document.Name;

                return new ValueTrackedTreeItemViewModel(
                   valueTrackedItem,
                   solution,
                   this,
                   _glyphService,
                   valueTrackingService,
                   classificationFormatMap,
                   _typeMap,
                   _threadingContext,
                   fileName,
                   children);
            }
        }

        private async Task ShowMessageAsync(string message, CancellationToken cancellationToken)
        {
            await _threadingContext.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            // Do something here
        }

        private static async Task<ISymbol?> GetSelectedSymbolAsync(TextSpan textSpan, CodeAnalysis.Document document, CancellationToken cancellationToken)
        {
            var root = await document.GetRequiredSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var selectedNode = root.FindNode(textSpan);
            if (selectedNode is null)
            {
                return null;
            }

            var semanticModel = await document.GetRequiredSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var selectedSymbol =
                semanticModel.GetSymbolInfo(selectedNode, cancellationToken).Symbol
                ?? semanticModel.GetDeclaredSymbol(selectedNode, cancellationToken);

            if (selectedSymbol is null)
            {
                return null;
            }

            return selectedSymbol switch
            {
                ILocalSymbol
                or IPropertySymbol { SetMethod: not null }
                or IFieldSymbol { IsReadOnly: false }
                or IEventSymbol
                or IParameterSymbol
                => selectedSymbol,

                _ => null
            };
        }
    }
}
