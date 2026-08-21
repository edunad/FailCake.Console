#region

using System;

#endregion

namespace FailCake.Console
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class CommandAttribute : Attribute
    {
        public readonly string name;
        public readonly string help;
        public readonly FCVAR flags;

        public CommandAttribute(string name, string help = null, FCVAR flags = FCVAR.NONE) {
            this.name = name;
            this.help = help;
            this.flags = flags;
        }
    }
}