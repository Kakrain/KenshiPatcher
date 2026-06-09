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
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using static System.Windows.Forms.AxHost;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TrayNotify;

namespace KenshiPatcher.Forms
{
    //Guide
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
            RefreshColumn(1);
            modsListView.Refresh();
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
            re.LoadModFile(mod.getModFilePath()!);
            var logform = getLogForm();
            if (logform == null) return;

            string headerText = re.GetHeaderAsString();

            List<string> notfounddeps = new();
            foreach (string d in re.getDependencies())
            {
                mergedMods.TryGetValue(d, out var m);
                if (m == null)
                {
                    notfounddeps.Add(d);
                }
            }
            string dependencies = "not found Dependencies: " + (notfounddeps.Count == 0 ? "none" : string.Join("|", notfounddeps)) + "\n";

            List<string> notfoundrefs = new();
            foreach (string d in re.getReferences())
            {
                mergedMods.TryGetValue(d, out var m);
                if (m == null)
                {
                    notfoundrefs.Add(d);
                }
            }
            string references = "not found References: " + (notfoundrefs.Count == 0 ? "none" : string.Join("|", notfoundrefs)) + "\n";


            // Always run UI updates on the UI thread
            if (logform.InvokeRequired)
            {
                logform.BeginInvoke((Action)(() =>
                {
                    logform.LogString(headerText);
                    logform.LogString(dependencies, Color.Red);
                    logform.Refresh();
                }));
            }
            else
            {
                logform.LogString(headerText);
                logform.LogString(dependencies, Color.Red);
                logform.LogString(references, Color.IndianRed);
                logform.Refresh();
            }
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
