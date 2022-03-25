using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Core.Extensions
{
    static class StringExtensions
    {
        static public bool IsNumeric(this string str)
        {
            if (String.IsNullOrEmpty(str))
            {
                return false;
            }

            return int.TryParse(str, out _);
        }
    }
}
