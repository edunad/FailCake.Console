#region

using System;

#endregion

namespace FailCake.Console
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ConsolePluginAttribute : Attribute
    {
        public readonly int priority;

        public ConsolePluginAttribute(int priority = 0) {
            this.priority = priority;
        }
    }
}