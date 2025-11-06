using KenshiCore;
using KenshiPatcher;
using KenshiPatcher.ExpressionReader;
using static KenshiPatcher.ExpressionReader.IndexExpression;

class Parser
{
    private Lexer lexer;
    private Token current;
    private readonly Dictionary<string, int> _precedence = new()
    {
        { "->", 0 },
        { "||", 1 },
        { "&&", 2 },
        { "==", 3 },{ "!=", 3 },
        { ">",  4 },{ "<",  4 },{ ">=", 4 },{ "<=", 4 },
        { "+",  5 },{ "-",  5 },
        { "*",  6 },{ "/",  6 },
    };

    public Parser(string text)
    {
        lexer = new Lexer(text);
        current = lexer.Next();
    }

    private void Eat(TokenType type)
    {
        if (current.Type == type)
            current = lexer.Next();
        else
            throw new Exception($"Expected {type}, got {current.Type}");
    }
    private IExpression<object> ParseGlobalFunctionCall(string funcName)
    {
        Eat(TokenType.LParen);
        var args = new List<IExpression<object>>();

        if (current.Type != TokenType.RParen)
        {
            args.Add(ParseExpression());
            while (current.Type == TokenType.Comma)
            {
                Eat(TokenType.Comma);
                args.Add(ParseExpression());
            }
        }

        Eat(TokenType.RParen);
        return new GlobalFunctionExpression(funcName, args);
    }
    public IExpression<object>ParseExpression(int minPrecedence = 0)
    {
        IExpression<object> left;
        if (current.Type == TokenType.AtSign)
        {
            Eat(TokenType.AtSign);

            if (current.Type != TokenType.Identifier)
                throw new Exception("Expected identifier after '@'");

            string funcName = current.OriginalText!;
            Eat(TokenType.Identifier);
            return ParseGlobalFunctionCall(funcName);
        }

        if (current.Type == TokenType.Operator && (current.OriginalText == "-" || current.OriginalText == "!"))
        {
            string op = current.OriginalText;
            Eat(TokenType.Operator);
            var operand = ParseExpression(7); // Unary has higher precedence than any binary (6)
            left = new UnaryExpression(operand, op);
        }
        else
        {
            left = ParsePrimary();
        }

        while (true)
        {
            var currentToken = this.current;
            if (currentToken.Type != TokenType.Operator) break;

            var op = currentToken.OriginalText;
            if (!_precedence.TryGetValue(op!, out int opPrecedence))
                break;

            if (opPrecedence < minPrecedence) break;

            Eat(TokenType.Operator);
            var right = ParseExpression(opPrecedence + 1);
            //left = new BinaryExpression(left, right, op!);
            if (op == "->")
            {
                // Special case: pipe operator
                left = new PipeExpression(left as RecordGroupExpression ?? throw new Exception("Left side of '->' must be a Definition"), right as ProcedureExpression?? throw new Exception("Right side of '->' must be a Procedure"));
            }
            else
            {
                // Normal binary expression
                left = new BinaryExpression(left, right, op!);
            }
        }

        return left;
    }

    private IExpression<object> ParsePrimary()
    {
        IExpression<object> expr;
        switch (current.Type)
        {
            case TokenType.IntLiteral:
                int intVal = int.Parse(current.OriginalText!, System.Globalization.CultureInfo.InvariantCulture);
                Eat(TokenType.IntLiteral);
                expr= new ObjectExpression<int>(new Literal<int>(intVal));
                break;

            case TokenType.DoubleLiteral:
                double doubleVal = double.Parse(current.OriginalText!, System.Globalization.CultureInfo.InvariantCulture);
                Eat(TokenType.DoubleLiteral);
                expr = new ObjectExpression<double>(new Literal<double>(doubleVal));
                break;

            case TokenType.StringLiteral:
                string str = current.OriginalText!;
                Eat(TokenType.StringLiteral);
                expr = new ObjectExpression<string>(new Literal<string>(str)); 
                break;

            case TokenType.BoolLiteral:
                bool boolVal = bool.Parse(current.OriginalText!);
                Eat(TokenType.BoolLiteral);
                expr = new ObjectExpression<bool>(new Literal<bool>(boolVal));
                break;
            case TokenType.Identifier:
                {
                    string name = current.OriginalText!;
                    Eat(TokenType.Identifier);

                    if (current.Type == TokenType.LBracket)
                        expr = new TableNameExpression(name);  // table name literal
                    else if (current.Type == TokenType.LParen)
                        expr = ParseFunctionCall(name);
                    else
                        expr = Patcher.Instance.definitions[name];

                    return ParsePostfix(expr); // handle indexing
                }
            case TokenType.LParen:
                Eat(TokenType.LParen);
                expr = ParseExpression();
                Eat(TokenType.RParen);
                CoreUtils.Print($"[()] Grouped expression => {expr}");
                break;
            case TokenType.LBracket:
                {
                    Eat(TokenType.LBracket);

                    var elements = new List<IExpression<object>>();

                    if (current.Type != TokenType.RBracket)
                    {
                        elements.Add(ParseExpression());

                        while (current.Type == TokenType.Comma)
                        {
                            Eat(TokenType.Comma);
                            elements.Add(ParseExpression());
                        }
                    }

                    Eat(TokenType.RBracket);

                    expr = new ArrayExpression(elements);
                    break;
                }

            default:
                throw new Exception($"Unexpected token {current.Type} in primary expression");
        }
        return ParsePostfix(expr);
    }
    
    private IExpression<object> ParseFunctionCall(string funcName)
    {
        Eat(TokenType.LParen);

        var args = new List<IExpression<object>>();

        if (current.Type != TokenType.RParen)
        {
            args.Add(ParseExpression());

            while (current.Type == TokenType.Comma)
            {
                Eat(TokenType.Comma);
                args.Add(ParseExpression());
            }
        }

        Eat(TokenType.RParen);

        if (BoolFunctionExpression.functions.ContainsKey(funcName))
            return new ObjectExpression<bool>(new BoolFunctionExpression(funcName, args));
        if (ProcedureExpression.procedures.ContainsKey(funcName))
            return new ProcedureExpression(funcName, args);
        return new FunctionExpression<object>(funcName, args);
    }
    private IExpression<object> ParsePostfix(IExpression<object> expr)
    {
        while (true)
        {
            if (current.Type == TokenType.LBracket)
            {
                Eat(TokenType.LBracket);
                var indexExpr = ParseExpression();
                Eat(TokenType.RBracket);

                expr = new IndexExpression(expr, indexExpr);
            }
            else
            {
                break;
            }
        }

        return expr;
    }
    
    public Func<ModRecord, object> ParseValueExpression()
    {
        var expr = ParseExpression();
        CoreUtils.Print("AST: " + expr.ToString());
        if (current.Type != TokenType.End)
            throw new Exception($"Unexpected tokens after expression: {current.Type}");

        return expr.GetFunc();
    }
}