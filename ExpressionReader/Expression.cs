using KenshiCore.Mods;
using KenshiCore.OgreEngineering;
using KenshiCore.ReverseEngineering;
using KenshiCore.UI;
using KenshiCore.Utilities;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
using static ScintillaNET.Style;

namespace KenshiPatcher.ExpressionReader
{

    public static class ExpressionUtils
    {
        public static string ExpectString(Expression<object> expression, ModRecord? r = null, Dictionary<string, object?>? locals = null)
        {
            object o = expression.Evaluate(r,locals)!;
            if (o is string s)
                return s;
            throw new FormatException($"Expression is expected to be a string: {expression.ToString()}");
        }
        public static (List<string>, List<ModRecord>) ExpectGroupRecord(Expression<object> expression, ModRecord? r = null, Dictionary<string, object?>? locals = null)
        {
            object o = expression.Evaluate(r, locals)!;
            if (o is (List<string> names, List<ModRecord> records))
                return (names, records);
            throw new FormatException($"Expression is expected to be a group: {expression.ToString()}");
        }
        public static Array ExpectArray(Expression<object> expression, ModRecord? r = null, Dictionary<string, object?>? locals = null)
        {
            object arrObj = expression.Evaluate(r, locals)!;
            if (arrObj is Array arr)
                return arr;
            throw new FormatException($"ExpectArray: argument must be an array: {expression.ToString()}");
        }
        public static Func<T, R> ExpectLambda<T, R>(Expression<object> expression, ModRecord? closure = null, Dictionary<string, object?>? locals = null)
        {
            var raw = ExpectLambda(expression, closure);

            return input =>
            {
                object? result = raw(new object?[] { input });

                return (R)Convert.ChangeType(result!, typeof(R));
            };
        }
        public static Func<object?[], object?> ExpectLambda(Expression<object> expression, ModRecord? closure = null, Dictionary<string, object?>? locals = null)
        {
            var lambdaExpr = ExpectLambdaExpression(expression);

            return (Func<object?[], object?>)lambdaExpr.Evaluate(closure, locals)!;
        }
        public static int ExpectInt(Expression<object> expression, ModRecord? r = null, Dictionary<string, object?>? locals = null)
        {
            object o = expression.Evaluate(r, locals)!;
            try
            {
                return (int)ValueCaster.ToInt64(o);
            }
            catch (Exception ex)
            {
                throw new FormatException($"Expression is expected to be an int: {expression} (value='{o}', type='{o?.GetType().Name ?? "null"}')", ex);
            }
        }
        public static bool ExpectBool(Expression<object> expression, ModRecord? r = null, Dictionary<string, object?>? locals = null)
        {
            object o = expression.Evaluate(r, locals)!;
            try
            {
                return (bool)o;
            }
            catch (Exception ex)
            {
                throw new FormatException($"Expression is expected to be an int: {expression} (value='{o}', type='{o?.GetType().Name ?? "null"}')", ex);
            }
        }
        public static LambdaExpression ExpectLambdaExpression(Expression<object> expression)
        {
            if (expression is LambdaExpression lambda)
                return lambda;

            throw new FormatException(
                $"Expression is expected to be a lambda: {expression}"
            );
        }
    }
    [DebuggerDisplay("{ToString()}")]
    public abstract class Expression
    {
        public abstract object? Evaluate(ModRecord? r, Dictionary<string, object?>? locals = null);
    }
    [DebuggerDisplay("{ToString()}")]
    public abstract class Expression<T> : Expression
    {
        public abstract T EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null);

        public override object? Evaluate(ModRecord? r, Dictionary<string, object?>? locals = null)
        {
            try
            {
                return EvaluateTyped(r, locals);
            }
            catch (System.NullReferenceException ex) when (r == null)
            {
                throw new MissingRecordException(r, "Null reference while evaluating record.", ex);
            }
        }
    }

    [DebuggerDisplay("{ToString()}")]
    public sealed class Literal<T> : Expression<T>
    {
        private readonly T value;

        public Literal(T value) => this.value = value;

        public override T EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null) => value;

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

        public override object? EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null)
        => inner.EvaluateTyped(r, locals);

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
        public override object EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null)
        {
            switch (op)
            {
                case "&&":
                    {
                        var l = left.Evaluate(r, locals);
                        if (!Convert.ToBoolean(l))
                            return false;
                        return Convert.ToBoolean(right.Evaluate(r, locals));
                    }

                case "||":
                    {
                        var l = left.Evaluate(r, locals);
                        if (Convert.ToBoolean(l))
                            return true;
                        return Convert.ToBoolean(right.Evaluate(r, locals));
                    }

                default:
                    return Operators[op](left.Evaluate(r, locals)!, right.Evaluate(r, locals)!);
            }
        }
        public override string ToString()
        {
            return $"BinaryExpression<{left.ToString()} {op} {right.ToString()}>";
        }
        public BinaryExpression(Expression<object> left, Expression<object> right, string sop)
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
        public override object EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null) => UnaryOperators[op](inner.Evaluate(r, locals)!);

        public override string ToString()
        {
            return $"({op} {inner.ToString()})";
        }

        public UnaryExpression(Expression<object> inner, string sop)
        {
            this.inner = inner;
            if (!UnaryOperators.TryGetValue(sop, out var v))
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
        public override object EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null) => value!;


        public override string ToString() => value?.ToString() ?? "null";
    }
    [DebuggerDisplay("{ToString()}")]
    public class FunctionExpression<T> : Expression<T>
    {
        public readonly List<Expression<object>> arguments;
        private readonly string functionname;
        private readonly Func<ModRecord, Dictionary<string, object?>?, T> func;
        private static readonly Random getrandom = new Random();

        //public static readonly Dictionary<string, Func<ModRecord, List<Expression<object>>, T>> functions =
        public static readonly Dictionary<string, Func<ModRecord, Dictionary<string, object?>?, List<Expression<object>>, T>> functions =
            new()
            {
                { "GetField", (r,locals, args) =>
                    {
                        if (args.Count != 1)
                            throw new Exception("GetField() expects exactly one argument");
                        var fieldName = args[0].Evaluate(r,locals)!.ToString();
                        return (T)Convert.ChangeType(r.GetFieldAsObject(fieldName!), typeof(T))!;
                    }
                },
                { "ToInt", (r,locals, args) =>
                    {
                        if (args.Count != 1) throw new Exception("ToInt expects exactly one argument");
                        var val = args[0].Evaluate(r,locals);

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
                { "ToFloat", (r,locals, args) =>
                    {
                        if (args.Count != 1)
                            throw new Exception("ToFloat expects exactly one argument");
                        var val = args[0].Evaluate(r,locals);
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
                { "Min", (r,locals, args) =>
                    {
                        Array arr =ExpressionUtils.ExpectArray(args[0],r,locals);
                        double min = Convert.ToDouble(arr.GetValue(0)!);
                        for (int i = 1; i < arr.Length; i++)
                            min = Math.Min(min, Convert.ToDouble(arr.GetValue(i)!));
                        object result = min % 1 == 0 ? (int)min : min;
                        return (T)Convert.ChangeType(result, typeof(T))!;
                    }
                },
                { "Max", (r,locals, args) =>
                    {
                        Array arr =ExpressionUtils.ExpectArray(args[0],r,locals);
                        double max = Convert.ToDouble(arr.GetValue(0)!);
                        for (int i = 1; i < arr.Length; i++)
                            max = Math.Max(max, Convert.ToDouble(arr.GetValue(i)!));
                        object result = max % 1 == 0 ? (int)max : max;
                        return (T)Convert.ChangeType(result, typeof(T))!;
                    }
                },
                { "ArrIndex", (r,locals, args) =>
                    {
                        var arrObj = args[0].Evaluate(r,locals);
                        var indexObj = args[1].Evaluate(r,locals);
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
                { "RandomFloat", (r,locals, args) =>
                    {
                        return (T)Convert.ChangeType(getrandom.NextDouble(), typeof(T))!;
                    }
                },
                { "RandomBetweenInts", (r,locals, args) =>
                    {
                        int min = (int)Convert.ToInt64(args[0].Evaluate(r,locals));
                        int max = (int)Convert.ToInt64(args[1].Evaluate(r,locals));
                        return (T)Convert.ChangeType(getrandom.Next(min,max), typeof(T))!;
                    }
                },
                { "ContainsCS", (r,locals, args) =>
                    {
                        string s0=ExpressionUtils.ExpectString(args[0],r,locals);
                        string s1=ExpressionUtils.ExpectString(args[1],r,locals);
                        return (T)Convert.ChangeType(s0.Contains(s1), typeof(T))!;
                    }
                },
                { "ContainsCI", (r,locals, args) =>
                    {
                        string s0=ExpressionUtils.ExpectString(args[0],r,locals);
                        string s1=ExpressionUtils.ExpectString(args[1],r,locals);
                        return (T)Convert.ChangeType(s0.ToLower().Contains(s1.ToLower()), typeof(T))!;
                    }
                },
                { "StartsWith", (r,locals, args) =>
                    {
                        string s0=ExpressionUtils.ExpectString(args[0],r,locals);
                        string s1=ExpressionUtils.ExpectString(args[1],r,locals);
                        return (T)Convert.ChangeType(s0.StartsWith(s1), typeof(T))!;
                    }
                },
                { "Count", (r,locals, args) =>
                    {
                        (List<string> modnames,List<ModRecord> sources) =ExpressionUtils.ExpectGroupRecord(args[0]);
                        return (T)Convert.ChangeType(sources.Count(), typeof(T))!;
                    }
                },
                { "Clone", (r,locals, args) =>
                    {
                        (List<string> modnames,List<ModRecord> sources) =ExpressionUtils.ExpectGroupRecord(args[0]);
                        if(sources.Count != 1){
                            throw new Exception($"Only one record is supposed to be cloned at a time, currently it is cloning {sources.Count}");
                        }
                        int n =ExpressionUtils.ExpectInt(args[1]);
                        List<string> clonedModNames=Enumerable.Repeat(Patcher.Instance.currentRE!.modname,n).ToList();
                        List<ModRecord> clonedRecords=Patcher.Instance.currentRE!.CloneRecord(sources.ElementAt(0),n);
                        return (T)(object)(new RecordGroupExpression((clonedModNames, clonedRecords)));
                    }
                },
                { "CherryPick", (r,locals, args) =>
                    {
                        (List<string> modnamesA,List<ModRecord> sourcesA) =ExpressionUtils.ExpectGroupRecord(args[0]);
                        (List<string> modnamesB,List<ModRecord> sourcesB) =ExpressionUtils.ExpectGroupRecord(args[1]);
                        
                        List<string> resultModNames=new();
                        List<ModRecord> resultRecords=new();

                        for(int j = 0;j<sourcesB.Count(); j++)
                        {
                            bool found=false;
                            var lambdaExpr = args[2] as LambdaExpression;
                            for(int i = 0; i < sourcesA.Count()&&!found; i++)
                            {
                                var lambda = (Func<object?[], object?>)lambdaExpr!.Evaluate(sourcesA[i], locals)!;
                                var value = lambda(new object?[] { (new List<string> { modnamesB[j] },new List<ModRecord> { sourcesB[j] })});
                                if ((bool)value!)
                                {
                                    //CoreUtils.Print($"added race: {sourcesA[i]} and skeleton link is: {sourcesA[i].GetFieldAsString("__skeleton_link__")}");
                                    resultRecords.Add(sourcesA[i]);
                                    resultModNames.Add(modnamesA[i]);
                                    found=true;
                                }
                            }
                        }
                        return (T)(object)(new RecordGroupExpression((resultModNames, resultRecords)));
                    }
                },
                { "CategorizeGivenField", (r,locals, args) =>
                    {
                        var categories = new Dictionary<string, Expression<object>>();
                        //var categories = new Dictionary<string, RecordGroupExpression>();

                        (List<string> modnames,List<ModRecord> sources) =ExpressionUtils.ExpectGroupRecord(args[0]);
                        string field=ExpressionUtils.ExpectString(args[1],r,locals);
                        
                        for(int i = 0;i < sources.Count(); i++)
                        {
                            string value=sources[i].GetFieldAsString(field)!;
                            if (!categories.Keys.Contains(value))
                            {
                                categories[value]=new RecordGroupExpression((new List<string>(), new List<ModRecord>()));
                            }
                            categories.TryGetValue(value, out var resultRecords);
                            ((RecordGroupExpression)resultRecords!).group.Item1.Add(modnames[i]);
                            ((RecordGroupExpression)resultRecords!).group.Item2.Add(sources[i]);

                        }
                        return (T)(object)categories;
                    }
                },
                { "FileExists", (r,locals, args) =>
                    {
                        string filepath=ExpressionUtils.ExpectString(args[0],r,locals);
                        return (T)Convert.ChangeType(File.Exists(filepath), typeof(T))!;
                    }
                },
                { "GetRealPath", (r,locals, args) =>
                    {
                        string filepath=ExpressionUtils.ExpectString(args[0],r,locals);
                        string realpath=ModRepository.Instance.ResolveRealPath(filepath);
                        //CoreUtils.Print(realpath.StartsWith("E_") ? realpath+": " + filepath : "\""+realpath.Replace("\\","/")+"\",");
                        if (!File.Exists(realpath))
                        {
                            CoreUtils.Print("Resolving real path for: "+r.StringId);
                            CoreUtils.Print(realpath.StartsWith("E_") ? realpath+": " + filepath : realpath);
                        }
                        return (T)Convert.ChangeType(realpath, typeof(T))!;
                    }
                },
                { "GetSkeletonLink", (r,locals, args) =>
                    {
                        string filepath=ExpressionUtils.ExpectString(args[0],r,locals);
                        string skeletonLink=FileAnalyzer.Instance.getSkeletonLink(filepath);
                        return (T)Convert.ChangeType(skeletonLink, typeof(T))!;
                    }
                },
                };
        public FunctionExpression(string funcName, List<Expression<object>> args)
        {
            functionname = funcName;
            arguments = args;

            if (!functions.TryGetValue(funcName, out var f))
                throw new Exception($"Unknown function {funcName}");

            func = (r,locals) => f(r,locals, arguments);
        }
        public override string ToString() => $"FunctionExpression<{functionname}>";

        public override T EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null) => func(r!,locals);

    }
    [DebuggerDisplay("{ToString()}")]
    public class VariableExpression : Expression<object>
    {
        public string Name { get; }
        public VariableExpression(string name) => Name = name;
        public override object EvaluateTyped(ModRecord? r,Dictionary<string, object?>? locals = null)
        {
            if (locals != null && locals.TryGetValue(Name, out var value))
                return value!;

            throw new Exception($"Variable '{Name}' cannot be evaluated outside a lambda");
        }
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

        public override object EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null)
        {
            object?[] values = new object?[elements.Count];
            for (int i = 0; i < elements.Count; i++)
                values[i] = elements[i].Evaluate(r,locals);
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

        private readonly Func<ModRecord, Dictionary<string, object?>?, bool> func;
        public static readonly Dictionary<string, Func<ModRecord, Dictionary<string, object?>, List<Expression<object>>, bool>> functions =
            new()
            {
            { "true", (_,__, ___) => true },
            { "false", (_,__, ___) => false },
            { "FieldExist", (r,locals, args) =>
                {
                    if (args.Count != 1)
                        throw new Exception("FieldExist expects exactly one argument");
                    string field = ExpressionUtils.ExpectString(args[0],r,locals);
                    return r.HasField(field) && !string.IsNullOrEmpty(r.GetFieldAsString(field));

                }
            },
            { "isExtraDataEmpty", (r,locals, args) =>
                {
                    string? category=null;
                    if (args.Count>0)
                        category=ExpressionUtils.ExpectString(args[0],r,locals);
                    return r.isExtraDataEmpty(category);
                }
            },
            { "FieldIsNotEmpty", (r,locals, args) =>
                {
                    if (args.Count != 1)
                        throw new Exception("FieldIsNotEmpty expects exactly one argument");
                    string field = ExpressionUtils.ExpectString(args[0],r,locals);
                    return r.HasField(field) && !string.IsNullOrEmpty(r.GetFieldAsString(field));
                }
            },
            { "isExtraDataOfAny", (r,locals, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("isExtraDataOfAny expects at least one argument");
                var definition = args[0].Evaluate(r,locals);
                string? category = args.Count > 1 ? args[1].Evaluate(r,locals)?.ToString() : null;
                int[]? variables = args.Count > 2 ? ConvertArray<int>(args[2].Evaluate(r,locals)) : null;
                if (definition is ValueTuple<List<string>, List<ModRecord>> group)
                    return group.Item2.Any(rec => rec.isExtraDataOfThis(r, category,variables));
                throw new Exception($"Definition '{definition}' malformed");
            }},
            { "hasAnyAsExtraData", (r,locals, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("hasAnyAsExtraData expects at least one argument");
                var definition = args[0].Evaluate(r,locals);
                string? category = args.Count > 1 ? args[1].Evaluate(r,locals)?.ToString() : null;
                int[]? variables = args.Count > 2 ? ConvertArray<int>(args[2].Evaluate(r,locals)) : null;
                if (definition is ValueTuple<List<string>, List<ModRecord>> group)
                    return group.Item2.Any(rec => rec.hasThisAsExtraData(r, category, variables));
                throw new Exception($"Definition '{definition}' malformed");
            }},
            { "allExtraDataIsWithin", (r,locals, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("allExtraDataIsWithin expects at least one argument");
                var definition = args[0].Evaluate(r,locals);
                string? category = args.Count > 1 ? args[1].Evaluate(r,locals)?.ToString():null;
                int[]? variables = args.Count > 2 ? ConvertArray<int>(args[2].Evaluate(r,locals)) : null;
                if (definition is ValueTuple<List<string>, List<ModRecord>> group)
                {
                    Dictionary<string, int[]>? extradata=r.GetExtraData(category);
                    if (extradata == null || extradata.Count == 0)
                        return true;
                    var allowedIds = group.Item2.Select(x => x.StringId).ToHashSet();
                    return extradata.Keys.All(rec =>allowedIds.Contains(rec));
                }
                throw new Exception($"Definition '{definition}' malformed");
            }},
            { "isRemoved", (r,locals, args) =>
                {
                    string field = "REMOVED";
                    return !string.IsNullOrEmpty(field) && r.HasField(field) && r.BoolFields[field];
                }
            },
            {
                "isAllChildrenUntil", (r,locals, args) =>
                    {
                        var testExpr = args[0];
                        var stopExpr = args[1];

                        string category = args.Count > 2
                            ? ExpressionUtils.ExpectString(args[2], r)
                            : "lines";

                        int maxVisits = args.Count > 3
                            ? ExpressionUtils.ExpectInt(args[3], r)
                            : 50000;

                        bool getEarly = args.Count > 4 ? ExpressionUtils.ExpectBool(args[4], r,locals) : true;
                        return !IsAnyChildUntil(
                            r,
                            rec => !Convert.ToBoolean(testExpr.Evaluate(rec,locals)),
                            rec => Convert.ToBoolean(stopExpr.Evaluate(rec,locals)),
                            category,
                            maxVisits,
                            getEarly
                        );
                    }
            },
            {
                "isAnyChildUntil", (r,locals, args) =>
                {
                    var testExpr = args[0];
                    var stopExpr = args[1];

                    string category = args.Count > 2
                        ? ExpressionUtils.ExpectString(args[2], r,locals)
                        : "lines";

                    int maxVisits = args.Count > 3
                        ? ExpressionUtils.ExpectInt(args[3], r,locals)
                        : 50000;

                    bool getEarly = args.Count > 4 ? ExpressionUtils.ExpectBool(args[4], r,locals) : true;
                    return IsAnyChildUntil(
                        r,
                        rec => Convert.ToBoolean(testExpr.Evaluate(rec,locals)),
                        rec => Convert.ToBoolean(stopExpr.Evaluate(rec,locals)),
                        category,
                        maxVisits,
                        getEarly
                    );
                }
            },
            {
                "isLoop", (r,locals, args) =>
                    {
                        string category = args.Count > 0 ? ExpressionUtils.ExpectString(args[0], r,locals) : "lines";
                        int maxVisits = args.Count > 1 ? ExpressionUtils.ExpectInt(args[1], r,locals) : 50000;
                        bool getEarly = args.Count > 4 ? ExpressionUtils.ExpectBool(args[2], r,locals) : true;

                        var visitedIds = new HashSet<string>(StringComparer.Ordinal);
                        int visitCount = 0;

                        bool Detect(ModRecord rec)
                        {
                            if (visitCount++ > maxVisits)
                                return false;

                            if (!visitedIds.Add(rec.StringId))
                                return true; // loop detected

                            var children = rec.GetExtraData(category);
                            if (children != null)
                            {
                                foreach (var kv in children)
                                {
                                    var child = Resolve(kv.Key, getEarly);
                                    if (child != null && Detect(child))
                                        return true;
                                }
                            }

                            visitedIds.Remove(rec.StringId);
                            return false;
                        }

                        return Detect(r);
                    }
            }
            };
        private static readonly Dictionary<(string, bool), ModRecord?> _resolveGlobalCache = new();
        
        private static ModRecord? Resolve(string id, bool getEarly = false)
        {
            var key = (id, getEarly);
            if (_resolveGlobalCache.TryGetValue(key, out var cached))
                return cached;
            var baseRec = ReverseEngineerRepository.Instance
                .searchModRecordByStringIdGlobally(id, getEarly);

            if (baseRec == null)
                return null;

            if (!getEarly)
            {
                var localPatch = Patcher.Instance.currentRE!
                    .searchModRecordByStringIdLocally(id);

                if (localPatch != null)
                {
                    baseRec.applyChangesFrom(localPatch);
                }
            }
            _resolveGlobalCache[key] = baseRec;
            return baseRec;
        }
        private readonly List<Expression<object>> arguments;
        private static bool IsAnyChildUntil(
        ModRecord root,
        Func<ModRecord, bool> match,
        Func<ModRecord, bool> stop,
        string category,
        int maxVisits = int.MaxValue,
        bool getEarly = true)
        {
            var visitedIds = new HashSet<string>(StringComparer.Ordinal);
            var resolveCache = new Dictionary<string, ModRecord?>(StringComparer.Ordinal);
            int visits = 0;

            bool Traverse(ModRecord rec)
            {
                if (++visits > maxVisits)
                    return false;

                if (!visitedIds.Add(rec.StringId))
                    return false;

                if (stop(rec))
                    return false; // stop node excluded, do not count, do not descend

                if (match(rec))
                    return true;

                var children = rec.GetExtraData(category);
                if (children != null)
                {
                    foreach (var kv in children)
                    {
                        var child = Get(kv.Key);
                        if (child != null && Traverse(child))
                            return true;
                    }
                }

                return false;
            }

            ModRecord? Get(string id)
            {
                if (!resolveCache.TryGetValue(id, out var rec))
                {
                    rec = Resolve(id, getEarly);
                    resolveCache[id] = rec;
                }
                return rec;
            }
            var kids = root.GetExtraData(category);
            if (kids != null)
            {
                foreach (var kv in kids)
                {
                    var child = Get(kv.Key);
                    if (child != null && Traverse(child))
                        return true;
                }
            }

            return false;
        }
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

            func = (r, locals) => f(r,locals!, arguments);
        }

        public override bool EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null) => func(r!, locals);
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

            public override string EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null) => Name;
            public override string ToString(){
                Patcher.Instance.tables.TryGetValue(Name, out var table);
                string result= $"Table<{Name}> count:{((table==null)?0:table.Count)}\n";
                if (table != null)
                {
                    List<string> keys = table.Keys.ToList();
                    int i = 0;
                    foreach (Expression e in table.Values)
                    {
                        result += $"<{Name}[{keys[i]}]>:\n";
                        result += e.ToString()+"\n";
                        i++;
                    }
                }
                return result;
            } 
        }

        public override object EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null)
        {
            var targetVal = target.Evaluate(r, locals);
            var indexVal = index.Evaluate(r, locals);
            if (targetVal is Dictionary<string, Expression<object>> dict)
            {
                string key = indexVal!.ToString()!;

                if (dict.TryGetValue(key, out var exprvalue))
                {
                    return exprvalue.Evaluate(r, locals)!;
                }

                throw new Exception($"Key '{key}' not found");
            }
            if (targetVal is System.Collections.IList list)
            {
                int idx = Convert.ToInt32(indexVal);

                if (idx < 0 || idx >= list.Count)
                    throw new Exception($"Array index {idx} out of range");

                return list[idx]!;
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
            if (target is TableNameExpression tableexp)
            {
                Patcher.Instance.tables.TryGetValue(tableexp.Name, out var table);
                string strindex=index.Evaluate(null)!.ToString()!;
                return $"{tableexp.Name}[{strindex}]: {table![strindex]}";
            }
            return $"IndexExpression: target: {target}, index: {index}";
        }
    }
    [DebuggerDisplay("{ToString()}")]
    public class RecordGroupExpression : Expression<object>
    {
        public (List<string>, List<ModRecord>) group;

        public RecordGroupExpression((List<string>, List<ModRecord>) group)
        {
            this.group = group;
        }

        public override object EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null) => group!;
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
                    string value = ValueCaster.ToInvariantString(args[1].Evaluate(record));
                    Patcher.Instance.currentRE!.SetField(record,strfieldname,value);
                    return null;
                }
            },
            { "SetFieldIfExist", (record, args) =>
                {
                    string field = ExpressionUtils.ExpectString(args[0], record);
                    if (!record.HasField(field))
                        return null;
                    string value = ValueCaster.ToInvariantString(args[1].Evaluate(record));
                    //string value = args[1].Evaluate(record)?.ToString() ?? "";
                    Patcher.Instance.currentRE!.SetField(record, field, value);
                    return null;
                }
            },
            { "SetText", (record, args) =>
                 {
                    var modifier = ExpressionUtils.ExpectLambda<string, string>(args[0], record);
                    Patcher.Instance.currentRE!.SetText(record, modifier);
                    return null;
                }
            },
            { "ForceSetField", (record, args) =>
                {
                    string strfieldname=ExpressionUtils.ExpectString(args[0],record);
                    string value = ValueCaster.ToInvariantString(args[1].Evaluate(record));
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
            { "DeleteRecordsFromPatch", (record, args) =>
                {
                    Patcher.Instance.currentRE!.deleteRecordFromPatch(record);
                    return null;
                }
            },
            { "DeleteEmptyRecordsFromPatch", (record, args) =>
                {
                    Patcher.Instance.currentRE!.deleteEmptyRecordFromPatch(record);
                    return null;
                }
            },
            { "DeleteFieldInPatch", (record, args) =>
                {
                    string field=ExpressionUtils.ExpectString(args[0],record);
                    Patcher.Instance.currentRE!.DeleteField(record,field);
                    return null;
                }
            },
            { "EditExtraData", (record, args) =>
                {
                    string category = ExpressionUtils.ExpectString(args[0]);
                    Array lambdaArray = ExpressionUtils.ExpectArray(args[1]);

                    Func<int[], bool>? isValid = null;
                    if (args.Count > 2)
                        isValid = ExpressionUtils.ExpectLambda<int[], bool>(args[2], record);
                    List<Func<int, int>> transformers = new();
                    foreach (var element in lambdaArray)
                    {
                        if (element is not Func<object?[], object?> raw)
                            throw new FormatException($"Array element is not a lambda: {element}");

                        transformers.Add(i => Convert.ToInt32(raw(new object?[] { i })));
                    }

                    Patcher.Instance.currentRE!.EditExtraData(record,category,transformers.ToArray(),isValid);

                    return null;
                }
            }
            };
        public static readonly Dictionary<string, Func<ModRecord, ModRecord, List<Expression<object>>, object?>> procedures_duogroup =
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
            { "RemoveExtraData", (record,source, args) =>
                {
                    string category = ExpressionUtils.ExpectString(args[1]);
                    Patcher.Instance.currentRE!.RemoveExtraData(record,source,category);
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
                var lambda = ExpressionUtils.ExpectLambda(args[2], source);
                object? value = lambda(new object?[] { sourceValue });
                Patcher.Instance.currentRE!.SetField(target, fieldName, ValueCaster.ToInvariantString(value));
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
                ProgressController progress=ProgressController.Instance;

                if (duoFunc != null)
                {
                    List<string> modnames = new();
                    List<ModRecord> sources = new();
                    bool obtained_group_record = true;
                    progress.Initialize(leftRecords.Count);
                    try
                    {
                        (modnames, sources) = ExpressionUtils.ExpectGroupRecord(arguments[0]);
                    }
                    catch (MissingRecordException)
                    {
                        obtained_group_record = false;
                    }
                    if (oneToOne)
                    {
                        if (!obtained_group_record)
                        {
                            throw new Exception("One-to-one procedure requires a group record as argument");
                        }
                        if (leftRecords.Count != sources!.Count)
                            throw new Exception("One-to-one procedure requires left and right groups to be the same length");
                        for (int i = 0; i < leftRecords.Count; i++)
                        {
                            duoFunc(leftRecords[i], sources[i], arguments);
                            progress.Report(i,$"running {procedureName} to record {i}");
                        }
                    }
                    else
                    {
                        int i = 0;
                        foreach (var record in leftRecords)
                        {
                            if (!obtained_group_record)
                            {
                                var (modn, srcs) = ExpressionUtils.ExpectGroupRecord(arguments[0], record);//here
                                modnames ??= new List<string>();
                                modnames.AddRange(modn);
                                sources = srcs;
                            }
                            foreach (var source in sources!)
                            {
                                duoFunc(record, source, arguments);
                            }
                            i++;
                            progress.Report(i, $"running {procedureName} to record {i}");
                        }
                    }
                    progress.Finish($"{procedureName} done");
                    Patcher.Instance.currentRE!.addReferences(modnames
                    .Where(m => !string.Equals(m, currentMod, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList());
                }
                else
                {
                    progress.Initialize(leftRecords.Count);
                    int i = 0;
                    foreach (var record in leftRecords)
                    {
                        oneFunc!(record, arguments);
                        i++;
                        progress.Report(i, $"running {procedureName} to record {i}");
                    }
                }
                Patcher.Instance.currentRE!.addDependencies(leftNames
                        .Where(m => !string.Equals(m, currentMod, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList());
                return tg;
            };
        }

        public void SetTarget((List<string>, List<ModRecord>) targetGroup)
        {
            target = targetGroup;
        }
        public override object EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null) => func(target!.Value);

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
        public override object EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null)
        {
            var leftValue = left.Evaluate(r);
            if (leftValue is not (List<string> names, List<ModRecord> records))
                throw new Exception("PipeExpression: left side must evaluate to a record group (List<string>, List<ModRecord>)");
            right.SetTarget((names, records));
            right.Evaluate(r, locals);
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
        public override object EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null)
        {
            return new Func<object?[], object?>(args =>
            {
                if (args.Length != Parameters.Count)
                    throw new Exception(
                        $"Lambda expected {Parameters.Count} args");

                var lambdaLocals =
                    locals != null
                        ? new Dictionary<string, object?>(locals)
                        : new Dictionary<string, object?>();

                for (int i = 0; i < Parameters.Count; i++)
                    lambdaLocals[Parameters[i]] = args[i];

                return Body.Evaluate(r, lambdaLocals);
            });
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
            { "InspectField", args =>
                {

                    var (names,records) =ExpressionUtils.ExpectGroupRecord(args[0]);
                    string field=ExpressionUtils.ExpectString(args[1]);
                    foreach(var rec in records)
                    {
                        CoreUtils.Print($"record: {rec.StringId} {field}: {rec.GetFieldAsString(field)}");
                    }
                }
            },
            { "ShowRecordEvolution", args =>
                {
                    string stringid=ExpressionUtils.ExpectString(args[0]);
                    CoreUtils.Print(ReverseEngineerRepository.Instance.GetRecordEvolution(stringid),0);
                }
            },
            { "Stop",args=>
                {
                    Patcher.Instance.Stop();
                }
            },
            { "ApplyCurrentPatch", (args) =>
                {
                    (List<string> modnames,List<ModRecord> sources) =ExpressionUtils.ExpectGroupRecord(args[0]);
                    ReverseEngineer current=Patcher.Instance.currentRE!;
                    foreach(ModRecord record in sources) {
                        ModRecord? current_record =current.searchModRecordByStringId(record.StringId);
                        if(current_record != null) {
                            record.applyChangesFrom(current_record);
                        }
                    }
                }
            },
            { "PropagateExtraDataByField", (args) =>
                {
                    (List<string> modnames,List<ModRecord> sources) =ExpressionUtils.ExpectGroupRecord(args[0]);
                    string field = ExpressionUtils.ExpectString(args[1]);
                    string category = ExpressionUtils.ExpectString(args[2]);
                    Array? arr=null;
                    if (args.Count() > 3)
                    {
                        arr =ExpressionUtils.ExpectArray(args[3]);
                    }
                    Dictionary<string, Dictionary<string, int[]>> extrasByField = new();
                    ProgressController progress=ProgressController.Instance;

                    progress.Initialize(sources.Count*2);
                    int i=0;
                    foreach (var record in sources)
                    {
                        progress.Report(i++, $"record {i}:{record.Name}");
                        i++;
                        if (!record.HasField(field))
                            continue;

                        string key = record.GetFieldAsString(field)?.ToString() ?? "";
                        if(key== "")
                            continue;
                        if (!extrasByField.TryGetValue(key, out var extras))
                        {
                            extrasByField[key] = new Dictionary<string, int[]>();
                        }
                        Dictionary<string, int[]>? extra=record.GetExtraData(category);
                        if(extra==null)
                            continue;
                        foreach (var kv in extra)
                        {
                            extrasByField[key][kv.Key] = kv.Value;
                        }
                    }
                    foreach (var record in sources)
                    {
                        progress.Report(i++, $"record {i}:{record.Name}");
                        i++;
                        if (!record.HasField(field))
                            continue;   

                        string key = record.GetFieldAsObject(field)?.ToString() ?? "";

                        if (!extrasByField.TryGetValue(key, out var extras))
                            continue;
                        foreach (var kv in extras)
                        {
                            Patcher.Instance.currentRE!.AddExtraDataString(record,kv.Key, category, arr==null?[0,0,0]:kv.Value);
                        }
                    }
                    progress.Finish("PropagateExtraDataByField Done!");
                    Patcher.Instance.currentRE!.addDependencies(modnames.Where(m => !string.Equals(m, Patcher.Instance.currentRE!.modname, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList());
                }
            },
                { "ExpandExtraDataByField", (args) =>
                {
                    (List<string> modnames,List<ModRecord> sources) =ExpressionUtils.ExpectGroupRecord(args[0]);
                    string field = ExpressionUtils.ExpectString(args[1]);
                    string category = ExpressionUtils.ExpectString(args[2]);
                    Array? arr=null;
                    if (args.Count() > 3)
                    {
                        arr =ExpressionUtils.ExpectArray(args[3]);
                    }
                    Dictionary<string, Dictionary<string, int[]>> skeletonToExtras = new();

                    // Build initial map
                    foreach (var record in sources)
                    {
                        if (!record.HasField(field))
                            continue;
                        string skeleton = record.GetFieldAsObject(field)?.ToString() ?? "";
                        if(skeleton =="")
                            continue;
                        if (!skeletonToExtras.TryGetValue(skeleton, out var extras))
                        {
                            skeletonToExtras[skeleton] = new Dictionary<string, int[]>();
                        }
                        Dictionary<string, int[]>? extra=record.GetExtraData(category);
                        if(extra==null)
                            continue;
                        foreach (var kv in extra)
                        {
                            skeletonToExtras[skeleton][kv.Key] = arr==null?[0,0,0]:kv.Value;//kv.Value;
                        }
                    }

                    // Expand until stable
                    bool changed;

                    do
                    {
                        changed = false;

                        var skeletons = skeletonToExtras.Keys.ToList();

                        for (int i = 0; i < skeletons.Count; i++)
                        {
                            for (int j = i + 1; j < skeletons.Count; j++)
                            {
                                var extrasA = skeletonToExtras[skeletons[i]];
                                var extrasB = skeletonToExtras[skeletons[j]];

                                bool overlap = extrasA.Keys.Any(extrasB.ContainsKey);

                                if (!overlap)
                                    continue;

                                int beforeA = extrasA.Count;
                                int beforeB = extrasB.Count;

                                foreach (var kv in extrasB)
                                    extrasA[kv.Key] = kv.Value;

                                foreach (var kv in extrasA)
                                    extrasB[kv.Key] = kv.Value;

                                if (extrasA.Count != beforeA ||
                                    extrasB.Count != beforeB)
                                {
                                    changed = true;
                                }
                            }
                        }

                    } while (changed);

                    // Apply back to records
                    foreach (var record in sources)
                    {
                        if (!record.HasField(field))
                            continue;
                        string skeleton = record.GetFieldAsObject(field)?.ToString() ?? "";
                        if(skeleton =="")
                            continue;
                        if (!skeletonToExtras.TryGetValue(skeleton, out var extras))
                            continue;

                        foreach (var kv in extras)
                        {
                            Patcher.Instance.currentRE!.AddExtraDataString(record,kv.Key, category, kv.Value);
                        }
                    }
                }
            },
        };
            private static string getStringFromArgs(List<Expression<object>> args)
            {
                StringBuilder sb = new StringBuilder();
                foreach (var arg in args)
                {
                    sb.Append(arg.ToString() + "\n");
                }
                return sb.ToString();
            }


            public GlobalFunctionExpression(string name, List<Expression<object>> args)
            {
                this.name = name;
                this.args = args;
            }

            public override object EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null)
            {
                if (!globalFuncs.TryGetValue(name, out var func))
                    throw new Exception($"Unknown global function '{name}'");
                func(args);
                return true;
            }

            public override string ToString() => $"@{name}({string.Join(", ", args)})";
        }
    
}


    

  
