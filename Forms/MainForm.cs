﻿using KenshiCore;
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
        public MainForm()
        {
            Text = "Kenshi Patcher";
            Width = 800;
            Height = 500;
            this.ForeColor = Color.FromArgb(unchecked((int)0xFFE9E4D7));
            setColors(Color.FromArgb(unchecked((int)0xFF2F2A24)), Color.FromArgb(unchecked((int)0xFF4C433A)));
            
            modsListView.SelectedIndexChanged += Mainform_SelectedIndexChanged;
            AddColumn("Patch Status", mod => getPatchStatus(mod),150);
            AddButton("Patch it!", PatchItClick, mod =>
            {
                var selectedMod = getSelectedMod();
                if (selectedMod == null)
                    return false;
                string modpath = selectedMod.getModFilePath()!;
                string dir = Path.GetDirectoryName(modpath)!;
                string modName = Path.GetFileNameWithoutExtension(modpath);
                string patchPath = Path.Combine(dir, modName + ".patch");
                return File.Exists(patchPath);
            });

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
            KPatcher!.runPatch(mod.getModFilePath()!);
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
            List<string> modsToRemove = new();
            foreach (var mod in mergedMods)
            {
                ReverseEngineer re = new ReverseEngineer();
                string realmodpath= mod.Value.getModFilePath()!;
                if (string.IsNullOrEmpty(realmodpath))
                {
                    modsToRemove.Add(mod.Key);
                    continue;
                }
                if (isModPatched(mod.Value))
                    realmodpath = getUnpatchedPath(mod.Value);
                re.LoadModFile(realmodpath);
                ReverseEngineersCache[mod.Value] = re;
                i++;
                ReportProgress(i, $"Engineered mod:{i}");
            }
            foreach (var key in modsToRemove)
                mergedMods.Remove(key);

            modsListView.BeginUpdate();
            try
            {
                foreach (ListViewItem item in modsListView.Items.Cast<ListViewItem>().ToList())
                {
                    if (item.Tag is ModItem mod && modsToRemove.Contains(mod.Name))
                        modsListView.Items.Remove(item);
                }
            }
            finally
            {
                modsListView.EndUpdate();
            }

            KPatcher = new Patcher(ReverseEngineersCache);

        }
        private void Mainform_SelectedIndexChanged(object? sender, EventArgs? e)
        {
            if (modsListView.SelectedItems.Count == 0)
                return;
            var selectedMod = getSelectedMod();
            if (!IndexChangeEnabled || selectedMod==null)
            {
                return;
            }
            
            
            modsListView.BeginUpdate();
            var logform = getLogForm();
            //logform.Reset();
            ReverseEngineer re = new ReverseEngineer();
            re.LoadModFile(selectedMod.getModFilePath()!);
            List<(string Text, Color Color)> bs = re.GetHeaderAsBlocks();
            /*foreach (var mod in ReverseEngineersCache.Keys)
            {
                if (string.Equals("No Cut Efficiency.mod", mod.Name, StringComparison.Ordinal))//"gamedata.base",BBGoatsBeastforge.mod
                {
                    if (ReverseEngineersCache.TryGetValue(mod, out var rebase))
                        bs = re.CompareWith(rebase, "ARMOUR", new List<string> { "cut into stun", "slot" });
                }
            }*/
            List<string> notfounddeps = new();
            foreach(string d in re.getDependencies())
            {
                mergedMods.TryGetValue(d, out var m);
                if (m== null)
                {
                    notfounddeps.Add(d);
                }
            }
            bs.Add(("not found Dependencies: " + (notfounddeps.Count==0 ? "none" : string.Join("|", notfounddeps)), Color.Red));
            
            InitializeProgress(0, bs.Count);
            logform.LogBlocks(bs, (done, label) => ReportProgress(done, label));
            modsListView.EndUpdate();
        }
    }
}
