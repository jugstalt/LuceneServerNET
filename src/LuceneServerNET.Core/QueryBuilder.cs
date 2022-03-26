using LuceneServerNET.Core.Extensions;
using LuceneServerNET.Core.Language;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LuceneServerNET.Core
{
    public class QueryBuilder
    {
        private readonly IEnumerable<Lang> _languages;

        public QueryBuilder(Languages language = Languages.None)
        {
            _languages = language != Languages.None ?
                GetLanguageParser(language) :
                new Lang[0];
        }

        public string ParseTerm(string term)
        {
            term = term
                .Replace("-", " ")
                .Trim();

            while (term.Contains("  "))
            {
                term = term.Replace("  ", " ");
            }

            var words = term
                .Split(' ')
                .Select(t => t.Replace("/", "//"));

            return $"({ Parse(words, false) }) OR ({ Parse(words, true) })";
        }

        #region Helper

        static private ConcurrentBag<Lang> _parsers = null;

        private string Parse(IEnumerable<string> words, bool appendWildcards)
        {
            foreach (var language in _languages)
            {
                words = language.ParseWords(words, appendWildcards);
            }

            words = words.Select(t =>
                {
                    if (t.StartsWith("(") && t.Contains(")"))
                    {
                        return $"+{t}";
                    }
                    if (t.Length > 2 || t.IsNumeric())
                    {
                        return AllowWildcards(appendWildcards, t) ? $"+{t}*" : $"+{t}";
                    }
                    else // sort part must must not be inluded with AND "+"
                    {
                        return AllowWildcards(appendWildcards, t) ? $"{t}*" : $"{t}";
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

        private bool AllowWildcards(bool appendWildcards, string term)
        {
            if (String.IsNullOrEmpty(term))
            {
                return false;
            };
            if (term.EndsWith("/"))
            {
                return false;
            }

            return appendWildcards;
        }

        #endregion
    }
}
