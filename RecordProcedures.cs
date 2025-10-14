using KenshiCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace KenshiPatcher
{
    public static class RecordProcedures
    {
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
                object result = FieldExpressionEvaluator.Evaluate(parts[1], t);
                string valueStr = Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                re.SetField(t, parts[0], valueStr);
            } } }
        };    //{ "SetField", new Procedure { Signature = ProcSignature.TargetAndString, Func = (re, t, s, arg) => {var parts = ParseFieldArg(arg); re.SetField(t, parts[0], parts[1]);}}}};
        private static string[] ParseFieldArg(string arg)
        {
            // Expect syntax: "fieldName",value
            // Match quoted field name and then value
            var match = Regex.Match(arg, @"^""([^""]+)""\s*,\s*(.+)$");
            if (!match.Success)
                throw new FormatException($"Invalid SetField argument: {arg}");

            string fieldName = match.Groups[1].Value.Trim();
            string value = match.Groups[2].Value.Trim();
            return new string[] { fieldName, value };
        }
    }
}
