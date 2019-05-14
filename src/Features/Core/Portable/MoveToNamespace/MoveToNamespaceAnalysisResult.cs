// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;

namespace Microsoft.CodeAnalysis.MoveToNamespace
{
    internal partial class MoveToNamespaceAnalysisResult
    {
        public static readonly MoveToNamespaceAnalysisResult Invalid = new MoveToNamespaceAnalysisResult();

        public SelectionAnalyzer.SelectionAnalysisResult SelectionAnalysis { get; }
        public bool CanPerform { get; }
        public string OriginalNamespace { get; }
        public ContainerType Container { get; }
        public ImmutableArray<string> Namespaces { get; }

        public MoveToNamespaceAnalysisResult(
            SelectionAnalyzer.SelectionAnalysisResult analyzerResult,
            string originalNamespace,
            ImmutableArray<string> namespaces,
            ContainerType container)
        {
            SelectionAnalysis = analyzerResult;
            OriginalNamespace = originalNamespace;
            Namespaces = namespaces;
            Container = container;
            CanPerform = true;
        }

        private MoveToNamespaceAnalysisResult()
        {
            CanPerform = false;
        }

    }
}
