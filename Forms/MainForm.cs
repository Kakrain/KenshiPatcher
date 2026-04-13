using KenshiCore;
using ScintillaNET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Interop;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

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
            this.ForeColor = Color.FromArgb(unchecked((int)0xFFE9E4D7));
            setColors(Color.FromArgb(unchecked((int)0xFF2F2A24)), Color.FromArgb(unchecked((int)0xFF4C433A)));
            
            AddColumn("Patch Status", mod => getPatchStatus(mod),150);
            AddButton("Patch it!", PatchItClick, mod =>
            {
                if (KPatcher == null || mod == null || ReverseEngineerRepository.Instance.busy)
                    return false;
                string modpath = mod.getModFilePath()!;
                string dir = Path.GetDirectoryName(modpath)!;
                string modName = Path.GetFileNameWithoutExtension(modpath);
                try
                {
                    string patchPath = Path.Combine(dir, modName + ".patch");
                    return File.Exists(patchPath);
                }
                catch (System.ArgumentNullException)
                {
                    return false;
                }
                
            });
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
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            modsListView.SelectedIndexChanged -= Mainform_SelectedIndexChanged;
            modsListView.SelectedIndexChanged += Mainform_SelectedIndexChanged;
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
        private void PatchItClick(object? sender, EventArgs e) {

            var mod = getSelectedMod();
            if (mod == null) {
                CoreUtils.Print("No mod selected", 1);
                return; 
            }
            string patchPath = mod.GetPatchTargetPath();//TODO: KPatcher war null?
            KPatcher!.runPatch(patchPath);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            await OnShownAsync(e);
        }
        private string? GetRealModPath(ModItem mod)
        {
            if (string.IsNullOrEmpty(mod.getModFilePath()))
                return null;

            if (isModPatched(mod))
                return getUnpatchedPath(mod);

            return mod.getModFilePath();
        }
        //TODO: save configuration file
        //TODO: patch status dont update after patching
        //TODO: maybe show progress of a patch?
        //TODO: replace all  MessaageBox with UiService
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
        private async Task OnShownAsync(EventArgs e)
        {
            await Task.Yield();
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
            string dependencies = "not found Dependencies: " + (notfounddeps.Count == 0 ? "none" : string.Join("|", notfounddeps));

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
            if (modsListView.SelectedItems.Count == 0)
                return;
            var selectedMod = getSelectedMod();
            if (!IndexChangeEnabled || selectedMod == null)
            {
                return;
            }
            modsListView.BeginUpdate();
            try
            {
                BeginInvoke(new Action(() => ShowModInfo(selectedMod)));
            }
            finally
            {
                modsListView.EndUpdate();
                modsListView.Refresh();
            }
        }
    }
}
