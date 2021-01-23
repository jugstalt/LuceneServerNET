using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Client.Exceptions
{
    public class LuceneServerClientException : Exception
    {
        public LuceneServerClientException(string message, Exception inner = null)
            : base(message, inner)
        {

        }
    }
}
