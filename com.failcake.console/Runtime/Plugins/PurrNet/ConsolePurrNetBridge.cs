#region

using System.Linq;
using PurrNet;
using UnityEngine;

#endregion

namespace FailCake.Console.Plugins
{
    [DisallowMultipleComponent]
    public sealed class ConsolePurrNetBridge : NetworkBehaviour
    {
        #region STATIC

        public static ConsolePurrNetBridge BRIDGE;

        #endregion

        #region HOOKS

        protected override void OnSpawned(bool asServer) {
            base.OnSpawned(asServer);
            ConsolePurrNetBridge.BRIDGE = this;

            if (asServer)
                ConsolePurrNetBridge.HookReplicatedCVars();
            else
                this.RequestConSync();
        }

        protected override void OnDespawned(bool asServer) {
            base.OnDespawned(asServer);

            if (asServer)
                ConsolePurrNetBridge.UnhookReplicatedCVars();
            else
                ConsoleRegistry.ClearRemoteStubs();

            if (ConsolePurrNetBridge.BRIDGE == this) ConsolePurrNetBridge.BRIDGE = null;
        }

        #endregion

        #region RPC

        [ServerRpc(requireOwnership: false)]
        public void ExecConServer(string line, RPCInfo info = default(RPCInfo)) {
            string output = Console.ExecuteCaptured(line, ConsoleContext.Remote(info.sender));
            if (!string.IsNullOrEmpty(output)) this.ReceiveConOutput(info.sender, output);
        }

        [TargetRpc]
        private void ReceiveConOutput(PlayerID target, string output) {
            Console.Msg(output);
        }

        [ObserversRpc(excludeSender: true)]
        private void ReceiveConCvarSync(string conName, string value) {
            ConsoleDispatcher.ApplyReplicated(conName, value);
        }

        [ServerRpc(requireOwnership: false)]
        public void RequestConSync(RPCInfo info = default(RPCInfo)) {
            Console.OutputManifest(out string manifest, out string values);
            this.ReceiveConManifest(info.sender, manifest, values);
        }

        [TargetRpc]
        private void ReceiveConManifest(PlayerID target, string manifest, string values) {
            Console.ApplyManifest(manifest, values);
        }

        [Server]
        public void ExecConOnClient(PlayerID target, string line) {
            this.ReceiveConExec(target, line);
        }

        [TargetRpc]
        private void ReceiveConExec(PlayerID target, string line) {
            CCommand cmd = new CCommand(line);
            if (cmd.argc == 0) return;

            ConsoleEntry entry = ConsoleRegistry.Find(cmd.Arg(0));
            if (entry == null || !entry.HasFlag(FCVAR.SERVER_CAN_EXECUTE)) return;

            Console.OnCLCommand?.Invoke(cmd, null);
        }

        #endregion

        #region PRIVATE

        private static void HookReplicatedCVars() {
            Console.Scan();
            foreach (ConsoleVar cv in ConsoleRegistry.GetAll().OfType<ConsoleVar>().Where(ConsolePurrNetBridge.IsReplicated)) cv.Changed += ConsolePurrNetBridge.OnConCvarChanged;
        }

        private static void UnhookReplicatedCVars() {
            foreach (ConsoleVar cv in ConsoleRegistry.GetAll().OfType<ConsoleVar>().Where(ConsolePurrNetBridge.IsReplicated)) cv.Changed -= ConsolePurrNetBridge.OnConCvarChanged;
        }

        private static bool IsReplicated(ConsoleVar cv) {
            return cv.HasFlag(FCVAR.REPLICATED);
        }

        private static void OnConCvarChanged(ConsoleVar cv, string oldValue) {
            if (!ConsolePurrNetBridge.BRIDGE || !ConsolePurrNetBridge.BRIDGE.isSpawned) return;
            ConsolePurrNetBridge.BRIDGE.ReceiveConCvarSync(cv.name, cv.GetString());
        }

        #endregion
    }
}