using KenshiCore;
using KenshiPatcher.ExpressionReader;
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace KenshiPatcher
{
    
    public class Patcher
    {

        private static Patcher? _instance;
        public static Patcher Instance
        {
            get
            {
                if (_instance == null)
                    throw new InvalidOperationException("Patcher instance has not been initialized.");
                return _instance;
            }
        }
        private readonly Dictionary<ModItem, ReverseEngineer> _engCache;
        public Dictionary<string, IExpression<object>> definitions=new();
        public Dictionary<string, Dictionary<string, IExpression<object>>> tables = new();
        private readonly string _definition = ":=";
        private readonly string _proc = "->";
        private readonly string _comment = ";";
        private readonly string _extraction = "<<<";
        private readonly string _globalfunc = "@";
        private bool stopping=false;
        private readonly List<string> basemods = new() { "gamedata.base", "rebirth.mod","Newwworld.mod","Dialogue.mod" };
        private List<string>? assumedReqs = null;
        public ReverseEngineer? currentRE;
        private static readonly Regex GroupPattern = new Regex(@"^\((?<mods>[\w.,*]+)\)\((?<body>[^)]*\|.*)\)$", RegexOptions.Compiled);
        private bool definitions_printed = false;
        public Patcher(Dictionary<ModItem, ReverseEngineer> modCache)
        {
            _engCache = modCache;
            definitions= new();
            tables = new();
            _instance = this;
        }
        public bool TryResolve(string name, out IExpression<object>? expr)
        {
            if (definitions.TryGetValue(name, out expr))
                return true;

            if (tables.TryGetValue(name, out var table))
            {
                expr = new Literal<object>(table);
                return true;
            }

            expr = null!;
            return false;
        }
        public void Define(string name, IExpression<object> expr)
        {
            definitions[name] = expr;
        }
        private void loadAssumedReqs()
        {
            if (assumedReqs != null)
                return;
            assumedReqs = new List<string>();
            foreach (var mod in _engCache.Keys)
            {
                if (basemods.Any(b => string.Equals(b, mod.Name, StringComparison.Ordinal)))
                {
                    if (_engCache.TryGetValue(mod, out var re))
                        assumedReqs.AddRange(re.GetModsNewRecords());
                }
            }

            // Deduplicate (case-insensitive)
            assumedReqs = assumedReqs
                .Distinct(StringComparer.Ordinal)
                .ToList();
            
        }
        public void Reset()
        {
            definitions.Clear();
            tables.Clear();
            currentRE = null;
        }
        public void runPatch(string path)
        {
            Reset();
            loadAssumedReqs();
            loadUnPatchedMod(path);
            string dir = Path.GetDirectoryName(path)!;
            string modName = Path.GetFileNameWithoutExtension(path);
            string patchPath = Path.Combine(dir, modName + ".patch");
            var lines = File.ReadAllLines(patchPath);
            CoreUtils.StartLog(modName, dir);
            try
            {
                ProcessPatchLines(lines);
                if (stopping)
                {
                    stopping = false;
                    CoreUtils.Print("Stopping due to Global Function", 1);
                    return;
                }
                savePatchedMod(path);
                MessageBox.Show($"{modName} patched!");
            }
            catch (Exception ex)
            {
                HandlePatchError(ex);
            }
            finally
            {
                CoreUtils.EndLog("Patch execution summary saved.");
            }
        }
        private void printAllDefinitions()
        {
            if (definitions_printed)
                return;
            definitions_printed = true;
            foreach (var def in definitions)
            {
                var expr = def.Value;
                var func = expr.GetFunc();
                var result = func(null); // or some context ModRecord

                if (result is ValueTuple<List<string>, List<ModRecord>> group)
                {
                    CoreUtils.Print($"Definition: {def.Key} has this many records: {group.Item2.Count}");
                }
                else
                {
                    CoreUtils.Print($"Definition: {def.Key} = {result}");
                }
            }
        }
        private void ProcessPatchLines(IEnumerable<string> lines)
        {
            int lineNumber = 0;
            foreach (string rawLine in lines)
            {
                if (stopping)
                {
                    return;
                }
                lineNumber++;
                string line = CleanLine(rawLine);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                ProcessPatchLine(line, lineNumber);
            }
        }
        private string CleanLine(string rawLine)
        {
            string line = rawLine.Trim();
            int commentIndex = line.IndexOf(_comment);
            if (commentIndex >= 0)
                line = line.Substring(0, commentIndex).Trim();
            return line;
        }
        private void ProcessPatchLine(string line, int lineNumber)
        {
            if (line.Contains(_definition))
                ParseDefinition(line);
            else if (line.Contains(_extraction))
                ParseExtraction(line);
            else if (line.StartsWith(_globalfunc))
                ParseProcedure(line);
            else if (line.Contains(_proc))
            {
                printAllDefinitions();
                ParseProcedure(line);
            }
            else
                throw new FormatException($"Unrecognized syntax at line {lineNumber}: {line}");
        }
        private void HandlePatchError(Exception ex)
        {
            CoreUtils.Print($"[ERROR] {ex.Message}\n{ex.StackTrace}", 1);
        }
        private void TrySetValue(string left, IExpression<object> expr)
        {
            // Detect if left side looks like table[index]
            var match = Regex.Match(left, @"^(\w+)\s*\[\s*([^\]]+)\s*\]$");
            if (match.Success)
            {
                string tableName = match.Groups[1].Value;
                string key = match.Groups[2].Value.Trim();

                if (!tables.TryGetValue(tableName, out var table))
                {
                    table = new Dictionary<string, IExpression<object>>();
                    tables[tableName] = table;
                }

                table[key] = expr;
                CoreUtils.Print($"[TrySetValue] Stored expression in table '{tableName}[{key}]'");
                return;
            }
            definitions[left] = expr;
            CoreUtils.Print($"[TrySetValue] Stored expression in definitions['{left}']");
        }
        private void loadUnPatchedMod(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Mod file not found: {path}");
            string dir = Path.GetDirectoryName(path)!;
            string modName = Path.GetFileNameWithoutExtension(path);
            string patchPath = Path.Combine(dir, modName + ".unpatched");

            if (!File.Exists(patchPath))
                File.Copy(path, patchPath, overwrite: true);
            currentRE = new ReverseEngineer();
            currentRE.LoadModFile(patchPath);
        }
        private void savePatchedMod(string path)
        {
            currentRE!.SaveModFile(path);
        }
        public void ParseDefinition(string text)
        {
            var def = text.Split(_definition);
            if (def.Length != 2)
                throw new FormatException($"Invalid patch definition format: '{text}'");

            string left = def[0].Trim();
            string right = def[1].Trim();

            TrySetValue(left, ParseExpression(right));
        }
        public static IExpression<object> ParseExpression(string text)
        {
            text = text.Trim();

            // 1. Check for record-group using regex
            var match = GroupPattern.Match(text);
            if (match.Success)
            {
                var group = Patcher.Instance.GetGroup(text);
                return new RecordGroupExpression(group);
            }

            // 2. Check for string literal
            if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length >= 2)
            {
                return new Literal<object>(text.Substring(1, text.Length - 2));
            }

            // 3. Numeric literal
            if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var dval))
                return new Literal<object>(dval);
            if (long.TryParse(text, System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture, out var lval))
                return new Literal<object>(lval);

            // 4. Table index: table[index]
            int bracket = text.IndexOf('[');
            if (bracket > 0 && text.EndsWith("]"))
            {
                string tableName = text.Substring(0, bracket).Trim();
                string indexText = text.Substring(bracket + 1, text.Length - bracket - 2).Trim();
                IExpression<object> tableExpr = new IndexExpression.TableNameExpression(tableName);
                IExpression<object> indexExpr = ParseExpression(indexText);
                return new IndexExpression(tableExpr, indexExpr);
            }

            // 5. Default: treat as variable reference
            return new VariableExpression(text);
        }

        public void ParseExtraction(string text)
        {
            var def = text.Split(_extraction);
            if (def.Length != 2)
                throw new FormatException($"Invalid extraction definition format: '{text}'");

            string left = def[0].Trim();  // new definition name
            string right = def[1].Trim();

            TrySetValue(left, GetExtraction(right));
        }
        public RecordGroupExpression GetExtraction(string text)
        {
            // Expecting text like: (oldGroup|condition)
            var match = Regex.Match(text, @"^\s*\(\s*(?<source>[A-Za-z0-9_]+)\s*\|\s*(?<condition>.+?)\s*\)\s*$");
            if (!match.Success)
                throw new FormatException($"Invalid extraction syntax: '{text}'");

            string oldDefinitionSource = match.Groups["source"].Value;
            string condition = match.Groups["condition"].Value;

            // Make sure the source definition exists
            if (!definitions.TryGetValue(oldDefinitionSource, out var expr))
                throw new InvalidOperationException($"Unknown source definition '{oldDefinitionSource}'.");

            // Get the actual (names, records)
            var func = expr.GetFunc();
            var (modNames, modRecords) = ((List<string>, List<ModRecord>))func(null);

            // Parse the condition
            CoreUtils.Print($"Parsing extraction condition: {condition}");
            var parser = new Parser(condition);
            var cond = parser.ParseValueExpression();
            Func<ModRecord, bool> predicate = r => (bool)cond(r);

            // Perform extraction
            var extractedNames = new List<string>();
            var extractedRecords = new List<ModRecord>();
            var indexesToRemove = new List<int>();

            for (int i = 0; i < modRecords.Count; i++)
            {
                if (predicate(modRecords[i]))
                {
                    extractedRecords.Add(modRecords[i]);
                    extractedNames.Add(modNames[i]);
                    indexesToRemove.Add(i);
                }
            }

            // Remove from source (reverse order)
            for (int i = indexesToRemove.Count - 1; i >= 0; i--)
            {
                modRecords.RemoveAt(indexesToRemove[i]);
                modNames.RemoveAt(indexesToRemove[i]);
            }

            // Update source definition
            definitions[oldDefinitionSource] = new RecordGroupExpression((modNames, modRecords));

            // Return the new group
            return new RecordGroupExpression((extractedNames, extractedRecords));
        }
        public void ParseProcedure(string text)
        {

            CoreUtils.Print($"Executing procedure: {text}");
            var parser = new Parser(text);
            var expr = parser.ParseExpression();
            expr.GetFunc()(null);
        }
        private List<ReverseEngineer> ParseModSelector(string selector)
        {
            if (selector.Equals("all", StringComparison.Ordinal))
                return _engCache.Values.ToList(); // all mods loaded
            var result = new List<ReverseEngineer>();
            // Use the shared helper to split safely
            var names = CoreUtils.SplitModList(selector)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var kvp in _engCache)
            {
                var modItem = kvp.Key;
                var re = kvp.Value;

                if (names.Contains(modItem.Name))
                    result.Add(re);
            }
            return result;
        }
        public string GetModRecordEvolution(string StringId)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var kvp in _engCache)
            {
                string modname = kvp.Key.Name;
                ReverseEngineer re = kvp.Value;
                ModRecord? r=re.searchModRecordByStringId(StringId);
                if (r != null)
                    sb.AppendLine($"{modname} => {r.ToString()}");
            }
            return sb.ToString();
        }
        public void Stop() {
            stopping = true;
        }
        private (string mode, string recordType, string condition) ParseRecordDefinition(string def)
        {
            var match = Regex.Match(def, @"^([AE]):([A-Z_]+)\|(.+)$");
            if (!match.Success)
                throw new FormatException($"Invalid record definition: ({def})");

            string mode = match.Groups[1].Value;
            string recordType = match.Groups[2].Value;
            string condition = match.Groups[3].Value.Trim();

            return (mode, recordType, condition);
        }
        public (List<string>, List<ModRecord>) GetGroup(string text)
        {
            var modSelector = ExtractRequiredParentheses(ref text, "mod selector");
            var definition = ExtractRequiredParentheses(ref text, "record definition");

            var (mode, recordType, condition) = ParseRecordDefinition(definition);
            var mods = ParseModSelector(modSelector);

            var predicate = BuildRecordPredicate(condition);
            var collected = CollectRecords(mods, recordType);
            var (modNames, mergedRecords) = FilterUniqueRecordsByPreference(collected);

            return FilterRecordsByPredicate(modNames, mergedRecords, predicate, mode);
        }
        private string ExtractRequiredParentheses(ref string text, string name)
        {
            string? content = ExtractParenthesesContent(ref text);
            if (content == null)
                throw new FormatException($"Missing {name} parentheses in: {text}");
            return content;
        }
        private Func<ModRecord, bool> BuildRecordPredicate(string condition)
        {
            var parser = new Parser(condition);
            CoreUtils.Print("Parsing condition: " + condition);
            var cond = parser.ParseValueExpression();
            return r => (bool)cond(r);
        }
        private IEnumerable<(ModRecord record, string modName)> CollectRecords(IEnumerable<ReverseEngineer> mods, string recordType)
        {
            foreach (var re in mods)
            {
                string modName = re.modname;
                foreach (var record in re.GetRecordsByTypeINMUTABLE(recordType))
                    yield return (record, modName);
            }
        }
        private (List<string>, List<ModRecord>) FilterRecordsByPredicate(
    List<string> modNames, List<ModRecord> records, Func<ModRecord, bool> predicate, string mode)
        {
            var finalNames = new List<string>();
            var finalRecords = new List<ModRecord>();

            for (int i = 0; i < records.Count; i++)
            {
                if (predicate(records[i]))
                {
                    finalRecords.Add(records[i]);
                    finalNames.Add(modNames[i]);
                    if (mode == "E")
                        break;
                }
            }

            return (finalNames, finalRecords);
        }
        private (List<string> modNames, List<ModRecord> records)
FilterUniqueRecordsByPreference(IEnumerable<(ModRecord record, string sourceModName)> records)
        {
            var resultModNames = new List<string>();
            var resultRecords = new List<ModRecord>();

            foreach (var group in records.GroupBy(x => x.record.StringId))
            {
                var recList = group.ToList();

                // --- Pass 1: find first "new" record and deep clone it ---
                var creatorPair = recList.FirstOrDefault(x => x.record.isNew());

                ModRecord merged;
                string creatorMod;

                if (creatorPair != default)
                {
                    merged = creatorPair.record.deepClone();
                    creatorMod = creatorPair.sourceModName;
                    foreach (var (record, _) in recList)
                    {
                        if (!object.ReferenceEquals(record, merged))
                            merged.applyChangesFrom(record);
                    }
                    resultRecords.Add(merged);
                    resultModNames.Add(creatorMod);
                }
                else
                {
                    CoreUtils.Print($"not found new record of:{recList.ToArray()[0].record.StringId}");
                }
            }

            return (resultModNames, resultRecords);
        }
        private string? ExtractParenthesesContent(ref string text)
        {
            text = text.Trim();
            if (!text.StartsWith("(")) return null;

            int depth = 0;
            int start = -1;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '(')
                {
                    if (depth == 0)
                        start = i + 1;
                    depth++;
                }
                else if (text[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        string content = text.Substring(start, i - start);
                        // return remainder too
                        text = text.Substring(i + 1).Trim();
                        return content.Trim();
                    }
                }
            }

            return null;
        }
    }
}
