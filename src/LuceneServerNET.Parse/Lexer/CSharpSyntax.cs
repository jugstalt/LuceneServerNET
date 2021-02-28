using LuceneServerNET.Parse.Lexer.Abstrations;

namespace LuceneServerNET.Parse.Lexer
{
    public class CSharpSyntax : ISyntax
    {
        #region Const

        private string[] _keywords = new[] { "abstract", "as", "base", "bool", "break", "by",
            "byte", "case", "catch", "char", "checked", "class", "const",
            "continue", "decimal", "default", "delegate", "do", "double",
            "descending", "explicit", "event", "extern", "else", "enum",
            "false", "finally", "fixed", "float", "for", "foreach", "from",
            "goto", "group", "if", "implicit", "in", "int", "interface",
            "internal", "into", "is", "lock", "long", "new", "null", "namespace",
            "object", "operator", "out", "override", "orderby",  "params",
            "private", "protected", "public", "readonly", "ref", "return",
            "switch", "struct", "sbyte", "sealed", "short", "sizeof",
            "stackalloc", "static", "string", "select",  "this",
            "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
            "unsafe", "ushort", "using", "var", "virtual", "volatile",
            "void", "while", "where", "yield" };

        private string[] _separator = new[] { ";", "{", "}", "\r", "\n", "\r\n" };

        private string[] _comments = new[] { "//", "/*", "*/" };

        private string[] _operators = new[] { "+", "-", "*", "/", "%", "&","(",")","[","]",
            "|", "^", "!", "~", "&&", "||",",",
            "++", "--", "<<", ">>", "==", "!=", "<", ">", "<=",
            ">=", "=", "+=", "-=", "*=", "/=", "%=", "&=", "|=",
            "^=", "<<=", ">>=", ".", "[]", "()", "?:", "=>", "??" };

        #endregion

        public string[] Keywords => _keywords;

        public string[] Separator => _separator;

        public string[] Comments => _comments;

        public string[] Operators => _operators;
    }
}
