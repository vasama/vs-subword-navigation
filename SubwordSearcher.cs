using System;
using System.Collections;
using System.Diagnostics;

static class BitArrayExt
{
	public static void SetRange(this BitArray t, (int f, int t) r, bool trans = true)
	{
		for (int i = r.f; i < r.t; i++) t[i] = trans;
	}
}

struct SubwordSearcher
{
	public struct Options
	{
		public static readonly Options Default;
	}

	enum CharClass
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
    static SubwordSearcher()
    {
        if (!((int)CharClass.Count <= TransTableSize))
            throw new Exception();
    }
#endif

	static int TransIndex(CharClass last, CharClass cur, CharClass next)
	{
		var high = (int)last << TransTableShift;
		var mid = (high | (int)cur) << TransTableShift;
		return mid | (int)next;
	}
	static (int, int) TransRange(CharClass from, CharClass cur) => (TransIndex(from, cur, 0), TransIndex(from, cur, CharClass.Count));

	BitArray transTable;

	BitArray CreateTransTable()
	{
		var r = new BitArray(TransTableSize * TransTableSize * TransTableSize, true);

		for (int i = 0; i < (int)CharClass.Count; ++i)
		{
			var cc = (CharClass)i;
			r.SetRange(TransRange(cc, cc), false);
			r.SetRange(TransRange(cc, CharClass.Whitespace), false);
		}

		r.SetRange(TransRange(CharClass.Uppercase, CharClass.Lowercase), false);
		r.SetRange(TransRange(CharClass.Uppercase, CharClass.Underscore), false);
		r.SetRange(TransRange(CharClass.Lowercase, CharClass.Underscore), false);

		r.SetRange(TransRange(CharClass.Uppercase, CharClass.Operator), false);
		r.SetRange(TransRange(CharClass.Lowercase, CharClass.Operator), false);
		r.SetRange(TransRange(CharClass.Uppercase, CharClass.Bracket), false);
		r.SetRange(TransRange(CharClass.Lowercase, CharClass.Bracket), false);

		r.SetRange(TransRange(CharClass.Whitespace, CharClass.Linebreak), false);

		r[TransIndex(CharClass.Uppercase, CharClass.Uppercase, CharClass.Lowercase)] = true;
		// r.SetRange(TransRange(CharClass.Bracket, CharClass.Bracket), true);
		return r;
	}

	public void SetOptions(Options options)
	{
		transTable = CreateTransTable();
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
