// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Windows.Input;
using Microsoft.VisualStudio.LanguageServices.Implementation.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.MoveToNamespace
{
    class MoveToNamespaceDialogViewModel : AbstractNotifyPropertyChanged
    {
        public MoveToNamespaceDialogViewModel(
            MoveToNamespaceEditorControl editorControl,
            string defaultNamespace)
        {
            EditorControl = editorControl;
            NamespaceName = defaultNamespace;
        }

        public MoveToNamespaceEditorControl EditorControl { get; }

        private string _namespaceName;
        public string NamespaceName
        {
            get => _namespaceName;
            set => SetProperty(ref _namespaceName, value);
        }

        internal void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            MoveToNamespaceEditorControl.HandleKeyDown(sender, e);
        }
    }
}
