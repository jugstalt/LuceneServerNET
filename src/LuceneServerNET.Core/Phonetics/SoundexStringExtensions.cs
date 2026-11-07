using System;
using System.Text;

namespace LuceneServerNET.Core.Phonetics
{
    static public class SoundexStringExtensions
    {

        public static string ToSoundex(this string data)
        {
            StringBuilder result = new StringBuilder();

            if (data != null && data.Length > 0)
            {
                result.Append(data.Substring(0, 1));

                // the code of the first letter counts as previous code (Pfister => P236)
                string previousCode = SoundexCode(char.ToLowerInvariant(data[0]));

                for (int i = 1; i < data.Length; i++)
                {
                    char currentLetter = char.ToLowerInvariant(data[i]);

                    if ("aeiouy".IndexOf(currentLetter) > -1)
                    {
                        // vowels separate equal codes (Tymczak => T522)
                        previousCode = "";
                        continue;
                    }

                    // h, w and other characters are ignored and do not separate (Ashcraft => A261)
                    string currentCode = SoundexCode(currentLetter);
                    if (currentCode == "")
                    {
                        continue;
                    }

                    if (currentCode != previousCode)
                    {
                        result.Append(currentCode);

                        if (result.Length == 4)
                        {
                            break;
                        }
                    }

                    previousCode = currentCode;
                }
            }
            if (result.Length < 4)
            {
                result.Append(new String('0', 4 - result.Length));
            }

            return result.ToString().ToUpper();
        }

        private static string SoundexCode(char letter)
        {
            if ("bfpv".IndexOf(letter) > -1) return "1";
            if ("cgjkqsxz".IndexOf(letter) > -1) return "2";
            if ("dt".IndexOf(letter) > -1) return "3";
            if (letter == 'l') return "4";
            if ("mn".IndexOf(letter) > -1) return "5";
            if (letter == 'r') return "6";
            return "";
        }

        public static int Difference(this string data1, string data2)
        {
            int result = 0;
            string soundex1 = data1.ToSoundex();
            string soundex2 = data2.ToSoundex();

            if (soundex1 == soundex2)
            {
                result = 4;
            }
            else
            {
                string sub1 = soundex1.Substring(1, 3);
                string sub2 = soundex1.Substring(2, 2);
                string sub3 = soundex1.Substring(1, 2);
                string sub4 = soundex1.Substring(1, 1);
                string sub5 = soundex1.Substring(2, 1);
                string sub6 = soundex1.Substring(3, 1);
                if (soundex2.IndexOf(sub1) > -1)
                {
                    result = 3;
                }
                else if (soundex2.IndexOf(sub2) > -1)
                {
                    result = 2;
                }
                else if (soundex2.IndexOf(sub3) > -1)
                {
                    result = 2;
                }
                else
                {
                    if (soundex2.IndexOf(sub4) > -1)
                    {
                        result++;
                    }

                    if (soundex2.IndexOf(sub5) > -1)
                    {
                        result++;
                    }

                    if (soundex2.IndexOf(sub6) > -1)
                    {
                        result++;
                    }
                }
                if (soundex1.Substring(0, 1) == soundex2.Substring(0, 1))
                {
                    result++;
                }
            }
            return (result == 0) ? 1 : result;
        }
    }
}
