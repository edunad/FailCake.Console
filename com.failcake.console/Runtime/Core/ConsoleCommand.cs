#region

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

#endregion

namespace FailCake.Console {
	public sealed class ConsoleCommand : ConsoleEntry {
		public readonly MethodInfo method;
		public readonly ParameterInfo[] parameters;

		internal ConsoleCommand(string name, string help, FCVAR flags, MethodInfo method) : base(name, help, flags) {
			this.method = method;
			this.parameters = method.GetParameters();
		}

		public (object result, string error) Invoke(CCommand command) {
			if (command.argc - 1 < this.RequiredArgCount()) return (null, $"Usage: {this.GetSignature()}");

			object[] bound = new object[this.parameters.Length];
			int argIndex = 1;
			for (int i = 0; i < this.parameters.Length; i++) {
				ParameterInfo parameter = this.parameters[i];
				if (parameter.ParameterType == typeof(CCommand)) {
					bound[i] = command;
					continue;
				}

				bound[i] = argIndex < command.argc
					? ConsoleParser.Parse(command.Arg(argIndex), parameter.ParameterType)
					: parameter.DefaultValue;
				argIndex++;
			}

			return (this.method.Invoke(null, bound), null);
		}

		public string GetSignature() {
			string signature = string.Join(" ", this.parameters.Where(ConsoleCommand.IsBoundParam).Select(ConsoleCommand.ParamSignature));
			return signature.Length > 0 ? $"{this.name} {signature}" : this.name;
		}

		#region PRIVATE METHODS

		private int RequiredArgCount() {
			return this.parameters.Count(ConsoleCommand.IsRequiredParam);
		}

		private static string ParamSignature(ParameterInfo parameter) {
			if (!parameter.HasDefaultValue) return $"<{parameter.ParameterType.Name} {parameter.Name}>";

			string value = parameter.DefaultValue == null
				? "null"
				: Convert.ToString(parameter.DefaultValue, CultureInfo.InvariantCulture);
			if (parameter.ParameterType == typeof(string) && parameter.DefaultValue != null) value = $"\"{value}\"";
			return $"[{parameter.ParameterType.Name} {parameter.Name}={value}]";
		}

		private static bool IsBoundParam(ParameterInfo parameter) {
			return parameter.ParameterType != typeof(CCommand);
		}

		private static bool IsRequiredParam(ParameterInfo parameter) {
			return ConsoleCommand.IsBoundParam(parameter) && !parameter.HasDefaultValue;
		}

		#endregion
	}
}
