using KenshiCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace KenshiPatcher.ExpressionReader
{
    public interface IExpression<T>
    {
        Func<ModRecord?, T> GetFunc();
    }
    public class Literal<T> : IExpression<T>
    {
        private readonly T value;
        public Literal(T value) => this.value = value;

        public Func<ModRecord?, T> GetFunc() => _ => value;
        public override string ToString()
        {
            return $"Literal<{ value?.ToString() ?? "null"}>";
        }
    }
    class NotExpression : IExpression<bool>
    {
        private readonly IExpression<bool> inner;
        public NotExpression(IExpression<bool> inner) => this.inner = inner;

        public Func<ModRecord?, bool> GetFunc()
        {
            var innerFunc = inner.GetFunc();
            return r => !innerFunc(r);
        }
    }
    class ObjectExpression<T> : IExpression<object>
    {
        private readonly IExpression<T> inner;
        public ObjectExpression(IExpression<T> inner) => this.inner = inner;

        public Func<ModRecord?, object> GetFunc()
        {
            var func = inner.GetFunc();
            return r => (object)func(r)!;
        }
        public override string ToString()
        {
            return this.inner.ToString()!;
        }
    }

    public class BinaryExpression : IExpression<object>
    {
        private readonly IExpression<object> left;
        private readonly IExpression<object> right;
        private readonly string op;
        public static readonly Dictionary<string, Func<object, object, object>> Operators = new()
        {
            { "+", (l, r) => (IsDoubleType(l)||IsDoubleType(r))?AsDouble(l)+AsDouble(r):AsInt64(l)+AsInt64(r)},
            { "-", (l, r) => (IsDoubleType(l)||IsDoubleType(r))?AsDouble(l)-AsDouble(r):AsInt64(l)-AsInt64(r)},
            { "*", (l, r) => (IsDoubleType(l)||IsDoubleType(r))?AsDouble(l)*AsDouble(r):AsInt64(l)*AsInt64(r)},
            { "/", (l, r) => (IsIntegerType(l)&&IsIntegerType(r)&&((AsInt64(l)%AsInt64(r)) == 0))?AsInt64(l)/AsInt64(r):AsDouble(l)/AsDouble(r)},
            { "&&", (l, r) => Convert.ToBoolean(l)&&Convert.ToBoolean(r) },
            { "||", (l, r) => Convert.ToBoolean(l)||Convert.ToBoolean(r) },
            { ">", (l, r) => Convert.ToDouble(l)>Convert.ToDouble(r) },
            { "<", (l, r) => Convert.ToDouble(l)<Convert.ToDouble(r) },
            { ">=", (l, r) => Convert.ToDouble(l)>=Convert.ToDouble(r) },
            { "<=", (l, r) => Convert.ToDouble(l)<=Convert.ToDouble(r) },
            { "==", (l, r) => l.Equals(r) },
            { "!=", (l, r) => !(bool)Operators!["=="](l, r) }

        };
        private static bool IsIntegerType(object value)
        {
            if (value is sbyte or byte or short or ushort or int or uint or long or ulong)
                return true;

            if (value is string s)
            {
                // Try parsing as integer (invariant culture, no decimals allowed)
                return long.TryParse(
                    s,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out _);
            }

            return false;
        }
        private static bool IsDoubleType(object value)
        {
            if (value is float or double)
                return true;

            if (value is string s)
            {
                return double.TryParse(
                    s,
                    System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out _);
            }

            return false;
        }
        public static double AsDouble(object value)
        {
            if (value is double d) return d;
            if (value is float f) return f;
            if (value is int i) return i;
            if (value is long l) return l;
            if (value is string s)
            {
                string ds = s.Replace(",", ".");
                if (double.TryParse(ds, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }
            throw new Exception($"Cannot convert '{value}' to double");
        }

        private static long AsInt64(object value)
        {
            if (value is long l) return l;
            if (value is int i) return i;
            if (value is string s && long.TryParse(
                s, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            // If it’s a double but whole (e.g., 4.0) we can still treat it as int
            if (value is double d && Math.Abs(d % 1) < double.Epsilon)
                return (long)d;

            throw new Exception($"Cannot convert '{value}' to integer");
        }
        public Func<ModRecord?, object> GetFunc()
        {
            var lf = left.GetFunc();
            var rf = right.GetFunc();
            return r =>
            {
                var lval = lf(r);
                var rval = rf(r);
                return Operators[op](lval, rval);
            };
        }
        public override string ToString()
        {
            return $"BinaryExpression<{left.ToString()} {op} {right.ToString()}>";
        }
        public BinaryExpression(IExpression<object> left, IExpression<object> right,string sop)
        {
            this.left = left;
            this.right = right;
            this.op = sop;
        }
    }
    public class UnaryExpression : IExpression<object>
    {
        private readonly IExpression<object> inner;
        //private readonly Func<object, object> op;
        private readonly string op;
        public static readonly Dictionary<string, Func<object, object>> UnaryOperators = new()
    {
        { "-", (x) => (x is double d) ? -d : (x is int i) ? -i : (x is long l) ? -l : -Convert.ToDouble(x) },
        { "!", (x) => !Convert.ToBoolean(x) }
    };

        public Func<ModRecord?, object> GetFunc()
        {
            var innerFunc = inner.GetFunc();

            CoreUtils.Print($"Creating unary expression {this.ToString()}");
            return r => UnaryOperators[op](innerFunc(r));
        }
        public override string ToString()
        {
            return $"({op} {inner.ToString()})";
        }
        public UnaryExpression(IExpression<object> inner, string sop)
        {
            this.inner = inner;
            if (!UnaryOperators.TryGetValue(sop, out var  v))
                throw new Exception($"Unknown unary operator '{sop}'");
            this.op = sop;
        }
    }
    public class UnaryMinusExpressionDouble : IExpression<double>
    {
        private readonly IExpression<double> inner;
        public UnaryMinusExpressionDouble(IExpression<double> inner) { this.inner = inner; }
        public Func<ModRecord?, double> GetFunc() { var innerFunc = inner.GetFunc(); return r => -innerFunc(r); }
    }
    public class UnaryMinusExpressionInt : IExpression<int>
    {
        private readonly IExpression<int> inner;
        public UnaryMinusExpressionInt(IExpression<int> inner) { this.inner = inner; }
        public Func<ModRecord?, int> GetFunc() { var innerFunc = inner.GetFunc(); return r => -innerFunc(r); }
    }
    public class ArithmeticExpression : IExpression<object>
    {
        private readonly IExpression<object> left;
        private readonly IExpression<object> right;
        private readonly Func<object, object, object> opFunc;

        public static readonly Dictionary<string, Func<object, object, object>> Operators = new()
    {
        { "+", (l, r) => Promote(l, r, (a, b) => a + b) },
        { "-", (l, r) => Promote(l, r, (a, b) => a - b) },
        { "*", (l, r) => Promote(l, r, (a, b) => a * b) },
        { "/", (l, r) => Promote(l, r, (a, b) => a / b) }
    };

        public ArithmeticExpression(IExpression<object> left, IExpression<object> right, string op)
        {
            this.left = left;
            this.right = right;

            if (!Operators.TryGetValue(op, out var f))
                throw new Exception($"Unknown arithmetic operator '{op}'");
            opFunc = f;
        }

        public Func<ModRecord?, object> GetFunc()
        {
            var lf = left.GetFunc();
            var rf = right.GetFunc();
            return r => opFunc(lf(r), rf(r));
        }

        private static object Promote(object l, object r, Func<double, double, double> f)
        {
            double ld = Convert.ToDouble(l);
            double rd = Convert.ToDouble(r);
            return f(ld, rd);
        }
    }
    public class ComparisonExpression : IExpression<bool>
    {
        private readonly IExpression<object> left;
        private readonly IExpression<object> right;
        private Func<object, object, bool> comparer;
        public static readonly Dictionary<string, Func<object, object, bool>> ComparisonOperators = new()
        {
            { "==", (l, r) => Equals(l, r) },
            { "!=", (l, r) => !Equals(l, r) },
            { ">",  (l, r) => Convert.ToDouble(l) > Convert.ToDouble(r) },
            { "<",  (l, r) => Convert.ToDouble(l) < Convert.ToDouble(r) },
            { ">=", (l, r) => Convert.ToDouble(l) >= Convert.ToDouble(r) },
            { "<=", (l, r) => Convert.ToDouble(l) <= Convert.ToDouble(r) }
        };
        public ComparisonExpression(IExpression<object> left, IExpression<object> right, string op)
        {
            this.left = left;
            this.right = right;
            if (!ComparisonOperators.TryGetValue(op, out var cmp))
                throw new Exception($"Unknown comparison operator '{op}'");
            this.comparer = ComparisonOperators[op];
        }
        public Func<ModRecord?, bool> GetFunc()
        {
            var lf = left.GetFunc();
            var rf = right.GetFunc();
            return r =>
            {
                var lval = lf(r);
                var rval = rf(r);
                if (lval == null || rval == null) return false;
                return comparer(lval, rval);
            };
        }
    }
    public class FunctionExpression<T> : IExpression<T>
    {
        private readonly List<IExpression<object>> arguments;
        private readonly string functionname;
        private readonly Func<ModRecord, T> func;

        public static readonly Dictionary<string, Func<ModRecord, List<IExpression<object>>, T>> functions =
            new()
            {
                { "GetField", (r, args) =>
                    {
                        if (args.Count != 1)
                            throw new Exception("GetField() expects exactly one argument");

                        var argFunc = args[0].GetFunc();
                        var fieldName = argFunc(r)?.ToString();
                        if (fieldName == null)
                            throw new Exception("GetField(): argument evaluated to null");

                        return (T)Convert.ChangeType(r.GetFieldAsObject(fieldName), typeof(T))!;
                    }
                }
            };

        public FunctionExpression(string funcName, List<IExpression<object>> args)
        {
            functionname = funcName;
            arguments = args;

            if (!functions.TryGetValue(funcName, out var f))
                throw new Exception($"Unknown function {funcName}");

            func = r => f(r, arguments);
        }
        public override string ToString() => $"FunctionExpression<{functionname}>";

        public Func<ModRecord?, T> GetFunc() => func!;

    }
        public class TernaryExpression : IExpression<object>
    {
        private readonly IExpression<bool> condition;
        private readonly IExpression<object> trueExpr;
        private readonly IExpression<object> falseExpr;

        public TernaryExpression(IExpression<bool> condition, IExpression<object> trueExpr, IExpression<object> falseExpr)
        {
            this.condition = condition;
            this.trueExpr = trueExpr;
            this.falseExpr = falseExpr;
        }

        public Func<ModRecord?, object> GetFunc()
        {
            var condFunc = condition.GetFunc();
            var trueFunc = trueExpr.GetFunc();
            var falseFunc = falseExpr.GetFunc();

            return record => condFunc(record) ? trueFunc(record) : falseFunc(record);
        }
    }
    public class VariableExpression : IExpression<object>
    {
        public string Name { get; }
        public VariableExpression(string name) => Name = name;

        public Func<ModRecord?, object> GetFunc()
        {
            return r => Name;
        }
        public override string ToString() => $"VariableExpression<{Name}>";
    }
    public class BoolFunctionExpression : IExpression<bool>
    {
        private readonly Func<ModRecord, bool> func;

        public static readonly Dictionary<string, Func<ModRecord, List<IExpression<object>>, bool>> functions =
            new()
            {
            { "true", (_, __) => true },
            { "false", (_, __) => false },

            // FieldExist(fieldName)
            { "FieldExist", (r, args) =>
                {
                    if (args.Count != 1)
                        throw new Exception("FieldExist expects exactly one argument");
                    string? field = args[0].GetFunc()(r)?.ToString();
                    return !string.IsNullOrEmpty(field) && r.HasField(field);
                }
            },
            { "FieldIsNotEmpty", (r, args) =>
                {
                    if (args.Count != 1)
                        throw new Exception("FieldIsNotEmpty expects exactly one argument");
                    string? field = args[0].GetFunc()(r)?.ToString();
                    return !string.IsNullOrEmpty(field) && !string.IsNullOrEmpty(r.GetFieldAsString(field));
                }
            },
            { "isExtraDataOfAny", (r, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("isExtraDataOfAny expects at least one argument");
                var varNameObj = args[0].GetFunc()(r);
                if (varNameObj == null)
                    throw new Exception("Missing variable name");
                string varName = varNameObj.ToString()!;
                string? category = args.Count > 1 ? args[1].GetFunc()(r)?.ToString() : null;
                if (!Patcher.Instance.definitions.TryGetValue(varName, out var def))
                    throw new Exception($"Definition '{varName}' not found");
                var definition=def.GetFunc()(null);
                if (definition is ValueTuple<List<string>, List<ModRecord>> group)
                    return group.Item2.Any(rec => rec.isExtraDataOfThis(r, category));
                throw new Exception($"Definition '{varName}' not found");
            }},
            { "hasAnyAsExtraData", (r, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("hasAnyAsExtraData expects at least one argument");
                var varNameObj = args[0].GetFunc()(r);
                if (varNameObj == null)
                    throw new Exception("Missing variable name");
                string varName = varNameObj.ToString()!;
                string? category = args.Count > 1 ? args[1].GetFunc()(r)?.ToString() : null;
                if (!Patcher.Instance.definitions.TryGetValue(varName, out var def))
                    throw new Exception($"Definition '{varName}' not found");
                var definition=def.GetFunc()(null);
                if (definition is ValueTuple<List<string>, List<ModRecord>> group)
                    return group.Item2.Any(rec => rec.hasThisAsExtraData(r, category));
                throw new Exception($"Definition '{varName}' not found");
            }}
            };
        private readonly List<IExpression<object>> arguments;

        public BoolFunctionExpression(string funcName, List<IExpression<object>> args)
        {
            arguments = args;
            if (!functions.TryGetValue(funcName, out var f))
                throw new Exception($"Unknown boolean function '{funcName}'");

            func = r => f(r, arguments);
        }

        public Func<ModRecord?, bool> GetFunc() => func!;
    }
    public class IndexExpression : IExpression<object>
    {
        private readonly IExpression<object> target;
        private readonly IExpression<object> index;

        public IndexExpression(IExpression<object> target, IExpression<object> index)
        {
            this.target = target;
            this.index = index;
        }
        public class TableNameExpression : IExpression<object>
        {
            public string Name { get; }
            public TableNameExpression(string name) => Name = name;

            public Func<ModRecord?, object> GetFunc()
            {
                return _ => Name; // just returns the table name
            }

            public override string ToString() => $"Table<{Name}>";
        }

        public Func<ModRecord?, object> GetFunc()
        {
            var targetFunc = target.GetFunc();
            var indexFunc = index.GetFunc();

            return record =>
            {
                var targetVal = targetFunc(record)?.ToString();
                var indexVal = indexFunc(record)?.ToString();

                if (targetVal == null)
                    throw new Exception("IndexExpression: table name evaluated to null.");

                if (indexVal == null)
                    throw new Exception("IndexExpression: index evaluated to null.");
                if (!Patcher.Instance.tables.TryGetValue(targetVal, out var table))
                    throw new Exception($"IndexExpression: Table '{targetVal}' not found in Patcher.Instance.tables.");
                if (!table.TryGetValue(indexVal, out var value))
                    throw new Exception($"IndexExpression: Key '{indexVal}' not found in table '{targetVal}'.");
                if (value is IExpression<object> expr)
                {
                    var func = expr.GetFunc();
                    var result = func(record);
                    return result;
                }

                // fallback — if table stored raw data somehow
                return value;
            };
        }

        public override string ToString()
        {
            return $"IndexExpression({target}[{index}])";
        }
    }
    public class RecordGroupExpression : IExpression<object>
    {
        private (List<string>, List<ModRecord>) group;

        public RecordGroupExpression((List<string>, List<ModRecord>) group)
        {
            this.group = group;
        }

        public Func<ModRecord?, object> GetFunc()
        {
            return _ => group;
        }
    }
}

    

  
