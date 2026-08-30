namespace FailCake.Console {
	public abstract class ConsoleEntry {
		public readonly string name;
		public readonly string help;
		public readonly FCVAR flags;

		protected ConsoleEntry(string name, string help, FCVAR flags) {
			this.name = name;
			this.help = help ?? string.Empty;
			this.flags = (flags & FCVAR.ADMIN) != 0 ? flags | FCVAR.SERVER : flags;
		}

		public bool HasFlag(FCVAR flag) {
			return (this.flags & flag) != 0;
		}
	}
}
