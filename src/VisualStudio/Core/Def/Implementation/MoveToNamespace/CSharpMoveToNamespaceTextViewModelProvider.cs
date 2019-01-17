using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Editor;
using Microsoft.VisualStudio.LanguageServices.Implementation.Utilities;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Projection;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.MoveToNamespace
{
    [Export(typeof(ITextViewModelProvider))]
    [ContentType(ContentTypeNames.CSharpContentType)]
    [TextViewRole(VisualStudioMoveToNamespaceOptionsService.MoveToNamespaceTextViewRole)]
    internal class CSharpMoveToNamespaceTextViewModelProvider : ITextViewModelProvider
    {
        [Import]
        public IProjectionBufferFactoryService ProjectionBufferFactoryService { get; set; }

        public ITextViewModel CreateTextViewModel(ITextDataModel dataModel, ITextViewRoleSet roles)
        {
            var namespaceSpan = GetNamespaceSpan(dataModel.DataBuffer.CurrentSnapshot);

            var elisionBuffer = ProjectionBufferFactoryService.CreateElisionBuffer(
                null,
                new NormalizedSnapshotSpanCollection(namespaceSpan),
                ElisionBufferOptions.None);

            return new ElisionBufferTextViewModel(dataModel, elisionBuffer);
        }

        private SnapshotSpan GetNamespaceSpan(ITextSnapshot snapshot)
        {
            var totalLineNumber = snapshot.LineCount;
            var start = snapshot.GetLineFromLineNumber(0).Start;
            for (int i = 0; i < totalLineNumber; i++)
            {
                var currentLine = snapshot.GetLineFromLineNumber(i);
                string text = currentLine.GetText().Trim();
                if (text.StartsWith("namespace", StringComparison.Ordinal))
                {
                    var namespaceLine = snapshot.GetLineFromLineNumber(i + 1);
                    int offset = namespaceLine.GetText().IndexOf("namespace", StringComparison.Ordinal) + "namespace ".Length;
                    start = namespaceLine.Start + offset;

                    return new SnapshotSpan(start, namespaceLine.Length - offset);
                }
            }

            throw new Exception("Unable to find namespace span.");
        }
    }
}
