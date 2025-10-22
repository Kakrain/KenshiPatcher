using KenshiCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace KenshiPatcher
{
    public static class RecordProcedures
    {
        public static readonly string sep = "|";
        public enum ProcSignature
        {
            TargetAndSource,
            TargetAndString
        }
        public class Procedure
        {
            public ProcSignature Signature;
            public required Action<ReverseEngineer, ModRecord, ModRecord, string> Func;
        }
        public static readonly Dictionary<string, Procedure> Procedures = new()
        {
            { "AddExtraData", new Procedure { Signature = ProcSignature.TargetAndSource, Func = (re, t, s, c) => re.AddExtraData(t, s, c) } },
            { "SetField", new Procedure { Signature = ProcSignature.TargetAndString, Func = (re, t, s, arg) => {
                var parts = ParseFieldArg(arg);
                re.SetField(t, parts[0], parts[1]);
            } } }
        };
        private static string[] ParseFieldArg(string arg)
        {
            // Pattern: match either "quoted text" or unquoted text between separators
            string pattern = $@"(?:(?:""([^""]*)"")|([^{sep}]+))";

            var matches = Regex.Matches(arg, pattern);
            var results = new List<string>();

            foreach (Match match in matches)
            {
                string value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                results.Add(value.Trim());
            }

            return results.ToArray();
        }
    }
}
