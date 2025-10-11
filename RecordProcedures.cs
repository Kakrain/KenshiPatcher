using KenshiCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KenshiPatcher
{
    public static class RecordProcedures
    {
        public static readonly Dictionary<string, Action<ReverseEngineer, ModRecord, ModRecord,string>> Procedures = new()
        {
            { "AddExtraData", (re, target, source,category) => re.AddExtraData(target, source, category) }
        };
        public static Action<ReverseEngineer, ModRecord, ModRecord, string> ParseProcedure(string name)
        {
            if (!Procedures.TryGetValue(name, out var proc))
                throw new FormatException($"Unknown procedure: {name}");
            return proc;
        }
    }
}
