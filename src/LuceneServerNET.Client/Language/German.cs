using System.Collections.Generic;

namespace LuceneServerNET.Client.Language
{
    internal class German : Lang
    {
        private IDictionary<string, string> _replaceChars = new Dictionary<string, string>()
        {
            { "ß", "ss" },
            { "ä","ae" },
            { "ö","oe" },
            { "ü","ue" },
            { "ss", "ß" },
            { "ae","ä" },
            { "oe","ö" },
            { "ue","ü" },
        };

        override public IDictionary<string, string> ReplaceChars()
        {
            return _replaceChars;
        }

        public override Languages Language => Languages.German;
    }
}
