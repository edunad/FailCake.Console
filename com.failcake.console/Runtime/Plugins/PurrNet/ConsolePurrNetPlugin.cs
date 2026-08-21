#region

using PurrNet;
using PurrNet.Transports;
using UnityEngine;
using Object = UnityEngine.Object;

#endregion

namespace FailCake.Console.Plugins
{
    [ConsolePlugin]
    public sealed class ConsolePurrNetPlugin : ConsolePlugin
    {
        protected override void OnLoad() {
            Console.IsMultiplayer = ConsolePurrNetPlugin.GetIsMultiplayer;
            Console.OnSVCommand = ConsolePurrNetPlugin.HandleSVCommand;
            Console.OnCLCommand = ConsolePurrNetPlugin.HandleCLCommand;

            NetworkManager.onAnyServerConnectionState += ConsolePurrNetPlugin.OnServerConnectionState;
        }

        protected override void OnUnload() {
            NetworkManager.onAnyServerConnectionState -= ConsolePurrNetPlugin.OnServerConnectionState;
        }

        #region PRIVATE

        private static bool GetIsMultiplayer() {
            NetworkManager manager = NetworkManager.main;
            return manager && (manager.isServer || manager.isClient);
        }

        private static (bool, string) HandleSVCommand(CCommand command, object userData) {
            if (!Console.IsMultiplayer()) return (true, Console.ExecuteCaptured(command.GetCommandString(), ConsoleContext.ServerLocal(userData)));
            if (!ConsolePurrNetBridge.BRIDGE || !ConsolePurrNetBridge.BRIDGE.isSpawned) return (false, "Not connected to server.");

            ConsolePurrNetBridge.BRIDGE.ExecConServer(command.GetCommandString());
            return (true, null);
        }

        private static (bool, string) HandleCLCommand(CCommand command, object userData) {
            return (true, Console.ExecuteCaptured(command.GetCommandString(), ConsoleContext.ClientLocal(userData)));
        }

        private static void OnServerConnectionState(ConnectionState state) {
            if (state != ConnectionState.Connected || ConsolePurrNetBridge.BRIDGE) return;

            NetworkManager manager = NetworkManager.main;
            if (!manager) return;

            GameObject go = new GameObject("Console.PurrNetBridge");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<ConsolePurrNetBridge>();

            manager.Spawn(go);
        }

        #endregion
    }
}