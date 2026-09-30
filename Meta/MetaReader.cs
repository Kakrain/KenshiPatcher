using KenshiCore.Mods;
using KenshiCore.ReverseEngineering;
using KenshiCore.Utilities;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace KenshiPatcher.Meta
{
    public class MetaReader
    {
        public MetaFile? mfile ;
     
        public MetaReader()
        {
            
        }
        public bool Read(string path)
        {
            mfile = new MetaFile();
            try
            {
                var reader = new PrimitiveReader(File.ReadAllBytes(path));

                string filename = Path.GetFileName(path);

                int version=reader.ReadInt();
                if (version != MetaFile.currentVersion)
                {
                    CoreUtils.Print($"Warning {filename} has outdated version {version}");
                    mfile = null;
                    return false;
                }
                int MetaCount = reader.ReadInt();

                for (int i = 0; i < MetaCount; i++)
                {
                    try
                    {
                        mfile.AddMetaInfo(ParseMetaInfo(ref reader));
                    }
                    catch (EndOfStreamException)
                    {
                        CoreUtils.Print($"⚠ Warning: EndOfStreamException found prematurely.");
                        return false;
                    }
                }
                return true;
            }

            catch (FileNotFoundException)
            {
                CoreUtils.Print($"⚠ Warning: File not found: {path}", 0);
                return false;
            }
            catch (EndOfStreamException)
            {
                CoreUtils.Print($"⚠ Warning: Unexpected end of file: {path}", 0);
                return false;
            }
            catch (UnsupportedModFileException)
            {
                CoreUtils.Print($"⚠ Warning: Unsupported File type: {path}", 0);
                return false;
            }
            catch (Exception ex)
            {
                CoreUtils.Print($"⚠ Failed to load meta '{path}': {ex.Message}", 0);
                return false;
            }

        }
        private MetaInfo ParseMetaInfo(ref PrimitiveReader reader)
        {
            string name = reader.ReadString();
            long size = reader.ReadLong();
            long lastModified = reader.ReadLong();
            return new MetaInfo(name,size,lastModified);
        }

    }
}
