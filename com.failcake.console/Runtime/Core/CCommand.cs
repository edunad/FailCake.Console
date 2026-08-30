#region

using System.Text;

#endregion

namespace FailCake.Console {
	public sealed class CCommand {
		public static readonly int MAX_ARGS = 64;

		public int argc;
		public string[] argv;
		public string argS;
		public string commandString;
		public ConsoleContext context;

		public CCommand() {
			this.argv = new string[CCommand.MAX_ARGS];
			this.argS = string.Empty;
			this.commandString = string.Empty;
		}

		public CCommand(string line) : this(line, default(ConsoleContext)) { }

		public CCommand(string line, ConsoleContext context) : this() {
			this.context = context;
			this.Tokenize(line);
		}

		public string Arg(int index) {
			return index >= 0 && index < this.argc ? this.argv[index] : string.Empty;
		}

		public string GetCommandString() {
			return this.commandString;
		}

		public bool Tokenize(string line) {
			this.argc = 0;
			this.commandString = line ?? string.Empty;
			this.argS = string.Empty;

			if (string.IsNullOrEmpty(line)) return false;

			int i = 0;
			int n = line.Length;
			int argv0End = 0;

			while (i < n) {
				while (i < n && char.IsWhiteSpace(line[i])) i++;
				if (i >= n) break;
				if (this.argc >= CCommand.MAX_ARGS) break;

				bool inQuote = false;
				StringBuilder sb = new StringBuilder();
				while (i < n) {
					char c = line[i];
					if (inQuote && c == '\\' && i + 1 < n) {
						char escaped = line[i + 1];
						switch (escaped) {
						case '\\':
						case '"':
							sb.Append(escaped);
							i += 2;
							continue;
						case 'n':
							sb.Append('\n');
							i += 2;
							continue;
						case 'r':
							sb.Append('\r');
							i += 2;
							continue;
						case 't':
							sb.Append('\t');
							i += 2;
							continue;
						}
					}

					if (c == '"') {
						inQuote = !inQuote;
						i++;
						continue;
					}

					if (!inQuote && char.IsWhiteSpace(c)) break;
					sb.Append(c);
					i++;
				}

				this.argv[this.argc++] = sb.ToString();
				if (this.argc == 1) argv0End = i;
			}

			if (this.argc > 0 && argv0End < n) this.argS = line[argv0End..].TrimStart();
			return this.argc > 0;
		}
	}
}
