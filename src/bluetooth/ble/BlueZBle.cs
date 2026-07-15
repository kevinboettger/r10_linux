using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;

namespace r10_bridge.bluetooth.ble
{
  // A small compatibility layer that mirrors the subset of the InTheHand.BluetoothLE
  // API surface the rest of this project relies on, implemented on top of BlueZ over
  // D-Bus (via the Linux.Bluetooth package). Keeping the same type names and method
  // shapes lets the device / connection code stay essentially unchanged when moving
  // from Windows (WinRT) to Linux (BlueZ).
  //
  // Prerequisite (handled by the OS, not this code): the R10 must already be paired
  // and trusted with the local adapter, e.g. via `bluetoothctl`. This layer only
  // enumerates known/paired devices and drives the GATT flow.

  /// <summary>
  /// Entry point mirroring <c>InTheHand.Bluetooth.Bluetooth</c>.
  /// </summary>
  public static class Bluetooth
  {
    private static readonly TimeSpan AdapterTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Returns the devices already known to the local BlueZ adapters that are paired
    /// (and therefore usable without a fresh discovery scan).
    /// </summary>
    public static async Task<IReadOnlyList<BluetoothDevice>> GetPairedDevicesAsync()
    {
      var result = new List<BluetoothDevice>();

      IReadOnlyList<Adapter> adapters;
      try
      {
        adapters = await BlueZManager.GetAdaptersAsync().WaitAsync(AdapterTimeout);
      }
      catch (Exception e)
      {
        BaseLogger.LogMessage($"Could not reach BlueZ over D-Bus: {e.Message}", "R10-BT", LogMessageType.Error);
        BaseLogger.LogMessage("Ensure the bluetooth service is running (sudo systemctl start bluetooth) and the D-Bus system bus is available.", "R10-BT", LogMessageType.Error);
        return result;
      }

      if (adapters.Count == 0)
      {
        BaseLogger.LogMessage("No Bluetooth adapters found. Is BlueZ running and is a controller present?", "R10-BT", LogMessageType.Error);
        return result;
      }

      foreach (Adapter adapter in adapters)
      {
        // Make sure the controller is powered on; otherwise no known devices are reported.
        try
        {
          if (!await adapter.GetPoweredAsync())
            await adapter.SetPoweredAsync(true);
        }
        catch
        {
          // Non-fatal: continue and let device enumeration surface any real problem.
        }

        IReadOnlyList<Device> devices;
        try
        {
          devices = await adapter.GetDevicesAsync();
        }
        catch
        {
          continue;
        }

        foreach (Device device in devices)
        {
          // The R10 will connect even when BlueZ hasn't bonded it (Paired=false),
          // as long as it's a known/trusted device. Accept paired OR trusted so a
          // trusted-but-unbonded R10 is still found.
          bool paired = false, trusted = false;
          try { paired = await device.GetPairedAsync(); } catch { /* ignore */ }
          try { trusted = await device.GetTrustedAsync(); } catch { /* ignore */ }
          if (!paired && !trusted)
            continue;

          string? name = null;
          try { name = await device.GetNameAsync(); } catch { /* ignore */ }
          if (string.IsNullOrEmpty(name))
          {
            try { name = await device.GetAliasAsync(); } catch { /* ignore */ }
          }

          string address = string.Empty;
          try { address = await device.GetAddressAsync(); } catch { /* ignore */ }

          result.Add(new BluetoothDevice(device, name, address));
        }
      }

      return result;
    }
  }

  /// <summary>
  /// Mirrors <c>InTheHand.Bluetooth.BluetoothDevice</c>.
  /// </summary>
  public class BluetoothDevice
  {
    internal Device Native { get; }
    public string? Name { get; }
    public string Id { get; }
    public RemoteGattServer Gatt { get; }

    /// <summary>Raised when the underlying BlueZ device reports a disconnect.</summary>
    public event EventHandler? GattServerDisconnected;

    internal BluetoothDevice(Device native, string? name, string id)
    {
      Native = native;
      Name = name;
      Id = id;
      Gatt = new RemoteGattServer(this);
      Native.Disconnected += OnNativeDisconnected;
    }

    private Task OnNativeDisconnected(Device sender, BlueZEventArgs e)
    {
      Gatt.MarkDisconnected();
      GattServerDisconnected?.Invoke(this, EventArgs.Empty);
      return Task.CompletedTask;
    }
  }

  /// <summary>
  /// Mirrors <c>InTheHand.Bluetooth.GattServer</c> (exposed as <c>BluetoothDevice.Gatt</c>).
  /// </summary>
  public class RemoteGattServer
  {
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);
    private readonly BluetoothDevice _device;

    public bool IsConnected { get; private set; }

    // Present for API compatibility with the Windows implementation. BlueZ reconnection
    // is driven by the reconnect loop in BluetoothConnection, so this is advisory only.
    public bool AutoConnect { get; set; }

    internal RemoteGattServer(BluetoothDevice device) => _device = device;

    internal void MarkDisconnected() => IsConnected = false;

    public async Task ConnectAsync()
    {
      try
      {
        // Don't treat an existing link (e.g. one left over from bluetoothctl) as a
        // failure — reuse it and just wait for services.
        bool alreadyConnected = false;
        try { alreadyConnected = await _device.Native.GetConnectedAsync(); } catch { /* ignore */ }

        if (!alreadyConnected)
        {
          try { await _device.Native.ConnectAsync(); } catch { /* may already be connecting */ }
        }

        // BlueZ resolves GATT asynchronously after the link is up; wait for both so the
        // subsequent GetPrimaryService/GetCharacteristic calls can succeed.
        await _device.Native.WaitForPropertyValueAsync("Connected", value: true, ConnectTimeout);
        await _device.Native.WaitForPropertyValueAsync("ServicesResolved", value: true, ConnectTimeout);
        IsConnected = true;
      }
      catch
      {
        // The caller loops on IsConnected and retries, matching the original behaviour.
        IsConnected = false;
      }
    }

    public void Disconnect()
    {
      try { _device.Native.DisconnectAsync().GetAwaiter().GetResult(); }
      catch { /* ignore */ }
      IsConnected = false;
    }

    public async Task<GattService> GetPrimaryServiceAsync(Guid uuid)
    {
      IGattService1 service = await _device.Native.GetServiceAsync(ToUuid(uuid));
      if (service == null)
        throw new InvalidOperationException($"GATT service {uuid} not found on device.");
      return new GattService(service);
    }

    internal static string ToUuid(Guid uuid) => uuid.ToString("D").ToLowerInvariant();
  }

  /// <summary>
  /// Mirrors <c>InTheHand.Bluetooth.GattService</c>.
  /// </summary>
  public class GattService
  {
    private readonly IGattService1 _native;

    internal GattService(IGattService1 native) => _native = native;

    public async Task<GattCharacteristic> GetCharacteristicAsync(Guid uuid)
    {
      Linux.Bluetooth.GattCharacteristic characteristic = await _native.GetCharacteristicAsync(RemoteGattServer.ToUuid(uuid));
      if (characteristic == null)
        throw new InvalidOperationException($"GATT characteristic {uuid} not found in service.");
      return new GattCharacteristic(characteristic);
    }
  }

  /// <summary>
  /// Mirrors <c>InTheHand.Bluetooth.GattCharacteristic</c>.
  /// </summary>
  public class GattCharacteristic
  {
    private static readonly IDictionary<string, object> WriteWithResponseOptions =
      new Dictionary<string, object> { { "type", "request" } };

    private readonly Linux.Bluetooth.GattCharacteristic _native;

    // BlueZ delivers writes over a single D-Bus connection; serialize them so the
    // framed BLE chunks reach the device in the order they were queued.
    private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
    private bool _notifying;

    public event EventHandler<GattCharacteristicValueChangedEventArgs>? CharacteristicValueChanged;

    internal GattCharacteristic(Linux.Bluetooth.GattCharacteristic native)
    {
      _native = native;
    }

    private Task OnNativeValue(Linux.Bluetooth.GattCharacteristic sender, GattCharacteristicValueEventArgs e)
    {
      CharacteristicValueChanged?.Invoke(this, new GattCharacteristicValueChangedEventArgs { Value = e.Value });
      return Task.CompletedTask;
    }

    public Task<byte[]> ReadValueAsync() => _native.ReadValueAsync(new Dictionary<string, object>());

    public async Task WriteValueWithResponseAsync(byte[] value)
    {
      await _writeLock.WaitAsync();
      try
      {
        await _native.WriteValueAsync(value, WriteWithResponseOptions);
      }
      finally
      {
        _writeLock.Release();
      }
    }

    public Task StartNotificationsAsync()
    {
      // Wire BlueZ value notifications lazily — only when the caller actually wants
      // them. Subscribing in the constructor would trigger org.bluez.Error.NotSupported
      // on read-only / write-only characteristics that can't notify (serial, firmware,
      // model, the interface writer), producing harmless-but-noisy errors.
      if (!_notifying)
      {
        _notifying = true;
        _native.Value += OnNativeValue;
      }
      return _native.StartNotifyAsync();
    }
  }

  /// <summary>
  /// Mirrors <c>InTheHand.Bluetooth.GattCharacteristicValueChangedEventArgs</c>.
  /// </summary>
  public class GattCharacteristicValueChangedEventArgs : EventArgs
  {
    public byte[] Value { get; set; } = Array.Empty<byte>();
  }
}
