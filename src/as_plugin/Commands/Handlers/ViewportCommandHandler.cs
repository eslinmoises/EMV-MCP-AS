using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// GET /api/v1/viewport/capture — SPEC-004 §1 <c>capture_viewport</c>.
    /// Returns the rendered 3D viewport as a Base64 PNG so a multimodal agent can look at the model.
    /// </summary>
    public static class ViewportCommandHandler
    {
        private const int DefaultWidth = 1600;
        private const int DefaultHeight = 900;
        private const int MinDimension = 64;
        private const int MaxDimension = 4096;

        public static CommandResult Capture(CommandContext ctx)
        {
            var width = Clamp((int)ctx.GetDouble("width", DefaultWidth));
            var height = Clamp((int)ctx.GetDouble("height", DefaultHeight));

            Bitmap? bitmap = null;
            string source;

            try
            {
                bitmap = CapturePreview(ctx.Doc, width, height);
                source = "preview";

                if (bitmap == null)
                {
                    bitmap = CaptureGraphicsSystem(ctx.Doc, width, height);
                    source = "graphics_system";
                }

                if (bitmap == null)
                {
                    return CommandResult.Fail(
                        "VIEWPORT_CAPTURE_FAILED", "AutoCAD returned no rendered frame for the active viewport.", 500,
                        "Make sure a model viewport is active and visible, then retry.");
                }

                string base64;
                using (var buffer = new MemoryStream())
                {
                    bitmap.Save(buffer, ImageFormat.Png);
                    base64 = Convert.ToBase64String(buffer.ToArray());
                }

                return CommandResult.Ok(new
                {
                    image_base64 = base64,
                    format = "png",
                    width = bitmap.Width,
                    height = bitmap.Height,
                    source,
                    active_dwg = ctx.Doc.Name
                });
            }
            catch (System.Exception ex)
            {
                return CommandResult.Fail(
                    "VIEWPORT_CAPTURE_FAILED", ex.Message, 500,
                    "Viewport capture needs a visible AutoCAD window; it fails on a minimised or headless session.",
                    ex.StackTrace);
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        /// <summary>Preferred path: AutoCAD renders the document preview off-screen at the size we ask for.</summary>
        private static Bitmap? CapturePreview(Document doc, int width, int height)
        {
            try
            {
                return DocumentExtension.CapturePreviewImage(doc, (uint)width, (uint)height);
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Fallback: snapshot the live graphics-system view. Only works while the viewport is on
        /// screen, which is why it is second choice.
        /// </summary>
        private static Bitmap? CaptureGraphicsSystem(Document doc, int width, int height)
        {
            try
            {
                var manager = doc.GraphicsManager;
                if (manager == null) return null;

                var view = manager.GetCurrent3dAcGsView(-1) ?? manager.GetCurrentAcGsView(-1);
                if (view == null) return null;

                return view.GetSnapshot(new Rectangle(0, 0, width, height));
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static int Clamp(int value)
        {
            if (value < MinDimension) return MinDimension;
            if (value > MaxDimension) return MaxDimension;
            return value;
        }
    }
}
