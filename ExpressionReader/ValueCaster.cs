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
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        // Is this value an integer (or integer-representing string)?
        public static bool IsIntegerLike(object? value)
        {
            if (value == null)
                return true;

            if (value is sbyte or byte or short or ushort or int or uint or long or ulong)
                return true;

            if (value is double d) // treat whole double as integer-like
                return Math.Abs(d % 1) < double.Epsilon;

            if (value is float f)
                return Math.Abs(f % 1) < float.Epsilon;

            if (value is string s)
            {
                return long.TryParse(s, NumberStyles.Integer, Inv, out _);
            }

            return false;
        }

        // Is this value a floating/decimal (or float-like string)?
        public static bool IsFloatingLike(object? value)
        {
            if (value == null)
                return true;

            if (value is double or float)
                return true;

            if (value is string s)
            {
                return double.TryParse(s, NumberStyles.Float, Inv, out _);
                //return double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _);
            }

            return false;
        }

        public static double ToDouble(object? value)
        {
            if (value == null)
                return 0;
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
                        return ParseDouble(s);
                    }

            }
            throw new InvalidCastException($"Cannot convert '{value}' to double");
        }
        public static string ToInvariantString(object? value)
        {
            if (value == null)
                return "";

            return value switch
            {
                float f => f.ToString(CultureInfo.InvariantCulture),
                double d => d.ToString(CultureInfo.InvariantCulture),
                decimal m => m.ToString(CultureInfo.InvariantCulture),

                _ => value.ToString()!
            };
        }
        private static double ParseDouble(string s)
        {
            s = s.Trim();

            if (string.IsNullOrEmpty(s))
                throw new InvalidCastException("Empty number");
            if (s.Contains(','))
                s = s.Replace(',', '.');
            if (s.Count(c => c == '.') > 1)
                throw new InvalidCastException($"Malformed number: '{s}'");
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return d;
            throw new InvalidCastException($"Cannot convert '{s}' to double");
        }

        public static long ToInt64(object? value)
        {
            if (value == null)
                return 0;

            switch (value)
            {
                case int i:
                    return i;

                case long l:
                    return l;

                case double d:
                    return (long)Math.Round(d);

                case float f:
                    return (long)Math.Round(f);
                case string s:
                    {
                        double d = ParseDouble(s);
                        return (long)Math.Round(d);
                    }
                default:
                    throw new Exception($"Unsupported type '{value.GetType()}' for integer cast");
            }
        }
        }
}
