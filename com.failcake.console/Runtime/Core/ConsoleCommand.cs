#region

using System.Linq;
using System.Reflection;

#endregion

namespace FailCake.Console
{
    public sealed class ConsoleCommand : ConsoleEntry
    {
        public readonly MethodInfo method;
        public readonly ParameterInfo[] parameters;
        public readonly object[] defaults;
        public readonly bool rawArgs;

        internal ConsoleCommand(string name, string help, FCVAR flags, MethodInfo method) : base(name, help, flags) {
            this.method = method;
            this.parameters = method.GetParameters();
            this.defaults = this.parameters.Select(ConsoleCommand.ParamDefault).ToArray();
            this.rawArgs = this.parameters.Length == 1 && this.parameters[0].ParameterType == typeof(CCommand);
        }

        public (object result, string error) Invoke(CCommand args) {
            if (this.rawArgs) return (this.method.Invoke(null, new object[] { args }), null);

            int provided = args.argc - 1;
            if (provided < this.RequiredArgCount()) return (null, $"Usage: {this.GetSignature()}");

            object[] bound = new object[this.parameters.Length];
            for (int i = 0; i < this.parameters.Length; i++)
            {
                int argIndex = i + 1;
                bound[i] = argIndex < args.argc
                    ? ConsoleParser.Parse(args.Arg(argIndex), this.parameters[i].ParameterType)
                    : this.defaults[i];
            }

            return (this.method.Invoke(null, bound), null);
        }

        public string GetSignature() {
            string sig = string.Join(" ", this.parameters.Select(ConsoleCommand.ParamSignature));
            return sig.Length > 0 ? $"{this.name} {sig}" : this.name;
        }

        private int RequiredArgCount() {
            return this.parameters.Count(ConsoleCommand.IsRequiredParam);
        }

        #region PRIVATE

        private static object ParamDefault(ParameterInfo p) {
            return p.DefaultValue;
        }

        private static string ParamSignature(ParameterInfo p) {
            return p.HasDefaultValue ? $"[{p.ParameterType.Name} {p.Name}]" : $"<{p.ParameterType.Name} {p.Name}>";
        }

        private static bool IsRequiredParam(ParameterInfo p) {
            return !p.HasDefaultValue;
        }

        #endregion
    }
}