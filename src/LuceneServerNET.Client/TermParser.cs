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

            var parts = term
                .Split(' ')
                .Select(t => t.Replace("/", "//"));

            foreach (var lang in langs)
            {
                parts = lang.ParseWords(parts, true);
            }

            parts = parts.Select(t =>
                {
                    if (t.StartsWith("(") && t.Contains(")"))
                    {
                        return $"+{t}";
                    }
                    return $"+{t}*";
                })
                .ToArray();

            return String.Join(" ", parts);
        }

        #region Helper

        static private ConcurrentBag<Lang> _parsers = null;

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
