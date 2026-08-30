#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

#endregion

namespace FailCake.Console {
	public static class ConsoleCfg {
		#region STATIC

		public static string ROOT_OVERRIDE;

		#endregion

		public static string GetRoot() {
			if (ConsoleCfg.ROOT_OVERRIDE != null) return Path.GetFullPath(ConsoleCfg.ROOT_OVERRIDE);
			#if UNITY_SERVER
			return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "cfg"));
			#else
			return Path.GetFullPath(Path.Combine(Application.persistentDataPath, "cfg"));
			#endif
		}

		public static void BootExec() {
			ConsoleCfg.Exec("config.cfg", false);
			#if UNITY_SERVER
			ConsoleCfg.Exec("server.cfg", false);
			#endif
			ConsoleCfg.Exec("autoexec.cfg", false);
		}

		public static bool Exec(string file, bool logMissing = true) {
			if (string.IsNullOrEmpty(file)) return false;

			string path = ConsoleCfg.ResolvePath(file);
			if (!File.Exists(path)) {
				if (logMissing) ConsoleOutput.Add($"exec: couldn't exec {file}");
				return false;
			}

			ConsoleOutput.Add($"executing {file}");

			ConsoleContext context = ConsoleContext.Config(Console.IS_SERVER_PROCESS);
			foreach (string raw in File.ReadAllLines(path)) {
				string line = raw.Trim();
				if (line.Length == 0 || line.StartsWith("//")) continue;

				ConsoleDispatcher.Execute(line, context);
			}

			return true;
		}

		public static void WriteConfig() {
			string root = ConsoleCfg.GetRoot();
			Directory.CreateDirectory(root);
			string path = Path.Combine(root, "config.cfg");
			string tempPath = path + ".tmp";

			IEnumerable<string> lines = ConsoleRegistry.GetAll()
				.OfType<ConsoleVar>()
				.Where(ConsoleCfg.IsArchivable)
				.Select(ConsoleCfg.FormatLine);

			File.WriteAllLines(tempPath, lines);
			if (File.Exists(path))
				File.Replace(tempPath, path, null);
			else
				File.Move(tempPath, path);
			ConsoleOutput.Add($"host_writeconfig: wrote {path}");
		}

		public static string ResolvePath(string file) {
			if (string.IsNullOrWhiteSpace(file)) throw new ArgumentException("Cfg filename cannot be empty.", nameof(file));

			string trimmed = file.Trim();
			if (Path.IsPathRooted(trimmed)) throw new ArgumentException("Cfg path must be relative to the cfg directory.", nameof(file));

			string name = trimmed.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + ".cfg";
			string root = ConsoleCfg.GetRoot();
			string path = Path.GetFullPath(Path.Combine(root, name));
			string relative = Path.GetRelativePath(root, path);

			if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
				relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
				throw new ArgumentException("Cfg path must stay within the cfg directory.", nameof(file));

			return path;
		}

		#region PRIVATE METHODS

		private static bool IsArchivable(ConsoleVar cv) {
			return cv.HasFlag(FCVAR.ARCHIVE) && !cv.HasFlag(FCVAR.HIDDEN) && !cv.HasFlag(FCVAR.PROTECTED) && !cv.HasFlag(FCVAR.REPLICATED);
		}

		private static string FormatLine(ConsoleVar cv) {
			return $"\"{ConsoleCfg.Escape(cv.name)}\" \"{ConsoleCfg.Escape(cv.GetString())}\"";
		}

		private static string Escape(string value) {
			StringBuilder result = new StringBuilder(value.Length);
			for (int i = 0; i < value.Length; i++) {
				switch (value[i]) {
				case '\\':
					result.Append("\\\\");
					break;
				case '"':
					result.Append("\\\"");
					break;
				case '\r':
					result.Append("\\r");
					break;
				case '\n':
					result.Append("\\n");
					break;
				case '\t':
					result.Append("\\t");
					break;
				default:
					result.Append(value[i]);
					break;
				}
			}

			return result.ToString();
		}

		#endregion
	}
}
