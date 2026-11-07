using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Parse.Exceptions
{
    public class InterpreterException : Exception
    {
        public InterpreterException(string message, Exception inner = null)
            : base($"Interpreter Exception: { message }", inner)
        { }
    }
}
