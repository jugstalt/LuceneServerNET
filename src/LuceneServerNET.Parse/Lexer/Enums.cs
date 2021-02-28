using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Parse.Lexer
{
    public enum TokenType
    {
        NewLine,
        NumericalConstant,
        LiteralConstant,
        CharacterConstant,
        Keyword,
        Operator,
        Separator,
        Identifier
    }
}
