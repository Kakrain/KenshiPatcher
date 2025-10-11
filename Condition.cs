using KenshiCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KenshiPatcher
{
    abstract class Condition
    {
        public abstract bool Evaluate(ModRecord r);
    }
    class FieldExistCondition : Condition
    {
        string field;
        public FieldExistCondition(string field) => this.field = field;
        public override bool Evaluate(ModRecord r) => r.HasField(field);
    }

    class FieldIsEmptyOrNotExistCondition : Condition
    {
        string field;
        public FieldIsEmptyOrNotExistCondition(string field) => this.field = field;
        public override bool Evaluate(ModRecord r) => !r.HasField(field)||string.IsNullOrEmpty(r.GetField(field));
    }
    class FieldIsEmptyAndExistCondition : Condition
    {
        string field;
        public FieldIsEmptyAndExistCondition(string field) => this.field = field;
        public override bool Evaluate(ModRecord r) => r.HasField(field) && string.IsNullOrEmpty(r.GetField(field));
    }
    class FieldIsNotEmptyCondition : Condition
    {
        string field;
        public FieldIsNotEmptyCondition(string field) => this.field = field;
        public override bool Evaluate(ModRecord r) =>
            r.HasField(field) && !string.IsNullOrEmpty(r.GetField(field));
    }
    class NotCondition : Condition
    {
        Condition inner;
        public NotCondition(Condition inner) => this.inner = inner;
        public override bool Evaluate(ModRecord r) => !inner.Evaluate(r);
    }

    class AndCondition : Condition
    {
        Condition left, right;
        public AndCondition(Condition left, Condition right) { this.left = left; this.right = right; }
        public override bool Evaluate(ModRecord r) => left.Evaluate(r) && right.Evaluate(r);
    }

    class OrCondition : Condition
    {
        Condition left, right;
        public OrCondition(Condition left, Condition right) { this.left = left; this.right = right; }
        public override bool Evaluate(ModRecord r) => left.Evaluate(r) || right.Evaluate(r);
    }
    enum TokenType { Identifier, String, LParen, RParen, And, Or, Not, Comma, End }

    class Token
    {
        public TokenType Type;
        public string? Value;
    }
    class Lexer
    {
        string text;
        int pos = 0;

        public Lexer(string text) => this.text = text;

        public Token Next()
        {
            while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;

            if (pos >= text.Length) return new Token { Type = TokenType.End };

            char c = text[pos];

            if (c == '(') { pos++; return new Token { Type = TokenType.LParen }; }
            if (c == ')') { pos++; return new Token { Type = TokenType.RParen }; }
            if (c == '!') { pos++; return new Token { Type = TokenType.Not }; }
            if (c == '&' && pos + 1 < text.Length && text[pos + 1] == '&') { pos += 2; return new Token { Type = TokenType.And }; }
            if (c == '|' && pos + 1 < text.Length && text[pos + 1] == '|') { pos += 2; return new Token { Type = TokenType.Or }; }
            if (c == ',') { pos++; return new Token { Type = TokenType.Comma }; }

            if (c == '"') // string literal
            {
                pos++;
                int start = pos;
                while (pos < text.Length && text[pos] != '"') pos++;
                string str = text.Substring(start, pos - start);
                pos++; // skip closing "
                return new Token { Type = TokenType.String, Value = str };
            }

            // identifier (function names)
            if (char.IsLetter(c))
            {
                int start = pos;
                while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_')) pos++;
                string id = text.Substring(start, pos - start);
                return new Token { Type = TokenType.Identifier, Value = id };
            }

            throw new Exception($"Unexpected character: {c}");
        }
    }
    class Parser
    {
        Lexer lexer;
        Token current;

        public Parser(string text)
        {
            lexer = new Lexer(text);
            current = lexer.Next();
        }

        void Eat(TokenType type)
        {
            if (current.Type == type) current = lexer.Next();
            else throw new Exception($"Expected {type}, got {current.Type}");
        }

        public Condition ParseExpression() => ParseOr();

        Condition ParseOr()
        {
            Condition left = ParseAnd();
            while (current.Type == TokenType.Or)
            {
                Eat(TokenType.Or);
                Condition right = ParseAnd();
                left = new OrCondition(left, right);
            }
            return left;
        }

        Condition ParseAnd()
        {
            Condition left = ParseNot();
            while (current.Type == TokenType.And)
            {
                Eat(TokenType.And);
                Condition right = ParseNot();
                left = new AndCondition(left, right);
            }
            return left;
        }

        Condition ParseNot()
        {
            if (current.Type == TokenType.Not)
            {
                Eat(TokenType.Not);
                return new NotCondition(ParseNot());
            }
            return ParsePrimary();
        }
        class LiteralCondition : Condition
        {
            bool value;
            public LiteralCondition(bool value) => this.value = value;
            public override bool Evaluate(ModRecord r) => value;
        }

        Condition ParsePrimary()
        {
            if (current.Type == TokenType.LParen)
            {
                Eat(TokenType.LParen);
                var expr = ParseExpression();
                Eat(TokenType.RParen);
                return expr;
            }

            if (current.Type == TokenType.Identifier)
            {
                string funcName = current.Value!;
                Eat(TokenType.Identifier);

                if (string.Equals(funcName, "true", StringComparison.OrdinalIgnoreCase))
                    return new LiteralCondition(true);
                if (string.Equals(funcName, "false", StringComparison.OrdinalIgnoreCase))
                    return new LiteralCondition(false);

                Eat(TokenType.LParen);
                string arg = current.Value!;
                Eat(TokenType.String);
                Eat(TokenType.RParen);

                switch (funcName)
                {
                    case "field_exist": return new FieldExistCondition(arg);
                    case "field_is_empty_or_not_exist": return new FieldIsEmptyOrNotExistCondition(arg);
                    case "field_is_empty_and_exist": return new FieldIsEmptyAndExistCondition(arg);
                    case "field_is_not_empty": return new FieldIsNotEmptyCondition(arg);
                    default: throw new Exception($"Unknown function {funcName}");
                }
            }

            throw new Exception($"Unexpected token {current.Type}");
        }
    }
}
