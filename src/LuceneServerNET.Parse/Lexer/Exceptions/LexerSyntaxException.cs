using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Parse.Lexer.Exceptions
{
    class LexerSyntaxException : LexerException
    {
        public LexerSyntaxException(string message, Exception innerException = null)
            :base($"Lexer Syntax Error: { message }", innerException) 
        { }
    }
}
