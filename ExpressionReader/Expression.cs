using KenshiCore;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Text;
using System.Xml.Linq;

namespace KenshiPatcher.ExpressionReader
{

    public static class ExpressionUtils
    { 
        public static string ExpectString(Expression<object> expression,ModRecord? r=null)
        {
            object o = expression.Evaluate(r)!;
            if (o is string s)
                return s;
            throw new FormatException($"Expression is expected to be a string: {expression.ToString()}");
        }
        public static (List<string>,List<ModRecord>) ExpectGroupRecord(Expression<object>expression, ModRecord? r = null)
        {
            object o = expression.Evaluate(r)!;
            if (o is (List<string> names, List<ModRecord> records))
                return (names, records);
            throw new FormatException($"Expression is expected to be a group: {expression.ToString()}");
        }
        public static Array ExpectArray(Expression<object> expression, ModRecord? r = null)
        {
            object arrObj = expression.Evaluate(r)!;
            if (arrObj is Array arr)
                return arr;  
            throw new FormatException("ExpectArray: second argument must be an array");
        }
        public static Func<object?[], object?> ExpectLambda(Expression<object> expression, ModRecord? r = null)
        {
            object arrObj = expression.Evaluate(r)!;
            if (arrObj is Func<object?[], object?> lambdaArray)
                return lambdaArray;
            throw new FormatException("ExpectArray: second argument must be a lambda");
        }
        public static int ExpectInt(Expression<object> expression, ModRecord? r = null)
        {
            object o = expression.Evaluate(r)!;
            try
            {
                return (int)ValueCaster.ToInt64(o);
            }
            catch (Exception ex)
            {
                throw new FormatException($"Expression is expected to be an int: {expression} (value='{o}', type='{o?.GetType().Name ?? "null"}')",ex);
            }
        }
    }
    [DebuggerDisplay("{ToString()}")]
    public abstract class Expression
    {
        public abstract object? Evaluate(ModRecord? r);
    }
    [DebuggerDisplay("{ToString()}")]
    public abstract class Expression<T> : Expression
    {
        public abstract T EvaluateTyped(ModRecord? r);

        public override object? Evaluate(ModRecord? r)
            => EvaluateTyped(r);
    }

    [DebuggerDisplay("{ToString()}")]
    public sealed class Literal<T> : Expression<T>
    {
        private readonly T value;

        public Literal(T value) => this.value = value;

        public override T EvaluateTyped(ModRecord? r) => value;

        public override string ToString()
        {
            return $"Literal<{value?.ToString() ?? "null"}>";
        }

    }
    [DebuggerDisplay("{ToString()}")]
    class ObjectExpression<T> : Expression<object?>
    {
        private readonly Expression<T> inner;

        public ObjectExpression(Expression<T> inner) => this.inner = inner;

        public override object? EvaluateTyped(ModRecord? r)
        => inner.EvaluateTyped(r);

        public override string ToString()
        {
            return this.inner.ToString()!;
        }
    }

    [DebuggerDisplay("{ToString()}")]
    public sealed class BinaryExpression : Expression<object>
    {
        public readonly Expression<object> left;
        public readonly Expression<object> right;
        public readonly string op;
        public static readonly Dictionary<string, Func<object, object, object>> Operators = new()
        {
            { "+", (l, r) =>
            {
                // string concatenation
                if (l is string || r is string)
                    return $"{l}{r}";

                // numeric addition
                if (ValueCaster.IsFloatingLike(l) || ValueCaster.IsFloatingLike(r))
                    return ValueCaster.ToDouble(l) + ValueCaster.ToDouble(r);

                return ValueCaster.ToInt64(l) + ValueCaster.ToInt64(r);
            }},
            //{ "+", (l, r) => (ValueCaster.IsFloatingLike(l) || ValueCaster.IsFloatingLike(r)) ? ValueCaster.ToDouble(l) + ValueCaster.ToDouble(r) : ValueCaster.ToInt64(l) + ValueCaster.ToInt64(r) },
            { "-", (l, r) => (ValueCaster.IsFloatingLike(l) || ValueCaster.IsFloatingLike(r)) ? ValueCaster.ToDouble(l) - ValueCaster.ToDouble(r) : ValueCaster.ToInt64(l) - ValueCaster.ToInt64(r) },
            { "*", (l, r) => (ValueCaster.IsFloatingLike(l) || ValueCaster.IsFloatingLike(r)) ? ValueCaster.ToDouble(l) * ValueCaster.ToDouble(r) : ValueCaster.ToInt64(l) * ValueCaster.ToInt64(r) },
            { "/", (l, r) =>
                    {
                        // if both integer-like and divisible -> integer division; otherwise floating division
                        if (ValueCaster.IsIntegerLike(l) && ValueCaster.IsIntegerLike(r))
                        {
                            var il = ValueCaster.ToInt64(l);
                            var ir = ValueCaster.ToInt64(r);
                            if (ir == 0) throw new DivideByZeroException();
                            if (il % ir == 0) return il / ir;
                            return (double)il / (double)ir;
                        }
                        return ValueCaster.ToDouble(l) / ValueCaster.ToDouble(r);
                    }
            },
            { "&&", (l, r) => Convert.ToBoolean(l) && Convert.ToBoolean(r) },
            { "||", (l, r) => Convert.ToBoolean(l) || Convert.ToBoolean(r) },
            { ">", (l, r) => ValueCaster.ToDouble(l) > ValueCaster.ToDouble(r) },
            { "<", (l, r) => ValueCaster.ToDouble(l) < ValueCaster.ToDouble(r) },
            { ">=", (l, r) => ValueCaster.ToDouble(l) >= ValueCaster.ToDouble(r) },
            { "<=", (l, r) => ValueCaster.ToDouble(l) <= ValueCaster.ToDouble(r) },
            { "==", (l, r) =>
                {
                    if (l == null && r == null) return true;
                    if (l == null || r == null) return false;

                    bool lIsInt = ValueCaster.IsIntegerLike(l);
                    bool rIsInt = ValueCaster.IsIntegerLike(r);
                    bool lIsFloat = ValueCaster.IsFloatingLike(l);
                    bool rIsFloat = ValueCaster.IsFloatingLike(r);

                    if (lIsInt && rIsInt)
                        return ValueCaster.ToInt64(l) == ValueCaster.ToInt64(r);

                    if ((lIsInt || lIsFloat) && (rIsInt || rIsFloat))
                        return Math.Abs(ValueCaster.ToDouble(l) - ValueCaster.ToDouble(r)) < double.Epsilon;

                    return l.Equals(r);
                }
            },
            { "!=", (l, r) => !(bool)Operators!["=="](l, r) },
            { "%", (l, r) =>
                (ValueCaster.IsIntegerLike(l) && ValueCaster.IsIntegerLike(r))
                    ? ValueCaster.ToInt64(l) % ValueCaster.ToInt64(r)
                    : (long)(ValueCaster.ToDouble(l) % ValueCaster.ToDouble(r))
            }
        };
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
        public override object EvaluateTyped(ModRecord? r) => Operators[op](left.Evaluate(r)!, right.Evaluate(r)!);
        public override string ToString()
        {
            return $"BinaryExpression<{left.ToString()} {op} {right.ToString()}>";
        }
        public BinaryExpression(Expression<object> left, Expression<object> right,string sop)
        {
            this.left = left;
            this.right = right;
            this.op = sop;
        }
    }
    [DebuggerDisplay("{ToString()}")]
    public sealed class UnaryExpression : Expression<object>
    {
        public readonly Expression<object> inner;
        public readonly string op;
        public static readonly Dictionary<string, Func<object, object>> UnaryOperators = new()
    {
        { "-", (x) => (x is double d) ? -d : (x is int i) ? -i : (x is long l) ? -l : -Convert.ToDouble(x) },
        { "!", (x) => !Convert.ToBoolean(x) }
    };
        public override object EvaluateTyped(ModRecord? r) => UnaryOperators[op](inner.Evaluate(r)!);

        public override string ToString()
        {
            return $"({op} {inner.ToString()})";
        }

        public UnaryExpression(Expression<object> inner, string sop)
        {
            this.inner = inner;
            if (!UnaryOperators.TryGetValue(sop, out var  v))
                throw new Exception($"Unknown unary operator '{sop}'");
            this.op = sop;
        }
    }
    [DebuggerDisplay("{ToString()}")]
    public class LiteralExpression : Expression<object>
    {
        private readonly object? value;

        public LiteralExpression(object? val)
        {
            value = val;
        }
        public override object EvaluateTyped(ModRecord? r) => value!;


        public override string ToString() => value?.ToString() ?? "null";
    }
    [DebuggerDisplay("{ToString()}")]
    public class FunctionExpression<T> : Expression<T>
    {
        public readonly List<Expression<object>> arguments;
        private readonly string functionname;
        private readonly Func<ModRecord, T> func;
        private static readonly Random getrandom = new Random();

        public static readonly Dictionary<string, Func<ModRecord, List<Expression<object>>, T>> functions =
            new()
            {
                { "GetField", (r, args) =>
                    {
                        if (args.Count != 1)
                            throw new Exception("GetField() expects exactly one argument");
                        var fieldName = args[0].Evaluate(r)!.ToString();
                        return (T)Convert.ChangeType(r.GetFieldAsObject(fieldName!), typeof(T))!;
                    }
                },
                { "ToInt", (r, args) =>
                    {
                        if (args.Count != 1) throw new Exception("ToInt expects exactly one argument");
                        var val = args[0].Evaluate(r);

                        try
                        {
                            var intVal = ValueCaster.ToInt64(val!);
                            return (T)Convert.ChangeType(intVal, typeof(T))!;
                        }
                        catch (Exception ex)
                        {
                            throw new Exception($"ToInt: cannot convert value of type '{val?.GetType().FullName ?? "null"}' (ToString()='{val}') to int: {ex.Message}", ex);
                        }
                    }
                },
                { "ToFloat", (r, args) =>
                    {
                        if (args.Count != 1)
                            throw new Exception("ToFloat expects exactly one argument");
                        var val = args[0].Evaluate(r);
                        try
                        {
                            var floatVal = ValueCaster.ToDouble(val!);
                            return (T)Convert.ChangeType((float)floatVal, typeof(T))!;
                        }
                        catch (Exception ex)
                        {
                            throw new Exception(
                                $"ToFloat: cannot convert value of type '{val?.GetType().FullName ?? "null"}' (ToString()='{val}') to float: {ex.Message}",
                                ex
                            );
                        }
                    }
                },
                { "Min", (r, args) =>
                    {
                        Array arr =ExpressionUtils.ExpectArray(args[0],r);
                        double min = Convert.ToDouble(arr.GetValue(0)!);
                        for (int i = 1; i < arr.Length; i++)
                            min = Math.Min(min, Convert.ToDouble(arr.GetValue(i)!));
                        object result = min % 1 == 0 ? (int)min : min;
                        return (T)Convert.ChangeType(result, typeof(T))!;
                    }
                },
                { "Max", (r, args) =>
                    {
                        Array arr =ExpressionUtils.ExpectArray(args[0],r);
                        double max = Convert.ToDouble(arr.GetValue(0)!);
                        for (int i = 1; i < arr.Length; i++)
                            max = Math.Max(max, Convert.ToDouble(arr.GetValue(i)!));
                        object result = max % 1 == 0 ? (int)max : max;
                        return (T)Convert.ChangeType(result, typeof(T))!;
                    }
                },
                { "ArrIndex", (r, args) =>
                    {
                        var arrObj = args[0].Evaluate(r);
                        var indexObj = args[1].Evaluate(r);
                        int index = Convert.ToInt32(indexObj);

                        switch (arrObj)
                        {
                            case int[] arr:
                                return (T)Convert.ChangeType(arr[index], typeof(T));

                            case object[] objArr:
                                // try unboxing to int
                                if (objArr[index] is int i) return (T)Convert.ChangeType( i, typeof(T));
                                if (objArr[index] is long l) return (T)Convert.ChangeType( (int)l, typeof(T));
                                if (objArr[index] is double d && Math.Abs(d % 1) < double.Epsilon)
                                    return (T)Convert.ChangeType((int)d, typeof(T));
                                throw new Exception($"ArrIndex: element at {index} is not an int: {objArr[index]}");

                            case List<int> list:
                                return (T)Convert.ChangeType( list[index], typeof(T));

                            default:
                                throw new Exception("ArrIndex: unsupported array type: " + arrObj?.GetType());
                        }
                    }
                },
                { "RandomFloat", (r, args) =>
                    {
                        return (T)Convert.ChangeType(getrandom.NextDouble(), typeof(T))!;
                    }
                },
                { "RandomBetweenInts", (r, args) =>
                    {
                        int min = (int)Convert.ToInt64(args[0].Evaluate(r));
                        int max = (int)Convert.ToInt64(args[1].Evaluate(r));
                        return (T)Convert.ChangeType(getrandom.Next(min,max), typeof(T))!;
                    }
                },
                { "ContainsCS", (r, args) =>
                    {
                        string s0=ExpressionUtils.ExpectString(args[0],r);
                        string s1=ExpressionUtils.ExpectString(args[1],r);
                        return (T)Convert.ChangeType(s0.Contains(s1), typeof(T))!;
                    }
                },
                { "ContainsCI", (r, args) =>
                    {
                        string s0=ExpressionUtils.ExpectString(args[0],r);
                        string s1=ExpressionUtils.ExpectString(args[1],r);
                        return (T)Convert.ChangeType(s0.ToLower().Contains(s1.ToLower()), typeof(T))!;
                    }
                },
                { "Count", (r, args) =>
                    {
                        (List<string> modnames,List<ModRecord> sources) =ExpressionUtils.ExpectGroupRecord(args[0]);
                        return (T)Convert.ChangeType(sources.Count(), typeof(T))!;
                    }
                },
                { "Clone", (r, args) =>
                    {
                        (List<string> modnames,List<ModRecord> sources) =ExpressionUtils.ExpectGroupRecord(args[0]);
                        if(sources.Count != 1){
                            throw new Exception($"Only one record is supposed to be cloned at a time, currently it is cloning {sources.Count}");
                        }
                        int n =ExpressionUtils.ExpectInt(args[1]);
                        List<string> clonedModNames=Enumerable.Repeat(Patcher.Instance.currentRE!.modname,n).ToList();
                        List<ModRecord> clonedRecords=Patcher.Instance.currentRE!.CloneRecord(sources.ElementAt(0),n);
                        //return (T)(object)(clonedModNames, clonedRecords);
                        return (T)(object)(new RecordGroupExpression((clonedModNames, clonedRecords)));
                    }
                }
                };
        public object? InvokeWithEvaluatedArgs(List<Expression<object>> args,ModRecord r)
        {
            return FunctionExpression<object>.functions[functionname](r!, args);
        }
        public FunctionExpression(string funcName, List<Expression<object>> args)
        {
            functionname = funcName;
            arguments = args;

            if (!functions.TryGetValue(funcName, out var f))
                throw new Exception($"Unknown function {funcName}");

            func = r => f(r, arguments);
        }
        public override string ToString() => $"FunctionExpression<{functionname}>";

        public override T EvaluateTyped(ModRecord? r) => func(r!);

    }
    [DebuggerDisplay("{ToString()}")]
    public class VariableExpression : Expression<object>
    {
        public string Name { get; }
        public VariableExpression(string name) => Name = name;

        public override object EvaluateTyped(ModRecord? r) => throw new Exception($"Variable '{Name}' cannot be evaluated outside a lambda");

        public override string ToString() => $"VariableExpression<{Name}>";
    }
    [DebuggerDisplay("{ToString()}")]
    public class ArrayExpression : Expression<object>
    {
        public readonly List<Expression<object>> elements;

        public ArrayExpression(List<Expression<object>> elements)
        {
            this.elements = elements;
        }

        public override object EvaluateTyped(ModRecord? r)
        {
            object?[] values = new object?[elements.Count];
            for (int i = 0; i < elements.Count; i++)
                values[i] = elements[i].Evaluate(r);
            return values;
        }

        public override string ToString()
        {
            return $"Array[{string.Join(", ", elements)}]";
        }
    }
    [DebuggerDisplay("{ToString()}")]
    public class BoolFunctionExpression : Expression<bool>
    {
        private readonly Func<ModRecord, bool> func;

        public static readonly Dictionary<string, Func<ModRecord, List<Expression<object>>, bool>> functions =
            new()
            {
            { "true", (_, __) => true },
            { "false", (_, __) => false },
            { "FieldExist", (r, args) =>
                {
                    if (args.Count != 1)
                        throw new Exception("FieldExist expects exactly one argument");
                    string? field = args[0].Evaluate(r)!.ToString();
                    return !string.IsNullOrEmpty(field) && r.HasField(field);
                }
            },
            { "FieldIsNotEmpty", (r, args) =>
                {
                    if (args.Count != 1)
                        throw new Exception("FieldIsNotEmpty expects exactly one argument");
                    string? field = args[0].Evaluate(r)!.ToString();
                    return !string.IsNullOrEmpty(field) && !string.IsNullOrEmpty(r.GetFieldAsString(field));
                }
            },
            { "isExtraDataOfAny", (r, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("isExtraDataOfAny expects at least one argument");
                var definition = args[0].Evaluate(r);
                string? category = args.Count > 1 ? args[1].Evaluate(r)?.ToString() : null;
                int[]? variables = args.Count > 2 ? ConvertArray<int>(args[2].Evaluate(r)) : null;
                if (definition is ValueTuple<List<string>, List<ModRecord>> group)
                    return group.Item2.Any(rec => rec.isExtraDataOfThis(r, category,variables));
                throw new Exception($"Definition '{definition}' malformed");
            }},
            { "hasAnyAsExtraData", (r, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("hasAnyAsExtraData expects at least one argument");
                var definition = args[0].Evaluate(r);
                string? category = args.Count > 1 ? args[1].Evaluate(r)?.ToString() : null;
                int[]? variables = args.Count > 2 ? ConvertArray<int>(args[2].Evaluate(r)) : null;
                if (definition is ValueTuple<List<string>, List<ModRecord>> group)
                    return group.Item2.Any(rec => rec.hasThisAsExtraData(r, category, variables));
                throw new Exception($"Definition '{definition}' malformed");
            }},
            { "allExtraDataIsWithin", (r, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("allExtraDataIsWithin expects at least one argument");
                var definition = args[0].Evaluate(r);
                string? category = args.Count > 1 ? args[1].Evaluate(r)?.ToString():null;
                int[]? variables = args.Count > 2 ? ConvertArray<int>(args[2].Evaluate(r)) : null;
                if (definition is ValueTuple<List<string>, List<ModRecord>> group)
                    return group.Item2.All(rec => rec.hasThisAsExtraData(r, category, variables));
                throw new Exception($"Definition '{definition}' malformed");
            }},
            { "isRemoved", (r, args) =>
                {
                    string field = "REMOVED";
                    return !string.IsNullOrEmpty(field) && r.HasField(field) && r.BoolFields[field];
                }
            }
            };
        private readonly List<Expression<object>> arguments;
        public static T[] ConvertArray<T>(object? value)
        {
            if (value is not Array arr)
                throw new Exception($"Expected array, got {value?.GetType().Name ?? "null"}");
            return arr.Cast<object>().Select(o => (T)Convert.ChangeType(o, typeof(T))).ToArray();
        }
        public BoolFunctionExpression(string funcName, List<Expression<object>> args)
        {
            arguments = args;
            if (!functions.TryGetValue(funcName, out var f))
                throw new Exception($"Unknown boolean function '{funcName}'");

            func = r => f(r, arguments);
        }

        public override bool EvaluateTyped(ModRecord? r) => func(r!);
    }
    public class IndexExpression : Expression<object>
    {
        private readonly Expression<object> target;
        private readonly Expression<object> index;

        public IndexExpression(Expression<object> target, Expression<object> index)
        {
            this.target = target;
            this.index = index;
        }
        public class TableNameExpression : Expression<object>
        {
            public string Name { get; }
            public TableNameExpression(string name) => Name = name;

            public override string EvaluateTyped(ModRecord? r) => Name;
            public override string ToString() => $"Table<{Name}>";
        }

        public override object EvaluateTyped(ModRecord? r)
        {
            var targetVal = target.Evaluate(r);
            var indexVal = index.Evaluate(r);

            if (targetVal is IList<object> list)
            {
                int idx = Convert.ToInt32(indexVal);
                if (idx < 0 || idx >= list.Count)
                    throw new Exception($"Array index {idx} out of range");
                return list[idx];
            }

            var targetStr = targetVal!.ToString();
            var indexStr = indexVal!.ToString();

            if (targetStr == null || indexStr == null)
                throw new Exception("IndexExpression: null table name or key");

            if (!Patcher.Instance.tables.TryGetValue(targetStr, out var table))
                throw new Exception($"Table '{targetStr}' not found");

            if (!table.TryGetValue(indexStr, out var value))
                throw new Exception($"Key '{indexStr}' not found in table '{targetStr}'");

            if (value is Expression<object> expr)
                return expr.Evaluate(r)!;

            return value;
        }

        public override string ToString()
        {
            return $"IndexExpression({target}[{index}])";
        }
    }
    [DebuggerDisplay("{ToString()}")]
    public class RecordGroupExpression : Expression<object>
    {
        private (List<string>, List<ModRecord>) group;

        public RecordGroupExpression((List<string>, List<ModRecord>) group)
        {
            this.group = group;
        }

        public override object EvaluateTyped(ModRecord? r) => group!;
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
    [DebuggerDisplay("{ToString()}")]
    public class ProcedureExpression : Expression<object>
    {
        private readonly string procedureName;
        private readonly List<Expression<object>> arguments;
        private readonly Func<(List<string>, List<ModRecord>), object> func;
        private (List<string>, List<ModRecord>)? target;
        private bool oneToOne = false;
        public void setOneToOne(bool v)
        {
            oneToOne = v;
        }
        public static bool containsFunc(string s)
        {
            return procedures_duogroup.ContainsKey(s) || procedures_onegroup.ContainsKey(s);
        }
        public static readonly Dictionary<string, Func<ModRecord, List<Expression<object>>, object?>> procedures_onegroup = new()
            {
            { "SetField", (record, args) => 
                {
                    string strfieldname=ExpressionUtils.ExpectString(args[0],record);
                    string value=args[1].Evaluate(record)!.ToString()!;
                    Patcher.Instance.currentRE!.SetField(record,strfieldname,value);
                    return null;
                }
            },
            { "ForceSetField", (record, args) =>
                {
                    string strfieldname=ExpressionUtils.ExpectString(args[0],record);
                    string value=args[1].Evaluate(record)!.ToString()!;
                    string strtype=ExpressionUtils.ExpectString(args[2],record);
                    Patcher.Instance.currentRE!.ForceSetField(record,strfieldname,value,strtype);
                    return null;
                }
            },
            { "DeleteRecords", (record, args) =>
                {
                    Patcher.Instance.currentRE!.deleteRecord(record);
                    return null;
                }
            },
            { "EditExtraData", (record, args) =>
                {
                    string category = ExpressionUtils.ExpectString(args[0]);
                    Array lambdaArray= ExpressionUtils.ExpectArray(args[1]);
                    Func<int[], bool>? isValid = null;
                    if (args.Count > 2)
                    {
                        var vf = ExpressionUtils.ExpectLambda(args[2]);
                        if (vf is not Func<object?[], object?> lambdaFactory)
                            throw new FormatException($"Array element is not a lambda: {vf}");

                        isValid = arr =>
                        {
                            object[] boxed = arr.Select(x => (object)x).ToArray();

                            var result = lambdaFactory(new object?[] { boxed });

                            if (result is bool b) return b;
                            throw new FormatException("Validator lambda did not return bool");
                        };
                    }
                    List<Func<int,int>> transformers = new();
                    foreach (var element in lambdaArray)
                    {
                        if (element is not Func<object?[], object?> lambdaFactory)
                            throw new FormatException($"Array element is not a lambda: {element}");

                        Func<int,int> transformer = x =>
                        {
                            var result = lambdaFactory(new object?[] { x });
                            if (result is int i) return i;
                            if (result is long l) return (int)l;
                            if (result is double d && Math.Abs(d % 1) < double.Epsilon)
                                return (int)d;
                            throw new FormatException($"Lambda did not return int: {result}");
                        };

                        transformers.Add(transformer);
                    }
                    Patcher.Instance.currentRE!.EditExtraData(record,category,transformers.ToArray(),isValid);
                    return null;
                    }
                }
            };
    public static readonly Dictionary<string, Func<ModRecord,ModRecord,List<Expression<object>>, object?>> procedures_duogroup =
            new()
            {
            { "AddExtraData", (record,source, args) =>
                {
                    string category = ExpressionUtils.ExpectString(args[1]);
                    int[]? arrayvar = null;
                    if (args.Count>2)
                    {
                        var result = args[2].Evaluate(record);
                        if (result is int[] arr)
                            arrayvar = arr;
                        else if (result is object[] objArr)
                        {
                            arrayvar = objArr.Select(o => (int)Convert.ChangeType(o!, typeof(int))).ToArray();
                        }
                        else
                            throw new FormatException($"Invalid array returned for category: {result}");
                    }
                    Patcher.Instance.currentRE!.AddExtraData(record,source,category,arrayvar==null?null:arrayvar);
                    return null;
                }
            },
            { "ForceAddExtraData", (record,source, args) =>
                {
                    string category = ExpressionUtils.ExpectString(args[1]);
                    int[]? arrayvar = null;
                    if (args.Count>2)
                    {
                        var result = args[2].Evaluate(record);
                        if (result is int[] arr)
                            arrayvar = arr;
                        else if (result is object[] objArr)
                        {
                            arrayvar = objArr.Select(o => (int)Convert.ChangeType(o!, typeof(int))).ToArray();
                        }
                        else
                            throw new FormatException($"Invalid array returned for category: {result}");
                    }
                    Patcher.Instance.currentRE!.AddExtraData(record,source,category,arrayvar==null?null:arrayvar,true);
                    return null;
                }
            },
            { "SetFieldFromOther", (target, source, args) =>
            {
                string fieldName = ExpressionUtils.ExpectString(args[1], target);
                object? sourceValue = target.GetFieldAsObject(fieldName);

                var lambdaExpr = args[2] as LambdaExpression
                    ?? throw new Exception("Third argument must be a lambda");
                var lambda = (Func<object?[], object?>)lambdaExpr.Evaluate(source)!;
                object? value = lambda(new object?[] { sourceValue });

                Patcher.Instance.currentRE!.SetField(target, fieldName, value?.ToString() ?? "");
                return null;
            }}
            };
        public ProcedureExpression(string name, List<Expression<object>> args)
        {
            procedureName = name;
            arguments = args;

            procedures_onegroup.TryGetValue(name, out var oneFunc);
            procedures_duogroup.TryGetValue(name, out var duoFunc);

            if (oneFunc == null && duoFunc == null)
                throw new Exception($"Unknown procedure {name}");

            func = tg =>
            {
                var currentMod = Patcher.Instance.currentRE!.modname;
                var (leftNames, leftRecords) = tg;

                if (duoFunc != null)
                {
                    (List<string> modnames, List<ModRecord> sources) = ExpressionUtils.ExpectGroupRecord(arguments[0]);

                    if (oneToOne)
                    {
                        if (leftRecords.Count != sources.Count)
                            throw new Exception("One-to-one procedure requires left and right groups to be the same length");
                        for (int i = 0; i < leftRecords.Count; i++)
                            duoFunc(leftRecords[i], sources[i], arguments);
                    }
                    else
                    {
                        foreach (var record in leftRecords)
                            foreach (var source in sources)
                                duoFunc(record, source, arguments);
                    }
                    Patcher.Instance.currentRE!.addReferences(modnames
                    .Where(m => !string.Equals(m, currentMod, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList());

                    //Patcher.Instance.currentRE!.addReferences(modnames.Distinct(StringComparer.Ordinal).ToList());
                }
                else
                {
                    foreach (var record in leftRecords)
                        oneFunc!(record, arguments);
                }
                Patcher.Instance.currentRE!.addDependencies(leftNames
                        .Where(m => !string.Equals(m, currentMod, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList());
                //Patcher.Instance.currentRE!.addDependencies(leftNames.Distinct(StringComparer.Ordinal).ToList());
                return tg;
            };
        }

        public void SetTarget((List<string>, List<ModRecord>) targetGroup)
        {
            target = targetGroup;
        }
        public override object EvaluateTyped(ModRecord? r) => func(target!.Value);

    }
    
    [DebuggerDisplay("{ToString()}")]
    public class PipeExpression : Expression<object>
    {
        private readonly RecordGroupExpression left;
        private readonly ProcedureExpression right; 

        public PipeExpression(RecordGroupExpression left, ProcedureExpression right)
        {
            this.left = left ?? throw new ArgumentNullException(nameof(left));
            this.right = right ?? throw new ArgumentNullException(nameof(right));
        }
        public override object EvaluateTyped(ModRecord? r)
        {
            var leftValue = left.Evaluate(r);
            if (leftValue is not (List<string> names, List<ModRecord> records))
                throw new Exception("PipeExpression: left side must evaluate to a record group (List<string>, List<ModRecord>)");
            right.SetTarget((names, records));
            right.Evaluate(r);
            return leftValue;
        }
        public override string ToString()
        {
            return $"PipeExpression({left} -> {right})";
        }
    }
    [DebuggerDisplay("{ToString()}")]
    public sealed class LambdaExpression : Expression<object>
    {
        public List<string> Parameters { get; }
        public Expression<object> Body { get; }
        public LambdaExpression(List<string> parameters, Expression<object> body)
        {
            Parameters = parameters;
            Body = body;
        }
        public override object EvaluateTyped(ModRecord? r)
        {
            return new Func<object?[], object?>(args =>
            {
                if (args.Length != Parameters.Count)
                    throw new Exception($"Lambda: expected {Parameters.Count} args, got {Parameters.Count}");
                var local = new Dictionary<string, object?>();

                for (int i = 0; i < Parameters.Count; i++)
                    local[Parameters[i]] = args[i];
                var result = EvaluateWithLocalScope(Body, local, r!);

                if (result is long l)
                    return (int)l;

                if (result is double d && Math.Abs(d % 1) < double.Epsilon)
                    return (int)d;

                return result;
            });
        }

        private object? EvaluateWithLocalScope(Expression<object> expr, Dictionary<string, object?> locals,ModRecord record)
        {
            if (expr is VariableExpression v)
            {
                if (locals.TryGetValue(v.Name, out var val))
                    return val;
                throw new Exception($"Unknown variable '{v.Name}'");
            }

            if (expr is BinaryExpression b)
            {
                var leftVal = EvaluateWithLocalScope(b.left, locals,record);
                var rightVal = EvaluateWithLocalScope(b.right, locals, record);

                if (!BinaryExpression.Operators.TryGetValue(b.op, out var binOp))
                    throw new Exception($"Unknown binary operator '{b.op}'");
                return binOp(leftVal!, rightVal!);
            }

            if (expr is UnaryExpression u)
            {
                var innerVal = EvaluateWithLocalScope(u.inner, locals, record);

                if (!UnaryExpression.UnaryOperators.TryGetValue(u.op, out var unOp))
                    throw new Exception($"Unknown unary operator '{u.op}'");

                return unOp(innerVal!);
            }
            if (expr is ArrayExpression arrayExpr)
            {
                var values = new List<object?>();
                foreach (var element in arrayExpr.elements)
                {
                    if (element is LambdaExpression)
                        values.Add(element);
                    else
                        values.Add(EvaluateWithLocalScope(element, locals, record));
                }
                return values.ToArray();
            }
            if (expr is FunctionExpression<object> fexpr)
            {
                var evaluatedArgs = new List<Expression<object>>();

                foreach (var arg in fexpr.arguments)
                {
                    var value = EvaluateWithLocalScope(arg, locals, record);
                    evaluatedArgs.Add(new LiteralExpression(value));
                }

                return fexpr.InvokeWithEvaluatedArgs(evaluatedArgs,record);
            }
            try
            {
                return expr.Evaluate(record);
                //return expr.Evaluate(null);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error evaluating expression of type {expr.GetType().Name} inside lambda: {ex.Message}", ex);
            }
        }

        public override string ToString()
        {
            return $"Lambda({string.Join(", ", Parameters)} => {Body})";
        }
    }
    [DebuggerDisplay("{ToString()}")]
    public class GlobalFunctionExpression : Expression<object>
    {
        private readonly string name;
        private readonly List<Expression<object>> args;

        public static readonly Dictionary<string, Action<List<Expression<object>>>> globalFuncs = new()
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
                    //CoreUtils.Print(Patcher.Instance.GetModRecordEvolution(stringid),0);
                    //ReverseEngineerRepository.Instance.GetRecordEvolution(stringid);
                    CoreUtils.Print(ReverseEngineerRepository.Instance.GetRecordEvolution(stringid),0);
                }
            },
            { "Stop",args=>
                {
                    Patcher.Instance.Stop();
                }
            }
        };
        private static string getStringFromArgs(List<Expression<object>> args)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var arg in args) {
                sb.Append(arg.ToString()+"\n");
            }
            return sb.ToString();
        }

        public GlobalFunctionExpression(string name, List<Expression<object>> args)
        {
            this.name = name;
            this.args = args;
        }

        public override object EvaluateTyped(ModRecord? r)
        {
            if (!globalFuncs.TryGetValue(name, out var func))
                throw new Exception($"Unknown global function '{name}'");
            func(args);
            return true;
        }

        public override string ToString() => $"@{name}({string.Join(", ", args)})";
    }

}

    

  
