#region

using System;

#endregion

namespace FailCake.Console
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class OnCVarChangedAttribute : Attribute
    {
        public readonly string name;

        public OnCVarChangedAttribute(string name) {
            this.name = name;
        }
    }
}