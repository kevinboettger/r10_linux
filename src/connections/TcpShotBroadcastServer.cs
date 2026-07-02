using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace r10_bridge
{
  class TcpShotBroadcastSession : NetCoreServer.TcpSession
  {
    public TcpShotBroadcastServer ServerRef { get; }

    public TcpShotBroadcastSession(TcpShotBroadcastServer server) : base(server)
    {
      ServerRef = server;
    }

    protected override void OnConnected()
    {
      ServerRef.Register(this);
    }

    protected override void OnDisconnected()
    {
      ServerRef.Unregister(this);
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
      // This server is broadcast-only; ignore incoming data
    }

    protected override void OnError(SocketError error)
    {
      BaseLogger.LogMessage($"TCP session error: {error}", "TCP", LogMessageType.Error);
    }
  }

  class TcpShotBroadcastServer : NetCoreServer.TcpServer
  {
    private readonly ConcurrentDictionary<Guid, TcpShotBroadcastSession> _sessions = new();

    public TcpShotBroadcastServer(int port) : base(IPAddress.Loopback, port)
    {
      BaseLogger.LogMessage($"Starting TCP broadcast on 127.0.0.1:{Port}", "TCP", LogMessageType.Informational);
    }

    protected override NetCoreServer.TcpSession CreateSession()
    {
      return new TcpShotBroadcastSession(this);
    }

    public void Register(TcpShotBroadcastSession session)
    {
      _sessions[session.Id] = session;
      BaseLogger.LogMessage($"TCP client connected: {session.Id}", "TCP", LogMessageType.Informational);
    }

    public void Unregister(TcpShotBroadcastSession session)
    {
      _sessions.TryRemove(session.Id, out _);
      BaseLogger.LogMessage($"TCP client disconnected: {session.Id}", "TCP", LogMessageType.Informational);
    }

    public void Broadcast(string message)
    {
      byte[] data = Encoding.UTF8.GetBytes(message + "\n");
      foreach (var session in _sessions.Values)
      {
        session.SendAsync(data);
      }
    }

    protected override void OnError(SocketError error)
    {
      BaseLogger.LogMessage($"TCP server error: {error}", "TCP", LogMessageType.Error);
    }
  }
}


