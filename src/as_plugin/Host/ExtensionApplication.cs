using System;
using System.Windows.Media;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using EMV.AdvanceSteel.Plugin.Server;

// ImplicitUsings + UseWindowsForms make a bare 'Application' ambiguous with
// System.Windows.Forms.Application, so the AutoCAD one is addressed through an alias.
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

// The AutoCAD ribbon lives in AdWindows.dll. Keeping it behind an alias avoids clashing with the
// WPF types that ImplicitUsings pulls in (RibbonButton, Orientation, ...).
using AdWin = Autodesk.Windows;

// System.Drawing (WinForms) and System.Windows.Media (WPF) export the same drawing names.
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using MediaBrushes = System.Windows.Media.Brushes;

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

                EmvRibbon.Install(ed);
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
                EmvRibbon.Uninstall();
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

        /// <summary>Rebuilds the ribbon tab by hand if a workspace switch dropped it.</summary>
        [CommandMethod("EMV_RIBBON")]
        public void RibbonCommand()
        {
            var ed = AcApplication.DocumentManager.MdiActiveDocument?.Editor;
            EmvRibbon.Install(ed);
        }
    }

    /// <summary>
    /// CONTRACT-011 - the <c>EMV AI Tools</c> ribbon tab. Each button only sends the matching
    /// AutoCAD command string, so the model work always happens on the AutoCAD main thread inside
    /// <see cref="EMV.AdvanceSteel.Plugin.Commands.AutoCadCommands"/> and never on a WPF callback.
    /// </summary>
    internal static class EmvRibbon
    {
        private const string TabId = "EMV_AI_TOOLS_TAB";

        private static bool _waitingForRibbon;
        private static bool _eventsHooked;

        /// <summary>
        /// Builds the tab now if the ribbon already exists. During a cold AutoCAD start
        /// <c>ComponentManager.Ribbon</c> is still null, so the build is deferred to
        /// <c>ItemInitialized</c>, which fires once the ribbon control is up.
        /// Also hooks workspace change and idle events to survive workspace restorations.
        /// </summary>
        internal static void Install(Editor? ed)
        {
            try
            {
                HookEvents();

                if (AdWin.ComponentManager.Ribbon != null)
                {
                    Build(ed);
                    return;
                }

                if (!_waitingForRibbon)
                {
                    _waitingForRibbon = true;
                    AdWin.ComponentManager.ItemInitialized += OnItemInitialized;
                }
            }
            catch (System.Exception ex)
            {
                // A missing ribbon must never take the IPC server down with it.
                ed?.WriteMessage($"\n[EMV-MCP-AS] Ribbon unavailable: {ex.Message}\n");
            }
        }

        private static void HookEvents()
        {
            if (_eventsHooked) return;
            _eventsHooked = true;

            AcApplication.SystemVariableChanged += OnSystemVariableChanged;
            AcApplication.Idle += OnIdleCheckRibbon;
        }

        private static void OnSystemVariableChanged(object? sender, SystemVariableChangedEventArgs e)
        {
            if (string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase))
            {
                Build(AcApplication.DocumentManager.MdiActiveDocument?.Editor);
            }
        }

        private static int _idleCheckCount = 0;
        private static void OnIdleCheckRibbon(object? sender, EventArgs e)
        {
            // Check during the first few idle loops after launch or workspace switches
            var ribbon = AdWin.ComponentManager.Ribbon;
            if (ribbon != null)
            {
                if (ribbon.FindTab(TabId) == null)
                {
                    Build(AcApplication.DocumentManager.MdiActiveDocument?.Editor);
                }
                else
                {
                    // If ribbon tab is present and stable after initial checks, we can ease off
                    _idleCheckCount++;
                    if (_idleCheckCount > 10)
                    {
                        AcApplication.Idle -= OnIdleCheckRibbon;
                    }
                }
            }
        }

        internal static void Uninstall()
        {
            try
            {
                if (_waitingForRibbon)
                {
                    AdWin.ComponentManager.ItemInitialized -= OnItemInitialized;
                    _waitingForRibbon = false;
                }

                if (_eventsHooked)
                {
                    AcApplication.SystemVariableChanged -= OnSystemVariableChanged;
                    AcApplication.Idle -= OnIdleCheckRibbon;
                    _eventsHooked = false;
                }

                var ribbon = AdWin.ComponentManager.Ribbon;
                var tab = ribbon?.FindTab(TabId);
                if (ribbon != null && tab != null) ribbon.Tabs.Remove(tab);
            }
            catch (System.Exception)
            {
                // Shutting down: nothing useful left to report.
            }
        }

        private static void OnItemInitialized(object? sender, AdWin.RibbonItemEventArgs e)
        {
            if (AdWin.ComponentManager.Ribbon == null) return;

            AdWin.ComponentManager.ItemInitialized -= OnItemInitialized;
            _waitingForRibbon = false;
            Build(AcApplication.DocumentManager.MdiActiveDocument?.Editor);
        }

        private static void Build(Editor? ed)
        {
            var ribbon = AdWin.ComponentManager.Ribbon;
            if (ribbon == null) return;

            // Re-entrant by design: Initialize, EMV_RIBBON and ItemInitialized can all land here.
            if (ribbon.FindTab(TabId) != null) return;

            var tab = new AdWin.RibbonTab
            {
                Title = "EMV AI Tools",
                Id = TabId,
                IsVisible = true
            };

            tab.Panels.Add(BuildPanel("Generativo",
                CreateButton(
                    "Nave\nParametrica", "EMV_PARAMETRIC_WAREHOUSE",
                    "Nave paramétrica",
                    "Genera una nave industrial completa en celosía (pórticos, correas, arriostramientos, "
                        + "rejillas y niveles) a partir de luz, longitud, separación y alturas.",
                    Icons.Warehouse),
                CreateButton(
                    "Portico\nSimple", "EMV_PORTAL_FRAME",
                    "Pórtico simple",
                    "Genera un único pórtico a dos aguas con pilares, dinteles y placas base.",
                    Icons.PortalFrame)));

            tab.Panels.Add(BuildPanel("Coordinacion BIM",
                CreateButton(
                    "Rejillas\ny Niveles", "EMV_GRIDS_LEVELS",
                    "Rejillas y niveles",
                    "Crea los ejes estructurales transversales y longitudinales más los niveles de base "
                        + "y alero, listos para coordinar con Revit.",
                    Icons.Grids)));

            tab.Panels.Add(BuildPanel("Auditoria",
                CreateButton(
                    "Detailing\nDoctor", "EMV_DOCTOR",
                    "Detailing doctor",
                    "Audita y repara roles de pieza, piezas principales de conjunto y anclajes de placa base.",
                    Icons.Doctor),
                CreateButton(
                    "BOM /\nComputo", "EMV_BOM",
                    "BOM / cómputo",
                    "Calcula tonelaje, perfiles lineales, chapas y área de pintura del modelo activo.",
                    Icons.Bom),
                CreateButton(
                    "Captura\n3D", "EMV_VIEWPORT",
                    "Captura 3D",
                    "Captura la vista activa del modelo y la guarda como PNG.",
                    Icons.Viewport)));

            ribbon.Tabs.Add(tab);
            ed?.WriteMessage("\n[EMV-MCP-AS] Ribbon tab 'EMV AI Tools' ready.\n");
        }

        private static AdWin.RibbonPanel BuildPanel(string title, params AdWin.RibbonButton[] buttons)
        {
            var source = new AdWin.RibbonPanelSource { Title = title };
            foreach (var button in buttons) source.Items.Add(button);
            return new AdWin.RibbonPanel { Source = source };
        }

        private static AdWin.RibbonButton CreateButton(
            string text,
            string command,
            string tooltipTitle,
            string tooltipContent,
            ImageSource icon)
        {
            return new AdWin.RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                LargeImage = icon,
                Image = icon,
                Size = AdWin.RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                // The leading underscore forces the English command name regardless of UI locale.
                CommandParameter = "_" + command + " ",
                CommandHandler = SendCommand.Instance,
                ToolTip = new AdWin.RibbonToolTip
                {
                    Title = tooltipTitle,
                    Content = tooltipContent,
                    Command = command,
                    IsHelpEnabled = false
                }
            };
        }

        /// <summary>
        /// Every button shares this handler: it only queues the command string on the active
        /// document, which is what keeps Advance Steel work on its own single thread.
        /// </summary>
        private sealed class SendCommand : System.Windows.Input.ICommand
        {
            internal static readonly SendCommand Instance = new();

            public event EventHandler? CanExecuteChanged
            {
                add { System.Windows.Input.CommandManager.RequerySuggested += value; }
                remove { System.Windows.Input.CommandManager.RequerySuggested -= value; }
            }

            public bool CanExecute(object? parameter) =>
                AcApplication.DocumentManager.MdiActiveDocument != null;

            public void Execute(object? parameter)
            {
                if (parameter is not AdWin.RibbonButton button) return;
                if (button.CommandParameter is not string macro || macro.Length == 0) return;

                var doc = AcApplication.DocumentManager.MdiActiveDocument;
                doc?.SendStringToExecute(macro, true, false, true);
            }
        }

        /// <summary>
        /// Vector glyphs drawn in code. Shipping geometry instead of PNG resources keeps the
        /// bundle a single DLL and scales cleanly on high-DPI screens.
        /// </summary>
        private static class Icons
        {
            private static readonly MediaColor Accent = MediaColor.FromRgb(0x3C, 0x9B, 0xE8);
            private static readonly MediaColor Warn = MediaColor.FromRgb(0xE8, 0x8B, 0x3C);
            private static readonly MediaColor Ok = MediaColor.FromRgb(0x62, 0xC4, 0x70);

            internal static readonly ImageSource Warehouse = Glyph(
                "M3,29 L3,17 L16,7 L29,17 L29,29 M3,29 L29,29 M3,17 L29,17 M16,7 L9,29 M16,7 L23,29", Accent);

            internal static readonly ImageSource PortalFrame = Glyph(
                "M5,29 L5,14 M27,29 L27,14 M5,14 L16,6 L27,14 M2,29 L8,29 M24,29 L30,29", Accent);

            internal static readonly ImageSource Grids = Glyph(
                "M4,4 L4,28 M12,4 L12,28 M20,4 L20,28 M28,4 L28,28 M4,10 L28,10 M4,22 L28,22", Accent);

            internal static readonly ImageSource Doctor = Glyph(
                "M16,6 L16,26 M6,16 L26,16 M4,16 A12,12 0 1 1 28,16 A12,12 0 1 1 4,16", Ok);

            internal static readonly ImageSource Bom = Glyph(
                "M6,8 L26,8 M6,14 L26,14 M6,20 L20,20 M6,26 L22,26", Warn);

            internal static readonly ImageSource Viewport = Glyph(
                "M4,9 L28,9 L28,25 L4,25 Z M11,9 L13,5 L19,5 L21,9 M16,17 m-4,0 a4,4 0 1,0 8,0 a4,4 0 1,0 -8,0", Warn);

            private static ImageSource Glyph(string pathData, MediaColor color)
            {
                var pen = new MediaPen(new SolidColorBrush(color), 2.0)
                {
                    LineJoin = PenLineJoin.Round,
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };

                var group = new DrawingGroup();

                // A transparent 32x32 backdrop pins the drawing's bounds so the ribbon does not
                // crop or re-scale each glyph differently.
                group.Children.Add(new GeometryDrawing(
                    MediaBrushes.Transparent, null, new RectangleGeometry(new System.Windows.Rect(0, 0, 32, 32))));
                group.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse(pathData)));

                var image = new DrawingImage(group);
                image.Freeze();
                return image;
            }
        }
    }
}
