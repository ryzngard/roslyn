// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using Microsoft.CodeAnalysis.Editor;
using Microsoft.CodeAnalysis.Editor.Commands;
using Microsoft.VisualStudio.LanguageServices.Implementation.Utilities;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.MoveToNamespace
{
    class MoveToNamespaceDialogViewModel : AbstractNotifyPropertyChanged
    {
        private readonly IEditorCommandHandlerService _commanding;
        private static Action Noop { get; } = new Action(() => { });
        private static Func<Commanding.CommandState> Unspecified { get; } = () => Commanding.CommandState.Unspecified;

        public MoveToNamespaceDialogViewModel(
            IWpfTextViewHost textViewHost,
            string defaultNamespace,
            IEditorCommandHandlerService commanding)
        {
            TextView = textViewHost;
            NamespaceName = defaultNamespace;
            _commanding = commanding;
        }

        private string _namespaceName;
        public string NamespaceName
        {
            get => _namespaceName;
            set => SetProperty(ref _namespaceName, value);
        }

        internal void OnPreviewKeyDown(KeyEventArgs e)
        {
            QueryAndExecute((v, b) => new Text.Editor.Commanding.Commands.TypeCharCommandArgs(v, b, GetCharFromKey(e.Key)));
        }

        public enum MapType : uint
        {
            MAPVK_VK_TO_VSC = 0x0,
            MAPVK_VSC_TO_VK = 0x1,
            MAPVK_VK_TO_CHAR = 0x2,
            MAPVK_VSC_TO_VK_EX = 0x3,
        }

        [DllImport("user32.dll")]
        public static extern int ToUnicode(
            uint wVirtKey,
            uint wScanCode,
            byte[] lpKeyState,
            [Out, MarshalAs(UnmanagedType.LPWStr, SizeParamIndex = 4)]
            StringBuilder pwszBuff,
            int cchBuff,
            uint wFlags);

        [DllImport("user32.dll")]
        public static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, MapType uMapType);

        public static char GetCharFromKey(Key key)
        {
            char ch = '\0';

            int virtualKey = KeyInterop.VirtualKeyFromKey(key);
            byte[] keyboardState = new byte[256];
            GetKeyboardState(keyboardState);

            uint scanCode = MapVirtualKey((uint)virtualKey, MapType.MAPVK_VK_TO_VSC);
            StringBuilder stringBuilder = new StringBuilder(2);

            int result = ToUnicode((uint)virtualKey, scanCode, keyboardState, stringBuilder, stringBuilder.Capacity, 0);
            switch (result)
            {
                case -1:
                    break;
                case 0:
                    break;
                case 1:
                    {
                        ch = stringBuilder[0];
                        break;
                    }
                default:
                    {
                        ch = stringBuilder[0];
                        break;
                    }
            }
            return ch;
        }

        /// <summary>
        /// Queries and potentially executes <see cref="ICommandHandler"/>s
        /// that handle <see cref="CommandArgs"/> produced by <paramref name="argsFactory"/>.
        /// </summary>
        /// <typeparam name="T">Command arguments are used to invoke matching <see cref="ICommandHandler{T}"/></typeparam>
        /// <param name="argsFactory">Function that returns an instance of <see cref="CommandArgs"/></param>
        /// <param name="queryStatus">Method invoked as legacy IOleCommandFilter QueryStatus</param>
        /// <param name="execute">Method invoked as legacy IOleCommandFilter Execute</param>
        private void QueryAndExecute<T>(Func<ITextView, ITextBuffer, T> argsFactory, Func<Commanding.CommandState> queryStatus, Action execute) where T : EditorCommandArgs
        {
            var state = _commanding.GetCommandState(argsFactory, queryStatus);
            if (state.IsAvailable)
                _commanding.Execute(argsFactory, execute);
        }

        /// <summary>
        /// Queries and potentially executes <see cref="ICommandHandler"/>s
        /// that handle <see cref="CommandArgs"/> produced by <paramref name="argsFactory"/>.
        /// </summary>
        /// <typeparam name="T">Command arguments are used to invoke matching <see cref="ICommandHandler{T}"/></typeparam>
        /// <param name="argsFactory">Function that returns an instance of <see cref="CommandArgs"/></param>
        public void QueryAndExecute<T>(Func<ITextView, ITextBuffer, T> argsFactory) where T : EditorCommandArgs
        {
            var state = _commanding.GetCommandState(argsFactory, Unspecified);
            if (state.IsAvailable)
                _commanding.Execute(argsFactory, Noop);
        }

        public IWpfTextViewHost TextView { get; }
    }
}
