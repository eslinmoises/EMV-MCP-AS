using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using EMV.AdvanceSteel.Plugin.Server;

// ImplicitUsings + UseWindowsForms make a bare 'Application' ambiguous with
// System.Windows.Forms.Application, so the AutoCAD one is addressed through an alias.
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(EMV.AdvanceSteel.Plugin.Host.ExtensionApplication))]

namespace EMV.AdvanceSteel.Plugin.Host
{
    public class ExtensionApplication : IExtensionApplication
    {
        private static IpcHttpServer? _ipcServer;
        public static System.Threading.SynchronizationContext? MainSyncContext { get; private set; }

        public void Initialize()
        {
            try
            {
                MainSyncContext = System.Threading.SynchronizationContext.Current;
                var doc = AcApplication.DocumentManager.MdiActiveDocument;
                var ed = doc?.Editor;
                ed?.WriteMessage("\n[EMV-MCP-AS] Loading Advance Steel MCP Extension (.NET 8)...\n");

                _ipcServer = new IpcHttpServer(port: 5055);
                _ipcServer.Start();

                ed?.WriteMessage("\n[EMV-MCP-AS] Local IPC Server listening on http://127.0.0.1:5055/api/v1/\n");
            }
            catch (System.Exception ex)
            {
                var ed = AcApplication.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage($"\n[EMV-MCP-AS] Error starting IPC Server: {ex.Message}\n");
            }
        }

        public void Terminate()
        {
            try
            {
                _ipcServer?.Stop();
                _ipcServer = null;
            }
            catch
            {
                // Suppress on exit
            }
        }

        [CommandMethod("EMV_MCP_STATUS")]
        public void StatusCommand()
        {
            var ed = AcApplication.DocumentManager.MdiActiveDocument?.Editor;
            bool running = _ipcServer?.IsRunning ?? false;
            ed?.WriteMessage($"\n[EMV-MCP-AS] Server is {(running ? "ONLINE (port 5055)" : "OFFLINE")}\n");
        }

        [CommandMethod("EMV_MCP_RESTART")]
        public void RestartCommand()
        {
            var ed = AcApplication.DocumentManager.MdiActiveDocument?.Editor;
            ed?.WriteMessage("\n[EMV-MCP-AS] Restarting IPC Server...\n");
            _ipcServer?.Stop();
            _ipcServer = new IpcHttpServer(port: 5055);
            _ipcServer.Start();
            ed?.WriteMessage("\n[EMV-MCP-AS] Restart complete.\n");
        }
    }
}
