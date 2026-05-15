using EmilsClaudeLaunchpad.Startup;
using Velopack;

namespace EmilsClaudeLaunchpad;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().SetArgs(args).Run();

        if (!SingleInstance.TryAcquire("EmilsClaudeLaunchpad"))
        {
            // A second launch — wake the already-running instance instead of dying silently.
            IpcServer.TryWakeRunningInstance();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }
}
