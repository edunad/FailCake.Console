#region

using System;
using System.Globalization;
using System.Reflection;

#endregion

namespace FailCake.Console
{
    public sealed class ConsoleVar : ConsoleEntry
    {
        public readonly string defaultValue;
        public readonly bool hasMin;
        public readonly float min;
        public readonly bool hasMax;
        public readonly float max;

        private readonly FieldInfo _field;
        private readonly Type _fieldType;

        public event Action<ConsoleVar, string> Changed;

        internal ConsoleVar(string name, string help, FCVAR flags, string defaultValue, bool hasMin, float min, bool hasMax, float max, FieldInfo field)
            : base(name, help, flags) {
            this.defaultValue = defaultValue ?? string.Empty;
            this.hasMin = hasMin;
            this.min = min;
            this.hasMax = hasMax;
            this.max = max;
            this._field = field;
            this._fieldType = field != null ? field.FieldType : typeof(string);
        }

        public string GetString() {
            object v = this._field != null ? this._field.GetValue(null) : null;
            return v == null ? string.Empty : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public float GetFloat() {
            object v = this._field != null ? this._field.GetValue(null) : null;
            return v == null ? 0f : Convert.ToSingle(v, CultureInfo.InvariantCulture);
        }

        public int GetInt() {
            object v = this._field != null ? this._field.GetValue(null) : null;
            return v == null ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture);
        }

        public bool GetBool() {
            return this.GetInt() != 0;
        }

        public void SetValue(string value) {
            object parsed = ConsoleParser.Parse(value, this._fieldType);
            if (this.hasMin || this.hasMax)
            {
                float f = Convert.ToSingle(parsed, CultureInfo.InvariantCulture);
                if (this.hasMin) f = MathF.Max(f, this.min);
                if (this.hasMax) f = MathF.Min(f, this.max);
                parsed = Convert.ChangeType(f, this._fieldType, CultureInfo.InvariantCulture);
            }

            string old = this.GetString();
            if (this._field != null) this._field.SetValue(null, parsed);
            this.Changed?.Invoke(this, old);
        }

        public void SetValue(float value) {
            if (this.hasMin) value = MathF.Max(value, this.min);
            if (this.hasMax) value = MathF.Min(value, this.max);

            string old = this.GetString();
            if (this._field != null) this._field.SetValue(null, Convert.ChangeType(value, this._fieldType, CultureInfo.InvariantCulture));
            this.Changed?.Invoke(this, old);
        }

        public void Revert() {
            this.SetValue(this.defaultValue);
        }

        public string ToDisplayString() {
            string value = this.HasFlag(FCVAR.PROTECTED) ? "PROTECTED" : this.GetString();
            return $"\"{this.name}\" = \"{value}\" ( def. \"{this.defaultValue}\" )\n{this.help}";
        }
    }
}