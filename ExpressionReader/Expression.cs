using KenshiCore.Mods;
using KenshiCore.ReverseEngineering;
using KenshiCore.UI;
using KenshiCore.Utilities;
using KenshiPatcher.Forms;
using KenshiPatcher.PatchModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
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
        public static Literal<object> ExpectLiteral(Expression<object> expression)
        {
            if (expression is Literal<object> literal)
                return literal;
            throw new FormatException($"Expression is expected to be a literal expression: {expression.ToString()}");
        }
        public static (List<string>, List<ModRecord>) ExpectGroupRecord(Expression<object> expression, ModRecord? r = null, Dictionary<string, object?>? locals = null)
        {
            object o = expression.Evaluate(r, locals)!;
            if (o is (List<string> names, List<ModRecord> records))
                return (names, records);
            if (o is RecordGroupExpression group)
                return group.group;
            throw new FormatException($"Expression is expected to be a group. " + $"Expression={expression}, " + $"EvaluatedType={o?.GetType().FullName}, " + $"EvaluatedValue={o}");
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
        private T value;

        public Literal(T value) => this.value = value;
        public void setValue(T newValue)
        {
            value = newValue;
        }

        public override T EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null) => value;

        public override string ToString()
        {
            if (value is System.Collections.IEnumerable enumerable && value is not string)
            {
                var items = enumerable.Cast<object?>().Select(x => x?.ToString() ?? "null");
                return $"Literal<{value.GetType()}> :[{string.Join(", ", items)}]";
            }
            return $"Literal<{value?.GetType()}> :{value?.ToString() ?? "null"}";
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
        const double eq_tolerance = 1e-9;
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
                        return Math.Abs(ValueCaster.ToDouble(l) - ValueCaster.ToDouble(r)) < eq_tolerance;
                        //return Math.Abs(ValueCaster.ToDouble(l) - ValueCaster.ToDouble(r)) < double.Epsilon;

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
    public class FunctionExpression<T> : Expression<T>
    {
        public readonly List<Expression<object>> arguments;
        private readonly string functionname;
        private readonly Func<ModRecord, Dictionary<string, object?>?, T> func;
        private static readonly Random getrandom = new Random();
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
                { "Length", (r,locals, args) =>
                    {
                        Array arr =ExpressionUtils.ExpectArray(args[0],r,locals);
                        return (T)Convert.ChangeType(arr.Length, typeof(T))!;
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
                { "RegexMatch", (r,locals,args) =>
                    {
                        string input = ExpressionUtils.ExpectString(args[0], r, locals);
                        string pattern = ExpressionUtils.ExpectString(args[1], r, locals);
                        return (T)Convert.ChangeType(Regex.IsMatch(input, pattern),typeof(T))!;
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
                {"Any", (r,locals, args) =>
                    {
                        Array array=ExpressionUtils.ExpectArray(args[0],r,locals);
                        Func<object, bool> istrue = ExpressionUtils.ExpectLambda<object, bool>(args[1], r,locals);
                        foreach (var item in array)
                        {
                            if(istrue(item))
                                return (T) Convert.ChangeType(true, typeof(T))!;
                        }
                        return (T) Convert.ChangeType(false, typeof(T))!;
                    }
                },
                {"All", (r,locals, args) =>
                    {
                        Array array=ExpressionUtils.ExpectArray(args[0],r,locals);
                        Func<object, bool> istrue = ExpressionUtils.ExpectLambda<object, bool>(args[1], r,locals);
                        foreach (var item in array)
                        {
                            if(!istrue(item))
                                return (T) Convert.ChangeType(false, typeof(T))!;
                        }
                        return (T) Convert.ChangeType(true, typeof(T))!;
                    }
                },
                {"SumInt", (r,locals, args) =>
                    {
                        Array array=ExpressionUtils.ExpectArray(args[0],r,locals);
                        Func<object, int> getvalue = ExpressionUtils.ExpectLambda<object, int>(args[1], r,locals);
                        int sum = 0;
                        foreach (var item in array)
                        {
                            sum += getvalue(item);
                        }
                        return (T) Convert.ChangeType(sum, typeof(T))!;
                    }
                },
                {"SumFloat", (r,locals, args) =>
                    {
                        Array array=ExpressionUtils.ExpectArray(args[0],r,locals);
                        Func<object, float> getvalue = ExpressionUtils.ExpectLambda<object, float>(args[1], r,locals);
                        float sum = 0;
                        foreach (var item in array)
                        {
                            sum += getvalue(item);
                        }
                        return (T) Convert.ChangeType(sum, typeof(T))!;
                    }
                },
                { "GetSkeletonLink", (r,locals, args) =>
                    {
                        string filepath=ExpressionUtils.ExpectString(args[0],r,locals);
                        string skeletonLink=FileAnalyzer.Instance.getSkeletonLink(filepath);
                        return (T)Convert.ChangeType(skeletonLink, typeof(T))!;
                    }
                },
                { "Intersects", (r,locals, args) =>
                    {
                        string filepath=ExpressionUtils.ExpectString(args[0],r,locals);
                        Array p1 =ExpressionUtils.ExpectArray(args[1],r,locals);
                        Array p2 =ExpressionUtils.ExpectArray(args[2],r,locals);
                        bool fast=false;
                        if(args.Count > 3)
                        {
                            fast = ExpressionUtils.ExpectBool(args[3],r,locals);
                        }

                        bool intersects=FileAnalyzer.Instance.Intersects(filepath, p1, p2,fast);
                        return (T)Convert.ChangeType(intersects, typeof(T));
                    }
                },
                { "IsInfluencedByBone", (r,locals, args) =>
                    {
                        string filepath=ExpressionUtils.ExpectString(args[0],r,locals);
                        int bone =ExpressionUtils.ExpectInt(args[1],r,locals);


                        bool influenced=FileAnalyzer.Instance.IsInfluencedByBone(filepath, bone);
                        return (T)Convert.ChangeType(influenced, typeof(T));
                    }
                },
                { "GetBoneInfluence", (r,locals, args) =>
                    {
                        string filepath=ExpressionUtils.ExpectString(args[0],r,locals);
                        int bone =ExpressionUtils.ExpectInt(args[1],r,locals);


                        float influence=FileAnalyzer.Instance.GetBoneInfluence(filepath, bone);
                        return (T)Convert.ChangeType(influence, typeof(T));
                    }
                },
                { "GetBoneInfluenceInfo", (r,locals, args) =>
                    {
                        string filepath=ExpressionUtils.ExpectString(args[0],r,locals);
                        string info=FileAnalyzer.Instance.GetBoneInfluenceInfo(filepath);
                        return (T)Convert.ChangeType(info, typeof(T));
                    }
                },
                { "GetIntersectionRatio", (r,locals, args) =>
                    {
                        string filepath=ExpressionUtils.ExpectString(args[0],r,locals);
                        Array p1 =ExpressionUtils.ExpectArray(args[1],r,locals);
                        Array p2 =ExpressionUtils.ExpectArray(args[2],r,locals);
                        int samples=-1;
                        if(args.Count > 3)
                        {
                            samples = ExpressionUtils.ExpectInt(args[3],r,locals);
                        }
                        double intersectionRatio=FileAnalyzer.Instance.GetIntersectionRatio(filepath, p1, p2, samples);
                        return (T)Convert.ChangeType(intersectionRatio, typeof(T));
                    }
                },
                { "Replace", (r,locals, args) =>
                    {
                        string main_string=ExpressionUtils.ExpectString(args[0],r,locals);
                        string search_string=ExpressionUtils.ExpectString(args[1],r,locals);
                        string replace_string=ExpressionUtils.ExpectString(args[2],r,locals);

                         bool ignoreCase =
                        args.Count > 3 &&
                        ExpressionUtils.ExpectBool(args[3], r, locals);

                        return (T)Convert.ChangeType(main_string.Replace(search_string, replace_string,ignoreCase? StringComparison.OrdinalIgnoreCase: StringComparison.Ordinal), typeof(T))!;
                    }
                },{
                    "ExtractChildrenUntil", (r, locals, args) =>
                    {
                        (List<string> modNames, List<ModRecord> sources) =
                            ExpressionUtils.ExpectGroupRecord(args[0]);
                        var matchExpr = args[1];
                        var stopExpr = args[2];
                        string category = args.Count > 3
                            ? ExpressionUtils.ExpectString(args[3], r)
                            : "lines";
                        bool pruneBranch = args.Count > 4
                            ? ExpressionUtils.ExpectBool(args[4], r, locals)
                            : false;
                        
                        int maxVisits = args.Count > 5
                            ? ExpressionUtils.ExpectInt(args[5], r)
                            : 50000;
                        bool getEarly = args.Count > 6
                            ? ExpressionUtils.ExpectBool(args[6], r, locals)    
                            : true;


                        ProgressController progress = ProgressController.Instance;
                        List<string> resultModNames = new();
                        List<ModRecord> resultRecords = new();

                        progress.Initialize(sources.Count);
                        int i= 0;
                        foreach (var (modName, source) in modNames.Zip(sources))
                        {
                            HashSet<string> visitedIds = new HashSet<string>(StringComparer.Ordinal);
                            Dictionary<string, ModRecord?> resolveCache = new Dictionary<string, ModRecord?>(StringComparer.Ordinal);
                            //int visits = 0;

                            void Traverse(ModRecord rec, int visits)
                            {
                                if (++visits > maxVisits)
                                    return;

                                if (!visitedIds.Add(rec.StringId))
                                    return;

                                if (Convert.ToBoolean(stopExpr.Evaluate(rec, locals)))
                                    return;
                                if(Convert.ToBoolean(matchExpr.Evaluate(rec, locals)))
                                {
                                    resultModNames.Add(modName);
                                    resultRecords.Add(rec);

                                    if (pruneBranch)
                                        return;
                                }

                                Dictionary<string,int[]>? children = rec.GetExtraData(category);

                                if (children == null)
                                    return;

                                foreach (var kv in children)
                                {
                                    var child = Get(kv.Key);
                                    if (child != null)
                                        Traverse(child,visits+1);
                                }
                            }

                            ModRecord? Get(string id)
                            {
                                if (!resolveCache.TryGetValue(id, out var rec))
                                {
                                    rec = Patcher.Resolve(id, getEarly);
                                    resolveCache[id] = rec;
                                }

                                return rec;
                            }

                            var kids = source.GetExtraData(category);
                            if (kids != null)
                            {
                                foreach (var kv in kids)
                                {
                                    var child = Get(kv.Key);
                                    if (child != null)
                                        Traverse(child,1);
                                }
                            }
                            i++;
                            progress.Report(i, $"ExtractChildrenUntil mod {i}");
                        }

                        progress.Finish();
                        return (T)(object)(new RecordGroupExpression((resultModNames, resultRecords)));
                    }
                },
            { "ChanceOfExtraData", (r,locals, args) =>
            {
                string category = ExpressionUtils.ExpectString(args[0],r,locals);
                Expression<object> selector = args[1];
                int value_index = ExpressionUtils.ExpectInt(args[2],r,locals);
                Func<ModRecord,bool> isPool= rr=>true;

                if (args.Count > 3)
                    isPool= rr=>ExpressionUtils.ExpectBool(args[3], rr, locals);

                int total = 0;
                int poolTotal = 0;
                Dictionary<string,int[]>? extraData = r.GetExtraData(category);
                double result=0.0;
                if(extraData == null)
                    return (T)Convert.ChangeType(result, typeof(T))!;
                foreach (var (strid, vars) in extraData)
                {
                    ModRecord? rec = Patcher.Resolve(strid);
                    if(rec == null)
                        continue;
                    if(isPool(rec))
                        poolTotal+= vars[value_index];
                    if(ExpressionUtils.ExpectBool(selector, rec, locals))
                        total+= vars[value_index];
                }
                if (poolTotal > 0)
                    result = 100.0 * total / poolTotal;
                return (T)Convert.ChangeType(result, typeof(T))!;
            }},
            { "GetExtraDataArguments", (r,locals, args) =>
            {
                string category = ExpressionUtils.ExpectString(args[0], r, locals);

                Dictionary<string, int[]>? extraData = r.GetExtraData(category);

                if (extraData == null)
                    return (T)(object)Array.Empty<int[]>();

                var result = new List<int[]>();

                foreach (var (strid, values) in extraData)
                {
                    ModRecord? record = Patcher.Resolve(strid);
                    if (record == null || record.isRemoved())
                        continue;
                    result.Add(values);
                }

                return (T)(object)result.ToArray();
            }},
            { "GetExtraDataRecords", (r,locals, args) =>
            {
                string category = ExpressionUtils.ExpectString(args[0],r,locals);
                List<string> res_modnames = new List<string>();
                List<ModRecord> res_records = new List<ModRecord>();

                Dictionary<string,int[]>? extraData = r.GetExtraData(category);
                if(extraData == null)
                    return (T)(object)(new RecordGroupExpression((res_modnames, res_records)));
                foreach (var (strid, vars) in extraData)
                {
                    ModRecord? rec = Patcher.Resolve(strid);
                    if(rec == null || rec.isRemoved())
                        continue;
                    res_modnames.Add("gamedata.base");
                    res_records.Add(rec);
                }
                return (T)(object)(new RecordGroupExpression((res_modnames, res_records)));
            }},
            { "GetFieldAsArray", (r,locals, args) =>
            {
                (List<string> modNames, List<ModRecord> sources) = ExpressionUtils.ExpectGroupRecord(args[0],r,locals);
                string field = ExpressionUtils.ExpectString(args[1],r,locals);

                var result = new List<object>();
                foreach (var record in sources)
                {
                    result.Add(record.GetFieldAsObject(field)!);
                }
                return (T)(object)result.ToArray();
            }},
            { "SortArray", (r,locals, args) =>
            {
                Array arr =ExpressionUtils.ExpectArray(args[0],r,locals);
                Array.Sort((object[])arr);
                return (T)(object)arr;
            }},
            { "BitwiseAnd", (r,locals, args) =>
            {
                int a = ExpressionUtils.ExpectInt(args[0],r,locals);
                int b = ExpressionUtils.ExpectInt(args[1],r,locals);
                return (T)Convert.ChangeType(a & b, typeof(T))!;
            }},
            { "BitwiseOr", (r,locals, args) =>
            {
                int a = ExpressionUtils.ExpectInt(args[0],r,locals);
                int b = ExpressionUtils.ExpectInt(args[1],r,locals);
                return (T)Convert.ChangeType(a | b, typeof(T))!;
            }},
            { "AddFlag", (r,locals, args) =>
            {
                return functions!["BitwiseOr"](r,locals, args);
            }},
            { "RemoveFlag", (r,locals, args) =>
            {
                int a = ExpressionUtils.ExpectInt(args[0],r,locals);
                int b = ExpressionUtils.ExpectInt(args[1],r,locals);
                return (T)Convert.ChangeType(a & ~b, typeof(T))!;
            }},
            { "HasFlag", (r,locals, args) =>
            {
                int a = ExpressionUtils.ExpectInt(args[0],r,locals);
                int b = ExpressionUtils.ExpectInt(args[1],r,locals);
                return (T)Convert.ChangeType((a & b) == b, typeof(T))!;
            }},
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
            return $"ArrayExpression[{string.Join(", ", elements)}]";
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
                    return r.HasField(field); //&& !string.IsNullOrEmpty(r.GetFieldAsString(field));

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
                    return !string.IsNullOrEmpty(field) && r.HasField(field) && r.BoolFields!=null && r.BoolFields[field];
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
                                    var child = Patcher.Resolve(kv.Key, getEarly);
                                    if (child != null && Detect(child))
                                        return true;
                                }
                            }

                            visitedIds.Remove(rec.StringId);
                            return false;
                        }

                        return Detect(r);
                    }
            },
                {
                "isIn", (r,locals, args) =>
                    {
                        var (modnames, sources) = ExpressionUtils.ExpectGroupRecord(args[0],r,locals);
                        return sources.Any(rec=>rec.StringId == r.StringId);
                    }
                }

            };
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
                    rec = Patcher.Resolve(id, getEarly);
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
        ModRecord? current = null;

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

        /*public override object EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null)
        {
            current = r;
            var targetVal = target.Evaluate(r, locals)!;
            var indexVal = index.Evaluate(r, locals)!;
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
        }*/
        public override object EvaluateTyped( ModRecord? r, Dictionary<string, object?>? locals = null)
        {
            current = r;

            object? targetVal;

            if (target is TableNameExpression tableName)
            {
                // A named definition can itself be indexable.
                if (Patcher.Instance.definitions.TryGetValue(tableName.Name, out var definition))
                {
                    targetVal = definition.Evaluate(r, locals);
                }
                else
                {
                    targetVal = tableName.Evaluate(r, locals);
                }
            }
            else
            {
                targetVal = target.Evaluate(r, locals);
            }

            var indexVal = index.Evaluate(r, locals)!;

            if (targetVal is Dictionary<string, Expression<object>> dict)
            {
                string key = indexVal.ToString()!;

                if (dict.TryGetValue(key, out var exprvalue))
                    return exprvalue.Evaluate(r, locals)!;

                throw new Exception($"Key '{key}' not found");
            }

            if (targetVal is System.Collections.IList list)
            {
                int idx = Convert.ToInt32(indexVal);

                if (idx < 0 || idx >= list.Count)
                    throw new Exception($"Array index {idx} out of range");

                return list[idx]!;
            }

            // Existing table lookup
            var targetStr = targetVal?.ToString();
            var indexStr = indexVal?.ToString();

            if (targetStr == null || indexStr == null)
                throw new Exception("IndexExpression: null table name or key");

            if (!Patcher.Instance.tables.TryGetValue(targetStr, out var table))
                throw new Exception($"Table '{targetStr}' not found");

            if (!table.TryGetValue(indexStr, out var value))
                throw new Exception($"Key '{indexStr}' not found in table '{targetStr}'");

            return value is Expression<object> expr
                ? expr.Evaluate(r, locals)!
                : value;
        }
        public override string ToString()
        {
            if (target is TableNameExpression tableexp && current != null)
            {
                Patcher.Instance.tables.TryGetValue(tableexp.Name, out var table);
                string strindex= index.Evaluate(current)!.ToString()!;
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
            }},
            { "AddExtraDataFromOther", (target, source, args) =>
            {
                string category_source = ExpressionUtils.ExpectString(args[1], target);
                string category_target = ExpressionUtils.ExpectString(args[2], target);
                int[]? arrayvar = null;
                if (args.Count>3)
                {
                    var result = args[3].Evaluate(target);
                    if (result is int[] arr)
                        arrayvar = arr;
                    else if (result is object[] objArr)
                    {
                        arrayvar = objArr.Select(o => (int)Convert.ChangeType(o!, typeof(int))).ToArray();
                    }
                    else
                        throw new FormatException($"Invalid array returned for category: {result}");
                }
                Dictionary<string,int[]>? extraData = source.GetExtraData(category_source);
                if(extraData == null)
                    return null;
                foreach (var (strid, vars) in extraData)
                {
                    ModRecord? rec = Patcher.Resolve(strid);
                    if(rec == null || rec.isRemoved())
                        continue;
                    Patcher.Instance.currentRE!.AddExtraData(target,rec,category_target,arrayvar==null?null:arrayvar);
                }
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
                    Patcher.Instance.currentRE!.addReferences(modnames.Where(m => !string.Equals(m, currentMod, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList());
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
                Patcher.Instance.currentRE!.addDependencies(leftNames.Where(m => !string.Equals(m, currentMod, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList());
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
        public static readonly Dictionary<string, Action<PatcherGlobalFunction>> globalParsers = new()
        {
            ["ForEach"] = node =>
            {
                while (Patcher.Instance.HasMoreLines())
                {
                    var child = PatcherNodeFactory.TryCreate(Patcher.Instance.NextLine());

                    if (child is null)
                        continue;

                    if (child is PatcherGlobalFunction p_global && p_global.Name == "End")
                    {
                        return;
                    }

                    node.Children.Add(child);
                }
                throw new SyntaxErrorException($"Missing @End for @ForEach starting with: {node.Line}");
            },
            ["Stop"] = node =>
            {
                while (Patcher.Instance.HasMoreLines())
                {
                    Patcher.Instance.NextLine();
                }
            },
            ["If"] = node =>
            {
                while (Patcher.Instance.HasMoreLines())
                {
                    var child = PatcherNodeFactory.TryCreate(Patcher.Instance.NextLine());

                    if (child is null)
                        continue;

                    if (child is PatcherGlobalFunction p_global && p_global.Name == "End")
                    {
                        return;
                    }

                    node.Children.Add(child);
                }
                throw new SyntaxErrorException($"Missing @End for @If starting with: {node.Line}");
            }
        };
        public static readonly Dictionary<string, Action<PatcherGlobalFunction>> globalExecutors = new()
        {
            { "Print", node =>
                {
                    List<Expression<object>> args = node.global!.GetArgs();
                    CoreUtils.Prompt(getStringFromArgs(args));
                }
            },
            { "Debug", node =>
                {
                    List<Expression<object>> args = node.global!.GetArgs();
                    CoreUtils.Print(getStringFromArgs(args));
                }
            },
            { "InspectRecord", node =>
                {
                    List<Expression<object>> args = node.global!.GetArgs();
                    var (names,records) =ExpressionUtils.ExpectGroupRecord(args[0]);
                    string stringid=ExpressionUtils.ExpectString(args[1]);
                    ModRecord? found=records.Find(rec=>rec.StringId==stringid);
                    if(found == null)
                        throw new Exception($"mod with stringId '{stringid}' not found");
                    CoreUtils.Print(CoreUtils.GetFormatter().getDataAsString(found),0);
                }
            },
            { "InspectField", node =>
                {
                    List<Expression<object>> args = node.global!.GetArgs();
                    var (names,records) =ExpressionUtils.ExpectGroupRecord(args[0]);
                    string field=ExpressionUtils.ExpectString(args[1]);
                    foreach(var rec in records)
                    {
                        CoreUtils.Print($"record: {rec.StringId} {field}: {rec.GetFieldAsString(field)}");
                    }
                }
            },
            { "InspectExtraData", node =>
                {
                    List<Expression<object>> args = node.global!.GetArgs();
                    var (names,records) =ExpressionUtils.ExpectGroupRecord(args[0]);
                    ReverseEngineerRepository RER=ReverseEngineerRepository.Instance;
                    string? category=null;
                    if (args.Count() > 1)
                    {
                        category =ExpressionUtils.ExpectString(args[1]);
                    }
                    ProgressController progress=ProgressController.Instance;
                    foreach(var rec in records)
                    {
                        Dictionary<string, int[]>? extraData = rec.GetExtraData(category);
                        CoreUtils.Print($"record: {rec.ToString()}");
                        int c=extraData?.Count ?? 0;
                        progress.Initialize(c);
                        CoreUtils.Print($"Extra Data Count: {c}");
                        CoreUtils.Print($"Extra Data Category: {category ?? "all"}");
                        foreach (var kv in extraData ?? new Dictionary<string, int[]>())
                        {
                            progress.ReportStep($"record: {rec.ToString()} extra data: {kv.Key} => [{string.Join(",", kv.Value)}]");
                            ModRecord? sourceRecord = RER.searchModRecordByStringIdGlobally(kv.Key,true);
                            CoreUtils.Print($"---{sourceRecord?.ToString() ?? kv.Key +"not found"}: [{string.Join(",", kv.Value)}]");
                        }
                        progress.Finish($"Finished inspecting record: {rec.ToString()}");
                    }
                }
            },
            { "ShowRecordEvolution", node =>
                {
                    List<Expression<object>> args = node.global!.GetArgs();
                    string stringid=ExpressionUtils.ExpectString(args[0]);
                    string? field=null;
                    if(args.Count>1)
                        field=ExpressionUtils.ExpectString(args[1]);
                    CoreUtils.Print(ReverseEngineerRepository.Instance.GetRecordEvolution(stringid,field),0);

                }
            },
            { "Stop",node =>{}
            },
            { "AskConfig",node =>
                {
                    List<Expression<object>> args = node.global!.GetArgs();
                    Literal<object> literal=ExpressionUtils.ExpectLiteral(args[0]);
                    string question =ExpressionUtils.ExpectString(args[1]);

                    KPatcherConfigForm configform=KPatcherConfigForm.Instance;
                    configform.AddOption(literal,question);
                }
            },
            { "ShowConfig",node =>
                {
                    KPatcherConfigForm configform=KPatcherConfigForm.Instance;
                    configform.Show();
                    
                }
            },
            { "If",node=>
                {
                    List<Expression<object>> args = node.global!.GetArgs();
                    bool condition = ExpressionUtils.ExpectBool(args[0]);
                    if (condition)
                    {
                        foreach (PatcherNode child in node.Children)
                        {
                            child.Execute();
                        }
                    }
                }
            },
                { "ForEach",node=> {
                    List<Expression<object>> args = node.global!.GetArgs();
                    Array array = ExpressionUtils.ExpectArray(args[0]);
                    string variableName = ExpressionUtils.ExpectString(args[1]);
                    foreach (object item in array)
                    {
                        Patcher.Instance.TrySetValue(variableName, new Literal<object>(item));
                        foreach (PatcherNode child in node.Children)
                        {
                            child.Execute();
                        }
                    }
                }
            },{ "End",node=> { }
            },
            { "ApplyCurrentPatch", (node) =>
                {
                    List<Expression<object>> args = node.global!.GetArgs();
                    (List<string> modnames,List<ModRecord> sources) =ExpressionUtils.ExpectGroupRecord(args[0]);
                    ReverseEngineer current=Patcher.Instance.currentRE!;
                    foreach(ModRecord record in sources) {
                        ModRecord? current_record =current.modData.GetRecordByStringId(record.StringId);
                        if(current_record != null) {
                            record.applyChangesFrom(current_record);
                        }
                    }
                }
            },
            { "PropagateExtraDataByField", (node) =>
                {
                    List<Expression<object>> args = node.global!.GetArgs();
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
                { "ExpandExtraDataByField", (node) =>
                {
                    List<Expression<object>> args = node.global!.GetArgs();
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
                { "SetRangeToField",node=>
                {
                    List<Expression<object>> args = node.global!.GetArgs();
                    var (modNames, records) =ExpressionUtils.ExpectGroupRecord(args[0]);
                    string field = ExpressionUtils.ExpectString(args[1]);
                    int start = ExpressionUtils.ExpectInt(args[2]);
                    int step = args.Count >= 4 ? ExpressionUtils.ExpectInt(args[3]) : 1;
                    for (int i = 0; i < records.Count; i++)
                    {
                        records[i].SetField(field,""+(start + i * step));
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
        public List<Expression<object>> GetArgs() => args;
            public override object EvaluateTyped(ModRecord? r, Dictionary<string, object?>? locals = null)
            {
                if (!globalExecutors.TryGetValue(name, out var func)) throw new Exception($"Unknown global function '{name}'");
                //func(args);
                return true;
            }

            public override string ToString() => $"@{name}({string.Join(", ", args)})";
        }
    
}


    

  
