using KenshiCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KenshiPatcher
{
    
    public class Patcher
    {
        //public delegate bool RecordCondition(ModRecord record);
        private readonly Dictionary<ModItem, ReverseEngineer> _engCache;
        public Dictionary<string, (List<string>,List<ModRecord>)> definitions;
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
        public void runPatch(string path)
        {
            loadAssumedReqs();
            loadUnPatchedMod(path);
            string dir = Path.GetDirectoryName(path)!;
            string modName = Path.GetFileNameWithoutExtension(path);
            string patchPath = Path.Combine(dir, modName + ".patch");
            var lines = File.ReadAllLines(patchPath);
            int lineNumber = 0;

            foreach (string rawLine in lines)
            {
                lineNumber++;
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(_comment))
                    continue;
                try
                {
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
                catch (Exception ex)
                {
                    MessageBox.Show($"Error in patch at line {lineNumber}:\n{line}\n{ex.Message}",
                        "Patch Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            savePatchedMod(path);
            MessageBox.Show($"{modName} patched!");
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
            definitions.Add(def[0], GetGroup(def[1]));
        }
        public void ParseProcedure(string text)
        {
            // Split at the _proc operator
            var procParts = text.Split(_proc, StringSplitOptions.RemoveEmptyEntries);
            if (procParts.Length != 2)
                throw new FormatException($"Invalid procedure syntax: '{text}'");

            string targetVar = procParts[0].Trim();
            string procCall = procParts[1].Trim();

            // Parse procedure name and its argument
            var match = Regex.Match(procCall, @"^(\w+)\(([^,]+)\s*,\s*""([^""]+)""\)$");
            if (!match.Success)
                throw new FormatException($"Invalid procedure call format: '{procCall}'");

            string procName = match.Groups[1].Value;
            string sourceVar = match.Groups[2].Value.Trim();
            string category = match.Groups[3].Value.Trim();

            // Get the procedure function
            if (!RecordProcedures.Procedures.TryGetValue(procName, out var func))
                throw new FormatException($"Unknown procedure '{procName}'");

            // Get the ModRecord lists from definitions
            if (!definitions.TryGetValue(targetVar, out var targetList))
                throw new FormatException($"Unknown variable '{targetVar}'");

            if (!definitions.TryGetValue(sourceVar, out var sourceList))
                throw new FormatException($"Unknown variable '{sourceVar}'");

            foreach(ModRecord t in targetList.Item2)
            {
                foreach(ModRecord s in sourceList.Item2)
                {
                    func(currentRE!, t, s, category);
                }
            }
            currentRE!.addDependencies(targetList.Item1.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            currentRE!.addReferences(sourceList.Item1.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }
        private List<ReverseEngineer> ParseModSelector(string selector)
        {
            if (selector.Equals("all", StringComparison.OrdinalIgnoreCase))
                return _engCache.Values.ToList(); // all mods loaded
            var result = new List<ReverseEngineer>();
            var names = selector
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

            // --- Step 4: parse condition ---
            var parser = new Parser(condition);
            Condition cond = parser.ParseExpression();
            Func<ModRecord, bool> predicate = r => cond.Evaluate(r);



            var collected = new List<(ModRecord record, string sourceModName)>();

            foreach (var re in targetMods)
            {
                string modName = re.modname;
                var matches = re.GetRecordsByTypeINMUTABLE(recordType)
                                .Where(r => r.isNew() && predicate(r))
                                .Select(r => (r, modName));

                collected.AddRange(matches);
            }
            return FilterUniqueRecordsByPreference(collected);
        }
        private (List<string> modNames, List<ModRecord> records)
    FilterUniqueRecordsByPreference(
        IEnumerable<(ModRecord record, string sourceModName)> records)
        {
            var resultModNames = new List<string>();
            var resultRecords = new List<ModRecord>();

            foreach (var group in records.GroupBy(x => x.record.StringId))
            {
                var recs = group.ToList();

                // 1️ Prefer: record.GetModName() is in assumedReqs
                var preferred = recs.FirstOrDefault(x =>
                    assumedReqs!.Contains(x.record.GetModName(), StringComparer.OrdinalIgnoreCase)//OrdinalIgnoreCase
                );

                // 2️ Next: sourceModName == record's mod name
                if (preferred.Equals(default((ModRecord, string))))
                {
                    preferred = recs.FirstOrDefault(x =>
                        string.Equals(x.sourceModName, x.record.GetModName(), StringComparison.OrdinalIgnoreCase)//OrdinalIgnoreCase
                    );
                }

                // 3️ Fallback: last record (latest override)
                if (preferred.Equals(default((ModRecord, string))))
                {
                    preferred = recs.Last();
                }

                resultRecords.Add(preferred.record);
                resultModNames.Add(preferred.sourceModName);
            }

            return (resultModNames, resultRecords);
        }
        /*private (List<string>,List<ModRecord>) FilterUniqueRecordsByPreference(IEnumerable<(ModRecord record, string sourceModName)> records)
        {
            var resultModNames = new List<string>();
            var resultRecords = new List<ModRecord>();

            foreach (var group in records.GroupBy(x => x.record.StringId))
            {
                var recs = group.ToList();
                var preferred = recs.Cast<(ModRecord record, string sourceModName)?>().FirstOrDefault(x => string.Equals(x?.sourceModName, x?.record.GetModName(), StringComparison.OrdinalIgnoreCase)) ?? recs.Last(); // fallback: last one
                resultRecords.Add(preferred.record);
                resultModNames.Add(preferred.sourceModName);
            }

            return (resultModNames, resultRecords);
        }*/
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
