using System;
using System.Collections;

namespace SubwordNavigation
{
	static class BitArrayExt
	{
		public static void SetRange(this BitArray t, (int f, int t) r, bool trans = true)
		{
			for (int i = r.f; i < r.t; i++) t[i] = trans;
		}
	}

	struct Scanner
	{
		enum CharClass : byte
		{
			Other,
			Uppercase,
			Lowercase,
			Numeral,
			Underscore,
			Whitespace,
			Linebreak,
			Bracket,
			Operator,

			Count,
		}

		static readonly CharClass[] CharClassTable = ((Func<CharClass[]>)(() =>
		{
			var r = new CharClass[128];
			for (int i = 'A'; i <= 'Z'; ++i)
				r[i] = CharClass.Uppercase;

			for (int i = 'a'; i <= 'z'; ++i)
				r[i] = CharClass.Lowercase;

			for (int i = '0'; i <= '9'; ++i)
				r[i] = CharClass.Numeral;

			r['_'] = CharClass.Underscore;

			r[' '] = CharClass.Whitespace;
			r['\t'] = CharClass.Whitespace;
			r['\v'] = CharClass.Whitespace;
			r['\f'] = CharClass.Whitespace;

			r['\r'] = CharClass.Linebreak;
			r['\n'] = CharClass.Linebreak;

			r['('] = CharClass.Bracket;
			r[')'] = CharClass.Bracket;
			r['['] = CharClass.Bracket;
			r[']'] = CharClass.Bracket;
			r['{'] = CharClass.Bracket;
			r['}'] = CharClass.Bracket;

			r['.'] = CharClass.Operator;
			r[','] = CharClass.Operator;
			r['='] = CharClass.Operator;
			r['+'] = CharClass.Operator;
			r['-'] = CharClass.Operator;
			r['*'] = CharClass.Operator;
			r['/'] = CharClass.Operator;
			r['%'] = CharClass.Operator;
			r['<'] = CharClass.Operator;
			r['>'] = CharClass.Operator;
			r['&'] = CharClass.Operator;
			r['|'] = CharClass.Operator;
			r['^'] = CharClass.Operator;
			return r;
		}))();

		static CharClass GetNonAsciiCharClass(char chr)
		{
			if (char.IsWhiteSpace(chr)) return CharClass.Whitespace;
			if (char.IsUpper(chr)) return CharClass.Uppercase;
			if (char.IsLower(chr)) return CharClass.Lowercase;
			if (char.IsNumber(chr)) return CharClass.Numeral;
			return CharClass.Other;
		}

		static CharClass GetCharClass(char chr)
		{
			if (chr >= CharClassTable.Length) return GetNonAsciiCharClass(chr);
			return CharClassTable[chr];
		}

		const int TransTableShift = 4;
		const int TransTableSize = 1 << TransTableShift;

#if DEBUG
		unsafe struct StaticAssert
		{
			const bool Condition = (int)CharClass.Count <= TransTableSize;

			// An error here means the static assert failed
			fixed int Array[Condition ? 1 : 0];
		}
#endif

		static int TransIndex(CharClass last, CharClass cur, CharClass next)
		{
			var high = (int)last << TransTableShift;
			var mid = (high | (int)cur) << TransTableShift;
			return mid | (int)next;
		}

		static (int, int) TransRange(CharClass from, CharClass cur)
			=> (TransIndex(from, cur, 0), TransIndex(from, cur, CharClass.Count));

		BitArray transTable;

		public void SetOptions(OptionsPage options)
		{
			var r = new BitArray(TransTableSize * TransTableSize * TransTableSize, true);

			void ForEachCharClass(Action<CharClass> action_)
			{
				for (int i_ = 0; i_ < (int)CharClass.Count; ++i_)
				{
					action_((CharClass)i_);
				}
			}

			ForEachCharClass(cc => r.SetRange(TransRange(cc, cc), false));

			if (options.RecognizePascal)
			{
				r.SetRange(TransRange(CharClass.Uppercase, CharClass.Lowercase), false);

				if (options.StopBetweenUpperAndPascal)
				{
					r[TransIndex(CharClass.Uppercase,
						CharClass.Uppercase, CharClass.Lowercase)] = true;
				}
			}

			if (options.StopBetweenOperators)
			{
				r.SetRange(TransRange(CharClass.Operator, CharClass.Operator), true);
			}

			if (options.StopBetweenBrackets)
			{
				r.SetRange(TransRange(CharClass.Bracket, CharClass.Bracket), true);
			}

			if (options.StopBetweenOperatorsAndBrackets)
			{
				r.SetRange(TransRange(CharClass.Operator, CharClass.Bracket), true);
				r.SetRange(TransRange(CharClass.Bracket, CharClass.Operator), true);
			}

			switch (options.SkipConnectedWhitespace)
			{
			case SkipConnectedWhitespace.BeforeAnything:
				ForEachCharClass(cc => r.SetRange(TransRange(CharClass.Whitespace, cc), false));
				break;

			case SkipConnectedWhitespace.AfterAnything:
				ForEachCharClass(cc => r.SetRange(TransRange(cc, CharClass.Whitespace), false));
				break;
			}

			switch (options.SkipConnectedUnderscores)
			{
			case SkipConnectedUnderscores.BeforeSubwords:
				r.SetRange(TransRange(CharClass.Underscore, CharClass.Uppercase), false);
				r.SetRange(TransRange(CharClass.Underscore, CharClass.Lowercase), false);
				break;

			case SkipConnectedUnderscores.AfterSubwords:
				r.SetRange(TransRange(CharClass.Uppercase, CharClass.Underscore), false);
				r.SetRange(TransRange(CharClass.Lowercase, CharClass.Underscore), false);
				break;
			}

			switch (options.SkipConnectedOperators)
			{
			case SkipConnectedOperators.BeforeWords:
				r.SetRange(TransRange(CharClass.Operator, CharClass.Uppercase), false);
				r.SetRange(TransRange(CharClass.Operator, CharClass.Lowercase), false);
				r.SetRange(TransRange(CharClass.Operator, CharClass.Underscore), false);
				break;

			case SkipConnectedOperators.AfterWords:
				r.SetRange(TransRange(CharClass.Uppercase, CharClass.Operator), false);
				r.SetRange(TransRange(CharClass.Lowercase, CharClass.Operator), false);
				r.SetRange(TransRange(CharClass.Underscore, CharClass.Operator), false);
				break;
			}

			switch (options.SkipConnectedBrackets)
			{
			case SkipConnectedBrackets.BeforeWords:
				r.SetRange(TransRange(CharClass.Bracket, CharClass.Uppercase), false);
				r.SetRange(TransRange(CharClass.Bracket, CharClass.Lowercase), false);
				break;

			case SkipConnectedBrackets.AfterWords:
				r.SetRange(TransRange(CharClass.Uppercase, CharClass.Bracket), false);
				r.SetRange(TransRange(CharClass.Lowercase, CharClass.Bracket), false);
				break;
			}

			r.SetRange(TransRange(CharClass.Whitespace, CharClass.Linebreak), false);

			transTable = r;
		}

		public int GetNextBoundary(string text, int index)
		{
			int length = text.Length;
			int lastIndex = length - 1;

			if (index + 1 >= lastIndex)
				return index + 1;

			var last = GetCharClass(text[index]);
			++index;
			var cur = GetCharClass(text[index]);

			while (index < lastIndex)
			{
				var next = GetCharClass(text[index + 1]);

				if (transTable[TransIndex(last, cur, next)])
					return index;

				last = cur;
				cur = next;
				++index;
			}

			if (!transTable[TransIndex(last, cur, CharClass.Linebreak)])
				++index;

			return index;
		}

		public int GetPrevBoundary(string text, int index)
		{
			if (index <= 1)
				return 0;

			var next = CharClass.Linebreak;
			var cur = CharClass.Linebreak;

			if (index < text.Length) next = GetCharClass(text[index]);
			--index;
			if (index < text.Length) cur = GetCharClass(text[index]);
			else index = text.Length;

			while (index > 0)
			{
				var last = GetCharClass(text[index - 1]);

				if (transTable[TransIndex(last, cur, next)])
					break;

				next = cur;
				cur = last;
				--index;
			}

			return index;
		}
	}
}
