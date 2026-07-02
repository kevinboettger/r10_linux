using Microsoft.Extensions.Configuration;
using System.Runtime.InteropServices;

namespace r10_bridge
{
  class Program
  {
    public static void Main()
    {
      
      IConfigurationBuilder builder = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory());

      if (File.Exists(Path.Join(Directory.GetCurrentDirectory(), "settings.json")))
      {
        builder.AddJsonFile("settings.json");
      }
      else
      {
        BaseLogger.LogMessage($"settings.json file not found or could not be opened in {Directory.GetCurrentDirectory()}", "Main", LogMessageType.Error);
      }

      IConfigurationRoot configuration = builder.Build();
      
      bool interactive = !Console.IsInputRedirected;
      if (interactive)
        Console.Title = "R10 Bridge";

      BaseLogger.LogMessage(interactive
        ? "R10 Bridge starting. Press enter key to close"
        : "R10 Bridge starting. Send SIGINT/SIGTERM (e.g. systemctl stop) to close", "Main");

      ConnectionManager manager = new ConnectionManager(configuration);

      if (interactive)
      {
        // Foreground / terminal: close on Enter, as before.
        Console.ReadLine();
      }
      else
      {
        // Non-interactive (e.g. systemd, redirected stdin): stdin is at EOF immediately,
        // so block until the service manager sends a stop signal instead of exiting now.
        using ManualResetEventSlim exitSignal = new ManualResetEventSlim(false);
        using (PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; exitSignal.Set(); }))
        using (PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; exitSignal.Set(); }))
        {
          exitSignal.Wait();
        }
      }

      BaseLogger.LogMessage("Shutting down...", "Main");
      manager.Dispose();
      BaseLogger.LogMessage("Exiting...", "Main");
    }
  }
}
