#region

using System.Linq;
using PurrNet;

#endregion

namespace FailCake.Console.Plugins {
	public static class ConsolePurrNetBridge {
		#region STATIC

		private const int MAX_REMOTE_COMMAND_CHARACTERS = 4096;
		private static bool REPLICATED_CVARS_HOOKED;

		[ServerRpc(requireOwnership: false)]
		public static void ExecConServer(string line, RPCInfo info = default(RPCInfo)) {
			if (string.IsNullOrWhiteSpace(line)) return;
			if (line.Length > ConsolePurrNetBridge.MAX_REMOTE_COMMAND_CHARACTERS) {
				ConsolePurrNetBridge.ReceiveConOutput(info.sender, "Command is too long.");
				return;
			}

			string output = Console.ExecuteCaptured(line, ConsoleContext.Remote(info.sender));
			if (!string.IsNullOrEmpty(output)) ConsolePurrNetBridge.ReceiveConOutput(info.sender, output);
		}

		[ServerRpc(requireOwnership: false)]
		public static void RequestConSync(RPCInfo info = default(RPCInfo)) {
			Console.OutputManifest(out string manifest, out string values);
			ConsolePurrNetBridge.ReceiveConManifest(info.sender, manifest, values);
		}

		public static void ExecConOnClient(PlayerID target, string line) {
			NetworkManager manager = NetworkManager.main;
			if (!manager || !manager.isServer) return;
			ConsolePurrNetBridge.ReceiveConExec(target, line);
		}

		internal static void HookReplicatedCVars() {
			if (ConsolePurrNetBridge.REPLICATED_CVARS_HOOKED) return;
			ConsolePurrNetBridge.REPLICATED_CVARS_HOOKED = true;
			Console.Scan();
			foreach (ConsoleVar cv in ConsoleRegistry.GetAll().OfType<ConsoleVar>().Where(ConsolePurrNetBridge.IsReplicated))
				cv.Changed += ConsolePurrNetBridge.OnConCvarChanged;
		}

		internal static void UnhookReplicatedCVars() {
			if (!ConsolePurrNetBridge.REPLICATED_CVARS_HOOKED) return;
			ConsolePurrNetBridge.REPLICATED_CVARS_HOOKED = false;
			foreach (ConsoleVar cv in ConsoleRegistry.GetAll().OfType<ConsoleVar>().Where(ConsolePurrNetBridge.IsReplicated))
				cv.Changed -= ConsolePurrNetBridge.OnConCvarChanged;
		}

		[TargetRpc]
		private static void ReceiveConOutput(PlayerID target, string output) {
			Console.Response(output);
		}

		[ObserversRpc(excludeSender: true)]
		private static void ReceiveConCvarSync(string conName, string value) {
			ConsoleDispatcher.ApplyReplicated(conName, value);
		}

		[TargetRpc]
		private static void ReceiveConManifest(PlayerID target, string manifest, string values) {
			Console.ApplyManifest(manifest, values);
		}

		[TargetRpc]
		private static void ReceiveConExec(PlayerID target, string line) {
			CCommand cmd = new CCommand(line, ConsoleContext.ClientLocal());
			if (cmd.argc == 0) return;

			ConsoleEntry entry = ConsoleRegistry.Find(cmd.Arg(0));
			if (entry == null || !entry.HasFlag(FCVAR.SERVER_CAN_EXECUTE)) return;

			Console.OnCLCommand?.Invoke(cmd, null);
		}

		private static bool IsReplicated(ConsoleVar cv) {
			return cv.HasFlag(FCVAR.REPLICATED);
		}

		private static void OnConCvarChanged(ConsoleVar cv, string oldValue) {
			NetworkManager manager = NetworkManager.main;
			if (!manager || !manager.isServer) return;
			ConsolePurrNetBridge.ReceiveConCvarSync(cv.name, cv.GetString());
		}

		#endregion
	}
}
