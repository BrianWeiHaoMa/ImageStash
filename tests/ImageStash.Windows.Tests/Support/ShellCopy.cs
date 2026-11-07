using System.Diagnostics;
using System.Text;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// File Explorer's Copy of files: the shell's own copy verb run on them from another program, a PowerShell, which
/// places the shell's data object on the clipboard and answers a paste's requests for its formats until disposed, as
/// File Explorer does while it runs. Once that program ends, the file list leaves the clipboard with it.
/// </summary>
public sealed class ShellCopy : IDisposable
{
    private const string Script = """
        param([string] $Folder, [string] $Names, [string] $Stop)
        $ProgressPreference = 'SilentlyContinue'
        Add-Type -AssemblyName System.Windows.Forms
        [void][System.Windows.Forms.Application]::OleRequired()
        $stopped = [Threading.EventWaitHandle]::OpenExisting($Stop)
        $items = (New-Object -ComObject Shell.Application).NameSpace($Folder).Items()
        $items.Filter(0x40, $Names)
        if ($items.Count -ne ($Names -split ';').Count) { [Console]::Out.WriteLine("found $($items.Count)"); exit 1 }
        $items.InvokeVerbEx('copy')
        [Console]::Out.WriteLine('copied'); [Console]::Out.Flush()
        # The data object answers requests through this thread's messages; two minutes at most if the check never says stop.
        $deadline = [DateTime]::UtcNow.AddMinutes(2)
        while (-not $stopped.WaitOne(10) -and [DateTime]::UtcNow -lt $deadline) { [System.Windows.Forms.Application]::DoEvents() }
        """;

    private readonly Process copier;
    private readonly EventWaitHandle stop;

    private ShellCopy(Process copier, EventWaitHandle stop)
    {
        this.copier = copier;
        this.stop = stop;
    }

    /// <summary>Copies the files named <paramref name="names"/> in <paramref name="folder"/> together, as one selection, and returns once they are on the clipboard.</summary>
    public static ShellCopy Of(string folder, params string[] names)
    {
        var stopName = @"Local\ImageStash-check-shell-copy-" + Guid.NewGuid().ToString("N");
        var stop = new EventWaitHandle(false, EventResetMode.ManualReset, stopName);
        var command = "& { " + Script + " } " + string.Join(" ", new[] { folder, string.Join(";", names), stopName }.Select(argument => "'" + argument.Replace("'", "''") + "'"));
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) })
        {
            start.ArgumentList.Add(argument);
        }

        var copy = new ShellCopy(Process.Start(start)!, stop);
        try
        {
            var said = copy.copier.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60)).GetAwaiter().GetResult();
            Assert.AreEqual("copied", said, $"the shell did not copy {string.Join(", ", names)}");
        }
        catch
        {
            copy.Dispose();
            throw;
        }

        return copy;
    }

    public void Dispose()
    {
        stop.Set();
        if (!copier.WaitForExit(10_000))
        {
            copier.Kill(entireProcessTree: true);
            copier.WaitForExit();
        }

        copier.Dispose();
        stop.Dispose();
    }
}
