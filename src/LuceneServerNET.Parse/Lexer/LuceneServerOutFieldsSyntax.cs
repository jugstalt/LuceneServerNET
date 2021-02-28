using LuceneServerNET.Parse.Lexer.Abstrations;

namespace LuceneServerNET.Parse.Lexer
{
    public class LuceneServerOutFieldsSyntax : ISyntax
    {
        #region Const

        private string[] _keywords = new[] { "REGEX_REPLACE", "AS" };

        private string[] _separator = new[] { ";" };

        private string[] _comments = new[] { "" };

        private string[] _operators = new[] { "(", ")", ",", "()", "." };

        #endregion

        public string[] Keywords => _keywords;

        public string[] Separator => _separator;

        public string[] Comments => _comments;

        public string[] Operators => _operators;
    }
}
