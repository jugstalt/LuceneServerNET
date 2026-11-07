using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Parse.Lexer.Abstractions
{
    public interface ISyntax
    {
        string[] Keywords { get; }
        string[] Separator { get; }
        string[] Comments { get; }
        string[] Operators { get; }
    }
}
