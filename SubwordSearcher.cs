using System;
using System.Diagnostics;

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

		Count,
	}

	unsafe struct CharClassTableStruct
	{
		public fixed byte table[128];
	};

	unsafe static CharClassTableStruct CreateCharClassTable()
	{
		CharClassTableStruct table;

		for (int i = 'A'; i <= 'Z'; ++i)
			table.table[i] = (byte)CharClass.Uppercase;

		for (int i = 'a'; i <= 'z'; ++i)
			table.table[i] = (byte)CharClass.Lowercase;

		for (int i = '0'; i <= '9'; ++i)
			table.table[i] = (byte)CharClass.Numeral;

		table.table['_'] = (byte)CharClass.Underscore;

		table.table[' '] = (byte)CharClass.Whitespace;
		table.table['\t'] = (byte)CharClass.Whitespace;
		table.table['\v'] = (byte)CharClass.Whitespace;
		table.table['\f'] = (byte)CharClass.Whitespace;

		table.table['\r'] = (byte)CharClass.Linebreak;
		table.table['\n'] = (byte)CharClass.Linebreak;

		table.table['('] = (byte)CharClass.Bracket;
		table.table[')'] = (byte)CharClass.Bracket;
		table.table['['] = (byte)CharClass.Bracket;
		table.table[']'] = (byte)CharClass.Bracket;
		table.table['{'] = (byte)CharClass.Bracket;
		table.table['}'] = (byte)CharClass.Bracket;

		return table;
	}

	static readonly CharClassTableStruct CharClassTable = CreateCharClassTable();

	static unsafe CharClass GetNonAsciiCharClass(char chr)
	{
		if (char.IsWhiteSpace(chr))
			return (CharClass)CharClassTable.table[(int)' '];

		if (char.IsUpper(chr))
			return (CharClass)CharClassTable.table[(int)'A'];

		if (char.IsLower(chr))
			return (CharClass)CharClassTable.table[(int)'a'];

		if (char.IsNumber(chr))
			return (CharClass)CharClassTable.table[(int)'0'];

		return CharClass.Other;
	}

	static unsafe CharClass GetCharClass(char chr)
	{
		int index = chr;

		if (index >= 128)
			return GetNonAsciiCharClass(chr);

		return (CharClass)CharClassTable.table[index % 128];
	}


	const int TransTableShift = 3;
	const int TransTableSize = 1 << TransTableShift;

#if DEBUG
	static SubwordSearcher()
	{
		if (!((int)CharClass.Count <= TransTableSize))
			throw new Exception();
	}
#endif

	unsafe struct TransTableStruct
	{
		fixed uint table[TransTableSize * TransTableSize * TransTableSize / 32];

		public unsafe bool Get(CharClass c0, CharClass c1, CharClass c2)
		{
			int index = ((int)c0 << (TransTableShift * 2)) | ((int)c1 << TransTableShift) | (int)c2;

			int wordIndex = index / 32;
			int bitIndex = index % 32;

			uint mask = 1u << bitIndex;

			fixed (uint* pTable = table)
				return (pTable[wordIndex] & mask) != 0;
		}

		public unsafe void Set(CharClass c0, CharClass c1, CharClass c2, bool trans)
		{
			int index = ((int)c0 << (TransTableShift * 2)) | ((int)c1 << TransTableShift) | (int)c2;

			int wordIndex = index / 32;
			int bitIndex = index % 32;

			uint mask = ~(1u << bitIndex);
			uint bit = (trans ? 1u : 0u) << bitIndex;

			fixed (uint* pTable = table)
				pTable[wordIndex] = (pTable[wordIndex] & mask) | bit;
		}

		public void Set(CharClass c0, CharClass c1, bool trans)
		{
			for (int i = 0; i < (int)CharClass.Count; ++i)
				Set(c0, c1, (CharClass)i, trans);
		}
	}



	TransTableStruct transTable;

	public void SetOptions(Options options)
	{
		transTable = CreateTransTable();
	}

	unsafe TransTableStruct CreateTransTable()
	{
		TransTableStruct table;

		for (int i = 0; i < TransTableSize; ++i)
		{
			table.Set((CharClass)i, (CharClass)i, true);
			table.Set((CharClass)i, CharClass.Whitespace, true);
		}

		table.Set(CharClass.Uppercase, CharClass.Lowercase, true);
		table.Set(CharClass.Uppercase, CharClass.Uppercase, CharClass.Lowercase, false);

		table.Set(CharClass.Uppercase, CharClass.Underscore, true);
		table.Set(CharClass.Lowercase, CharClass.Underscore, true);

		table.Set(CharClass.Whitespace, CharClass.Linebreak, true);

		table.Set(CharClass.Bracket, CharClass.Bracket, false);

		return table;
	}


	public int GetNextBoundary(string text, int index)
	{
		int length = text.Length;
		int lastIndex = length - 1;

		if (index >= lastIndex)
			return index + 1;

		CharClass c0 = GetCharClass(text[index]);
		CharClass c1 = GetCharClass(text[++index]);

		while (index < lastIndex)
		{
			CharClass c2 = GetCharClass(text[index + 1]);

			if (!transTable.Get(c0, c1, c2))
				return index;

			c0 = c1;
			c1 = c2;
			++index;
		}

		if (transTable.Get(c0, c1, CharClass.Linebreak))
			++index;

		return index;
	}

	public int GetPrevBoundary(string text, int index)
	{
		if (index <= 1)
			return 0;

		CharClass c2;
		CharClass c1;

		if (index > text.Length)
		{
			c2 = CharClass.Linebreak;
			c1 = CharClass.Linebreak;
			index = text.Length;
		}
		else
		{
			c2 = index < text.Length ? GetCharClass(text[index]) : CharClass.Linebreak;
			c1 = GetCharClass(text[--index]);
		}

		while (index > 0)
		{
			CharClass c0 = GetCharClass(text[index - 1]);

			if (!transTable.Get(c0, c1, c2))
				break;

			c2 = c1;
			c1 = c0;
			--index;
		}

		return index;
	}
}
