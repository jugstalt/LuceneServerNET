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
                return;
            }
                
            if(outFieldsText.Contains(";"))
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

            _names = this.Fields.Select(f => f.Name).ToArray();
        }

        public IEnumerable<QueryOutField> Fields
        {
            get;
        }

        public IEnumerable<string> Names => _names;
    }
}
