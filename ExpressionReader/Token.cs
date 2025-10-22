using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KenshiPatcher.ExpressionReader
{
    public enum TokenType
    {
        Identifier,     // function names, variables
        BoolLiteral,    // true/false
        LBracket,
        RBracket,
        IntLiteral,     // optional: distinguish int
        DoubleLiteral,  // optional: distinguish double
        StringLiteral,  // "text"
        Operator,       // + or && for example
        LParen,
        RParen,
        Comma,
        End
    }

    public class Token
    {
        public TokenType Type;
        public string OriginalText;         // the original text
        public object? LiteralValue; // parsed number, bool, or string
        private int pos = 0;
        private List<Token> peekBuffer = new List<Token>();
    }
}
