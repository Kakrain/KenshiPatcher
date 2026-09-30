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
using KenshiPatcher.PatchModel;
using System.Linq;
using System.Security.Cryptography.Xml;
using System.Security.Policy;
using System.Text;
using KenshiPatcher.Meta;

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
            ReverseEngineerRepository.Instance.ignoreKenshiFixer = false;
            AddColumn("Patch Status", mod => getPatchStatus(mod),150);

            AddToggle("Also save Update Delta", "update_delta");

            AddButton("Patch it!", PatchItClick);
            AddButton("Reset Patch", ResetPatchClick);
            AddButton("Update Patch", UpdateItClick);
            AddButton("Update All", ResetPatchClick);
            modsListView.SelectedIndexChanged += Mainform_SelectedIndexChanged;
            KPatcherConfigForm.Initialize();
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
        private void PatchItClick(object? sender, EventArgs e)
        {
            if (!UiService.ShowYesNoQuestion("Unless you are planning on importing or a new save, consider Updating instead.","are you sure?"))
                return;
            Patch(getSelectedMods(), false);
        }
        private void UpdateItClick(object? sender, EventArgs e)
        {
            Patch(getSelectedMods(),true);
        }
        private void UpdateAll(object? sender, EventArgs e)
        {
            Patch(ModRepository.Instance.GetMergedMods().Values.ToList(), true);
        }
        private async void Patch(List<ModItem> allmods,bool isUpdate)
        {
            var mods = allmods.Where(m => File.Exists(m.getPatchPath())).ToList();
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
                bool success = await KPatcher!.RunPatchAsync(mod.GetPatchTargetPath()!, isUpdate);

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

            RefreshColumn(2);
            modsListView.Refresh();
            RefreshSelectedModInfo();
        }
        private void ResetPatchClick(object? sender, EventArgs e)
        {
            if (!UiService.ShowYesNoQuestion("Are you sure you want to Reset this patch?.", "are you sure?"))
                return;
            var mods = getSelectedMods()
                .Where(m => File.Exists(m.getPatchPath()))
                .ToList();

            if (mods.Count == 0)
            {
                UiService.ShowMessage("No patched mod selected", "Error", MessageBoxIcon.Error);
                return;
            }

            foreach (var mod in mods)
            {
                string? modPath = mod.getModFilePath();
                if (modPath == null)
                    continue;

                string dir = Path.GetDirectoryName(modPath)!;
                string modName = Path.GetFileNameWithoutExtension(modPath);

                string unpatchedPath = Path.Combine(dir, modName + ".unpatched");
                string logPath = Path.Combine(dir, modName + "_patch.log");
                string metapath = Path.Combine(dir, modName + ".meta");
                if (!File.Exists(unpatchedPath))
                {
                    //UiService.ShowMessage( $"No backup found for {modName}.mod","Error", MessageBoxIcon.Error);
                    continue;
                }

                File.Copy(unpatchedPath, modPath, overwrite: true);

                File.Delete(unpatchedPath);

                if (File.Exists(logPath))
                    File.Delete(logPath);
                if (File.Exists(metapath))
                    File.Delete(metapath);
            }

            RefreshColumn(2);
            modsListView.Refresh();
            RefreshSelectedModInfo();
        }

        protected override async Task AfterModsLoadedAsync()
        {
            await Task.Run(() => MetaInfo.LoadFromMods(mergedMods));
            await Task.Run(() => RERepository.LoadFromMods( mergedMods));
            KPatcher = Patcher.Instance;
        }
        private void ShowModInfo(ModItem mod)
        {
            ReverseEngineer re = new ReverseEngineer(mod.Name);
            string modPath = mod.getModFilePath()!;
            var logform = getLogForm();
            if ((logform == null)|| (modPath == null)) return;
            //if (modPath == null) return;
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
            string headerText = CoreUtils.GetFormatter().GetHeaderAsString(re.modData);
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
            if (!File.Exists(filePath))
                return null;

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    return File.ReadAllText(filePath);
                }
                catch (IOException)
                {
                    Thread.Sleep(50); // wait for writer to finish
                }
            }

            return "file is busy";
        }
        private string BuildMissingDependenciesList(ReverseEngineer re)
        {
            var missing = re.getDependenciesAsList()
                .Where(item => !mergedMods.ContainsKey(item))
                .ToList();

            return $"not found Dependencies : {(missing.Count == 0 ? "none" : string.Join("|", missing))}\n";
        }
        private string BuildMissingReferencesList(ReverseEngineer re)
        {
            var missing = re.getReferencesAsList()
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
