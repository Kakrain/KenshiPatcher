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
    public static class ExpressionUtils
    { 
        public static string ExpectString(IExpression<object> expression,ModRecord? r=null)
        {
            object o = expression.GetFunc()(r);
            if (o is string s)
                return s;
            throw new FormatException($"Expression is expected to be a string: {expression.ToString()}");
        }
        public static (List<string>,List<ModRecord>) ExpectGroupRecord(IExpression<object>expression, ModRecord? r = null)
        {
            object o = expression.GetFunc()(r);
            if (o is (List<string> names, List<ModRecord> records))
                return (names, records);
            throw new FormatException($"Expression is expected to be a group: {expression.ToString()}");

        }


    }

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
            { "==", (l, r) => l == null && r == null ? true : l == null || r == null ? false : l.Equals(r)  },
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
    public class ArrayExpression : IExpression<object>
    {
        private readonly List<IExpression<object>> elements;

        public ArrayExpression(List<IExpression<object>> elements)
        {
            this.elements = elements;
        }

        public Func<ModRecord?, object> GetFunc()
        {
            var funcs = elements.Select(e => e.GetFunc()).ToList();

            return record =>
            {
                if (funcs.Count == 0) return Array.Empty<object>();
                var values = funcs.Select(f => f(record)).ToList();
                Type elementType = values[0]?.GetType() ?? typeof(object);
                foreach (var val in values)
                {
                    if (val == null) continue; // optional: treat null as compatible
                    if (val.GetType() != elementType)
                        throw new FormatException($"ArrayExpression elements have mixed types: {elementType} vs {val.GetType()}");
                }
                Array array = Array.CreateInstance(elementType, values.Count);
                for (int i = 0; i < values.Count; i++)
                    array.SetValue(values[i], i);

                return array;
            };
        }

        public override string ToString()
        {
            return $"Array[{string.Join(", ", elements)}]";
        }
    }
    public class BoolFunctionExpression : IExpression<bool>
    {
        private readonly Func<ModRecord, bool> func;

        public static readonly Dictionary<string, Func<ModRecord, List<IExpression<object>>, bool>> functions =
            new()
            {
            { "true", (_, __) => true },
            { "false", (_, __) => false },
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
                var definition = args[0].GetFunc()(r);
                string? category = args.Count > 1 ? args[1].GetFunc()(r)?.ToString() : null;
                int[]? variables = args.Count > 2 ? ConvertArray<int>(args[2].GetFunc()(r)) : null;
                if (definition is ValueTuple<List<string>, List<ModRecord>> group)
                    return group.Item2.Any(rec => rec.isExtraDataOfThis(r, category,variables));
                throw new Exception($"Definition '{definition}' malformed");
            }},
            { "hasAnyAsExtraData", (r, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("hasAnyAsExtraData expects at least one argument");
                var definition = args[0].GetFunc()(r);
                string? category = args.Count > 1 ? args[1].GetFunc()(r)?.ToString() : null;
                int[]? variables = args.Count > 2 ? ConvertArray<int>(args[2].GetFunc()(r)) : null;
                if (definition is ValueTuple<List<string>, List<ModRecord>> group)
                    return group.Item2.Any(rec => rec.hasThisAsExtraData(r, category, variables));
                throw new Exception($"Definition '{definition}' malformed");
            }},
            { "allExtraDataIsWithin", (r, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("allExtraDataIsWithin expects at least one argument");
                var definition = args[0].GetFunc()(r);
                string? category = args.Count > 1 ? args[1].GetFunc()(r)?.ToString() : null;
                int[]? variables = args.Count > 2 ? ConvertArray<int>(args[2].GetFunc()(r)) : null;
                if (definition is ValueTuple<List<string>, List<ModRecord>> group)
                    return group.Item2.All(rec => rec.hasThisAsExtraData(r, category, variables));
                throw new Exception($"Definition '{definition}' malformed");
            }}
            };
        private readonly List<IExpression<object>> arguments;
        public static T[] ConvertArray<T>(object? value)
        {
            if (value is not Array arr)
                throw new Exception($"Expected array, got {value?.GetType().Name ?? "null"}");
            return arr.Cast<object>().Select(o => (T)Convert.ChangeType(o, typeof(T))).ToArray();
        }
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
                var targetVal = targetFunc(record);
                var indexVal = indexFunc(record);

                if (targetVal is IList<object> list)
                {
                    int idx = Convert.ToInt32(indexVal);
                    if (idx < 0 || idx >= list.Count)
                        throw new Exception($"Array index {idx} out of range");
                    return list[idx];
                }

                // existing table-handling logic below...
                var targetStr = targetVal?.ToString();
                var indexStr = indexVal?.ToString();

                if (targetStr == null || indexStr == null)
                    throw new Exception("IndexExpression: null table name or key");

                if (!Patcher.Instance.tables.TryGetValue(targetStr, out var table))
                    throw new Exception($"Table '{targetStr}' not found");

                if (!table.TryGetValue(indexStr, out var value))
                    throw new Exception($"Key '{indexStr}' not found in table '{targetStr}'");

                if (value is IExpression<object> expr)
                    return expr.GetFunc()(record);

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
        public override string ToString()
        {
            StringBuilder sb = new();
            var (names, records) = group;
            for (int i = 0; i < names.Count; i++)
            {
                sb.AppendLine($"{names[i]} => {records[i].ToString()}");
            }
            return sb.ToString();
        }
    }
        public class PipeExpression : IExpression<object>
        {
            private readonly RecordGroupExpression left;    // left is explicitly a RecordGroupExpression
            private readonly ProcedureExpression right;     // right is explicitly a ProcedureExpression

            public PipeExpression(RecordGroupExpression left, ProcedureExpression right)
            {
                this.left = left ?? throw new ArgumentNullException(nameof(left));
                this.right = right ?? throw new ArgumentNullException(nameof(right));
            }

            public Func<ModRecord?, object> GetFunc()
            {
                var leftFunc = left.GetFunc();

                return record =>
                {
                    var leftValue = leftFunc(record);

                    if (leftValue is not (List<string> names, List<ModRecord> records))
                        throw new Exception("PipeExpression: left side must evaluate to a record group (List<string>, List<ModRecord>)");

                    right.SetTarget((names, records));
                    var procFunc = right.GetFunc();
                    procFunc(record);
                    return leftValue;
                };
            }
            public override string ToString()
            {
                return $"PipeExpression({left} -> {right})";
            }
        }
    
    public class ProcedureExpression : IExpression<object>
    {
        private readonly string procedureName;
        private readonly List<IExpression<object>> arguments;
        private readonly Func<(List<string>, List<ModRecord>), object> func;
        private (List<string>, List<ModRecord>)? target;

        public static readonly Dictionary<string, Func<(List<string>, List<ModRecord>), List<IExpression<object>>, object?>> procedures =
            new()
            {
            { "SetField", (targetGroup, args) =>
                {

                    foreach (var record in targetGroup.Item2)
                    {
                        if(!(args[0].GetFunc()(record) is string strfieldname))
                            throw new FormatException($"Invalid field name: ({args[0].ToString()})");
                        string value=args[1].GetFunc()(record).ToString()!;
                        Patcher.Instance.currentRE!.SetField(record,strfieldname,value);
                    }
                    return null;
                }
            },
            { "ForceSetField", (targetGroup, args) =>
                {
                    foreach (var record in targetGroup.Item2)
                    {
                        if(!(args[0].GetFunc()(record) is string strfieldname))
                            throw new FormatException($"Invalid field name: ({args[0].ToString()})");
                        string value=args[1].GetFunc()(record).ToString()!;
                        if(!(args[2].GetFunc()(null) is string strtype))
                            throw new FormatException($"Invalid field name: ({args[2].ToString()})");
                        Patcher.Instance.currentRE!.ForceSetField(record,strfieldname,value,strtype);
                    }
                    return null;
                }
            },
            { "AddExtraData", (targetGroup, args) =>
                {
                    if(!(args[0].GetFunc()(null) is (List<string> modnames, List<ModRecord> sources)))
                        throw new FormatException($"Invalid definition name: ({args[0].ToString()})");
                    if(!(args[1].GetFunc()(null) is string category))
                        throw new FormatException($"Invalid category: ({args[1].ToString()})");
                    Func<ModRecord?, object>? func_array=null;
                    if (args.Count>2)
                        func_array=args[2].GetFunc();
                    foreach (var record in targetGroup.Item2)
                    {
                        int[]? arrayvar = null;
                        if (func_array != null)
                        {
                            var result = func_array(record);
                            if (result is int[] arr)
                                arrayvar = arr;
                            else
                                throw new FormatException($"Invalid array returned for category: {result}");
                        }
                        foreach (var source in sources)
                        {
                            Patcher.Instance.currentRE!.AddExtraData(record,source,category,func_array==null?null:arrayvar);
                        }
                    }
                    Patcher.Instance.currentRE!.addReferences(modnames.Distinct(StringComparer.Ordinal).ToList());
                    return null;
                }
            }
            };

        public ProcedureExpression(string name, List<IExpression<object>> args)
        {
            procedureName = name;
            arguments = args;
            if (!procedures.TryGetValue(name, out var f))
                throw new Exception($"Unknown procedure {name}");
            func = tg =>
            {
                var result = f(tg, arguments);
                Patcher.Instance.currentRE!.addDependencies(tg.Item1.Distinct(StringComparer.Ordinal).ToList());
                return result!;
            };
        }

        public void SetTarget((List<string>, List<ModRecord>) targetGroup)
        {
            target = targetGroup;
        }

        public Func<ModRecord?, object> GetFunc()
        {
            if (target == null)
                throw new Exception("ProcedureExpression has no target");

            return _ => func(target.Value);
        }
    }
    public class GlobalFunctionExpression : IExpression<object>
    {
        private readonly string name;
        private readonly List<IExpression<object>> args;

        public static readonly Dictionary<string, Action<List<IExpression<object>>>> globalFuncs = new()
        {
            { "Print", args =>
                {
                    CoreUtils.Print(getStringFromArgs(args),1);
                }
            },
            { "Debug", args =>
                {
                    CoreUtils.Print(getStringFromArgs(args),0);
                }
            },
            { "InspectRecord", args =>
                {
                    
                    var (names,records) =ExpressionUtils.ExpectGroupRecord(args[0]);
                    string stringid=ExpressionUtils.ExpectString(args[1]);
                    ModRecord? found=records.Find(rec=>rec.StringId==stringid);
                    if(found == null)
                        throw new Exception($"mod with stringId '{stringid}' not found");
                    CoreUtils.Print(found.getDataAsString(),0);
                }
            },
            { "ShowRecordEvolution", args =>
                {
                    string stringid=ExpressionUtils.ExpectString(args[0]);
                    CoreUtils.Print(Patcher.Instance.GetModRecordEvolution(stringid),0);
                }
            },
            { "Stop",args=>
                {
                    Patcher.Instance.Stop();
                }
            }
        };
        private static string getStringFromArgs(List<IExpression<object>> args)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var arg in args) {
                sb.Append(arg.ToString()+"\n");
            }
            return sb.ToString();
        }

        public GlobalFunctionExpression(string name, List<IExpression<object>> args)
        {
            this.name = name;
            this.args = args;
        }

        public Func<ModRecord?, object> GetFunc()
        {
            if (!globalFuncs.TryGetValue(name, out var func))
                throw new Exception($"Unknown global function '{name}'");

            return _ =>
            {
                func(args);
                return true;
            };
        }

        public override string ToString() => $"@{name}({string.Join(", ", args)})";
    }

}

    

  
