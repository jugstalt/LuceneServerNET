using LuceneServerNET.Core.Models.Mapping;
using System;
using System.Linq;

namespace LuceneServerNET.Extensions
{
    static public class StringValueExtensions
    {
        #region Encode

        static public string ToStringWithAsciiEncode(this object str, FieldMapping field)
        {
            if (str == null || field?.EncodeCharacters == null || field.EncodeCharacters.Length == 0)
            {
                return str?.ToString() ?? string.Empty;
            }

            var result = str.ToString();
            foreach (var character in field.EncodeCharacters)
            {
                result = result.Replace(character.ToString(), character.ToAsciiEncoded());
            }

            return result;
        }

        #endregion

        #region Decode

        static public object DecodeAsciiCharacters(this object strObject,
                                                   FieldMapping field)
        {
            if (field == null || field?.EncodeCharacters == null || field.EncodeCharacters.Length == 0)
            {
                return strObject;
            }

            if (strObject is string)
            {
                if (field.FieldType == "text")
                {
                    var str = strObject.ToString();
                    foreach (var character in field.EncodeCharacters)
                    {
                        str = str.Replace(character.ToAsciiEncoded(), character.ToString());
                    }
                    return str;
                }
            }

            return strObject;
        }

        #endregion

        #region Term Parsing

        static public string ParseSerachTerm(this string term, IndexMapping mapping)
        {
            if (term == null ||
                term.Contains(":") ||
                mapping?.Fields == null)
            {
                return term?.ToString() ?? string.Empty;
            }

            foreach (var field in mapping.Fields
                                         .Where(f => f.Index && f.EncodeCharacters != null))
            {
                foreach (var character in field.EncodeCharacters)
                {
                    switch (character)
                    {
                        case '/':
                            term = term.Replace("//", character.ToAsciiEncoded());
                            break;
                        default:
                            term = term.Replace(character.ToString(), character.ToAsciiEncoded());
                            break;
                    }
                }
            }

            return term;
        }

        #endregion

        static public string ToAsciiEncoded(this char c)
        {
            string hex = Convert.ToByte(c).ToString("x2");

            return $"__ascii_{hex}_";
        }
    }
}
