using System.Text.Json;
using System.Text.Json.Serialization;
using r10_bridge.Api;
using Microsoft.Extensions.Configuration;

namespace r10_bridge
{
  public class ConnectionManager: IDisposable
  {
    private BluetoothConnection? BluetoothConnection { get; }
    internal HttpApiServer? ApiServer { get; }
    internal TcpShotBroadcastServer? TcpBroadcastServer { get; }
    private bool disposedValue;
    internal BallData? LastBallData { get; private set; }
    internal ClubData? LastClubData { get; private set; }
    internal DateTime? LastShotAtUtc { get; private set; }

    public ConnectionManager(IConfigurationRoot configuration)
    {
      if (bool.Parse(configuration.GetSection("bluetooth")["enabled"] ?? "false"))
        BluetoothConnection = new BluetoothConnection(this, configuration.GetSection("bluetooth"));

      if (bool.Parse(configuration.GetSection("httpApi")["enabled"] ?? "true"))
      {
        ApiServer = new HttpApiServer(this, configuration.GetSection("httpApi"));
        ApiServer.Start();
      }

      if (bool.Parse(configuration.GetSection("tcpBroadcast")["enabled"] ?? "false"))
      {
        int port = int.Parse(configuration.GetSection("tcpBroadcast")["port"] ?? "5510");
        TcpBroadcastServer = new TcpShotBroadcastServer(port);
        TcpBroadcastServer.Start();
      }
    }

    internal void SendShot(BallData? ballData, ClubData? clubData)
    {
      LastBallData = ballData;
      LastClubData = clubData;
      LastShotAtUtc = DateTime.UtcNow;
      if (TcpBroadcastServer != null)
      {
        string json = JsonSerializer.Serialize(new { ballData = LastBallData, clubData = LastClubData, atUtc = LastShotAtUtc });
        TcpBroadcastServer.Broadcast(json);
      }
    }

    

    // Device actions and status accessors for HTTP API
    internal object GetDeviceStatus()
    {
      bool connected = BluetoothConnection?.Device?.Gatt?.IsConnected ?? false;
      bool ready = BluetoothConnection?.LaunchMonitor?.Ready ?? false;
      int? battery = BluetoothConnection?.LaunchMonitor?.Battery;
      var tilt = BluetoothConnection?.LaunchMonitor?.DeviceTilt;
      return new
      {
        connected,
        ready,
        battery,
        firmware = BluetoothConnection?.LaunchMonitor?.Firmware,
        model = BluetoothConnection?.LaunchMonitor?.Model,
        serial = BluetoothConnection?.LaunchMonitor?.Serial,
        tilt = tilt == null ? null : new { roll = tilt.Roll, pitch = tilt.Pitch }
      };
    }

    internal bool WakeDevice()
    {
      var status = BluetoothConnection?.LaunchMonitor?.WakeDevice();
      return status != null;
    }

    internal bool StartTiltCalibration()
    {
      var status = BluetoothConnection?.LaunchMonitor?.StartTiltCalibration();
      return status != null;
    }

    internal bool ResetTiltCalibration()
    {
      var status = BluetoothConnection?.LaunchMonitor?.ResetTiltCalibrartion();
      return status != null;
    }

    protected virtual void Dispose(bool disposing)
    {
      if (!disposedValue)
      {
        if (disposing)
        {
          ApiServer?.Dispose();
          TcpBroadcastServer?.Dispose();
          BluetoothConnection?.Dispose();
        }
        disposedValue = true;
      }
    }

    public void Dispose()
    {
      Dispose(disposing: true);
      GC.SuppressFinalize(this);
    }
  }
}