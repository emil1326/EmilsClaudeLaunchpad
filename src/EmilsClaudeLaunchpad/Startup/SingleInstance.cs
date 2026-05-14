namespace EmilsClaudeLaunchpad.Startup;

public static class SingleInstance
{
    private static Mutex? _mutex;

    public static bool TryAcquire(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, name: $"Local\\{name}", out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
        }
        return createdNew;
    }
}
