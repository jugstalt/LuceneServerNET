using System;

namespace LuceneServerNET.Extensions
{
    static public class CompareExtensions
    {
        static public string OrTake(this string currentStringValue, string alternativeStringValue)
        {
            return String.IsNullOrEmpty(currentStringValue) ? alternativeStringValue : currentStringValue;
        }

        static public int OrTake(this int currentIntValue, int alternativeIntValue)
        {
            return currentIntValue > 0 ? currentIntValue : alternativeIntValue;
        }
    }
}
