using ClaudeMeter.UI;

namespace ClaudeMeter;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\ClaudeMeter.SingleInstance", out var createdNew);
        if (!createdNew)
            return;

        ApplicationConfiguration.Initialize();
        using var context = new TrayApplicationContext();
        Application.Run(context);
    }
}
