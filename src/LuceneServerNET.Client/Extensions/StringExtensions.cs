using System;

namespace LuceneServerNET.Client.Extensions
{
    static public class StringExtensions
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
