namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// A folder reached through a substituted drive, as <c>subst</c> makes one: the first free letter of Q to Z mapped to the
/// folder, and unmapped once disposed, so a check that fails still gives the letter back.
/// </summary>
public sealed class SubstDrive : IDisposable
{
    private SubstDrive(string letter) => Letter = letter;

    /// <summary>The drive, as <c>Q:</c>.</summary>
    public string Letter { get; }

    /// <summary>The first free letter of Q to Z mapped to <paramref name="folder"/>.</summary>
    public static SubstDrive For(string folder)
    {
        var drive = "QRSTUVWXYZ".Select(letter => letter + ":").First(letter => !Directory.Exists(letter + @"\"));
        Run("subst.exe", $"{drive} \"{folder}\"");
        return new SubstDrive(drive);
    }

    /// <summary>Runs <paramref name="file"/> with <paramref name="arguments"/>, no window shown, and fails unless it exits 0.</summary>
    public static void Run(string file, string arguments)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, arguments) { CreateNoWindow = true, UseShellExecute = false })!;
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, $"{file} {arguments}");
    }

    public void Dispose() => Run("subst.exe", $"{Letter} /D");
}
