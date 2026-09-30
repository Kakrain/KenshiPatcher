using KenshiCore.Mods;
using KenshiCore.ReverseEngineering;
using KenshiCore.UI;
using KenshiCore.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KenshiPatcher.Meta
{
    public class MetaInfo
    {

        public static Dictionary<string, MetaInfo> allinfos = new();
        public string name { get; }
        public long size { get; }
        public long lastModified { get; }
        
        public MetaInfo(string name,long size,long lastModified)
        {
            this.name = name;
            this.size = size;
            this.lastModified = lastModified;
        }
        public static bool LoadMetaInfo(string filepath)
        {
            string filename = Path.GetFileName(filepath); 
            FileInfo file = new FileInfo(filepath);

            if (!file.Exists)
            {
                CoreUtils.Print($"Meta file not found {filename}.");
                return false;
            }
            allinfos[filename] = new MetaInfo(filename, file.Length, file.LastWriteTimeUtc.Ticks);
            return true;
        }
        public static MetaInfo? FromFile(string filepath)
        {
            string filename=Path.GetFileName(filepath);

            if (!allinfos.ContainsKey(filename))
            {
                if (!LoadMetaInfo(filepath))
                    return null;
            }
            return allinfos[filename];
        }
        public static MetaInfo? GetMetaInfo(string filename)
        {
            if (!allinfos.ContainsKey(filename))
            {
                return null;
            }
            return allinfos[filename];
        }
        public static void LoadFromMods(Dictionary<string, ModItem> mods)
        {
            allinfos.Clear();
            //ProgressController progress = ProgressController.Instance;
            //progress.Initialize(mods.Count);
            //int i = 0;
            foreach (var kv in mods)
            {
                string? path = kv.Value.getModFilePath();
                if (string.IsNullOrEmpty(path))
                    continue;
                LoadMetaInfo(path);

                //i++;
               // progress.Report(i, $"MetaInfo loaded from mod {i}");
            }
           // progress.Finish();
            //busy = false;
        }
    }
}
