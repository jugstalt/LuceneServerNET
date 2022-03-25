using System.Linq;
using System.Text;

namespace LuceneServerNET.Core.Phonetics
{
    static public class StringExtensions
    {
        static public string ToPhonetics(this string term, Algorithm algorithm)
        {
            var words = term.TermToLowercaseWords();

            StringBuilder sb = new StringBuilder();

            foreach (var word in words)
            {
                if (sb.Length > 0)
                {
                    sb.Append(" ");
                }

                // do not encode: numbers, start with ".", contains numbers
                if (!word.StartsWithLetter() || word.ContainsDigits() || word.Length < 2)
                {
                    sb.Append(word);
                }
                else
                {
                    switch (algorithm)
                    {
                        case Algorithm.Soundex:
                            sb.Append(word.ToSoundex());
                            break;
                        case Algorithm.ColognePhonetics:
                            sb.Append(word.ToColognePhonetics(cleanDoubles: true, cleanZeros: false));
                            break;
                        case Algorithm.ColognePhonetics_with_doubles:
                            sb.Append(word.ToColognePhonetics(cleanDoubles: false, cleanZeros: false));
                            break;
                        case Algorithm.ColognePhonetics_clean_zero:
                            sb.Append(word.ToColognePhonetics(cleanDoubles: true, cleanZeros: true));
                            break;
                        default:
                            sb.Append(word);
                            break;
                    }
                }
            }

            return sb.ToString();
        }

        static public bool StartsWithLetter(this string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return false;
            }

            return char.IsLetter(str[0]);
        }

        static public bool ContainsDigits(this string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return false;
            }

            for (var i = 0; i < str.Length; i++)
            {
                if (char.IsDigit(str[i]))
                {
                    return true;
                }
            }

            return false;
        }

        static public string[] TermToLowercaseWords(this string term)
        {
            while (term.Contains("  "))  // remove double spaces
            {
                term = term.Replace("  ", " ");
            }

            return term.Split(' ').Select(s => s.ToLower()).ToArray();
        }
    }
}
