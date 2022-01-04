using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LuceneServerNET.Client.Language
{
    internal abstract class Lang
    {
        public IEnumerable<string> ParseWords(IEnumerable<string> words, bool appendWildards)
        {
            List<string> result = new List<string>();
            var _replace = ReplaceChars();

            foreach (var word in words)
            {
                if (_replace.Keys.Any(s => word.Contains(s)))
                {
                    StringBuilder parsedWord = new StringBuilder();

                    parsedWord.Append("(");
                    parsedWord.Append(word);
                    if (appendWildards)
                    {
                        parsedWord.Append("*");
                    }

                    foreach (var key in _replace.Keys)
                    {
                        if (word.Contains(key))
                        {
                            parsedWord.Append($" OR { word.Replace(key, _replace[key]) }");
                            if (appendWildards)
                            {
                                parsedWord.Append("*");
                            }
                        }
                    }

                    parsedWord.Append(")");
                    result.Add(parsedWord.ToString());
                }
                else
                {
                    result.Add(word);
                }
            }

            return result;
        }

        abstract public IDictionary<string, string> ReplaceChars();

        abstract public Languages Language { get; }
    }
}
