using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace SubwordNavigation
{
    public sealed class OptionsPage : DialogPage
    {

    }

    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuid)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    public sealed class SubwordNavigationPackage : AsyncPackage
    {
        public const string PackageGuid = "25bcedb6-b77f-49b1-af0e-bc047dcb6e11";

        public const string PackageCmdSetGuid = "105b2c43-ede9-477b-af95-7e91e2cc11fb";
        public const int CommandIdNext = 0x100;
        public const int CommandIdPrev = 0x101;
        public const int CommandIdNextExtend = 0x102;
        public const int CommandIdPrevExtend = 0x103;
        public const int CommandIdDeleteToEnd = 0x104;
        public const int CommandIdDeleteToStart = 0x105;

        DTE2 m_dte;

		SubwordSearcher m_searcher;

        public SubwordNavigationPackage()
        {
            m_searcher.SetOptions(SubwordSearcher.Options.Default);
        }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            m_dte = (DTE2)await this.GetServiceAsync(typeof(DTE));

            var packageCmdSetGuid = new Guid(PackageCmdSetGuid);
            var commandService = (OleMenuCommandService)await this.GetServiceAsync(typeof(IMenuCommandService));
            commandService.AddCommand(new MenuCommand(SubwordNext, new CommandID(packageCmdSetGuid, CommandIdNext)));
            commandService.AddCommand(new MenuCommand(SubwordPrev, new CommandID(packageCmdSetGuid, CommandIdPrev)));
            commandService.AddCommand(new MenuCommand(SubwordNextExtend, new CommandID(packageCmdSetGuid, CommandIdNextExtend)));
            commandService.AddCommand(new MenuCommand(SubwordPrevExtend, new CommandID(packageCmdSetGuid, CommandIdPrevExtend)));
            commandService.AddCommand(new MenuCommand(SubwordDeleteToEnd, new CommandID(packageCmdSetGuid, CommandIdDeleteToEnd)));
            commandService.AddCommand(new MenuCommand(SubwordDeleteToStart, new CommandID(packageCmdSetGuid, CommandIdDeleteToStart)));
        }

        static void Swap<T>(ref T a, ref T b)
        {
            T c = a;
            a = b;
            b = c;
        }

        [DebuggerDisplay("{Line}:{Column}")]
        struct TextPos
        {
            public int Line;
            public int Column;

            public static bool operator ==(TextPos a, TextPos b)
            {
                return a.Line == b.Line && a.Column == b.Column;
            }

            public static bool operator !=(TextPos a, TextPos b)
            {
                return a.Line != b.Line || a.Column != b.Column;
            }

            public static bool operator <(TextPos a, TextPos b)
            {
                if (a.Line < b.Line) return true;
                if (a.Line > b.Line) return false;
                return a.Column < b.Column;
            }

            public static bool operator >(TextPos a, TextPos b)
            {
                if (a.Line > b.Line) return true;
                if (a.Line < b.Line) return false;
                return a.Column > b.Column;
            }
        }

        enum Action
        {
            Move,
            Extend,
            Delete,
        }

        static int GetLineLength(IVsTextLines textLines, int line)
        {
            int length;
            textLines.GetLengthOfLine(line, out length);
            return length;
        }

        static (TextPos, TextPos) GetBoxSelection(IVsTextView textView)
        {
            TextPos beg;
            TextPos end;

            textView.GetSelection(
                out beg.Line, out beg.Column,
                out end.Line, out end.Column);

            return (beg, end);
        }

        static (TextPos, TextPos) NormalizeBoxSelection(TextPos anchor, TextPos select)
        {
            return anchor > select ? (select, anchor) : (anchor, select);
        }
        enum Direction { Forward, Backward }

        TextPos GetNextPos(IVsTextView textView, IVsTextLines textLines,
            TextPos pos, Direction direction, bool boxSelect, bool movePastEndOfLine)
        {
            TextPos newpos;
            if (direction == Direction.Backward)
            {
                // begin of line
                if (pos.Column == 0)
                {
                    if (boxSelect)
                    {
                        newpos = pos;
                    }
                    else if (pos.Line == 0)
                    {
                        newpos = pos;
                    }
                    else
                    {
                        newpos.Line = pos.Line - 1;
                        newpos.Column = GetLineLength(textLines, newpos.Line);
                    }
                }
                else
                {
                    int length = GetLineLength(textLines, pos.Line);

                    textView.GetTextStream(pos.Line, 0, pos.Line, length, out var text);

                    newpos.Line = pos.Line;
                    newpos.Column = m_searcher.GetPrevBoundary(text, pos.Column);
                }
            }
            else
            {
                int length = GetLineLength(textLines, pos.Line);

                // end of line
                if (pos.Column >= length)
                {
                    if (boxSelect)
                    {
                        if (movePastEndOfLine)
                        {
                            newpos.Line = pos.Line;
                            newpos.Column = pos.Column + 1;
                        }
                        else
                        {
                            newpos = pos;
                        }
                    }
                    else
                    {
                        textLines.GetLineCount(out var lineCount);

                        if (pos.Line >= lineCount - 1)
                        {
                            newpos = pos;
                        }
                        else
                        {
                            newpos.Line = pos.Line + 1;
                            newpos.Column = 0;
                        }
                    }
                }
                else
                {
                    textView.GetTextStream(pos.Line, 0, pos.Line, length, out var text);

                    newpos.Line = pos.Line;
                    newpos.Column = m_searcher.GetNextBoundary(text, pos.Column);
                }
            }
            return newpos;
        }


        void Execute(Action action, Direction direction)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var textManager = (IVsTextManager)GetService(typeof(SVsTextManager));

            textManager.GetActiveView(1, null, out var textView);

            if (textView == null)
                return;

            textView.GetBuffer(out var textLines);

            bool boxSelect = textView.GetSelectionMode() == TextSelMode.SM_BOX;

            TextPos newpos;
            if (boxSelect && action == Action.Move)
            {
                var (beg, end) = GetBoxSelection(textView);

                newpos = direction == Direction.Forward ? end : beg;
                textView.SetSelectionMode(TextSelMode.SM_STREAM);
                textView.SetCaretPos(newpos.Line, newpos.Column);
                return;
            }

            TextPos pos;
            textView.GetCaretPos(out pos.Line, out pos.Column);

            // box selections can extend past the end of the the line
            bool movePastEndOfLine = action == Action.Extend;

            newpos = GetNextPos(textView, textLines, pos,
                direction, boxSelect, movePastEndOfLine);

            switch (action)
            {
                case Action.Move:
                    if (newpos != pos)
                    {
                        textView.SetCaretPos(newpos.Line, newpos.Column);
                    }
                    break;

                case Action.Extend:
                    if (newpos != pos)
                    {
                        var (anchor, select) = GetBoxSelection(textView);

                        textView.SetSelection(
                            anchor.Line, anchor.Column,
                            newpos.Line, newpos.Column);
                    }
                    break;

                //TODO: fix selection after undoing a delete
                case Action.Delete:
                    {
                        var (anchor, select) = GetBoxSelection(textView);
                        var (beg, end) = NormalizeBoxSelection(anchor, select);

                        if (boxSelect)
                        {
                            if (beg.Column > end.Column)
                                Swap(ref beg.Column, ref end.Column);

                            beg.Column = Math.Min(beg.Column, newpos.Column);
                            end.Column = Math.Max(end.Column, newpos.Column);

                            var undoContext = m_dte.UndoContext;

                            bool newUndoContext = !undoContext.IsOpen;
                            if (newUndoContext) undoContext.Open("Subword delete");

                            try
                            {
                                for (int i = beg.Line; i <= end.Line; ++i)
                                {
                                    textLines.GetLengthOfLine(i, out var length);

                                    textView.GetTextStream(i, 0, i, length, out var text);

                                    int endColumn = Math.Min(end.Column, length);

                                    if (endColumn > beg.Column)
                                    {
                                        textLines.ReplaceLines(i, beg.Column,
                                            i, endColumn, IntPtr.Zero, 0, null);
                                    }
                                }

                                textView.SetSelection(
                                    anchor.Line, beg.Column,
                                    select.Line, beg.Column);
                            }
                            finally
                            {
                                if (newUndoContext) undoContext.Close();
                            }
                        }
                        else
                        {
                            if (newpos < beg) beg = newpos;
                            if (newpos > end) end = newpos;

                            if (beg.Line == end.Line)
                            {
                                textLines.GetLengthOfLine(beg.Line, out var length);
                            }

                            textLines.ReplaceLines(
                                beg.Line, beg.Column,
                                end.Line, end.Column, IntPtr.Zero, 0, null);
                        }
                    }
                    break;
            }
        }

        void SubwordNext(object sender, EventArgs e)
        {
            Execute(Action.Move, Direction.Forward);
        }

        void SubwordPrev(object sender, EventArgs e)
        {
            Execute(Action.Move, Direction.Backward);
        }

        void SubwordNextExtend(object sender, EventArgs e)
        {
            Execute(Action.Extend, Direction.Forward);
        }

        void SubwordPrevExtend(object sender, EventArgs e)
        {
            Execute(Action.Extend, Direction.Backward);
        }

        void SubwordDeleteToEnd(object sender, EventArgs e)
        {
            Execute(Action.Delete, Direction.Forward);
        }

        void SubwordDeleteToStart(object sender, EventArgs e)
        {
            Execute(Action.Delete, Direction.Backward);
        }
    }
}
