using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KenshiPatcher.ExpressionReader
{
    
    class Lexer
    {
        private readonly string text;
        private int pos = 0;

        public Lexer(string text) => this.text = text;

        public Token Next()
        {
            SkipWhitespace();

            if (pos >= text.Length) return new Token { Type = TokenType.End };

            char c = text[pos];

            if (c == '@')
            {
                pos++;
                return new Token { Type = TokenType.AtSign, OriginalText = "@" };
            }
            if (char.IsDigit(c)) return ReadNumber();
            if (char.IsLetter(c)) return ReadIdentifierOrBool();
            if (c == '"') return ReadString();
            if ("+-*/&|=!><".Contains(c)) return ReadOperator();
            if (c == '(') { pos++; return new Token { Type = TokenType.LParen, OriginalText = "(" }; }
            if (c == ')') { pos++; return new Token { Type = TokenType.RParen, OriginalText = ")" }; }
            if (c == ',') { pos++; return new Token { Type = TokenType.Comma, OriginalText = "," }; }
            if (c == '[') { pos++; return new Token { Type = TokenType.LBracket, OriginalText = "[" }; }
            if (c == ']') { pos++; return new Token { Type = TokenType.RBracket, OriginalText = "]" }; }

            throw new Exception($"Unexpected char {c}");
        }

        private void SkipWhitespace() { while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++; }

        private Token ReadNumber()
        {
            int start = pos;
            if (text[pos] == '-') pos++; // negative numbers
            while (pos < text.Length && (char.IsDigit(text[pos]) || text[pos] == '.')) pos++;

            string numText = text.Substring(start, pos - start);
            if (numText.Contains('.'))
                return new Token { Type = TokenType.DoubleLiteral, OriginalText = numText, LiteralValue = double.Parse(numText) };
            else
                return new Token { Type = TokenType.IntLiteral, OriginalText = numText, LiteralValue = int.Parse(numText) };
        }

        private Token ReadIdentifierOrBool()
        {
            int start = pos;
            while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_')) pos++;
            string id = text.Substring(start, pos - start);

            if (id == "true") return new Token { Type = TokenType.BoolLiteral, OriginalText = id, LiteralValue = true };
            if (id == "false") return new Token { Type = TokenType.BoolLiteral, OriginalText = id, LiteralValue = false };

            return new Token { Type = TokenType.Identifier, OriginalText = id };
        }

        private Token ReadString()
        {
            pos++; // skip opening "
            int start = pos;
            while (pos < text.Length && text[pos] != '"') pos++;
            string str = text.Substring(start, pos - start);
            pos++; // skip closing "
            return new Token { Type = TokenType.StringLiteral, OriginalText = str, LiteralValue = str };
        }

        private Token ReadOperator()
        {
            // Handle multi-char operators like ==, !=, >=, <=, &&, ||
            if (pos + 1 < text.Length)
            {
                string two = text.Substring(pos, 2);
                if (two == "==" || two == "!=" || two == ">=" || two == "<=" || two == "&&" || two == "||" || two == "->" || two == "=>")
                {
                    pos += 2;
                    return new Token { Type = TokenType.Operator, OriginalText = two };
                }
            }

            string one = text[pos].ToString();
            pos++;
            return new Token { Type = TokenType.Operator, OriginalText = one };
        }
    }
}
