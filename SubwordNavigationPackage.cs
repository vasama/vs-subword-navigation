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

			Guid packageCmdSetGuid = new Guid(PackageCmdSetGuid);

			OleMenuCommandService commandService = (OleMenuCommandService)await this.GetServiceAsync(typeof(IMenuCommandService));
			commandService.AddCommand(new MenuCommand(SubWordNext, new CommandID(packageCmdSetGuid, CommandIdNext)));
			commandService.AddCommand(new MenuCommand(SubWordPrev, new CommandID(packageCmdSetGuid, CommandIdPrev)));
			commandService.AddCommand(new MenuCommand(SubWordNextExtend, new CommandID(packageCmdSetGuid, CommandIdNextExtend)));
			commandService.AddCommand(new MenuCommand(SubWordPrevExtend, new CommandID(packageCmdSetGuid, CommandIdPrevExtend)));
			commandService.AddCommand(new MenuCommand(SubWordDeleteToEnd, new CommandID(packageCmdSetGuid, CommandIdDeleteToEnd)));
			commandService.AddCommand(new MenuCommand(SubWordDeleteToStart, new CommandID(packageCmdSetGuid, CommandIdDeleteToStart)));
		}

		static void Swap<T>(ref T a, ref T b)
		{
			T c = a;
			a = b;
			b = c;
		}

		[DebuggerDisplay("{{{Line}:{Column}}}")]
		struct TextPos
		{
			public int Line;
			public int Column;

			public static bool operator<(TextPos a, TextPos b)
			{
				return a.Line < b.Line || a.Column < b.Column;
			}

			public static bool operator>(TextPos a, TextPos b)
			{
				return a.Line > b.Line || a.Column > b.Column;
			}
		}

		static int GetLineLength(IVsTextLines textLines, int line)
		{
			int length;
			textLines.GetLengthOfLine(line, out length);
			return length;
		}

		enum Action
		{
			Move,
			Extend,
			Delete,
		}

		void Execute(Action action, bool reverse)
		{
			ThreadHelper.ThrowIfNotOnUIThread();

			IVsTextManager textManager = (IVsTextManager)GetService(typeof(SVsTextManager));

			IVsTextView textView = null;
			textManager.GetActiveView(1, null, out textView);

			if (textView == null)
				return;

			IVsTextLines textLines = null;
			textView.GetBuffer(out textLines);

			TextPos pos;
			textView.GetCaretPos(out pos.Line, out pos.Column);

			bool boxSelect = textView.GetSelectionMode() == TextSelMode.SM_BOX;

			TextPos newpos;
			if (reverse)
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
						if (action != Action.Delete)
							return;

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

					string text;
					textView.GetTextStream(pos.Line, 0, pos.Line, length, out text);

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
						if (action == Action.Extend)
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
						int lineCount;
						textLines.GetLineCount(out lineCount);

						if (pos.Line >= lineCount - 1)
						{
							if (action != Action.Delete)
								return;

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
					string text;
					textView.GetTextStream(pos.Line, 0, pos.Line, length, out text);

					newpos.Line = pos.Line;
					newpos.Column = m_searcher.GetNextBoundary(text, pos.Column);
				}
			}

			switch (action)
			{
			case Action.Move:
				textView.SetCaretPos(newpos.Line, newpos.Column);
				break;

			case Action.Extend:
				{
					TextPos anchor;
					TextPos select;

					textView.GetSelection(out anchor.Line, out anchor.Column, out select.Line, out select.Column);
					textView.SetSelection(anchor.Line, anchor.Column, newpos.Line, newpos.Column);
				}
				break;

			//TODO: fix selection after undoing a delete
			case Action.Delete:
				{
					TextPos anchor;
					TextPos select;

					textView.GetSelection(out anchor.Line, out anchor.Column, out select.Line, out select.Column);

					if (boxSelect)
					{
						TextPos beg;
						beg.Line = Math.Min(anchor.Line, select.Line);
						beg.Column = Math.Min(Math.Min(anchor.Column, select.Column), newpos.Column);

						TextPos end;
						end.Line = Math.Max(anchor.Line, select.Line);
						end.Column = Math.Max(Math.Max(anchor.Column, select.Column), newpos.Column);

						UndoContext undoContext = m_dte.UndoContext;

						bool newUndoContext = !undoContext.IsOpen;
						if (newUndoContext) undoContext.Open("Sub-word delete");

						try
						{
							for (int i = beg.Line; i <= end.Line; ++i)
							{
								int length;
								textLines.GetLengthOfLine(i, out length);

								string text;
								textView.GetTextStream(i, 0, i, length, out text);

								int endColumn = Math.Min(end.Column, length);

								if (endColumn > beg.Column)
									textLines.ReplaceLines(i, beg.Column, i, endColumn, IntPtr.Zero, 0, null);
							}

							textView.SetSelection(anchor.Line, beg.Column, select.Line, beg.Column);
						}
						finally
						{
							if (newUndoContext) undoContext.Close();
						}
					}
					else
					{
						TextPos beg = anchor;
						TextPos end = select;

						if (beg > end)
							Swap(ref beg, ref end);

						if (newpos < beg)
							beg = newpos;

						if (newpos > end)
							end = newpos;

						textLines.ReplaceLines(beg.Line, beg.Column, end.Line, end.Column, IntPtr.Zero, 0, null);
					}
				}
				break;
			}
		}

		void SubWordNext(object sender, EventArgs e)
		{
			Execute(Action.Move, false);
		}

		void SubWordPrev(object sender, EventArgs e)
		{
			Execute(Action.Move, true);
		}

		void SubWordNextExtend(object sender, EventArgs e)
		{
			Execute(Action.Extend, false);
		}

		void SubWordPrevExtend(object sender, EventArgs e)
		{
			Execute(Action.Extend, true);
		}

		void SubWordDeleteToEnd(object sender, EventArgs e)
		{
			Execute(Action.Delete, false);
		}

		void SubWordDeleteToStart(object sender, EventArgs e)
		{
			Execute(Action.Delete, true);
		}
	}
}
