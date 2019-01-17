using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.MoveToNamespace
{
    class MoveToNamespaceEditorControl : FrameworkElement, IOleCommandTarget
    {
        private readonly IEditorOperationsFactoryService _editorOperationsFactoryService;
        private readonly IVsEditorAdaptersFactoryService _editorAdaptersFactoryService;
        private readonly OLE.Interop.IServiceProvider _oleServiceProvider;
        private IEditorOperations _editorOperations;
        private IOleCommandTarget _nextCommandTarget;

        public IVsTextView TextView { get; }
        public IWpfTextViewHost TextViewHost { get; }
        public IVsTextLines TextLines => TextViewHost.TextView.TextBuffer as IVsTextLines;
        protected override int VisualChildrenCount => 1;
        public string Text
        {
            get
            {
                var buffer = TextLines;
                if (buffer == null) return null;

                int length;
                string text;
                buffer.GetLengthOfLine(0, out length);
                buffer.GetLineText(0, 0, 0, length, out text);
                return text;
            }
        }

        public MoveToNamespaceEditorControl(
            IVsTextView textView,
            IWpfTextViewHost textViewHost,
            IEditorOperationsFactoryService editorOperationsFactoryService,
            IVsEditorAdaptersFactoryService editorFactoryService,
            OLE.Interop.IServiceProvider serviceProvider)
        {
            TextView = textView;
            TextViewHost = textViewHost;
            _editorOperationsFactoryService = editorOperationsFactoryService;
            _editorAdaptersFactoryService = editorFactoryService;
            _oleServiceProvider = serviceProvider;

            InstallCommandFilter();
            InitializeEditorControl();
        }

        private void InitializeEditorControl()
        {
            AddLogicalChild(this.TextViewHost.HostControl);
            AddVisualChild(this.TextViewHost.HostControl);
        }

        private void InstallCommandFilter()
        {
            if (_editorOperationsFactoryService != null)
            {
                _editorOperations = _editorOperationsFactoryService.GetEditorOperations(this.TextViewHost.TextView);
            }

            //if (this.adornedTextBlock.CompletionHandlerProvider != null)
            //{
            //    string initialContent = this.adornedTextBlock.UserInputOnStartEditing == null ? this.adornedTextBlock.DefaultTextOnStartEditing : this.adornedTextBlock.UserInputOnStartEditing;

            //    this.adornedTextBlock.CompletionHandlerProvider.CreateIntellisenseCompletionHandler(this.ViewAdapter, this.wpfTextView, string.IsNullOrEmpty(initialContent));
            //}

            ErrorHandler.ThrowOnFailure(this.TextView.AddCommandFilter(this, out this._nextCommandTarget));
        }

        /// <summary>
        /// Return visual child at given index
        /// </summary>
        /// <param name="index">child index</param>
        /// <returns>returns visual child</returns>
        protected override Visual GetVisualChild(int index) => this.TextViewHost?.HostControl;

        protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
        {
            TextViewHost.HostControl.Arrange(new Rect(new Point(0, 0), finalSize));
            return finalSize;
        }

        protected override void OnGotFocus(RoutedEventArgs e)
        {
            e.Handled = true;
            this.TextViewHost.TextView.VisualElement.Focus();
        }

        private static DependencyObject TryGetParent(DependencyObject obj)
        {
            return (obj is Visual) ? VisualTreeHelper.GetParent(obj) : null;
        }

        private static T GetParentOfType<T>(DependencyObject element) where T : Visual
        {
            var parent = TryGetParent(element);
            if (parent is T)
            {
                return (T)parent;
            }
            if (parent == null)
            {
                return null;
            }
            return GetParentOfType<T>(parent);
        }

        internal static void HandleKeyDown(object sender, KeyEventArgs e)
        {
            var elementWithFocus = Keyboard.FocusedElement as UIElement;

            if (elementWithFocus is IWpfTextView)
            {
                var moveToNamespaceEditorControl = GetParentOfType<MoveToNamespaceEditorControl>(elementWithFocus);

                if (moveToNamespaceEditorControl != null && moveToNamespaceEditorControl.TextView != null)
                {
                    switch (e.Key)
                    {
                        case Key.Escape:
                        case Key.Tab:
                        case Key.Enter:
                            e.Handled = true;
                            break;

                        default:
                            // Let the editor control handle the keystrokes
                            var msg = ComponentDispatcher.CurrentKeyboardMessage;

                            var oleInteropMsg = new OLE.Interop.MSG();

                            oleInteropMsg.hwnd = msg.hwnd;
                            oleInteropMsg.message = (uint)msg.message;
                            oleInteropMsg.wParam = msg.wParam;
                            oleInteropMsg.lParam = msg.lParam;
                            oleInteropMsg.pt.x = msg.pt_x;
                            oleInteropMsg.pt.y = msg.pt_y;

                            e.Handled = moveToNamespaceEditorControl.HandleKeyDown(oleInteropMsg);
                            break;
                    }
                }
            }
            else
            {
                if (e.Key == Key.Escape)
                {
                    //OnCancel();
                }
            }
        }

        private bool HandleKeyDown(OLE.Interop.MSG message)
        {
            uint editCmdID = 0;
            Guid editCmdGuid = Guid.Empty;
            int VariantSize = 16;

            var filterKeys = Package.GetGlobalService(typeof(SVsFilterKeys)) as IVsFilterKeys2;

            if (filterKeys != null)
            {
                int translated;
                int firstKeyOfCombo;
                var pMsg = new OLE.Interop.MSG[1];
                pMsg[0] = message;
                ErrorHandler.ThrowOnFailure(filterKeys.TranslateAcceleratorEx(pMsg,
                    (uint)(__VSTRANSACCELEXFLAGS.VSTAEXF_NoFireCommand | __VSTRANSACCELEXFLAGS.VSTAEXF_UseTextEditorKBScope | __VSTRANSACCELEXFLAGS.VSTAEXF_AllowModalState),
                    0,
                    null,
                    out editCmdGuid,
                    out editCmdID,
                    out translated,
                    out firstKeyOfCombo));

                if (translated == 1)
                {
                    var inArg = IntPtr.Zero;
                    try
                    {
                        // if the command is undo (Ctrl + Z) or redo (Ctrl + Y) then leave it as IntPtr.Zero because of a bug in undomgr.cpp where 
                        // it does undo or redo only for null, VT_BSTR and VT_EMPTY 
                        if ((int)message.wParam != Convert.ToInt32('Z') && (int)message.wParam != Convert.ToInt32('Y'))
                        {
                            inArg = Marshal.AllocHGlobal(VariantSize);
                            Marshal.GetNativeVariantForObject(message.wParam, inArg);
                        }

                        return Exec(ref editCmdGuid, editCmdID, 0, inArg, IntPtr.Zero) == VSConstants.S_OK;
                    }
                    finally
                    {
                        if (inArg != IntPtr.Zero)
                            Marshal.FreeHGlobal(inArg);
                    }
                }
            }

            // no translation available for this message
            return false;
        }


        /// <summary>
        /// Query command status
        /// </summary>
        /// <param name="pguidCmdGroup">Command group guid</param>
        /// <param name="cmdCount">The number of commands in the OLECMD array</param>
        /// <param name="prgCmds">The set of command ids</param>
        /// <param name="cmdText">Unuses pCmdText</param>
        /// <returns>A Microsoft.VisualStudio.OLE.Interop.Constants value</returns>
        public int QueryStatus(ref Guid pguidCmdGroup, uint cmdCount, OLECMD[] prgCmds, IntPtr cmdText)
        {
            // Return status UNKNOWNGROUP if the passed command group is different than the ones we know about
            if (pguidCmdGroup != VsMenus.guidStandardCommandSet2K &&
                pguidCmdGroup != VsMenus.guidStandardCommandSet97)
            {
                return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_UNKNOWNGROUP;
            }

            // 1. For the commands we support and don't need to have a custom implementation
            // simply ask the next command handler in the filter chain for the command status
            // 2. For the commands we have a custom implementation, calculate and return status value
            // 3. For other commands, set status to NOTSUPPORTED (0)
            for (int i = 0; i < cmdCount; i++)
            {
                if (this.IsPassThroughCommand(ref pguidCmdGroup, prgCmds[i].cmdID))
                {
                    OLECMD[] cmdArray = new OLECMD[] { new OLECMD() };
                    cmdArray[0].cmdID = prgCmds[i].cmdID;
                    int hr = this._nextCommandTarget.QueryStatus(ref pguidCmdGroup, 1, cmdArray, cmdText);

                    if (ErrorHandler.Failed(hr))
                    {
                        continue;
                    }

                    prgCmds[i].cmdf = cmdArray[0].cmdf;
                }
                else if ((pguidCmdGroup == VsMenus.guidStandardCommandSet97 && prgCmds[i].cmdID == StandardCommands.Cut.ID) ||
                            (pguidCmdGroup == VsMenus.guidStandardCommandSet2K && prgCmds[i].cmdID == (uint)VSConstants.VSStd2KCmdID.CUT) ||
                            (pguidCmdGroup == VsMenus.guidStandardCommandSet97 && prgCmds[i].cmdID == StandardCommands.Copy.ID) ||
                            (pguidCmdGroup == VsMenus.guidStandardCommandSet2K && prgCmds[i].cmdID == (uint)VSConstants.VSStd2KCmdID.COPY))
                {
                    prgCmds[i].cmdf = (uint)OLECMDF.OLECMDF_SUPPORTED;

                    //if (this.CanCutCopy())
                    //{
                    //    prgCmds[i].cmdf |= (uint)OLECMDF.OLECMDF_ENABLED;
                    //}
                }
                else if ((pguidCmdGroup == VsMenus.guidStandardCommandSet97 && prgCmds[i].cmdID == StandardCommands.Paste.ID) ||
                            (pguidCmdGroup == VsMenus.guidStandardCommandSet2K && prgCmds[i].cmdID == (uint)VSConstants.VSStd2KCmdID.PASTE))
                {
                    prgCmds[i].cmdf = (uint)OLECMDF.OLECMDF_SUPPORTED;

                    //if (this.CanPaste())
                    //{
                    //    prgCmds[i].cmdf |= (uint)OLECMDF.OLECMDF_ENABLED;
                    //}
                }
                else
                {
                    prgCmds[i].cmdf = 0;
                }
            }

            return VSConstants.S_OK;
        }

        /// <summary>
        /// Executes the given shell command
        /// </summary>
        /// <param name="pguidCmdGroup">Command group guid</param>
        /// <param name="cmdID">Command id</param>
        /// <param name="cmdExecOpt">Options for the executing command</param>
        /// <param name="pvaIn">The input arguments structure</param>
        /// <param name="pvaOut">The command output structure</param>
        /// <returns>Exec return value</returns>
        public int Exec(ref Guid pguidCmdGroup, uint cmdID, uint cmdExecOpt, IntPtr pvaIn, IntPtr pvaOut)
        {
            // Return status UNKNOWNGROUP if the passed command group is different than the ones we know about
            if (pguidCmdGroup != VsMenus.guidStandardCommandSet2K &&
                pguidCmdGroup != VsMenus.guidStandardCommandSet97)
            {
                return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_UNKNOWNGROUP;
            }

            int hr = 0;

            // 1. For the commands we support and don't need to have a custom implementation
            // simply pass the command to the next command handler in the filter chain
            // 2. For the commands we have a custom implementation, carry out the command
            // don't pass it to the next command handler
            // 3. For other commands, simply return with NOTSUPPORTED
            if (this.IsPassThroughCommand(ref pguidCmdGroup, cmdID))
            {
                hr = this._nextCommandTarget.Exec(ref pguidCmdGroup, cmdID, cmdExecOpt, pvaIn, pvaOut);
            }
            //else if ((pguidCmdGroup == VsMenus.guidStandardCommandSet97 && cmdID == StandardCommands.Cut.ID) ||
            //        (pguidCmdGroup == VsMenus.guidStandardCommandSet2K && cmdID == (uint)VSConstants.VSStd2KCmdID.CUT))
            //{
            //    if (this.CanCutCopy())
            //    {
            //        this.Cut();
            //    }

            //    hr = VSConstants.S_OK;
            //}
            //else if ((pguidCmdGroup == VsMenus.guidStandardCommandSet97 && cmdID == StandardCommands.Copy.ID) ||
            //    (pguidCmdGroup == VsMenus.guidStandardCommandSet2K && cmdID == (uint)VSConstants.VSStd2KCmdID.COPY))
            //{
            //    if (this.CanCutCopy())
            //    {
            //        this.Copy();
            //    }

            //    hr = VSConstants.S_OK;
            //}
            //else if ((pguidCmdGroup == VsMenus.guidStandardCommandSet97 && cmdID == StandardCommands.Paste.ID) ||
            //        (pguidCmdGroup == VsMenus.guidStandardCommandSet2K && cmdID == (uint)VSConstants.VSStd2KCmdID.PASTE))
            //{
            //    if (this.CanPaste())
            //    {
            //        this.Paste();
            //    }

            //    hr = VSConstants.S_OK;
            //}
            //else if (pguidCmdGroup == VsMenus.guidStandardCommandSet2K && cmdID == (uint)VSConstants.VSStd2KCmdID.SHOWCONTEXTMENU)
            //{
            //    if (!this.adornedTextBlock.SuppressContextMenu)
            //    {
            //        this.ContextMenu.IsOpen = true;
            //    }
            //    else
            //    {
            //        this.adornedTextBlock.SuppressContextMenu = false;
            //    }

            //    hr = VSConstants.S_OK;
            //}
            else
            {
                hr = (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
            }

            return hr;
        }

        /// <summary>
        /// Determines whether the given command should be passed to the
        /// next command handler in the text view command filter chain.
        /// </summary>
        /// <param name="pguidCmdGroup">The command group guid</param>
        /// <param name="cmdID">The command id</param>
        /// <returns>True, if the command is supported and should be passed to the next command handler</returns>
        private bool IsPassThroughCommand(ref Guid pguidCmdGroup, uint cmdID)
        {
            if (pguidCmdGroup == VsMenus.guidStandardCommandSet2K)
            {
                switch ((VSConstants.VSStd2KCmdID)cmdID)
                {
                    case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                    case VSConstants.VSStd2KCmdID.TYPECHAR:
                    case VSConstants.VSStd2KCmdID.BACKSPACE:
                    case VSConstants.VSStd2KCmdID.TAB:
                    case VSConstants.VSStd2KCmdID.BACKTAB:
                    case VSConstants.VSStd2KCmdID.DELETE:
                    case VSConstants.VSStd2KCmdID.DELETEWORDRIGHT:
                    case VSConstants.VSStd2KCmdID.DELETEWORDLEFT:
                    case VSConstants.VSStd2KCmdID.DELETETOBOL:
                    case VSConstants.VSStd2KCmdID.DELETETOEOL:
                    case VSConstants.VSStd2KCmdID.UP:
                    case VSConstants.VSStd2KCmdID.DOWN:
                    case VSConstants.VSStd2KCmdID.LEFT:
                    case VSConstants.VSStd2KCmdID.LEFT_EXT:
                    case VSConstants.VSStd2KCmdID.LEFT_EXT_COL:
                    case VSConstants.VSStd2KCmdID.RIGHT:
                    case VSConstants.VSStd2KCmdID.RIGHT_EXT:
                    case VSConstants.VSStd2KCmdID.RIGHT_EXT_COL:
                    case VSConstants.VSStd2KCmdID.EditorLineFirstColumn:
                    case VSConstants.VSStd2KCmdID.EditorLineFirstColumnExtend:
                    case VSConstants.VSStd2KCmdID.BOL:
                    case VSConstants.VSStd2KCmdID.BOL_EXT:
                    case VSConstants.VSStd2KCmdID.BOL_EXT_COL:
                    case VSConstants.VSStd2KCmdID.EOL:
                    case VSConstants.VSStd2KCmdID.EOL_EXT:
                    case VSConstants.VSStd2KCmdID.EOL_EXT_COL:
                    case VSConstants.VSStd2KCmdID.SELECTALL:
                    case VSConstants.VSStd2KCmdID.CANCEL:
                    case VSConstants.VSStd2KCmdID.WORDPREV:
                    case VSConstants.VSStd2KCmdID.WORDPREV_EXT:
                    case VSConstants.VSStd2KCmdID.WORDPREV_EXT_COL:
                    case VSConstants.VSStd2KCmdID.WORDNEXT:
                    case VSConstants.VSStd2KCmdID.WORDNEXT_EXT:
                    case VSConstants.VSStd2KCmdID.WORDNEXT_EXT_COL:
                    case VSConstants.VSStd2KCmdID.SELECTCURRENTWORD:
                    case VSConstants.VSStd2KCmdID.TOGGLE_OVERTYPE_MODE:
                        return true;
                }
            }
            else if (pguidCmdGroup == VsMenus.guidStandardCommandSet97)
            {
                switch ((VSConstants.VSStd97CmdID)cmdID)
                {
                    case VSConstants.VSStd97CmdID.Delete:
                    case VSConstants.VSStd97CmdID.SelectAll:
                    case VSConstants.VSStd97CmdID.Undo:
                    case VSConstants.VSStd97CmdID.Redo:
                        return true;
                }
            }

            return false;
        }
    }
}
