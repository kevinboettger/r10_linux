using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace r10_bridge
{
  class HttpApiSession : NetCoreServer.HttpSession
  {
    public ConnectionManager ConnectionManager { get; }
    public HttpApiServer ApiServer { get; }

    public HttpApiSession(HttpApiServer server, ConnectionManager connectionManager) : base(server)
    {
      ConnectionManager = connectionManager;
      ApiServer = server;
    }

    protected override void OnReceivedRequest(NetCoreServer.HttpRequest request)
    {
      try
      {
        string url = request.Url ?? "/";
        string method = request.Method ?? "GET";

        if (method == "GET" && url == "/")
        {
          if (ApiServer.EnableDashboard)
          {
            string html = HttpApiServer.DashboardHtml;
            SendResponseAsync(Response.MakeGetResponse(html, "text/html; charset=utf-8"));
            return;
          }
          else
          {
            SendResponseAsync(Response.MakeOkResponse());
            return;
          }
        }

        if (method == "GET" && url == "/api/status")
        {
          string json = JsonSerializer.Serialize(ConnectionManager.GetDeviceStatus());
          SendResponseAsync(Response.MakeGetResponse(json, "application/json"));
          return;
        }

        if (method == "GET" && url == "/api/last-shot")
        {
          string json = JsonSerializer.Serialize(new {
            ballData = ConnectionManager.LastBallData,
            clubData = ConnectionManager.LastClubData,
            atUtc = ConnectionManager.LastShotAtUtc
          });
          SendResponseAsync(Response.MakeGetResponse(json, "application/json"));
          return;
        }

        if (method == "POST" && url == "/api/wake")
        {
          bool ok = ConnectionManager.WakeDevice();
          SendResponseAsync(Response.MakeGetResponse(JsonSerializer.Serialize(new { ok }), "application/json"));
          return;
        }

        if (method == "POST" && url == "/api/tilt/calibrate/start")
        {
          bool ok = ConnectionManager.StartTiltCalibration();
          SendResponseAsync(Response.MakeGetResponse(JsonSerializer.Serialize(new { ok }), "application/json"));
          return;
        }

        if (method == "POST" && url == "/api/tilt/calibrate/reset")
        {
          bool ok = ConnectionManager.ResetTiltCalibration();
          SendResponseAsync(Response.MakeGetResponse(JsonSerializer.Serialize(new { ok }), "application/json"));
          return;
        }

        SendResponseAsync(Response.MakeErrorResponse());
      }
      catch
      {
        SendResponseAsync(Response.MakeErrorResponse());
      }
    }

    protected override void OnError(SocketError error)
    {
      BaseLogger.LogMessage($"HTTP API session error: {error}", "HTTP", LogMessageType.Error);
    }
  }

  class HttpApiServer : NetCoreServer.HttpServer
  {
    public ConnectionManager ConnectionManager { get; }
    public bool EnableDashboard { get; }

    public HttpApiServer(ConnectionManager connectionManager, IConfigurationSection configuration)
      : base(IPAddress.Loopback, int.Parse(configuration["port"] ?? "5001"))
    {
      ConnectionManager = connectionManager;
      EnableDashboard = bool.Parse(configuration["enableDashboard"] ?? "true");
      BaseLogger.LogMessage($"Starting HTTP API on http://127.0.0.1:{Port}", "HTTP", LogMessageType.Informational);
    }

    protected override NetCoreServer.TcpSession CreateSession()
    {
      return new HttpApiSession(this, ConnectionManager);
    }

    protected override void OnError(SocketError error)
    {
      BaseLogger.LogMessage($"HTTP API server error: {error}", "HTTP", LogMessageType.Error);
    }

    public static string DashboardHtml = @"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
  <title>R10 HTTP API</title>
  <style>
    body { font-family: Arial, sans-serif; margin: 24px; }
    pre { background: #f4f4f4; padding: 12px; border-radius: 6px; }
    .row { display: flex; gap: 24px; }
    .card { flex: 1; border: 1px solid #ddd; border-radius: 6px; padding: 12px; }
    button { padding: 8px 12px; margin-right: 8px; }
  </style>
  <script>
    async function refresh() {
      const s = await fetch('/api/status').then(r => r.json()).catch(()=>null);
      const l = await fetch('/api/last-shot').then(r => r.json()).catch(()=>null);
      document.getElementById('status').textContent = JSON.stringify(s, null, 2);
      document.getElementById('lastshot').textContent = JSON.stringify(l, null, 2);
    }
    async function call(path){
      await fetch(path, { method: 'POST' });
      refresh();
    }
    setInterval(refresh, 1500);
    window.onload = refresh;
  </script>
  </head>
  <body>
    <h2>R10 HTTP API</h2>
    <div>
      <button onclick=""call('/api/wake')"">Wake</button>
      <button onclick=""call('/api/tilt/calibrate/start')"">Start Tilt Calibration</button>
      <button onclick=""call('/api/tilt/calibrate/reset')"">Reset Tilt Calibration</button>
    </div>
    <div class=""row"">
      <div class=""card"">
        <h3>Status</h3>
        <pre id=""status""></pre>
      </div>
      <div class=""card"">
        <h3>Last Shot</h3>
        <pre id=""lastshot""></pre>
      </div>
    </div>
  </body>
</html>";
  }
}


