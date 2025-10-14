using KenshiCore;
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
        private Dictionary<ModItem, ReverseEngineer> ReverseEngineersCache = new();
        private Patcher? KPatcher;
        private Button PatchButton;
        public MainForm()
        {
            Text = "Kenshi Patcher";
            Width = 800;
            Height = 500;
            this.ForeColor = Color.FromArgb(unchecked((int)0xFFE9E4D7));
            setColors(Color.FromArgb(unchecked((int)0xFF2F2A24)), Color.FromArgb(unchecked((int)0xFF4C433A)));
            
            modsListView.SelectedIndexChanged += ModsListView_SelectedIndexChanged;
            AddColumn("Patch Status", mod => getPatchStatus(mod),150);
            PatchButton=AddButton("Patch it!",PatchItClick);
            
        }
        private string getPatchStatus(ModItem mod)
        {
            string modpath = mod.getModFilePath()!;
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
            KPatcher!.runPatch(getSelectedMod().getModFilePath()!);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            await OnShownAsync(e);
        }
        private async Task OnShownAsync(EventArgs e)
        {
            if (InitializationTask != null)
                await InitializationTask;
            ExcludeUnselectedMods();
            LoadBaseGameData();
            InitializeProgress(0, mergedMods.Count);
            int i = 0;
            foreach (var mod in mergedMods)
            {
                ReverseEngineer re = new ReverseEngineer();
                re.LoadModFile(mod.Value.getModFilePath()!);
                ReverseEngineersCache[mod.Value] = re;
                ReportProgress(i, $"Engineered mod:{i}");
                i++;
            }
            KPatcher = new Patcher(ReverseEngineersCache);

        }
        private void ModsListView_SelectedIndexChanged(object? sender, EventArgs? e)
        {
            if (!IndexChangeEnabled)
            {
                return;
            }
            if (modsListView.SelectedItems.Count == 0)
                return;


            var selectedMod = getSelectedMod();
            string modpath = selectedMod.getModFilePath()!;
            string dir = Path.GetDirectoryName(modpath)!;
            string modName = Path.GetFileNameWithoutExtension(modpath);

            string patchPath = Path.Combine(dir, modName + ".patch");
            PatchButton.Enabled = File.Exists(patchPath);

            modsListView.BeginUpdate();
            
            var logform = getLogForm();

            
            logform.Reset();
            ReverseEngineer re = new ReverseEngineer();
            re.LoadModFile(selectedMod.getModFilePath()!);
            //List<(string Text, Color Color)> bs = re.ValidateAllDataAsBlocks();
            //List<(string Text, Color Color)> bs = re.GetAllDataAsBlocks(1,"ARMOUR", new List<string> { "cut into stun", "material type", "cut def bonus","class","level bonus","pierce def mult" });
            //List<(string Text, Color Color)> bs = re.GetAllDataAsBlocks(1);
            List<(string Text, Color Color)> bs = re.GetAllDataAsBlocks(-1);
            /*List<(string Text, Color Color)> bs=new List<(string Text, Color Color)>();
            foreach (var mod in ReverseEngineersCache.Keys)
            {
                if (string.Equals("No Cut Efficiency.mod", mod.Name, StringComparison.Ordinal))//"gamedata.base",BBGoatsBeastforge.mod
                {
                    if (ReverseEngineersCache.TryGetValue(mod, out var rebase))
                        bs = re.CompareWith(rebase, "ARMOUR", new List<string> { "cut into stun", "material type", "cut def bonus", "class", "level bonus", "pierce def mult" });
                }
            }*/




            InitializeProgress(0, bs.Count);
            logform.LogBlocks(bs, (done, label) => ReportProgress(done, label));


            modsListView.EndUpdate();
        }
    }
}
