using LuceneServerNET.Parse.Lexer.Exceptions;
using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Parse.Lexer.Extensions
{
    static public class TokenExtensions
    {
        static public string ToCommandLine(this IEnumerable<Token> tokens)
        {
            var sb = new StringBuilder();

            if (tokens != null)
            {
                foreach (var token in tokens)
                {
                    var tokenString = token.TokenValue;

                    switch (token.TokenType)
                    {
                        case TokenType.LiteralConstant:
                            tokenString = $"\"{ tokenString }\"";
                            break;
                        case TokenType.CharacterConstant:
                            tokenString = $"'{ tokenString }'";
                            break;
                    }

                    sb.Append(tokenString);
                }
            }

            return sb.ToString();
        }

        static public IEnumerable<IEnumerable<Token>> GetStatements(this IEnumerable<Token> tokens, string separator = "")
        {
            List<IEnumerable<Token>> statements = new List<IEnumerable<Token>>();

            List<Token> statement = new List<Token>();
            foreach (var token in tokens)
            {
                if (token.TokenType == TokenType.Separator &&
                    (String.IsNullOrEmpty(separator) || separator == token.TokenValue))
                {
                    if (statement.Count > 0)
                    {
                        statements.Add(statement);
                        statement = new List<Token>();
                    }
                }
                else
                {
                    statement.Add(token);
                }
            }

            return statements;
        }

        static public bool IsPointOperator(this Token token)
        {
            return token.TokenType == TokenType.Operator && token.TokenValue == ".";
        }

        static public bool IsCommaOperator(this Token token)
        {
            return token.TokenType == TokenType.Operator && token.TokenValue == ",";
        }

        static public List<Token> CollectParameters(this Token[] tokens, ref int index)
        {
            List<Token> parametes = new List<Token>();
            var operatorToken = tokens[index];

            if (operatorToken.TokenType != TokenType.Operator)
            {
                throw new LexerException($"{ operatorToken.TokenValue } is no operator token. Syntax error?");
            }

            if (operatorToken.TokenValue == "()")
            {
                return parametes;
            }

            string closingTokken = String.Empty;
            switch (operatorToken.TokenValue)
            {
                case "(":
                    closingTokken = ")";
                    break;
                case "{":
                    closingTokken = "}";
                    break;
                case "[":
                    closingTokken = "]";
                    break;
            }

            if (String.IsNullOrEmpty(closingTokken))
            {
                throw new LexerException($"Can't determine closing token for '{ operatorToken.TokenValue }'");
            }

            int level = 0;
            for (int i = index + 1; i < tokens.Length; i++)
            {
                if (tokens[i].TokenType == TokenType.Operator && tokens[i].TokenValue == operatorToken.TokenValue)
                {
                    level++;
                }

                if (tokens[i].TokenType == TokenType.Operator && tokens[i].TokenValue == closingTokken)
                {
                    if (level == 0)
                    {
                        index = i;
                        break;
                    } else
                    {
                        level--;
                    }
                }

                if (!tokens[i].IsCommaOperator())
                {
                    parametes.Add(tokens[i]);
                }
            }

            if (level != 0)
            {
                throw new LexerException($"CollectParameters: { tokens.ToCommandLine() }");
            }

            return parametes;
        }
    }
}
