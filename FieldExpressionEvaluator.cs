using KenshiCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace KenshiPatcher
{
    public static class FieldExpressionEvaluator
    {
        //private static readonly Regex fieldPattern = new(@"GetField\(\s*""([^""]+)""\s*\)", RegexOptions.Compiled);
        private static readonly Regex fieldPattern = new(@"GetField\(\s*""([^""]+)""\s*\)", RegexOptions.Compiled);
        private static readonly Regex tablePattern = new(@"(\w+)\s*\[\s*([^\]]+)\s*\]", RegexOptions.Compiled);
        public static Dictionary<string, Dictionary<string, string>> Tables { get; set; } = new(); 
        
        public static void SetTables(Dictionary<string, Dictionary<string, string>> tables)
        {
            Tables = tables;
        }
        public static object Evaluate(string expression, ModRecord record)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return 0f;
            // Replace GetField("field") with its value (as string or numeric literal)
            string processed = fieldPattern.Replace(expression, match =>
            {
                string field = match.Groups[1].Value;
                object? value = record.GetFieldAsObject(field);

                if (value == null)
                    throw new Exception($"Field '{field}' not found in record '{record.Name}'.");

                return FormatValueForExpression(value);
            });

            // Step 2: Replace table lookups (tableName[key])
            processed = tablePattern.Replace(processed, match =>
            {
                string tableName = match.Groups[1].Value;
                string keyExpr = match.Groups[2].Value.Trim();

                // Evaluate key (it may itself contain GetField())
                object keyValue = Evaluate(keyExpr, record);
                string key = keyValue.ToString()!;

                if (Tables == null || !Tables.TryGetValue(tableName, out var table))
                    throw new Exception($"Table '{tableName}' not found.");

                if (!table.TryGetValue(key, out var tableVal))
                    throw new Exception($"Key '{key}' not found in table '{tableName}'.");

                return tableVal; // directly insert numeric literal or quoted string
            });

            // If expression is purely numeric or contains arithmetic — evaluate it
            if (LooksNumericExpression(processed))
            {
                var dt = new DataTable();
                object result = dt.Compute(processed, "");
                return Convert.ToSingle(result, CultureInfo.InvariantCulture);
            }

            // Otherwise return the processed string directly
            return processed;
        }

        private static string FormatValueForExpression(object value)
        {
            switch (value)
            {
                case float f:
                    return f.ToString(CultureInfo.InvariantCulture);
                case double d:
                    return d.ToString(CultureInfo.InvariantCulture);
                case int i:
                    return i.ToString(CultureInfo.InvariantCulture);
                case long l:
                    return l.ToString(CultureInfo.InvariantCulture);
                case bool b:
                    return b ? "1" : "0";
                case string s:
                    // quote strings to keep them safe in expressions
                    return $"\"{s}\"";
                case float[] arr when arr.Length > 0:
                    return arr[0].ToString(CultureInfo.InvariantCulture);
                default:
                    throw new Exception($"Unsupported field type: {value.GetType()}");
            }
        }

        private static bool LooksNumericExpression(string expr)
        {
            // Heuristic: if it contains digits, operators, and no quotes → treat as math
            return expr.IndexOf('"') == -1 &&
                   Regex.IsMatch(expr, @"[\d\+\-\*/\(\)]");
        }
    }
}
