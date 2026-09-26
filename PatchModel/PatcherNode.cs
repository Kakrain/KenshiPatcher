using KenshiCore.Mods;
using KenshiCore.UI;
using KenshiCore.Utilities;
using KenshiPatcher.ExpressionReader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace KenshiPatcher.PatchModel
{
    public abstract class PatcherNode
    {
        public static readonly string _definition = ":=";
        public static readonly string _extraction = "<<<";
        public static readonly string _procedure = "->";
        public static readonly string _procedure2 = "~>";
        public static readonly string _globalfunc = "@";

        public string Line { get; }
        public PatcherNode(string line)
        {
            this.Line = line;
            Parse();
        }
        public abstract void Parse();
        public abstract void Execute();
        public static void ExecuteFromZero(string line) {
            CoreUtils.Print($"Executing procedure from Zero: {line}");
            (new Parser(line)).ParseExpression().Evaluate(null);
        }
    }
    public class PatcherDefinition : PatcherNode
    {
        private string left = "";
        private string right = "";
        public PatcherDefinition(string line) : base(line){}
        public override void Parse()
        {
            var def = Line.Split(_definition);
            if (def.Length != 2)
                throw new FormatException($"Invalid patch definition format: '{Line}'");

            left = def[0].Trim();
            right = def[1].Trim();
        }
        public override void Execute()
        {
            TrySetValue(left, ParseExpression(right));
        }
        public void TrySetValue(string left, Expression<object> expr)
        {
            if (expr is FunctionExpression<object> fe)
            {
                CoreUtils.Print($"[TrySetValue] Executing side-effect function for '{left}'");
                var result = expr.Evaluate(null);
                if (result is Dictionary<string, Expression<object>> table)
                {
                    Patcher.Instance.SetTable(left, table);
                    CoreUtils.Print($"[TrySetValue] Stored generated table '{left}'");
                    return;
                }
                if (result is Expression<object> exprResult)
                {
                    Patcher.Instance.SetDefinition(left, exprResult);
                }
                else
                {
                    CoreUtils.Print($"converted to Literal : {result!.ToString()}");
                    Patcher.Instance.SetDefinition(left, new Literal<object>(result!));
                }

                return;
            }
            // Detect if left side looks like table[index]
            var match = Regex.Match(left, @"^(\w+)\s*\[\s*([^\]]+)\s*\]$");
            if (match.Success)
            {
                string tableName = match.Groups[1].Value;
                string key = match.Groups[2].Value.Trim();
                Patcher.Instance.AddToTable(tableName, key, expr);
                CoreUtils.Print($"[TrySetValue] Stored expression in table '{tableName}[{key}]'");
                return;
            }

            Patcher.Instance.SetDefinition(left, expr);
            CoreUtils.Print($"[TrySetValue] Stored expression in definitions['{left}']");
        }
        private static Expression<object> ParseExpression(string text)
        {
            text = text.Trim();
            CoreUtils.Print($"Parsing expression: {text}");
            if (LooksLikeFunctionCall(text) || text.StartsWith("["))
            {
                CoreUtils.Print($"Parsing function call expression: {text}");
                var parser = new Parser(text);
                return parser.ParseExpression();
            }
            // 1. Check for string literal
            if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length >= 2)
            {
                return new Literal<object>(text.Substring(1, text.Length - 2));
            }
            if (bool.TryParse(text, out var bval))
                return new Literal<object>(bval);
            if (long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var lval))
                return new Literal<object>(lval);
            // 2. Numeric literal
            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dval))
                return new Literal<object>(dval);


            // 3. Table index: table[index]
            int bracket = text.IndexOf('[');
            if (bracket > 0 && text.EndsWith("]"))
            {
                string tableName = text.Substring(0, bracket).Trim();
                string indexText = text.Substring(bracket + 1, text.Length - bracket - 2).Trim();
                Expression<object> tableExpr = new IndexExpression.TableNameExpression(tableName);
                Expression<object> indexExpr = ParseExpression(indexText);
                return new IndexExpression(tableExpr, indexExpr);
            }

            if (Patcher.Instance.definitions.TryGetValue(text, out var definition))
            {
                if (definition is RecordGroupExpression group_exp)
                {
                    List<string> names = group_exp.group.Item1;
                    List<ModRecord> records = group_exp.group.Item2;

                    return new RecordGroupExpression((new List<string>(names), new List<ModRecord>(records)));
                }
                return definition;
            }
            var group = Patcher.Instance.GetGroup(text);
            CoreUtils.Print($"Parsed record group with {group.Item2.Count} records.");
            return new RecordGroupExpression(group);
        }
        private static bool LooksLikeFunctionCall(string text)
        {
            // Must start with identifier
            int i = 0;
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;

            int start = i;
            if (i >= text.Length || !char.IsLetter(text[i])) return false;

            i++;
            while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                i++;

            // Must be immediately followed by '('
            if (i >= text.Length || text[i] != '(') return false;

            // Must be a known function
            string name = text.Substring(start, i - start);
            return FunctionExpression<object>.functions.ContainsKey(name);
        }
    }
    public class PatcherExtraction : PatcherNode
    {
        private string left = "";
        private string right = "";
        public PatcherExtraction(string line) : base(line){}
        public override void Parse()
        {
            var def = Line.Split(_extraction);
            if (def.Length != 2)
                throw new FormatException($"Invalid extraction definition format: '{Line}'");

            left = def[0].Trim();  // new definition name
            right = def[1].Trim();
        }
        public override void Execute()
        {
            Patcher.Instance.TrySetValue(left, GetExtraction(right));
        }
        private RecordGroupExpression GetExtraction(string text)
        {
            // Expecting text like: (oldGroup|condition)
            var match = Regex.Match(text, @"^\s*\(\s*(?<source>[A-Za-z0-9_]+)\s*\|\s*(?<condition>.+?)\s*\)\s*$");
            if (!match.Success)
                throw new FormatException($"Invalid extraction syntax: '{text}'");

            string oldDefinitionSource = match.Groups["source"].Value;
            string condition = match.Groups["condition"].Value;

            // Make sure the source definition exists
            Expression<object>? expr = Patcher.Instance.GetDefinitionIfExist(oldDefinitionSource);
            if (expr==null)
                throw new InvalidOperationException($"Unknown source definition '{oldDefinitionSource}'.");

            // Get the actual (names, records)
            var (modNames, modRecords) = ((List<string>, List<ModRecord>))expr.Evaluate(null)!;//func

            // Parse the condition
            CoreUtils.Print($"Parsing extraction condition: {condition}");
            var parser = new Parser(condition);
            var cond = parser.ParseValueExpression();
            Func<ModRecord, bool> predicate = r => (bool)cond(r, null!);

            // Perform extraction
            var extractedNames = new List<string>();
            var extractedRecords = new List<ModRecord>();
            var indexesToRemove = new List<int>();

            var progress = ProgressController.Instance;
            progress.Initialize(modRecords.Count);
            for (int i = 0; i < modRecords.Count; i++)
            {
                progress.Report(i, $"Extracting {i}/{modRecords.Count}");
                if (predicate(modRecords[i]))
                {
                    extractedRecords.Add(modRecords[i]);
                    extractedNames.Add(modNames[i]);
                    indexesToRemove.Add(i);
                }
            }
            progress.Finish();

            // Remove from source (reverse order)
            for (int i = indexesToRemove.Count - 1; i >= 0; i--)
            {
                modRecords.RemoveAt(indexesToRemove[i]);
                modNames.RemoveAt(indexesToRemove[i]);
            }

            // Update source definition
            Patcher.Instance.SetDefinition(oldDefinitionSource, new RecordGroupExpression((modNames, modRecords)));
            CoreUtils.Print($"Extracted {extractedRecords.Count} of {modRecords.Count + extractedRecords.Count} records from '{oldDefinitionSource}' into new group.");
            // Return the new group
            return new RecordGroupExpression((extractedNames, extractedRecords));
        }

    }
    public class PatcherProcedure : PatcherNode
    {
        public PatcherProcedure(string line) : base(line){}
        public override void Parse()
        {
        }
        public override void Execute()
        {
            ExecuteFromZero(Line);
        }
    }
    public class PatcherGlobalFunction : PatcherNode
    {
        public string Name => ExtractFunctionName(Line); //{ get; }
        public GlobalFunctionExpression? global;
        public List<PatcherNode> Children { get; } = new();
        public PatcherGlobalFunction(string line) : base(line) { }
        public override void Parse()
        {
            if (GlobalFunctionExpression.globalParsers.TryGetValue(Name, out var parser))
            {
                parser(this);
                return;
            }
            if(!GlobalFunctionExpression.globalExecutors.ContainsKey(Name))
            {
                throw new FormatException($"Unknown global function: {Name}");
            }
        }
        private static string ExtractFunctionName(string line)
        {
            int startIndex = line.IndexOf('@') + 1;
            int endIndex = line.IndexOf('(', startIndex);
            if (endIndex == -1) endIndex = line.Length;
            return line.Substring(startIndex, endIndex - startIndex).Trim();
        }
        public override void Execute()
        {
            var expression = new Parser(Line).ParseExpression();
            if (expression is not GlobalFunctionExpression global) throw new FormatException($"Expected global function: {Line}");
            if (GlobalFunctionExpression.globalExecutors.TryGetValue(Name, out var function))
            {
                this.global = global;
                function(this);
                return;
            }
        }
    }
}