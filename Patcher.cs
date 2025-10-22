using KenshiCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using static KenshiPatcher.RecordProcedures;
using static System.Runtime.InteropServices.JavaScript.JSType;
using KenshiPatcher.ExpressionReader;

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
        public Dictionary<string, (List<string>,List<ModRecord>)> definitions;
        public Dictionary<string, Dictionary<string, string>> tables;
        private readonly string _definition = ":=";
        private readonly string _proc = "->";
        private readonly string _comment = ";";
        private readonly List<string> basemods = new() { "gamedata.base", "rebirth.mod","Newwworld.mod","Dialogue.mod" };
        private List<string>? assumedReqs = null;
        public ReverseEngineer? currentRE;
        public Patcher(Dictionary<ModItem, ReverseEngineer> modCache)
        {
            _engCache = modCache;
            definitions= new();
            tables = new();
            _instance = this;
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
            int lineNumber = 0;
            CoreUtils.StartLog(modName, dir);
            try
            {
                foreach (string rawLine in lines)
                {
                    lineNumber++;
                    string line = rawLine.Trim();
                    int commentIndex = line.IndexOf(_comment);
                    if (commentIndex >= 0)
                        line = line.Substring(0, commentIndex).Trim();

                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    if (line.Contains(_definition))
                    {
                        ParseDefinition(line);
                    }
                    else if (line.Contains(_proc))
                    {
                        ParseProcedure(line);                    
                    }
                    else
                    {
                        throw new FormatException($"Unrecognized syntax at line {lineNumber}: {line}");
                    }
                }
                savePatchedMod(path);
                MessageBox.Show($"{modName} patched!");
            }
            catch (Exception ex)
            {
                CoreUtils.Print($"[ERROR] {ex.Message}\n{ex.StackTrace}", 1);
            }
            finally
            {
                CoreUtils.EndLog("Patch execution summary saved.");
            }
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
            var tableMatch = Regex.Match(left, @"^(\w+)\s*\[\s*([^\]]+)\s*\]$");
            if (tableMatch.Success)
            {
                string tableName = tableMatch.Groups[1].Value;
                string key = tableMatch.Groups[2].Value.Trim();

                if (!tables.ContainsKey(tableName))
                    tables[tableName] = new Dictionary<string, string>();

                tables[tableName][key] = right;
                return; // done, no further processing
            }
            definitions.Add(left, GetGroup(right));
        }
        public void ParseProcedure(string text)
        {
            // Split at the _proc operator (e.g., "->")
            var procParts = text.Split(_proc, StringSplitOptions.RemoveEmptyEntries);
            if (procParts.Length != 2)
                throw new FormatException($"Invalid procedure syntax: '{text}'");

            string targetVar = procParts[0].Trim();
            string procCall = procParts[1].Trim(); // e.g., SetField(...)

            // Extract procedure name and raw arguments
            var match = Regex.Match(procCall, @"^(\w+)\((.*)\)$");
            if (!match.Success)
                throw new FormatException($"Invalid procedure call format: '{procCall}'");

            string procName = match.Groups[1].Value;
            string rawArgs = match.Groups[2].Value.Trim();

            // Lookup procedure
            if (!RecordProcedures.Procedures.TryGetValue(procName, out var proc))
                throw new FormatException($"Unknown procedure '{procName}'");

            // Get target records
            if (!definitions.TryGetValue(targetVar, out var targetList))
                throw new FormatException($"Unknown variable '{targetVar}'");

            // Split arguments respecting parentheses
            var argParts = SplitArgs(rawArgs);

            switch (proc.Signature)
            {
                case ProcSignature.TargetAndSource:
                    {
                        if (argParts.Length != 2)
                            throw new FormatException($"Invalid arguments for {procName}: {rawArgs}");

                        string sourceVar = argParts[0].Trim();
                        string category = argParts[1].Trim().Trim('"');

                        if (!definitions.TryGetValue(sourceVar, out var sourceList))
                            throw new FormatException($"Unknown variable '{sourceVar}'");

                        foreach (var t in targetList.Item2)
                            foreach (var s in sourceList.Item2)
                                proc.Func(currentRE!, t, s, category);

                        currentRE!.addReferences(sourceList.Item1.Distinct(StringComparer.Ordinal).ToList());
                        break;
                    }

                case ProcSignature.TargetAndString:
                    {
                        // Example: SetField("field name", expression)
                        if (argParts.Length != 2)
                            throw new FormatException($"Invalid arguments for {procName}: {rawArgs}");

                        string fieldName = argParts[0].Trim().Trim('"');
                        string exprText = argParts[1].Trim();

                        // Parse expression using your Parser
                        var parser = new Parser(exprText);
                        CoreUtils.Print("Parsing expression: " + exprText);
                        var exprFunc = parser.ParseValueExpression();
                        foreach (var t in targetList.Item2)
                        {
                            var value = exprFunc(t);
                            //CoreUtils.Print($"Record: cut_into_stun={t.GetFieldAsString("cut into stun")}, cut_def_bonus={t.GetFieldAsString("cut def bonus")}, level_bonus={t.GetFieldAsString("level bonus")}, material_type={t.GetFieldAsString("material type")}, class={t.GetFieldAsString("class")}");
                            proc.Func(currentRE!, t, t, $"\"{fieldName}\"{RecordProcedures.sep}{Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)}");
                        }
                        break;
                    }

                default:
                    throw new NotImplementedException($"Unhandled procedure signature: {proc.Signature}");
            }
            CoreUtils.Print($"dependencies: { string.Join(",",targetList.Item1.Distinct(StringComparer.Ordinal).ToList())}");
            currentRE!.addDependencies(targetList.Item1.Distinct(StringComparer.Ordinal).ToList());
        }

        // Helper to split procedure arguments while respecting nested parentheses
        private string[] SplitArgs(string input)
        {
            var args = new List<string>();
            int parenLevel = 0;
            int lastSplit = 0;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == '(') parenLevel++;
                else if (c == ')') parenLevel--;
                else if (c == ',' && parenLevel == 0)
                {
                    args.Add(input.Substring(lastSplit, i - lastSplit).Trim());
                    lastSplit = i + 1;
                }
            }

            // Add last argument
            if (lastSplit < input.Length)
                args.Add(input.Substring(lastSplit).Trim());

            return args.ToArray();
        }
        private List<ReverseEngineer> ParseModSelector(string selector)
        {
            if (selector.Equals("all", StringComparison.Ordinal))
                return _engCache.Values.ToList(); // all mods loaded
            var result = new List<ReverseEngineer>();
            // Use the shared helper to split safely
            var names = CoreUtils.SplitModList(selector)
                .ToHashSet(StringComparer.Ordinal);
            //foreach ( var name in names) { 
            //    MessageBox.Show("Mod in selector: " + name);
           // }
            foreach (var kvp in _engCache)
            {
                var modItem = kvp.Key;
                var re = kvp.Value;

                if (names.Contains(modItem.Name))
                    result.Add(re);
            }
            return result;
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
        public (List<string>,List<ModRecord>) GetGroup(string text)
        {
            //MessageBox.Show("Input: " + text);
            var group = new List<ModRecord>();

            // --- Step 1: extract first (...) for mod selector ---
            string? modSelector = ExtractParenthesesContent(ref text);
            if (modSelector == null)
                throw new FormatException("Missing mod selector (first parentheses).");

            List<ReverseEngineer> targetMods = ParseModSelector(modSelector);

            // --- Step 2: extract second (...) for record definition ---
            string? definition = ExtractParenthesesContent(ref text);
            if (definition == null)
                throw new FormatException($"Faulty record definition (second parentheses): {text}.");

            // --- Step 3: parse record definition ---
            var (mode, recordType, condition) = ParseRecordDefinition(definition);

            // --- Step 4: collect records based on condition ---
            var parser = new Parser(condition);
            CoreUtils.Print("Parsing condition: " + condition);
            var cond = parser.ParseValueExpression();
            Func<ModRecord, bool> predicate = r=>(bool)cond(r);

            var collected = new List<(ModRecord record, string sourceModName)>();

            foreach (var re in targetMods)
            {
                string modName = re.modname;
                var records = re.GetRecordsByTypeINMUTABLE(recordType)
                                .Select(r => (r, modName));

                collected.AddRange(records);
            }
            var (modNames, mergedRecords) = FilterUniqueRecordsByPreference(collected);
            var finalModNames = new List<string>();
            var finalRecords = new List<ModRecord>();

            for (int i = 0; i < mergedRecords.Count; i++)
            {
                var rec = mergedRecords[i];
                if (predicate(rec))
                {
                    finalRecords.Add(rec);
                    finalModNames.Add(modNames[i]);
                }
            }
            return (finalModNames, finalRecords);
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
                    CoreUtils.Print($"not found:{recList.ToArray()[0].record.StringId}");
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

            return null; // Unbalanced parentheses
        }
    }
}
