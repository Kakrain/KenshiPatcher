using KenshiCore.ReverseEngineering;
using KenshiCore.UI;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace KenshiPatcher.Meta
{
    public class MetaWriter
    {
        public MetaWriter(){}
        public void WriteMetaFile(string filepath,MetaFile file)
        {
            try
            {
                var buffer = new ArrayBufferWriter<byte>();
                PrimitiveWriter _writer = new PrimitiveWriter(buffer);

                _writer.WriteInt(MetaFile.currentVersion);
                _writer.WriteInt(file.metas.Count);
                foreach(MetaInfo info in file.metas)
                {
                    WriteMetaInfo(ref _writer,info);
                }


                File.WriteAllBytes(filepath, buffer.WrittenSpan.ToArray());
            }
            catch (System.IO.IOException)
            {
                UiService.ShowMessage($"The process cannot access the file {filepath}");
            }
        }
        private void WriteMetaInfo(ref PrimitiveWriter _writer,MetaInfo info)
        {
            _writer.WriteString(info.name);
            _writer.WriteLong(info.size);
            _writer.WriteLong(info.lastModified);
        }
    }
}
