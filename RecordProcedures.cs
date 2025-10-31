using KenshiCore;
using KenshiPatcher.ExpressionReader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace KenshiPatcher
{
    public static class RecordProcedures
    {
        public static readonly string sep = "|";
        public enum ProcSignature
        {
            TargetAndSource,
            TargetAndExpression
        }
        public class Procedure
        {
            public ProcSignature Signature;
            public required Action<ReverseEngineer, ModRecord, ModRecord, List<IExpression<object>>> Func;
        }
        public static readonly Dictionary<string, Procedure> Procedures = new()
        {
            //{ "AddExtraData", new Procedure { Signature = ProcSignature.TargetAndSource, Func = (re, t, s, c) => re.AddExtraData(t, s, c) } },
            { "AddExtraData", new Procedure { Signature = ProcSignature.TargetAndSource, Func = (re, t, s, expressions) => re.AddExtraData(t, s, (expressions[0].GetFunc()(null) as string)!) } },
            { "SetField", new Procedure { Signature = ProcSignature.TargetAndExpression, Func = (re, t, s, expressions) => {
                //var parts = ParseFieldArg(arg);
                //re.SetField(t, parts[0], parts[1]);
                if(!(expressions[0].GetFunc()(null) is string strfieldname))
                    throw new FormatException($"Invalid field name: ({expressions[0].ToString()})");
                re.SetField(t,strfieldname,expressions[1].GetFunc()(t).ToString()!);
            } } },
            { "ForceSetField", new Procedure { Signature = ProcSignature.TargetAndExpression, Func = (re, t, s, expressions) => {
                //var parts = ParseFieldArg(arg);
                //re.ForceSetField(t, parts[0], parts[1], parts[2]);
                if(!(expressions[0].GetFunc()(null) is string strfieldname))
                    throw new FormatException($"Invalid field name: ({expressions[0].ToString()})");
                if(!(expressions[2].GetFunc()(null) is string strtype))
                    throw new FormatException($"Invalid field name: ({expressions[2].ToString()})");
                re.ForceSetField(t,strfieldname,expressions[1].GetFunc()(t).ToString()!,strtype);

            } } }
            
        };
        /*private static string[] ParseFieldArg(string arg)
        {
            CoreUtils.Print($"Parsing field arg: {arg}");
            // Pattern: match either "quoted text" or unquoted text between separators
            //string pattern = $@"(?:(?:""([^""]*)"")|([^{sep}]+))";

            var matches = Regex.Matches(arg, pattern);
            var results = new List<string>();

            foreach (Match match in matches)
            {
                string value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                results.Add(value.Trim());
            }

            return results.ToArray();
        }*/
    }
}
