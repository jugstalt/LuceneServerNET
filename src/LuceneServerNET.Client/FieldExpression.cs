using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Client
{
    static class FieldExpression
    {
        static public string NWordsPattern(int n)
        {
            // @"^((?:\S+\s+){0}\S+).*" "${1}"
            return @"^((?:\S+\s+){" + Math.Max(n - 1, 0) + @"}\S+).*";
        }

        static public string NCharactersPattern(int n)
        {
            // "^(.{80}).*$" "${1}"
            return @"^(.{" + Math.Max(n, 0) + @"}).*$";
        }
    }
}
