using LuceneServerNET.Core.Models.Mapping;
using System;
using System.Linq;

namespace LuceneServerNET.Extensions
{
    static public class StringValueExtensions
    {
        #region Encode

        static public string ToStringWithAsciiEncode(this object str, 
                                                     FieldMapping field,
                                                     IndexMapping mapping)
        {
            if (field == null ||
                mapping?.PrimaryFields == null ||
                mapping?.PrimaryFieldsEncodeCharacters == null ||
                mapping.PrimaryFieldsEncodeCharacters.Length == 0 ||
                field.IsPrimaryField(mapping) == false)
            {
                return str?.ToString() ?? string.Empty;
            }

            var result = str.ToString();
            foreach (var character in mapping.PrimaryFieldsEncodeCharacters)
            {
                result = result.Replace(character.ToString(), character.ToAsciiEncoded());
            }

            return result;
        }

        #endregion

        #region Decode

        static public object DecodeAsciiCharacters(this object strObject,
                                                   FieldMapping field,
                                                   IndexMapping mapping)
        {
            if (field == null ||
                mapping?.PrimaryFields == null ||
                mapping?.PrimaryFieldsEncodeCharacters == null ||
                mapping.PrimaryFieldsEncodeCharacters.Length == 0 ||
                field.IsPrimaryField(mapping) == false)
            {
                return strObject;
            }
           
            if (strObject is string)
            {
                if (field.FieldType == "text" || field.FieldType == "string")
                {
                    var str = strObject.ToString();
                    foreach (var character in mapping.PrimaryFieldsEncodeCharacters)
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
                mapping?.PrimaryFieldsEncodeCharacters == null ||
                mapping.PrimaryFieldsEncodeCharacters.Length == 0)
            {
                return term?.ToString() ?? string.Empty;
            }

            if (term.Contains(":"))
            {
                // ToDo:
                return term;
            }
            else
            {
                // PrimaryFields

                foreach (var character in mapping.PrimaryFieldsEncodeCharacters)
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

        static public bool IsPrimaryField(this FieldMapping field,
                                          IndexMapping mapping)
        {
            if (mapping?.PrimaryFields == null || field == null)
                return false;

            return mapping.PrimaryFields.Contains(field.Name);
        }

        static public string ToAsciiEncoded(this char c)
        {
            string hex = Convert.ToByte(c).ToString("x2");

            return $"__ascii_{hex}_";
        }
    }
}
