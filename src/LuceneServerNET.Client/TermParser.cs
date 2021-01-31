using System;
using System.Linq;

namespace LuceneServerNET.Client
{
    public class TermParser
    {
        public TermParser()
        {

        } 

        public string Parse(string term)
        {
            term = term
                .Replace("-", " ")
                .Trim();

            while (term.Contains("  "))
            {
                term = term.Replace("  ", " ");
            }

            var parts = term
                .Split(' ')
                .Select(t => t.Replace("/", "//"))
                .Select(t => $"+{t}*")
                .ToArray();

            return String.Join(" ", parts);
        }
    }
}
