using KenshiCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KenshiPatcher.ExpressionReader
{
    internal static class ValueCaster
    {
        // Is this value an integer (or integer-representing string)?
        public static bool IsIntegerLike(object? value)
        {
            if (value is sbyte or byte or short or ushort or int or uint or long or ulong)
                return true;

            if (value is double d) // treat whole double as integer-like
                return Math.Abs(d % 1) < double.Epsilon;

            if (value is float f)
                return Math.Abs(f % 1) < float.Epsilon;

            if (value is string s)
            {
                return long.TryParse(s,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out _);
            }

            return false;
        }

        // Is this value a floating/decimal (or float-like string)?
        public static bool IsFloatingLike(object? value)
        {
            if (value is double or float)
                return true;

            if (value is string s)
            {
                return double.TryParse(s,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out _);
            }

            return false;
        }

        public static double ToDouble(object value)
        {
            switch (value)
            {
                case double d: return d;
                case float f: return f;
                case int i: return i;
                case long l: return l;
                case uint ui: return ui;
                case ulong ul: return ul;
                case short sh: return sh;
                case ushort ush: return ush;
                case byte b: return b;
                case sbyte sb: return sb;
                case string s:
                    {
                        // tolerate both "." and "," decimal separators by normalizing
                        var normalized = s.Replace(',', '.');
                        if (double.TryParse(normalized, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed))
                            return parsed;
                        break;
                    }
            }
            throw new InvalidCastException($"Cannot convert '{value ?? "null"}' to double");
        }

        public static long ToInt64(object value)
        {
            if (value == null)
                throw new Exception("Cannot cast null to int");

            switch (value)
            {
                case int i:
                    return i;

                case long l:
                    return l;

                case double d:
                    return (long)Math.Round(d);   // <--- IMPORTANT
                                                  // NO string parsing

                case float f:
                    return (long)Math.Round(f);

                case string s:
                    // Try parsing as integer first (dot-only)
                    if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l2))
                        return l2;

                    // Try parse as double -> round
                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d2))
                        return (long)Math.Round(d2);

                    // Try locale-aware double parse -> round
                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var d3))
                        return (long)Math.Round(d3);

                    throw new Exception($"Cannot convert '{s}' to integer");

                default:
                    throw new Exception($"Unsupported type '{value.GetType()}' for integer cast");
            }
        }
        }
}
