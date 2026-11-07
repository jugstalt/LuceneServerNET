using System;

namespace LuceneServerNET.Engine.Exceptions
{
    /// <summary>The requested index does not exist (API v2: HTTP 404).</summary>
    public class IndexNotFoundException : Exception
    {
        public IndexNotFoundException(string message = "Index not exists") : base(message) { }
    }

    /// <summary>The request conflicts with the current index state, e.g. index already exists (API v2: HTTP 409).</summary>
    public class IndexConflictException : Exception
    {
        public IndexConflictException(string message) : base(message) { }
    }
}
