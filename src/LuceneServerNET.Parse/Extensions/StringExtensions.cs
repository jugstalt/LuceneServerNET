using System;
using System.Collections.Generic;
using System.Linq;

namespace LuceneServerNET.Parse.Extensions
{
    static public class StringExtensions
    {
        static public IEnumerable<string> GetSentence(this string text)
        {
            return (text ?? String.Empty)
                        .Replace("!", ".")
                        .Replace("?", ".")
                        .Split('.')
                        .Select(s => s.Trim());
        }

        static public IEnumerable<string> GetTermParts(this string term)
        {
            if (String.IsNullOrEmpty(term))
                return new string[0];

            term = term.Trim();

            while (term.Contains("  "))
                term = term.Replace("  ", " ");

            return term.Split(' ');
        }

        static public string GetTermSentencesOrDefault(this IEnumerable<string> sentences, IEnumerable<string> termParts, int takeHits = 2, int takeDefaults = 2)
        {
            if (sentences == null)
            {
                return String.Empty;
            }

            List<string> select = new List<string>();

            foreach (var sentence in sentences)
            {
                foreach (var term in termParts)
                {
                    if (sentence.IndexOf(term, StringComparison.InvariantCultureIgnoreCase) >= 0)
                    {
                        select.Add(sentence);
                        break;
                    }
                }
            }

            if (select.Count == 0)
            {
                if (takeDefaults > 0)
                {
                    return string.Join(". ", sentences.Take(takeDefaults));
                }
                return String.Empty;
            }

            return String.Join(" ... ", select.Take(takeHits));
        }
    }
}
