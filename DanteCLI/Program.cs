using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace DanteCLI;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();
        Application.Start((p) =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
