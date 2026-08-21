#region

using System;
using System.Globalization;
using UnityEngine;

#endregion

namespace FailCake.Console
{
    public static class ConsoleParser
    {
        public static bool CanParse(Type type) {
            if (type == null) return false;
            if (type == typeof(string)) return true;
            if (type == typeof(bool)) return true;
            if (type == typeof(int) || type == typeof(float) || type == typeof(double) ||
                type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
                type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) ||
                type == typeof(sbyte) || type == typeof(decimal))
                return true;
            if (type.IsEnum) return true;
            if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4)) return true;
            return type == typeof(Color) || type == typeof(Color32);
        }

        public static object Parse(string value, Type type) {
            if (type == typeof(string)) return value ?? string.Empty;
            if (type == typeof(bool)) return ConsoleParser.ParseBool(value);
            if (type.IsEnum) return Enum.Parse(type, value, true);
            if (type == typeof(Vector2)) return ConsoleParser.ParseVector2(value);
            if (type == typeof(Vector3)) return ConsoleParser.ParseVector3(value);
            if (type == typeof(Vector4)) return ConsoleParser.ParseVector4(value);
            if (type == typeof(Color)) return ConsoleParser.ParseColor(value);
            if (type == typeof(Color32)) return ConsoleParser.ParseColor32(value);
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }

        public static bool ParseBool(string value) {
            if (string.IsNullOrEmpty(value)) return false;
            return value.ToLowerInvariant() switch {
                "1" or "true" or "on" or "yes"  => true,
                "0" or "false" or "off" or "no" => false,
                var _                           => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && f != 0f
            };
        }

        public static float ParseFloat(string value) {
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result);
            return result;
        }

        private static Vector2 ParseVector2(string value) {
            string[] p = ConsoleParser.SplitComponents(value);
            return new Vector2(
                p.Length > 0 ? ConsoleParser.ParseFloat(p[0]) : 0f,
                p.Length > 1 ? ConsoleParser.ParseFloat(p[1]) : 0f);
        }

        private static Vector3 ParseVector3(string value) {
            string[] p = ConsoleParser.SplitComponents(value);
            return new Vector3(
                p.Length > 0 ? ConsoleParser.ParseFloat(p[0]) : 0f,
                p.Length > 1 ? ConsoleParser.ParseFloat(p[1]) : 0f,
                p.Length > 2 ? ConsoleParser.ParseFloat(p[2]) : 0f);
        }

        private static Vector4 ParseVector4(string value) {
            string[] p = ConsoleParser.SplitComponents(value);
            return new Vector4(
                p.Length > 0 ? ConsoleParser.ParseFloat(p[0]) : 0f,
                p.Length > 1 ? ConsoleParser.ParseFloat(p[1]) : 0f,
                p.Length > 2 ? ConsoleParser.ParseFloat(p[2]) : 0f,
                p.Length > 3 ? ConsoleParser.ParseFloat(p[3]) : 0f);
        }

        private static Color ParseColor(string value) {
            if (ConsoleParser.TryParseHexColor(value, out Color c)) return c;
            string[] p = ConsoleParser.SplitComponents(value);
            return new Color(
                p.Length > 0 ? ConsoleParser.ParseFloat(p[0]) : 0f,
                p.Length > 1 ? ConsoleParser.ParseFloat(p[1]) : 0f,
                p.Length > 2 ? ConsoleParser.ParseFloat(p[2]) : 0f,
                p.Length > 3 ? ConsoleParser.ParseFloat(p[3]) : 1f);
        }

        private static Color32 ParseColor32(string value) {
            return ConsoleParser.ParseColor(value);
        }

        private static bool TryParseHexColor(string value, out Color color) {
            color = Color.white;
            if (string.IsNullOrEmpty(value)) return false;

            string hex = value;
            if (hex.StartsWith("#"))
                hex = hex[1..];
            else if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];

            if (hex.Length != 6 && hex.Length != 8) return false;
            if (!ConsoleParser.IsHex(hex)) return false;

            byte r = Convert.ToByte(hex[..2], 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            byte a = hex.Length == 8 ? Convert.ToByte(hex.Substring(6, 2), 16) : (byte)255;

            color = new Color32(r, g, b, a);
            return true;
        }

        private static bool IsHex(string s) {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!ConsoleParser.IsHexDigit(c)) return false;
            }

            return true;
        }

        private static bool IsHexDigit(char c) {
            return c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
        }

        private static string[] SplitComponents(string value) {
            if (string.IsNullOrEmpty(value)) return Array.Empty<string>();
            string trimmed = value.Trim('(', ')', '[', ']', ' ', '"');
            return trimmed.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}