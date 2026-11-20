using KenshiCore.Mods;
using KenshiCore.ReverseEngineering;
using KenshiCore.UI;
using KenshiCore.Utilities;
using KenshiPatcher.ExpressionReader;
using KenshiPatcher.Forms;
using KenshiPatcher.Meta;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using static System.Net.Mime.MediaTypeNames;
namespace KenshiPatcher.PatchModel
{
    public sealed class Patcher
    {
        private const string firstMod = "gamedata.base";
        private static Patcher? _instance;
        public static Patcher Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new Patcher();
                }
                return _instance;
            }
        }
        public Dictionary<string, Expression<object>> definitions=new();
        public Dictionary<string, Dictionary<string, Expression<object>>> tables = new();
        private Queue<string> linesToProcess;
        private List<PatcherNode> nodes;
        public ReverseEngineer? currentRE;
        private MetaReader metareader = new MetaReader();
        private ReverseEngineer? existingPatch = null;
        private string modname = "";
        private List<string>? modsToPatch = null;//means patch all mods.
        public bool RestrictToPatchMods = false;
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
            modsToPatch = null;
            definitions = new();
            tables = new();
            nodes = new();
            _instance = this;
            linesToProcess = new();
            _resolveGlobalCache.Clear();
        }
        public void Reset()
        {
            modsToPatch = null;
            linesToProcess = new();
            nodes.Clear();
            definitions.Clear();
            tables.Clear();
            currentRE = null;
            //_resolveGlobalCache.Clear(); // Do not clear the global cache on reset; it should persist across patch runs.
        }
        public async Task<bool> RunPatchAsync(string path, bool isUpdate)
        {
            return await Task.Run(() =>isUpdate&&CanUpdate(path)? RunUpdate(path): RunFresh(path));
        }

        private bool CanUpdate(string path)
        {
            string dir = Path.GetDirectoryName(path)!;
            string modName = Path.GetFileNameWithoutExtension(path);

            string unpatchedPath =Path.Combine(dir, modName + ".unpatched");

            string metaPath =Path.Combine(dir, modName + ".meta");

            return File.Exists(unpatchedPath)&& File.Exists(metaPath);
        }

        private bool RunFresh(string path)
        {
            try
            {
                PreparePatch(path);

                ReverseEngineerRepository.Instance.ReloadMod(path, true);
                ProcessPatchLines();
                ExecuteNodes();

                SaveFreshResult(path);

                return true;
            }
            catch (Exception ex)
            {
                HandlePatchError(ex);
                return false;
            }
        }

        private bool RunUpdate(string path)
        {
            try
            {
                PreparePatch(path);

                ReverseEngineer initial = ReverseEngineerRepository.Instance.LoadMod(Path.ChangeExtension(path, ".unpatched"))!;

                initial.MergeReverseEngineer(ReverseEngineerRepository.Instance.LoadMod(path)!);

                ReverseEngineerRepository.Instance.SetMod(initial, Path.GetFileName(path));
                MetaFile currentMFile = MetaFile.getMetaFileUntil(modname);
                string metapath = Path.ChangeExtension(path, ".meta");//GetMetaPath(path);

                PrepareUpdate(metapath, currentMFile);
                if (modsToPatch!.Count == 1)
                {
                    CoreUtils.Print("METAFILE UP TO DATE skipping");
                    return true;
                }
                ProcessPatchLines();
                ExecuteNodes();

                var duplicates = currentRE!.modData.GetRecords().GroupBy(r => r.StringId).Where(g => g.Count() > 1).ToList();

                foreach (var group in duplicates)
                {
                    CoreUtils.Print($"DUPLICATE StringId: {group.Key}");

                    foreach (var record in group)
                        CoreUtils.Print($"  Type={record.RecordType}, Id={record.Id}, Name={record.Name}");
                }




                SaveUpdateResult(path, metapath, currentMFile);

                return true;
            }
            catch (Exception ex)
            {
                HandlePatchError(ex);
                return false;
            }
        }
        private void SaveFreshResult(string path)
        {
            new MetaWriter().WriteMetaFile(Path.ChangeExtension(path, ".meta"), MetaFile.getMetaFileUntil(modname));

            savePatchedMod(path);
            ReverseEngineerRepository.Instance.ReloadMod(path);
        }
        private void SaveUpdateResult(string path,string metapath,MetaFile currentMFile)
        {
            if (CoreUtils.toggles["update_delta"])
            {
                string deltaPath = Path.Combine(Path.GetDirectoryName(path)!,Path.GetFileNameWithoutExtension(path) + "_delta.mod");
                savePatchedMod(deltaPath);
            }

            if (existingPatch != null)
            {
                existingPatch.MergeReverseEngineer(currentRE!);
                currentRE = existingPatch;
            }

            savePatchedMod(path);
            MetaFile mergedMeta = MetaFile.Merge( metareader.mfile!, currentMFile);

            new MetaWriter().WriteMetaFile(metapath, mergedMeta);

            ReverseEngineerRepository.Instance.ReloadMod(path);
        }
        private bool PrepareUpdate(string metapath, MetaFile currentMFile)
        {
            if (!metareader.Read(metapath) || metareader.mfile == null)
                return true;

            modsToPatch = MetaFile.GetModsToBePatched(metareader.mfile,currentMFile);
            existingPatch = ReverseEngineerRepository.Instance.LoadMod(Path.ChangeExtension(metapath, ".mod"));
            currentRE!.modData.Header!.lastStringIdNr= existingPatch?.modData.Header?.lastStringIdNr ?? currentRE!.modData.Header!.lastStringIdNr;
            return modsToPatch.Count != 1;
        }
        private void PreparePatch(string path)
        {
            Reset();

            loadUnPatchedMod(path);

            string dir = Path.GetDirectoryName(path)!;
            string modName = Path.GetFileNameWithoutExtension(path);

            modname = modName + ".mod";

            string patchPath = Path.Combine(dir, modName + ".patch");

            linesToProcess = new Queue<string>( File.ReadAllLines(patchPath));

            CoreUtils.StartLog(modName, dir);
        }
        private void ExecuteNodes()
        {
            ProcessPatchLines();

            foreach (PatcherNode node in nodes)
                node.Execute();
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
            currentRE = new ReverseEngineer(modName+".mod");
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
        private (string mode, string recordType, string condition, bool makeWhole) ParseRecordDefinition(string def)
        {
            //var match = Regex.Match(def, @"^([AE]|\d+):([A-Z_]+)\|(.+)$");
            var match = Regex.Match(def, @"^([AE]|\d+):([A-Z_]+)(\+)?\|(.+)$");
            if (!match.Success)
                throw new FormatException($"Invalid record definition: ({def})");

            string mode = match.Groups[1].Value;
            string recordType = match.Groups[2].Value;
            bool makeWhole = match.Groups[3].Success;
            string condition = match.Groups[4].Value.Trim();

            return (mode, recordType, condition, makeWhole);
        }
        public (List<string>, List<ModRecord>) GetGroup(string text)
        {
            var modSelector = ExtractRequiredParentheses(ref text, "mod selector");
            var definition = ExtractRequiredParentheses(ref text, "record definition");

            var (mode, recordType, condition, makeWhole) = ParseRecordDefinition(definition);
            List<ReverseEngineer> mods = ParseModSelector(modSelector, this.modname);
            if (modsToPatch!= null && RestrictToPatchMods)
            {
                mods = mods.Where(r => modsToPatch!.Contains(r.modname)).ToList();
            }
            var predicate = BuildRecordPredicate(condition);
            var collected = CollectRecords(mods, recordType);
            (List<string> modNames, List<ModRecord> mergedRecords) = MergeRecords(collected, makeWhole);
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
                foreach (var record in re.modData.GetRecordsByType(recordType)) { 
                    //if (!ForceAllMods && existingPatch != null && existingPatch.modData.ContainsRecord(record.StringId))
                    //{
                    //    continue;
                    //}
                    yield return (record, modName);
                }
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
        private (List<string> modNames, List<ModRecord> records) MergeRecords(IEnumerable<(ModRecord record, string sourceModName)> records, bool MakeWhole=false)
        {
            var resultModNames = new List<string>();
            var resultRecords = new List<ModRecord>();
            ModTemplate mtemplate = CoreUtils.GetTemplate();    
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
                ModRecord? merged = null;
                string creatorMod = firstMod;

                if (creatorIndex >= 0)
                {
                    var creatorPair = recList[creatorIndex];

                    merged = creatorPair.record.deepClone();
                    creatorMod = creatorPair.sourceModName;

                    // Apply only changes that happen after the creator.
                    for (int j = creatorIndex + 1; j < recList.Count; j++)
                    {
                        merged.applyChangesFrom(recList[j].record);
                    }

                }
                else
                {
                    //CoreUtils.Print($"not found new record of:{recList[0].record.StringId}");
                    if (MakeWhole)
                    {
                        for (int j = 0; j < recList.Count; j++)
                        {
                            if (merged == null)
                            {
                                merged = recList[j].record.deepClone();
                            }
                            else
                            {
                                merged.applyChangesFrom(recList[j].record);
                            }
                        }
                    }
                    merged = merged==null?null:mtemplate.GetWhole(merged, currentRE!.modData.Header!.FileType);
                }
                if (merged != null)
                {
                    resultRecords.Add(merged);
                    resultModNames.Add(creatorMod);
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
                var localPatch = Patcher.Instance.currentRE!.modData.GetRecordByStringId(id);

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
