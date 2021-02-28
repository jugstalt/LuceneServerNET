using LuceneServerNET.Parse.Lexer;
using LuceneServerNET.Parse.Lexer.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LuceneServerNET.Parse
{
    public class QueryOutFields
    {
        private readonly string[] _names;

        public QueryOutFields(string outFieldsText)
        {
            outFieldsText = outFieldsText?.Trim();

            if (String.IsNullOrEmpty(outFieldsText))
            {
               // do nothing
            }
            else if (outFieldsText.Contains(";"))
            {
                // Parse, Lexer
                if (!outFieldsText.EndsWith(";"))
                    outFieldsText = $"{ outFieldsText };";

                var lexicalAnalyser = new LexicalAnalyser(new LuceneServerOutFieldsSyntax());
                var tokens = lexicalAnalyser.Tokenize(outFieldsText);

                Fields = tokens.GetStatements()
                               .Select(s => new QueryOutField(s))
                               .ToArray();
            }
            else
            {
                // Simple
                Fields = outFieldsText.Split(',')
                                      .Select(f => f.Trim())
                                      .Select(f => new QueryOutField(f))
                                      .ToArray();
            }

            _names = this.Fields?.Select(f => f.Name).ToArray() ?? new string[0];

            if (this.Fields == null || Fields.Count() == 0)
                this.Fields = new QueryOutField[]
                {
                    new QueryOutField("*")
                };
        }

        public IEnumerable<QueryOutField> Fields
        {
            get;
        }

        public IEnumerable<string> Names => _names;

        public QueryOutField this[string name]
            => this.Fields.Where(f => f.Name == name).FirstOrDefault() ??
               this.Fields.Where(f => f.Name == "*").FirstOrDefault();

        public bool UseField(string name) =>
            _names.Count() == 0 ||  // any field
            _names.Contains("*") ||
            _names.Contains(name);
    }
}
