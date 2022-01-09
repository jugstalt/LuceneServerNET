using LuceneServerNET.Client.Extensions;
using LuceneServerNET.Client.Language;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LuceneServerNET.Client
{
    public class TermParser
    {
        public TermParser()
        {

        } 

        public string Parse(string term, Languages language = Languages.None)
        {
            term = term
                .Replace("-", " ")
                .Trim();

            while (term.Contains("  "))
            {
                term = term.Replace("  ", " ");
            }

            var langs = language != Languages.None ?
                GetLanguageParser(language) :
                new Lang[0];

            var words = term
                .Split(' ')
                .Select(t => t.Replace("/", "//"));

            return $"({ Parse(words, langs, false) }) OR ({ Parse(words, langs, true) })";
        }

        #region Helper

        static private ConcurrentBag<Lang> _parsers = null;

        private string Parse(IEnumerable<string> words, IEnumerable<Lang> langParsers, bool appendWildcards)
        {
            foreach (var langParser in langParsers)
            {
                words = langParser.ParseWords(words, appendWildcards);
            }

            words = words.Select(t =>
                {
                    if (t.StartsWith("(") && t.Contains(")"))
                    {
                        return $"+{t}";
                    }
                    if (t.Length > 2 || t.IsNumeric())
                    {
                        return appendWildcards ? $"+{t}*" : $"+{t}";
                    }
                    else // sort part must must not be inluded with AND "+"
                    {
                        return appendWildcards ? $"{t}*" : $"{t}";
                    }
                })
                .ToArray();

            return String.Join(" ", words);
        }

        private IEnumerable<Lang> GetLanguageParser(Languages languageParser)
        {
            if (_parsers == null)
            {
                var bag = new ConcurrentBag<Lang>();
                foreach (var type in Assembly.GetAssembly(typeof(Lang)).GetTypes())
                {
                    try
                    {
                        if (!type.IsAbstract && typeof(Lang).IsAssignableFrom(type))
                        {
                            bag.Add(Activator.CreateInstance(type) as Lang);
                        }
                    }
                    catch { }
                }
                _parsers = bag;
            }

            return _parsers?.Where(p => p.Language == languageParser);
        }

        #endregion
    }
}
