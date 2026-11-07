using System.Runtime.InteropServices;
using ImageStash.Seams;

namespace ImageStash.Windows.Seams;

/// <summary>
/// One instance per Windows session: a <c>Local\</c> named mutex held for the process's lifetime, and a
/// <c>Local\</c> named event a second launch signals (S7: <c>Local\</c> names are scoped to the caller's session).
/// </summary>
public sealed class WindowsOneInstance : IOneInstance, IDisposable
{
    public const string SessionName = "ImageStash";

    private readonly Mutex mutex;
    private readonly EventWaitHandle showRequests;
    private readonly ManualResetEvent stopping = new(false);
    private Thread? listener;
    private int? holdingThread;

    /// <param name="name">The name both kernel objects are derived from; checks pass a name of their own.</param>
    public WindowsOneInstance(string name)
    {
        mutex = new Mutex(false, $@"Local\{name}.Instance");
        showRequests = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.ShowWindow");
    }

    public bool TryHold()
    {
        bool held;
        try
        {
            held = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // The previous instance was killed; its mutex is now this one's.
            held = true;
        }

        holdingThread = held ? Environment.CurrentManagedThreadId : null;
        return held;
    }

    public void SignalRunning()
    {
        // Transfer the second launch's foreground permission before the running instance handles its request (G3).
        AllowSetForegroundWindow(unchecked((uint)-1)); // ASFW_ANY
        showRequests.Set();
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    public void ListenForShowRequests(Action showRequested)
    {
        listener = new Thread(() =>
        {
            while (WaitHandle.WaitAny([showRequests, stopping]) == 0)
            {
                showRequested();
            }
        })
        {
            IsBackground = true,
            Name = "Show requests",
        };
        listener.Start();
    }

    public void Dispose()
    {
        stopping.Set();
        // The listener waits on the handles disposed below, so it ends first; a show request in flight completes.
        listener?.Join();
        // Only the thread that took a mutex can release it; any other holder is released when its thread ends.
        if (holdingThread == Environment.CurrentManagedThreadId)
        {
            mutex.ReleaseMutex();
        }

        holdingThread = null;

        mutex.Dispose();
        showRequests.Dispose();
        stopping.Dispose();
    }
}
