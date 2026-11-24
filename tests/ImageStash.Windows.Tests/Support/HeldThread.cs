using System.Runtime.InteropServices;

namespace ImageStash.Windows.Tests.Support;

/// <summary>A thread held still by the check, so what it would answer or report comes late (the Target seam's hook, a window's thread).</summary>
public static class HeldThread
{
    /// <summary>Holds <paramref name="thread"/> until disposed, so what it would answer or report comes late.</summary>
    public static IDisposable Suspended(uint thread)
    {
        var handle = OpenThread(ThreadSuspendResume, false, thread);
        Assert.AreNotEqual(nint.Zero, handle);
        if (SuspendThread(handle) == uint.MaxValue)
        {
            CloseHandle(handle);
            Assert.Fail("the thread could not be held");
        }

        return new Resumes(handle);
    }

    private sealed class Resumes(nint handle) : IDisposable
    {
        private int done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref done, 1) == 0)
            {
                ResumeThread(handle);
                CloseHandle(handle);
            }
        }
    }

    private const uint ThreadSuspendResume = 0x0002;

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint threadId);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint SuspendThread(nint thread);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint ResumeThread(nint thread);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
