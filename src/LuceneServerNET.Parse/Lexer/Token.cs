using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Parse.Lexer
{
    public class Token
    {
        public Token(TokenType tokenType, string tokenValue = "")
        {
            this.TokenType = tokenType;
            this.TokenValue = tokenValue;
        }

        public TokenType TokenType { get; set; }
        public string TokenValue { get; set; }

        public override string ToString()
        {
            return $"{ this.TokenType }: { this.TokenValue }";
        }
    }
}
