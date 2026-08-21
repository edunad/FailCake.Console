#region

using System;

#endregion

namespace FailCake.Console
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class CVarAttribute : Attribute
    {
        public readonly string name;
        public readonly string defaultValue;
        public readonly FCVAR flags;
        public readonly string help;
        public readonly float min;
        public readonly float max;
        public readonly bool hasMin;
        public readonly bool hasMax;

        public CVarAttribute(string name, string defaultValue, FCVAR flags = FCVAR.NONE, string help = null, float min = float.NaN, float max = float.NaN) {
            this.name = name;
            this.defaultValue = defaultValue ?? string.Empty;
            this.flags = flags;
            this.help = help;
            this.hasMin = !float.IsNaN(min);
            this.hasMax = !float.IsNaN(max);
            this.min = this.hasMin ? min : 0f;
            this.max = this.hasMax ? max : 0f;
        }
    }
}