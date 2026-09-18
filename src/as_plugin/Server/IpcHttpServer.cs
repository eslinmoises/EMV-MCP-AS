using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EMV.AdvanceSteel.Plugin.Commands;

namespace EMV.AdvanceSteel.Plugin.Server
{
    public class IpcHttpServer
    {
        private readonly int _port;
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _listenerTask;

        public bool IsRunning => _listener?.IsListening ?? false;

        public IpcHttpServer(int port = 5055)
        {
            _port = port;
        }

        public void Start()
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/api/v1/");
            _listener.Start();

            _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _listener?.Stop();
                _listener?.Close();
            }
            catch
            {
                // Suppress on shutdown
            }
            finally
            {
                _listener = null;
                _cts = null;
            }
        }

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(context));
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Continue loop
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            var sw = Stopwatch.StartNew();
            var req = context.Request;
            var res = context.Response;

            res.Headers.Add("Access-Control-Allow-Origin", "*");
            res.ContentType = "application/json; charset=utf-8";

            try
            {
                string path = req.Url?.PathAndQuery ?? req.Url?.AbsolutePath ?? "";
                string method = req.HttpMethod;
                string body = "";

                if (req.HasEntityBody)
                {
                    using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                    body = await reader.ReadToEndAsync();
                }

                // Dispatch to AutoCAD main thread
                var result = await CommandDispatcher.DispatchAsync(method, path, body);
                sw.Stop();

                var envelope = new
                {
                    success = result.Success,
                    data = result.Data,
                    error = result.Error,
                    execution_time_ms = sw.ElapsedMilliseconds
                };

                byte[] buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
                res.StatusCode = result.StatusCode;
                res.ContentLength64 = buffer.Length;
                await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var errEnvelope = new
                {
                    success = false,
                    data = (object?)null,
                    error = new
                    {
                        code = "INTERNAL_SERVER_ERROR",
                        message = ex.Message,
                        details = ex.StackTrace,
                        suggestion = "Review Advance Steel command dispatcher log."
                    },
                    execution_time_ms = sw.ElapsedMilliseconds
                };
                byte[] buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(errEnvelope));
                res.StatusCode = 500;
                res.ContentLength64 = buffer.Length;
                await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            finally
            {
                res.Close();
            }
        }
    }
}
