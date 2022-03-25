using System;
using System.Linq;
using System.Text;

namespace LuceneServerNET.Core.Phonetics
{
    static public class ColognePhoneticsStringExtensions
    {
        private static readonly char[] Group0 = { 'a', 'e', 'i', 'j', 'o', 'u', 'y', 'ä', 'ö', 'ü' };

        private static readonly char[] Group3 = { 'f', 'v', 'w' };

        private static readonly char[] Group4 = { 'g', 'k', 'q', };

        private static readonly char[] Group6 = { 'm', 'n' };

        private static readonly char[] Group8 = { 's', 'z', 'ß' };

        private static readonly char[] GroupCFirst = { 'a', 'h', 'k', 'l', 'o', 'q', 'r', 'u', 'x' };

        private static readonly char[] GroupCNoFirst = { 'a', 'h', 'k', 'o', 'q', 'u', 'x' };

        private static readonly char[] GroupCPrevious = { 's', 'z' };

        private static readonly char[] GroupDTPrevious = { 'c', 's', 'z' };

        private static readonly char[] GroupXFollow = { 'c', 'k', 'q' };

        public static string ToColognePhonetics(this string inputWord, bool cleanDoubles = true, bool cleanZeros = false)
        {
            char[] inputChars = inputWord.ToLowerInvariant().ToCharArray();

            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < inputChars.Length; i++)
            {
                char entry = inputChars[i];

                if (!Char.IsLetter(entry)) // Ignore
                {
                    continue;
                }

                if (entry.Equals('h'))  // Ignure
                {
                    continue;
                }

                if (Group0.Contains(entry))
                {
                    sb.Append("0");
                    continue;
                }

                if (Group3.Contains(entry))
                {
                    sb.Append("3");
                    continue;
                }

                if (Group4.Contains(entry))
                {
                    sb.Append("4");
                    continue;
                }

                if (Group6.Contains(entry))
                {
                    sb.Append("6");
                    continue;
                }

                if (Group8.Contains(entry))
                {
                    sb.Append("8");
                    continue;
                }

                if (entry.Equals('b'))
                {
                    sb.Append("1");
                    continue;
                }

                if (entry.Equals('l'))
                {
                    sb.Append("5");
                    continue;
                }

                if (entry.Equals('r'))
                {
                    sb.Append("7");
                    continue;
                }


                if (entry.Equals('p'))
                {
                    if (i + 1 >= inputChars.Length)
                    {
                        sb.Append("1");
                        continue;
                    }

                    char next = inputChars[i + 1];

                    if (next.Equals('h'))
                    {
                        sb.Append("3");
                        continue;
                    }

                    sb.Append("1");
                    continue;
                }

                if (entry.Equals('x'))
                {
                    // if first letter
                    if (i == 0)
                    {
                        sb.Append("48");
                        continue;
                    }

                    // previous letter
                    char previous = inputChars[i - 1];

                    // compare with privous group
                    if (GroupXFollow.Contains(previous))
                    {
                        sb.Append("8");
                        continue;
                    }

                    sb.Append("48");
                    continue;
                }

                if (entry.Equals('d') || entry.Equals('t'))
                {
                    // if last letter in arry
                    if (i + 1 >= inputChars.Length)
                    {
                        sb.Append("2");
                        continue;
                    }

                    // next letter
                    char next = inputChars[i + 1];

                    if (GroupDTPrevious.Contains(next))
                    {
                        sb.Append("8");
                        continue;
                    }

                    sb.Append("2");
                    continue;
                }

                if (entry.Equals('c'))
                {
                    // is last letter in array
                    if (i + 1 >= inputChars.Length)
                    {
                        continue;
                    }

                    // if C is "Anlaut"
                    if (i == 0)
                    {
                        char next = inputChars[i + 1];

                        if (GroupCFirst.Contains(next))
                        {
                            sb.Append("4");
                            continue;
                        }

                        sb.Append("8");
                        continue;
                    }
                    else // not an "Anlaut"
                    {
                        // is last letter in array
                        if (i + 1 >= inputChars.Length)
                        {
                            continue;
                        }

                        char next = inputChars[i + 1];
                        char previous = inputChars[i - 1];

                        if (GroupCPrevious.Contains(previous))
                        {
                            sb.Append("8");
                            continue;
                        }
                        else
                        {
                            if (GroupCNoFirst.Contains(next))
                            {
                                sb.Append("4");
                                continue;
                            }

                            sb.Append("8");
                            continue;
                        }
                    }
                }
            }

            var result = sb.ToString();
            if (cleanDoubles)
            {
                result = result.CleanDoubles();
            }

            if (cleanZeros)
            {
                result = result.CleanZeros();
            }

            return result;
        }
        

        private static string CleanDoubles(this string input)
        {
            StringBuilder sb = new StringBuilder();

            char[] content = input.ToCharArray();

            char previous = new char();

            for (int i = 0; i < content.Length; i++)
            {
                char entry = content[i];

                if (!entry.Equals(previous))
                {
                    sb.Append(entry);
                }

                previous = entry;
            }

            return sb.ToString();
        }

        public static string CleanZeros(this string input)
        {
            StringBuilder sb = new StringBuilder();
            char[] content = input.ToCharArray();

            for (int i = 0; i < content.Length; i++)
            {
                char entry = content[i];

                // skip all zeros except in first place
                if (!entry.Equals('0') || i == 0)
                {
                    sb.Append(entry);
                }
            }
            return sb.ToString();
        }
    }
}
