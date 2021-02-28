using LuceneServerNET.Parse.Lexer.Abstrations;
using LuceneServerNET.Parse.Lexer.Exceptions;
using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Parse.Lexer
{
    public class LexicalAnalyser
    {
        private readonly ISyntax _syntax;

        public LexicalAnalyser(ISyntax syntax)
        {
            _syntax = syntax;
        }

        public IEnumerable<Token> Tokenize(string text)
        {
            List<Token> tokens = new List<Token>();
            while (!String.IsNullOrEmpty(text))
            {
                var token = GetNextLexicalAtom(ref text);
                tokens.Add(token);
            }
            return tokens;
        }

        private Token Parse(string item)
        {
            if (Int32.TryParse(item, out int ok))
            {
                return new Token(TokenType.NumericalConstant, item);
            }

            if (item.Equals("\r\n") || item.Equals("\n"))
            {
                return new Token(TokenType.NewLine);
            }

            if (CheckKeyword(item) == true)
            {
                return new Token(TokenType.Keyword, item);
            }

            if (CheckOperator(item) == true)
            {
                return new Token(TokenType.Operator, item);
            }
            if (CheckDelimiter(item) == true)
            {
                return new Token(TokenType.Separator, item);
            }

            return new Token(TokenType.Identifier, item);
        }

        private bool CheckOperator(string str)
        {
            if (Array.IndexOf(_syntax.Operators, str) > -1)
            {
                return true;
            }

            return false;
        }

        private bool CheckDelimiter(string str)
        {
            if (Array.IndexOf(_syntax.Separator, str) > -1)
            {
                return true;
            }

            return false;
        }
        private bool CheckKeyword(string str)
        {
            if (Array.IndexOf(_syntax.Keywords, str) > -1)
            {
                return true;
            }

            return false;
        }
        private bool CheckComments(string str)
        {
            if (Array.IndexOf(_syntax.Comments, str) > -1)
            {
                return true;
            }

            return false;
        }

        private Token GetNextLexicalAtom(ref string item)
        {
            StringBuilder tokenString = new StringBuilder();
            for (int i = 0; i < item.Length; i++)
            {
                if (CheckDelimiter(item[i].ToString()))
                {
                    if (i + 1 < item.Length && CheckDelimiter(item.Substring(i, 2)))
                    {
                        tokenString.Append(item.Substring(i, 2));
                        item = item.Remove(i, 2);
                        return Parse(tokenString.ToString());
                    }
                    else
                    {
                        tokenString.Append(item[i]);
                        item = item.Remove(i, 1);
                        return Parse(tokenString.ToString());
                    }

                }
                else if (CheckOperator(item[i].ToString()))
                {
                    if (i + 1 < item.Length && (CheckOperator(item.Substring(i, 2))))
                    {
                        if (i + 2 < item.Length && CheckOperator(item.Substring(i, 3)))
                        {
                            tokenString.Append(item.Substring(i, 3));
                            item = item.Remove(i, 3);
                            return Parse(tokenString.ToString());
                        }
                        else
                        {
                            tokenString.Append(item.Substring(i, 2));
                            item = item.Remove(i, 2);
                            return Parse(tokenString.ToString());
                        }
                    }
                    else if (CheckComments(item.Substring(i, 2)))
                    {
                        if (item.Substring(i, 2).Equals("//"))
                        {
                            do
                            {
                                i++;
                            } while (item[i] != '\n');
                            item = item.Remove(0, i + 1);
                            item = item.Trim(' ', '\t', '\r', '\n');
                            i = -1;
                        }
                        else
                        {
                            do
                            {
                                i++;
                            } while (item.Substring(i, 2).Equals("*/") == false);
                            item = item.Remove(0, i + 2);
                            item = item.Trim(' ', '\t', '\r', '\n');
                            i = -1;
                        }

                    }
                    else
                    {
                        if (item[i] == '-' && Int32.TryParse(item[i + 1].ToString(), out int ok))
                        {
                            continue;
                        }

                        tokenString.Append(item[i]);
                        item = item.Remove(i, 1);
                        return Parse(tokenString.ToString());
                    }

                }
                else if (item[i] == '\'')
                {
                    int j = i + 1;
                    if (item[j] == '\\')
                    {
                        j += 2;
                    }
                    else
                    {
                        j++;
                    }
                    if (item[j] != '\'')
                    {
                        throw new LexerSyntaxException($"Invalid char constant { item }...");
                    }

                    var result = new Token(TokenType.CharacterConstant, item.Substring(i + 1, j - i - 1));
                    item = item.Remove(i, j - i + 1);
                    return result;
                }
                else if (item[i] == '"')
                {
                    int j = i + 1;

                    bool valid = false;
                    while (j < item.Length - 1)
                    //while (item[j] != '"' && item[j - 1] != '\\')
                    {
                        if (item[j] == '"' && item[j - 1] != '\\')
                        {
                            valid = true;
                            break;
                        }
                        j++;
                    }

                    if (!valid)
                    {
                        throw new LexerSyntaxException($"Invalid literal constant { item }...");
                    }

                    var result = new Token(TokenType.LiteralConstant, EscapeLiteral(item.Substring(i + 1, j - i - 1)));
                    item = item.Remove(i, j - i + 1);
                    return result;
                }
                else if (i < item.Length - 1 && (item[i + 1].ToString().Equals(" ") || CheckDelimiter(item[i + 1].ToString()) == true || CheckOperator(item[i + 1].ToString()) == true))
                {
                    if (Parse(item.Substring(0, i + 1)).TokenType == TokenType.NumericalConstant && item[i + 1] == '.')
                    {
                        int j = i + 2;
                        while (item[j].ToString().Equals(" ") == false && CheckDelimiter(item[j].ToString()) == false && CheckOperator(item[j].ToString()) == false)
                        {
                            j++;
                        }

                        int ok;
                        if (Int32.TryParse(item.Substring(i + 2, j - i - 2), out ok))
                        {
                            var result = new Token(TokenType.NumericalConstant, item.Substring(0, j));
                            item = item.Remove(0, j);
                            return result;
                        }

                    }
                    tokenString.Append(item.Substring(0, i + 1));
                    item = item.Remove(0, i + 1);
                    return Parse(tokenString.ToString());
                }
            }

            if (!String.IsNullOrEmpty(item))
            {
                throw new LexerSyntaxException($"Unexpecting end of line '{ item }'");
            }

            return null;
        }

        private string EscapeLiteral(string literal)
        {
            return literal
                    .Replace("\\\"", "\"")
                    .Replace("\\'", "\'")
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r");
        }
    }
}
