using KenshiCore.Mods;
using KenshiCore.ReverseEngineering;
using KenshiCore.UI;
using KenshiCore.Utilities;
using ScintillaNET;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Interop;
using System.IO;
using System.Linq;
using System.Security.Cryptography.Xml;
using System.Security.Policy;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using static System.Windows.Forms.AxHost;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TrayNotify;

namespace KenshiPatcher.Forms
{
    public class MainForm : ProtoMainForm
    {
        private Boolean IndexChangeEnabled = true;
        ReverseEngineerRepository RERepository = ReverseEngineerRepository.Instance;
        private Patcher? KPatcher;
        public MainForm()
        {
            Text = "Kenshi Patcher";
            Width = 800;
            Height = 500;
            ThemeManager.Set(
                new AppTheme
                {
                    Background = Color.FromArgb(unchecked((int)0xFF2F2A24)),
                    Secondary = Color.FromArgb(unchecked((int)0xFF4C433A)),
                    Foreground = Color.FromArgb(unchecked((int)0xFFE9E4D7))
                });
            
            AddColumn("Patch Status", mod => getPatchStatus(mod),150);
            AddButton("Patch it!", PatchItClick);
            modsListView.SelectedIndexChanged += Mainform_SelectedIndexChanged;
        }

        protected override void LoadMods()
        {
            var repo = ModRepository.Instance;

            repo.LoadBaseGameMods();
            repo.LoadGameDirMods();
            repo.LoadWorkshopMods();
            repo.LoadSelectedMods();
            repo.excludeUnselectedMods = true;
        }
        
        private string getPatchStatus(ModItem mod)
        {
            string? modpath = mod.getModFilePath()!;
            if (modpath == null)
                return "deleted";
            string dir = Path.GetDirectoryName(modpath)!;
            string modName = Path.GetFileNameWithoutExtension(modpath);

            string patchPath = Path.Combine(dir, modName + ".patch");
            string unpatchedPath = Path.Combine(dir, modName + ".unpatched");
            if (!File.Exists(patchPath))
                return "_";
            return (File.Exists(unpatchedPath) ? "patched already" : "not patched");
        }
        private async void PatchItClick(object? sender, EventArgs e)
        {
            var mods = getSelectedMods().Where(m => File.Exists(m.getPatchPath())).ToList();

            if (mods.Count == 0)
            {
                UiService.ShowMessage("No mod available for patching selected", "Error", MessageBoxIcon.Error);
                return;
            }
            var failedMods = new List<string>();
            StringBuilder sb = new StringBuilder();
            var sw = Stopwatch.StartNew();
            foreach (var mod in mods)
            {
                bool success = await KPatcher!.RunPatchAsync(mod.GetPatchTargetPath()!);

                if (!success)
                    failedMods.Add(mod.Name);
            }
            if (failedMods.Count == 0)
            {
                sw.Stop();
                sb.AppendJoin(", ", mods.ConvertAll(m => m.Name));
                UiService.ShowMessage(
                    $"{sb} patched in {sw.Elapsed:mm\\:ss\\.fff}");
            }
            else
            {
                sw.Stop();
                UiService.ShowMessage(
                    $"Finished with errors.\n Failed: {string.Join(", ", failedMods)} \n Read the .log file for details",
                    "Patch Errors",
                    MessageBoxIcon.Warning);
            }
            var logForm = getLogForm();
            logForm.Reset();

            RefreshColumn(1);
            modsListView.Refresh();
            RefreshSelectedModInfo();
        }
        

        protected override async Task AfterModsLoadedAsync()
        {
            await Task.Run(() =>
                RERepository.LoadFromMods(
                    mergedMods,
                    CoreUtils.GetRealModPath
                )
            );

            KPatcher = new Patcher();
        }
        private void ShowModInfo(ModItem mod)
        {
            ReverseEngineer re = new ReverseEngineer();
            string modPath = mod.getModFilePath()!;
            var logform = getLogForm();
            if (logform == null) return;

            try
            {
                re.LoadModFile(modPath);
            }
            catch (UnsupportedModFileException ex)
            {
                CoreUtils.Print($"Error loading mod file for {mod.Name}: {ex.Message}");
                logform.LogString($"Error loading mod file for {mod.Name}: {ex.Message}");
                return;
                //re.LoadModFile(path);
            }
            string headerText = re.GetHeaderAsString();
            // Always run UI updates on the UI thread
            string? patchLog =GetFileAsText(Path.ChangeExtension(modPath, null) + "_patch.log");
            void UpdateUi()
            {
                logform.LogString(headerText);
                if(patchLog != null){
                    logform.LogString($"Patch Log for {mod.Name}:{Environment.NewLine}",Color.Blue);
                    logform.LogString($"{patchLog+ Environment.NewLine}", Color.AliceBlue);
                }
                logform.LogString(BuildMissingDependenciesList(re), Color.Red);
                logform.LogString(BuildMissingReferencesList(re), Color.IndianRed);
                logform.Refresh();
            }
            if (logform.InvokeRequired)
                logform.BeginInvoke((Action)UpdateUi);
            else
                UpdateUi();
        }
        private void RefreshSelectedModInfo()
        {
            var mods = getSelectedMods();

            foreach (var mod in mods)
            {
                ShowModInfo(mod);
            }
        }
        private string? GetFileAsText(string filePath)
        {
            string? result= null;
            if(File.Exists(filePath))
                result= File.ReadAllText(filePath);
            return result;
        }
        private string BuildMissingDependenciesList(ReverseEngineer re)
        {
            var missing = re.getDependencies()
                .Where(item => !mergedMods.ContainsKey(item))
                .ToList();

            return $"not found Dependencies : {(missing.Count == 0 ? "none" : string.Join("|", missing))}\n";
        }
        private string BuildMissingReferencesList(ReverseEngineer re)
        {
            var missing = re.getReferences()
                .Where(item => !mergedMods.ContainsKey(item))
                .ToList();

            return $"not found References : {(missing.Count == 0 ? "none" : string.Join("|", missing))}\n";
        }
        private void Mainform_SelectedIndexChanged(object? sender, EventArgs? e)
        {
            var mods = getSelectedMods();
            if (mods.Count==0|| !IndexChangeEnabled)
                return;
            modsListView.BeginUpdate();
            try
            {
                foreach (var mod in mods)
                {
                    if (mod == null)
                        continue;
                    BeginInvoke(new Action(() => ShowModInfo(mod)));
                }
            }
            finally
            {
                modsListView.EndUpdate();
                modsListView.Refresh();
            }
        }
    }
}
