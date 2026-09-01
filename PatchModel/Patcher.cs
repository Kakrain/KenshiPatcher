using KenshiPatcher.ExpressionReader;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using KenshiCore.UI;
using KenshiCore.Mods;
using KenshiCore.ReverseEngineering;
using KenshiCore.Utilities;
using KenshiPatcher.Forms;
namespace KenshiPatcher.PatchModel
{
    public sealed class Patcher
    {
        private static Patcher? _instance;
        public static Patcher Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new Patcher();
                }
                    //throw new InvalidOperationException("Patcher instance has not been initialized.");
                return _instance;
            }
        }
        public Dictionary<string, Expression<object>> definitions=new();
        public Dictionary<string, Dictionary<string, Expression<object>>> tables = new();
        private Queue<string> linesToProcess;
        private List<PatcherNode> nodes;
        public ReverseEngineer? currentRE;
        private string modname = "";
        public string NextLine()
        {
            if (linesToProcess.Count == 0)
                throw new InvalidOperationException("No more lines to process.");
            return linesToProcess.Dequeue();
        }
        public bool HasMoreLines()
        {
            return linesToProcess.Count > 0;
        }
        private Patcher()
        {
            definitions = new();
            tables = new();
            nodes = new();
            _instance = this;
            linesToProcess = new();
            _resolveGlobalCache.Clear();
        }
        public void Reset()
        {
            linesToProcess = new();
            nodes.Clear();
            definitions.Clear();
            tables.Clear();
            currentRE = null;
            //_resolveGlobalCache.Clear(); // Do not clear the global cache on reset; it should persist across patch runs.
        }
        public async Task<bool> RunPatchAsync(string path)
        {
            return await Task.Run(() => runPatch(path));
        }
        public bool runPatch(string path)
        {
            Reset();
            loadUnPatchedMod(path);

            ReverseEngineerRepository.Instance.ReloadMod(path,true);

            string dir = Path.GetDirectoryName(path)!;
            string modName = Path.GetFileNameWithoutExtension(path);
            this.modname = modName+".mod";
            string patchPath = Path.Combine(dir, modName + ".patch");
            linesToProcess = new Queue<string>(File.ReadAllLines(patchPath));
            CoreUtils.StartLog(modName, dir);
            try
            {
                ProcessPatchLines();
                foreach (PatcherNode node in nodes)
                {
                    node.Execute();
                }
                savePatchedMod(path);
                ReverseEngineerRepository.Instance.ReloadMod(path);
                return true;
            }
            catch (Exception ex)
            {
                HandlePatchError(ex);
                return false;
            }
            finally
            {
                CoreUtils.EndLog("Patch execution summary saved.");
            }
        
        }
        private void ProcessPatchLines()
        {
            while(HasMoreLines())
            {
                var node = PatcherNodeFactory.TryCreate(NextLine());
                if (node != null)
                    nodes.Add(node);
            }
        }
        private void HandlePatchError(Exception ex)
        {
            CoreUtils.Prompt($"[ERROR] {ex.Message}\n{ex.StackTrace}");
        }
        public void SetTable(string name, Dictionary<string, Expression<object>> table)
        {
            tables[name] = table;
        }
        public void SetDefinition(string name, Expression<object> expr)
        {
            definitions[name] = expr;
        }
        public void AddToTable(string tableName, string key, Expression<object> expr)
        {
            if (!tables.TryGetValue(tableName, out var table))
            {
                table = new Dictionary<string, Expression<object>>();
                tables[tableName] = table;
            }
            table[key] = expr;
        }
        public void TrySetValue(string left, Expression<object> expr)
        {
            if (expr is FunctionExpression<object>)
                {
                    CoreUtils.Print($"[TrySetValue] Executing side-effect function for '{left}'");
                    var result = expr.Evaluate(null);
                    if (result is Dictionary<string, Expression<object>> table)
                    {
                        tables[left] = table;
                        CoreUtils.Print($"[TrySetValue] Stored generated table '{left}'");
                        return;
                    }
                    if (result is Expression<object> exprResult)
                    {
                        definitions[left] = exprResult;
                    }
                    else
                    {
                        CoreUtils.Print($"converted to Literal : {result!.ToString()}");
                        definitions[left] = new Literal<object>(result!);
                    }
                
                return;
                }
            // Detect if left side looks like table[index]
            var match = Regex.Match(left, @"^(\w+)\s*\[\s*([^\]]+)\s*\]$");
            if (match.Success)
            {
                string tableName = match.Groups[1].Value;
                string key = match.Groups[2].Value.Trim();

                if (!tables.TryGetValue(tableName, out var table))
                {
                    table = new Dictionary<string, Expression<object>>();
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
        public Expression<object>? GetDefinitionIfExist(string name)
        {
            if (definitions.TryGetValue(name, out var expr))
                return expr;
            return null;
        }
        private List<ReverseEngineer> ParseModSelector(string selector,string? currentPatchName = null)
        {
            return ReverseEngineerRepository.Instance.ParseModSelector(selector,currentPatchName);
        }
        private (string mode, string recordType, string condition) ParseRecordDefinition(string def)
        {
            //var match = Regex.Match(def, @"^([AE]):([A-Z_]+)\|(.+)$");
            var match = Regex.Match(def, @"^([AE]|\d+):([A-Z_]+)\|(.+)$");
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
            List<ReverseEngineer> mods = ParseModSelector(modSelector, this.modname);

            var predicate = BuildRecordPredicate(condition);
            var collected = CollectRecords(mods, recordType);
            (List<string> modNames, List<ModRecord> mergedRecords) = MergeRecords(collected);
            ( modNames, mergedRecords) =ReverseEngineerRepository.FilterRemovedRecords(modNames, mergedRecords);
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
            return r => (bool)cond(r,null!);
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
            List<string> modNames,
            List<ModRecord> records,
            Func<ModRecord, bool> predicate,
            string mode)
        {
            var finalNames = new List<string>();
            var finalRecords = new List<ModRecord>();

            // Determine numeric limit if mode is a number
            int limit = mode == "A" ? int.MaxValue :
                        mode == "E" ? 1 :
                        int.TryParse(mode, out var n) ? n :
                        int.MaxValue;

            var progress = ProgressController.Instance;
            progress.Initialize(records.Count);

            for (int i = 0; i < records.Count; i++)
            {
                if (predicate(records[i]))
                {
                    finalRecords.Add(records[i]);
                    finalNames.Add(modNames[i]);

                    if (finalRecords.Count >= limit)
                        break;
                }
                progress.Report(i, $"Filtering records {i+1}/{records.Count}");
            }
            progress.Finish();
            return (finalNames, finalRecords);
        }
        private (List<string> modNames, List<ModRecord> records) MergeRecords(IEnumerable<(ModRecord record, string sourceModName)> records)
        {
            var resultModNames = new List<string>();
            var resultRecords = new List<ModRecord>();

            var grouped = records.GroupBy(x => x.record.StringId).ToList();

            ProgressController progress = ProgressController.Instance;
            progress.Initialize(grouped.Count);
            int i = 0;
            foreach (var group in grouped)
            {
                var recList = group.ToList();
                int creatorIndex = -1;

                for (int j = recList.Count - 1; j >= 0; j--)
                {
                    if (recList[j].record.isNew())
                    {
                        creatorIndex = j;
                        break;
                    }
                }
                if (creatorIndex >= 0)
                {
                    var creatorPair = recList[creatorIndex];

                    var merged = creatorPair.record.deepClone();
                    var creatorMod = creatorPair.sourceModName;

                    // Apply only changes that happen after the creator.
                    for (int j = creatorIndex + 1; j < recList.Count; j++)
                    {
                        merged.applyChangesFrom(recList[j].record);
                    }

                    resultRecords.Add(merged);
                    resultModNames.Add(creatorMod);
                }
                else
                {
                    CoreUtils.Print($"not found new record of:{recList[0].record.StringId}");
                }
                i++;
                progress.Report(i, $"Merging records {i}/{grouped.Count}");
            }

            progress.Finish();
            return (resultModNames, resultRecords);
        }
        private static readonly Dictionary<(string, bool), ModRecord?> _resolveGlobalCache = new();
        public static ModRecord? Resolve(string id, bool getEarly = false)
        {
            var key = (id, getEarly);
            if (_resolveGlobalCache.TryGetValue(key, out var cached))
                return cached;
            var baseRec = ReverseEngineerRepository.Instance.searchModRecordByStringIdGlobally(id, getEarly);

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
