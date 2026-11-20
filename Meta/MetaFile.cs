using KenshiCore.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace KenshiPatcher.Meta
{
    public class MetaFile
    {

        public const int currentVersion = 1;
        public List<MetaInfo> metas{ get; }
        public MetaFile()
        {
            metas = new();
        }
        public void AddMetaInfo(MetaInfo info)
        {
            metas.Add(info);
        }
        public static MetaFile getMetaFileUntil(string modname)
        {
            MetaFile result = new MetaFile();
            foreach(string key in MetaInfo.allinfos.Keys)
            {
                result.AddMetaInfo(MetaInfo.allinfos[key]);
                if (key.Equals(modname))//including current patch
                    return result;
            }
            CoreUtils.Print($"Warning: No {modname} found while generating Metafile");
            return result;
        }
        public static MetaFile Merge(MetaFile oldMFile, MetaFile newMFile)
        {
            MetaFile result = new MetaFile();

            string patchName = newMFile.metas[^1].name;

            var oldByName = oldMFile.metas.ToDictionary(x => x.name);

            var newByName = newMFile.metas.ToDictionary(x => x.name);

            foreach (MetaInfo newInfo in newMFile.metas)
            {
                if (newInfo.name == patchName)
                    continue;

                if (!oldByName.ContainsKey(newInfo.name))
                {
                    result.AddMetaInfo(newInfo);
                }
                else
                {
                    result.AddMetaInfo(newInfo);
                }
            }
            return result;

        }
        public static List<string> GetModsToBePatched(MetaFile oldMFile, MetaFile newMFile)
        {//last mod is the patch itself
            List<string> result = new();
            for (int i = 0; i < newMFile.metas.Count - 1; i++)
            {
                MetaInfo newInfo = newMFile.metas[i];
                MetaInfo? oldInfo = oldMFile.metas.FirstOrDefault(x => x.name == newInfo.name);
                // new mod
                if (oldInfo == null)
                {
                    result.Add(newInfo.name);
                    continue;
                }
                // Existing mod changed
                if (newInfo.size != oldInfo.size || newInfo.lastModified != oldInfo.lastModified)
                {
                    result.Add(newInfo.name);
                }
            }
            result.Add(newMFile.metas[^1].name);
            CoreUtils.Print("METAFILE LIST: " + string.Join(",", result)+ "last new: "+newMFile.metas.Last().name);
            return result;
        }
    }
}
