using LuceneServerNET.Parse.Excepitons;
using LuceneServerNET.Parse.Lexer;
using LuceneServerNET.Parse.Lexer.Extensions;
using LuceneServerNET.Parse.Methods.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace LuceneServerNET.Parse.Methods.OutFields.Extensions
{
    static public class MethodExtensions
    {
        static public IEnumerable<OutFieldMethod> Methods(this IEnumerable<Token> statement)
        {
            var methods = new List<OutFieldMethod>();

            var tokens = statement.ToArray();
            var tokensCount = tokens.Count();

            for (int i = 0; i < tokensCount; i++)
            {
                var token = tokens[i];
                if (token.IsPointOperator())
                {
                    var keywordToken = tokens[++i];
                    if (keywordToken.TokenType != TokenType.Keyword)
                    {
                        throw new InterpreterException($"{ keywordToken.TokenValue } is not a keyword");
                    }

                    var method = OutFieldMethods.Get(keywordToken.TokenValue);
                    if(method == null)
                    {
                        throw new InterpreterException($"Method not implemented: { keywordToken.TokenValue }");
                    }

                    var operatorToken = tokens[++i];

                    var parameters = tokens.CollectParameters(ref i);

                    methods.Add(new OutFieldMethod(method, parameters.Select(p=>p.TokenValue).ToArray()));
                }
            }

            return methods;
        }
    }
}
