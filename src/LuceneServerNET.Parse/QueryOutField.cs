using LuceneServerNET.Parse.Lexer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LuceneServerNET.Parse.Lexer.Extensions;
using LuceneServerNET.Parse.Methods.OutFields.Extensions;
using LuceneServerNET.Parse.Methods.OutFields;

namespace LuceneServerNET.Parse
{
    public class QueryOutField
    {
        private IEnumerable<Token> _tokens;
        private IEnumerable<OutFieldMethod> _methods;

        public QueryOutField(string name)
        {
            this.Name = name;
        }

        public QueryOutField(IEnumerable<Token> tokens)
        {
            var nameToken = tokens.FirstOrDefault();

            if (nameToken == null || nameToken.TokenType != TokenType.Identifier)
            {
                throw new Exception($"Invalid outfiled syntax: { tokens.ToCommandLine() }");
            }
            else
            {
                this.Name = nameToken.TokenValue;
            }

            _tokens = tokens.Skip(1);
            _methods = _tokens.Methods();
        }

        public string Name { get; }

        public string CommandLine()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("this");

            sb.Append(_tokens.ToCommandLine());

            return sb.ToString();
        }

        public IEnumerable<OutFieldMethod> ApplyMethods => _methods ?? new OutFieldMethod[0];
    }
}
