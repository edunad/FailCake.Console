#region

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

#endregion

namespace FailCake.Console
{
    public static class ConsoleDispatcher
    {
        public static void Execute(string line, ConsoleContext context) {
            if (string.IsNullOrEmpty(line)) return;
            foreach (string sub in ConsoleDispatcher.SplitSemicolons(line))
            {
                string trimmed = sub.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("//")) continue;
                ConsoleDispatcher.ExecuteOne(trimmed, context);
            }
        }

        public static string ExecuteCaptured(string line, ConsoleContext context) {
            ConsoleOutput.CaptureBuffer buffer = new ConsoleOutput.CaptureBuffer();
            ConsoleOutput.CaptureBuffer previous = ConsoleOutput.CAPTURE;

            ConsoleOutput.CAPTURE = buffer;

            try
            {
                ConsoleDispatcher.Execute(line, context);
            }
            finally
            {
                ConsoleOutput.CAPTURE = previous;
            }

            return buffer.GetText();
        }

        public static void ApplyReplicated(string name, string value) {
            ConsoleVar cv = ConsoleRegistry.FindVar(name);
            cv?.SetValue(value);
        }

        private static void ExecuteOne(string line, ConsoleContext context) {
            CCommand cmd = new CCommand(line, context);
            if (cmd.argc == 0) return;

            if (context.echo) ConsoleOutput.Add($"] {line}");

            string name = cmd.Arg(0);
            ConsoleEntry entry = ConsoleRegistry.Find(name);

            if (entry == null)
            {
                ConsoleOutput.Add($"Unknown command \"{name}\"");
                return;
            }

            if (context.source == ConSource.Remote && !entry.HasFlag(FCVAR.SERVER))
            {
                ConsoleOutput.Add($"Unknown command \"{name}\"");
                return;
            }

            if (!Console.IS_SERVER_PROCESS && entry.HasFlag(FCVAR.SERVER))
            {
                ConsoleDispatcher.ForwardToServer(cmd, context);
                return;
            }

            if (entry is ConsoleStubEntry)
            {
                ConsoleOutput.Add($"Unknown command \"{name}\"");
                return;
            }

            if (!ConsoleDispatcher.PassGates(entry, context)) return;

            switch (entry)
            {
                case ConsoleVar cv:
                    ConsoleDispatcher.HandleVar(cv, cmd, context);
                    break;
                case ConsoleCommand cc:
                    ConsoleDispatcher.HandleCommand(cc, cmd);
                    break;
            }
        }

        private static bool PassGates(ConsoleEntry entry, ConsoleContext context) {
            if (context.source == ConSource.ServerConsole) return true;

            if (entry.HasFlag(FCVAR.CHEAT) && Console.IsMultiplayer())
            {
                ConsoleVar sv = ConsoleRegistry.FindVar("sv_cheats");
                if (sv == null || !sv.GetBool())
                {
                    ConsoleOutput.Add($"Can't use cheat command {entry.name} in multiplayer, unless the server has sv_cheats set to 1.");
                    return false;
                }
            }

			#if UNITY_SERVER
			if (entry.HasFlag(FCVAR.ADMIN) && !ConsoleDispatcher.HasAdminAccess(context)) {
				ConsoleOutput.Add("You don't have permission to run this command.");
				return false;
			}
			#endif

            return true;
        }

        private static void HandleVar(ConsoleVar cv, CCommand cmd, ConsoleContext context) {
            if (cmd.argc == 1)
            {
                Console.Response(cv.ToDisplayString());
                return;
            }

			#if UNITY_SERVER
			if (context.source == ConSource.Remote && !ConsoleDispatcher.HasAdminAccess(context)) {
				ConsoleOutput.Add("You don't have permission to change this cvar.");
				return;
			}
			#endif

            if (cv.HasFlag(FCVAR.REPLICATED))
                if (!Console.IS_SERVER_PROCESS && context.source != ConSource.Remote && Console.IsMultiplayer())
                {
                    ConsoleOutput.Add($"Can't change replicated ConsoleVar {cv.name}. Server enforces: \"{cv.GetString()}\"");
                    return;
                }

            string value = cmd.Arg(1);
            try
            {
                cv.SetValue(value);
            }
            catch (Exception ex)
            {
                ConsoleOutput.Add($"Failed to set {cv.name}: {ex.Message}");
            }
        }

        private static void HandleCommand(ConsoleCommand cc, CCommand cmd) {
            try
            {
                (object result, string error) = cc.Invoke(cmd);
                if (error != null)
                {
                    ConsoleOutput.Add(error);
                    return;
                }

                ConsoleDispatcher.PrintResult(result);
            }
            catch (Exception ex)
            {
                string msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                ConsoleOutput.Add($"Error: {msg}");
            }
        }

        private static void ForwardToServer(CCommand command, ConsoleContext context) {
            if (Console.OnSVCommand == null) throw new UnityException("Missing OnSVCommand");

            (bool success, string response) = Console.OnSVCommand.Invoke(command, context.userData);
            if (!success && !string.IsNullOrEmpty(response)) ConsoleOutput.Add(response);
        }

		#if UNITY_SERVER
		private static bool HasAdminAccess(ConsoleContext context) {
			if (context.source == ConSource.ServerConsole) return true;
			return context.source == ConSource.Remote && context.userData != null && Console.IsAdmin(context.userData);
		}
		#endif

        private static void PrintResult(object result) {
            switch (result)
            {
                case null:
                    return;
                case string s:
                {
                    if (s.Length > 0) Console.Response(s);
                    return;
                }
                case IEnumerable ie:
                {
                    foreach (object item in ie) Console.Response(item != null ? item.ToString() : string.Empty);
                    return;
                }
                default:
                    Console.Response(result.ToString());
                    break;
            }
        }

        private static IEnumerable<string> SplitSemicolons(string line) {
            bool inQuote = false;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuote && c == '\\' && i + 1 < line.Length)
                {
                    sb.Append(c).Append(line[++i]);
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuote = !inQuote;
                        break;
                    case ';' when !inQuote:
                        yield return sb.ToString();
                        sb.Clear();
                        continue;
                }

                sb.Append(c);
            }

            yield return sb.ToString();
        }
    }
}
