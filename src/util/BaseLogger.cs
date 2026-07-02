namespace r10_bridge
{
  public enum LogMessageType
  {
    Incoming,
    Outgoing,
    Informational,
    Error
  }
  public static class BaseLogger
  {
    private static readonly object lockObject = new object();
    private static StreamWriter? logFile;

    /// <summary>Absolute path of the current log file, or null if file logging isn't active.</summary>
    public static string? LogFilePath { get; private set; }

    /// <summary>
    /// Starts writing a plain-text copy of every log line to a file (in addition to the
    /// console). Logs go to &lt;directory&gt;/r10-bridge-yyyy-MM-dd.log; the directory
    /// defaults to a "logs" folder next to the running app. Safe to call once at startup.
    /// </summary>
    public static void InitFileLogging(string? directory = null)
    {
      lock (lockObject)
      {
        if (logFile != null)
          return;
        try
        {
          string dir = directory ?? Path.Combine(Directory.GetCurrentDirectory(), "logs");
          Directory.CreateDirectory(dir);
          string path = Path.Combine(dir, $"r10-bridge-{DateTime.Now:yyyy-MM-dd}.log");
          logFile = new StreamWriter(path, append: true) { AutoFlush = true };
          LogFilePath = Path.GetFullPath(path);
        }
        catch (Exception e)
        {
          Console.WriteLine($"(could not open log file: {e.Message})");
        }
      }
    }

    public static void LogDebug(string message) => LogMessage(message, "DEBUG", LogMessageType.Informational, ConsoleColor.Gray);
    public static void LogMessage(string message, string component = "", LogMessageType type = LogMessageType.Informational, ConsoleColor color = ConsoleColor.White)
    {
      string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
      string marker = type switch
      {
        LogMessageType.Incoming => ">>",
        LogMessageType.Outgoing => "<<",
        LogMessageType.Error => "XX",
        _ => "||",
      };

      lock (lockObject)
      {
        // Console (colored)
        Console.Write($"{timestamp} ");
        Console.ForegroundColor = color;
        Console.Write($"{component.PadLeft(6)} ");
        Console.ResetColor();

        if (type == LogMessageType.Error)
        {
          Console.ForegroundColor = ConsoleColor.Red;
          Console.WriteLine($"{marker} {message}");
          Console.ResetColor();
        }
        else
        {
          Console.Write($"{marker} ");
          Console.WriteLine(message);
        }

        // File (plain text, no color codes)
        logFile?.WriteLine($"{timestamp} {component.PadLeft(6)} {marker} {message}");
      }
    }
  }
}
