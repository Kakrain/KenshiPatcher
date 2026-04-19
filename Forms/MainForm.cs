using KenshiCore.Mods;
using KenshiCore.ReverseEngineering;
using KenshiCore.UI;
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

            repo.LoadBaseGameMods(Path.Combine(ModManager.kenshiPath!, "data"));
            repo.LoadGameDirMods(ModManager.gamedirModsPath!);
            repo.LoadWorkshopMods(ModManager.workshopModsPath!);
            repo.LoadSelectedMods(Path.Combine(ModManager.kenshiPath!, "data", "mods.cfg"));
            repo.excludeUnselectedMods = true;
        }
        private bool isModPatched(ModItem mod)
        {
            string modpath = mod.getModFilePath()!;
            string dir = Path.GetDirectoryName(modpath)!;
            string modName = Path.GetFileNameWithoutExtension(modpath);
            string patchPath = Path.Combine(dir, modName + ".patch");
            string unpatchedPath = Path.Combine(dir, modName + ".unpatched");
            if (!File.Exists(patchPath))
                return false;
            return File.Exists(unpatchedPath);
        }
        private string getUnpatchedPath(ModItem mod)
        {
            string modpath = mod.getModFilePath()!;
            string dir = Path.GetDirectoryName(modpath)!;
            string modName = Path.GetFileNameWithoutExtension(modpath);
            string unpatchedPath = Path.Combine(dir, modName + ".unpatched");
            return unpatchedPath;
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
            StringBuilder sb = new StringBuilder();
            var sw = Stopwatch.StartNew();
            foreach (var mod in mods)
            {
                string patchPath = mod.GetPatchTargetPath()!;
                await KPatcher!.RunPatchAsync(patchPath);
            }
            sb.AppendJoin(", ", mods.ConvertAll(m=>m.Name));
            sw.Stop();
            UiService.ShowMessage($"{sb.ToString()} patched in {sw.Elapsed:mm\\:ss\\.fff}");

            RefreshColumn(1);
            modsListView.Refresh();
        }
        private string? GetRealModPath(ModItem mod)
        {
            if (string.IsNullOrEmpty(mod.getModFilePath()))
                return null;

            if (isModPatched(mod))
                return getUnpatchedPath(mod);

            return mod.getModFilePath();
        }

        protected override async Task AfterModsLoadedAsync()
        {
            await Task.Run(() =>
                RERepository.LoadFromMods(
                    mergedMods,
                    GetRealModPath
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
