using System;
using System.Collections.Generic;
using System.Linq;

namespace LuceneServerNET.Client.Extensions
{
    public static class OutFieldsExtensions
    {
        public static string FirstNWords(this string field, int n)
        {
            return $"{ field }.WORDS({ n })";
        }

        public static string FirstNCharacters(this string field, int n)
        {
            return $"{ field }.CHARS({ n })";
        }

        public static string IncludedTerms(this string field, IEnumerable<string> terms)
        {
            return $"{ field }.INCL(\"{ String.Join(" ", terms.Select(t => t?.Trim()).Where(t => !String.IsNullOrEmpty(t))) }\")";
        }

        public static string SentencesWith(this string field, IEnumerable<string> terms, int takeHits = 2, int takeDefaults = 2)
        {
            return $"{ field }.SENTENCES_WITH(\"{ String.Join(" ", terms.Select(t => t?.Trim()).Where(t => !String.IsNullOrEmpty(t))) }\",{ takeHits },{ takeDefaults })";
        }

        public static string As(this string field, string name)
        {
            return $"{ field }.AS(\"{ name }\")";
        }

        public static string ToOutFieldsParameterString(this IEnumerable<string> outFields)
        {
            string outFieldsString = null;

            if (outFields != null && outFields.Count() > 0)
            {
                string separator = outFields.Where(o => o != null && o.Contains("(")).Count() > 0 ? ";" : ",";

                outFieldsString = String.Join(separator, outFields.Where(f => !String.IsNullOrEmpty(f)).Select(f => f.Trim()));

                if (separator == ";")
                {
                    outFieldsString += ";";
                }
            }

            return outFieldsString;
        }
    }
}
