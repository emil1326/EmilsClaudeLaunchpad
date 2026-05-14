using EmilsClaudeLaunchpad.Discovery;
using EmilsClaudeLaunchpad.Startup;
using Velopack;

namespace EmilsClaudeLaunchpad;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--scan")
        {
            foreach (var c in ChatScanner.DiscoverAll())
                Console.WriteLine($"{c.LastModified:yyyy-MM-dd HH:mm}  {c.ShortId}  [{c.WorkingDir}]  {c.Preview}");
            return;
        }

        VelopackApp.Build().SetArgs(args).Run();

        if (!SingleInstance.TryAcquire("EmilsClaudeLaunchpad"))
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }
}
