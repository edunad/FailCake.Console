#region

using PurrNet;
using PurrNet.Transports;

#endregion

namespace FailCake.Console.Plugins {
	[ConsolePlugin]
	public sealed class ConsolePurrNetPlugin : ConsolePlugin {
		#region STATIC

		private static void OnServerConnectionState(ConnectionState state) {
			if (state == ConnectionState.Connected)
				ConsolePurrNetBridge.HookReplicatedCVars();
			else if (state == ConnectionState.Disconnected)
				ConsolePurrNetBridge.UnhookReplicatedCVars();
		}

		private static void OnClientConnectionState(ConnectionState state) {
			if (state == ConnectionState.Connected)
				ConsolePurrNetBridge.RequestConSync();
			else if (state == ConnectionState.Disconnected)
				ConsoleRegistry.ClearRemoteStubs();
		}

		private static bool IsMultiplayer() {
			NetworkManager manager = NetworkManager.main;
			return manager && (manager.isServer || manager.isClient);
		}

		private static (bool success, string response) ExecuteServerCommand(CCommand command, object userData) {
			NetworkManager manager = NetworkManager.main;
			if (!manager || !manager.isClient) return (false, "Not connected to server.");

			ConsolePurrNetBridge.ExecConServer(command.GetCommandString());
			return (true, null);
		}

		private static (bool success, string response) ExecuteClientCommand(CCommand command, object userData) {
			return (true, Console.ExecuteCaptured(command.GetCommandString(), ConsoleContext.ClientLocal(userData)));
		}

		#endregion

		protected override void OnLoad() {
			Console.IsMultiplayer = ConsolePurrNetPlugin.IsMultiplayer;
			Console.OnSVCommand = ConsolePurrNetPlugin.ExecuteServerCommand;
			Console.OnCLCommand = ConsolePurrNetPlugin.ExecuteClientCommand;

			NetworkManager.onAnyServerConnectionState -= ConsolePurrNetPlugin.OnServerConnectionState;
			NetworkManager.onAnyClientConnectionState -= ConsolePurrNetPlugin.OnClientConnectionState;
			NetworkManager.onAnyServerConnectionState += ConsolePurrNetPlugin.OnServerConnectionState;
			NetworkManager.onAnyClientConnectionState += ConsolePurrNetPlugin.OnClientConnectionState;

			NetworkManager manager = NetworkManager.main;
			if (!manager) return;
			if (manager.isServer) ConsolePurrNetPlugin.OnServerConnectionState(ConnectionState.Connected);
			if (manager.isClient) ConsolePurrNetPlugin.OnClientConnectionState(ConnectionState.Connected);
		}

		protected override void OnUnload() {
			NetworkManager.onAnyServerConnectionState -= ConsolePurrNetPlugin.OnServerConnectionState;
			NetworkManager.onAnyClientConnectionState -= ConsolePurrNetPlugin.OnClientConnectionState;

			ConsolePurrNetBridge.UnhookReplicatedCVars();
			ConsoleRegistry.ClearRemoteStubs();
			Console.ResetNetworkHandlers();
		}
	}
}
