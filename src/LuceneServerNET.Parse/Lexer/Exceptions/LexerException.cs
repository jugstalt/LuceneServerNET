using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Parse.Lexer.Exceptions
{
    public class LexerException : Exception
    {
        public LexerException() { }
        public LexerException(string message, Exception innerException = null)
            :base(message, innerException)
        {

        }
    }
}
