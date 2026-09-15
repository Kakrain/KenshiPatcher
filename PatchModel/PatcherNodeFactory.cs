using KenshiCore.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace KenshiPatcher.PatchModel
{
    public static class PatcherNodeFactory
    {
        private static readonly string _comment = ";";
        private static readonly Dictionary<string, Func<string, PatcherNode>> contain_constructors = new()
        {
            [PatcherNode._definition] = line => new PatcherDefinition(line),
            [PatcherNode._extraction] = line => new PatcherExtraction(line),
            [PatcherNode._procedure] = line => new PatcherProcedure(line),
            [PatcherNode._procedure2] = line => new PatcherProcedure(line),
        };
        private static readonly Dictionary<string, Func<string, PatcherNode>> startswith_constructors = new()
        {
            [PatcherNode._globalfunc] = line => new PatcherGlobalFunction(line),
        };
        private static string CleanLine(string rawLine)
        {
            string line = rawLine.Trim();
            int commentIndex = line.IndexOf(_comment);
            if (commentIndex >= 0)
                line = line.Substring(0, commentIndex).Trim();
            return line;
        }
        public static PatcherNode? TryCreate(string line)
        {
            string cleanedLine = CleanLine(line);
            //CoreUtils.Print("Test line:" + cleanedLine);
            //CoreUtils.Prompt("Test line:" + cleanedLine);
            if (string.IsNullOrWhiteSpace(cleanedLine))
                return null;
            foreach (var kvp in contain_constructors)
            {
                if (cleanedLine.Contains(kvp.Key, StringComparison.Ordinal))
                {

                    ///CoreUtils.Print("Test line:" + cleanedLine+ "is "+ kvp.Value(cleanedLine));
                    return kvp.Value(cleanedLine);

                }
            }
            foreach (var kvp in startswith_constructors)
            {
                if (cleanedLine.StartsWith(kvp.Key, StringComparison.Ordinal))
                {
                    //CoreUtils.Print("Test line:" + cleanedLine + "is " + kvp.Value(cleanedLine));
                    return kvp.Value(cleanedLine);

                }
            }
            throw new FormatException($"Unrecognized line: ({line})");
        }
    }
}
