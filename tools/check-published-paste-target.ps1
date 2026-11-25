# The published checks' separate paste target, its launcher and its input log.

# Another program's window to paste into: a PowerShell with an edit box that has the focus, logging each key message,
# character and paste its window and edit box receive to a file, which the check reads while it is written.
$targetScript = @'
param([string] $Log, [int] $X, [int] $Y, [string] $ExpectedFile)
Add-Type -ReferencedAssemblies System.Windows.Forms -TypeDefinition @"
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class CheckPasteTarget {
    delegate IntPtr WndProc(IntPtr window, uint message, IntPtr w, IntPtr l);
    delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr w, IntPtr l, UIntPtr id, UIntPtr data);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WindowClass { public uint Size, Style; public WndProc Procedure; public int ClassExtra, WindowExtra; public IntPtr Instance, Icon, Cursor, Background; public string Menu, Name; public IntPtr SmallIcon; }
    [StructLayout(LayoutKind.Sequential)]
    struct Message { public IntPtr Window; public uint Id; public IntPtr W, L; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassExW(ref WindowClass c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern int GetMessageW(out Message m, IntPtr window, uint a, uint b);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref Message m);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessageW(ref Message m);
    [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr LoadCursorW(IntPtr instance, IntPtr id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string name);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr window, SubclassProc procedure, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr w, IntPtr l);
    static readonly WndProc mainProcedure = TopWindow;
    static readonly SubclassProc editProcedure = Edit;
    static IntPtr edit;
    static StreamWriter log;
    static string expectedFile;
    public static void Run(string path, int x, int y, string file) {
        expectedFile = file;
        log = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        WindowClass c = new WindowClass();
        c.Size = (uint)Marshal.SizeOf(typeof(WindowClass));
        c.Procedure = mainProcedure;
        c.Instance = GetModuleHandleW(null);
        c.Cursor = LoadCursorW(IntPtr.Zero, (IntPtr)32512);
        c.Background = (IntPtr)6;
        c.Name = "ImageStashPublishedCheckTarget";
        RegisterClassExW(ref c);
        IntPtr main = CreateWindowExW(0, c.Name, "ImageStash check target", 0x10CF0000, x, y, 400, 160, IntPtr.Zero, IntPtr.Zero, c.Instance, IntPtr.Zero);
        edit = CreateWindowExW(0, "EDIT", "", 0x50810004, 10, 10, 360, 90, main, IntPtr.Zero, c.Instance, IntPtr.Zero);
        SetWindowSubclass(edit, editProcedure, UIntPtr.Zero, UIntPtr.Zero);
        SetFocus(edit);
        Say("ready " + main.ToInt64());
        Message m;
        while (GetMessageW(out m, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref m); DispatchMessageW(ref m); }
    }
    static IntPtr TopWindow(IntPtr window, uint message, IntPtr w, IntPtr l) {
        Log("window", message, w);
        if (message == 7 && edit != IntPtr.Zero) { SetFocus(edit); }
        if (message == 2) { PostQuitMessage(0); return IntPtr.Zero; }
        return DefWindowProcW(window, message, w, l);
    }
    static IntPtr Edit(IntPtr window, uint message, IntPtr w, IntPtr l, UIntPtr id, UIntPtr data) {
        if (message == 0x302 && expectedFile.Length != 0) {
            try {
                var files = System.Windows.Forms.Clipboard.GetFileDropList();
                bool matched = files.Count == 1 && string.Equals(files[0], expectedFile, StringComparison.OrdinalIgnoreCase);
                Say(matched ? "paste-file matched" : "paste-file differed");
            }
            catch (ExternalException) { Say("paste-file unavailable"); }
        }
        Log("edit", message, w);
        return DefSubclassProc(window, message, w, l);
    }
    static void Log(string where, uint message, IntPtr w) {
        string name = message == 0x100 ? "key WM_KEYDOWN" : message == 0x101 ? "key WM_KEYUP" : message == 0x104 ? "key WM_SYSKEYDOWN" : message == 0x105 ? "key WM_SYSKEYUP" : message == 0x102 ? "other WM_CHAR" : message == 0x106 ? "other WM_SYSCHAR" : message == 0x302 ? "other WM_PASTE" : null;
        if (name != null) { Say(name + ":0x" + w.ToInt64().ToString("X") + " " + where); }
    }
    static void Say(string line) { lock (typeof(CheckPasteTarget)) { log.WriteLine(line); } }
}
"@
[CheckPasteTarget]::Run($Log, $X, $Y, $ExpectedFile)
'@

function Lines([string] $path) {
    if (-not (Test-Path $path)) { return @() }
    $stream = [IO.FileStream]::new($path, 'Open', 'Read', 'ReadWrite')
    try { $reader = [IO.StreamReader]::new($stream); @($reader.ReadToEnd() -split "`r?`n" | Where-Object { $_ -ne '' }) }
    finally { $stream.Dispose() }
}

function StartPowerShell([string] $script, [string[]] $arguments) {
    $command = '& { ' + $script + ' } ' + (($arguments | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ' ')
    # No console and no start-up SW_HIDE, so the window it shows is shown.
    $start = [Diagnostics.ProcessStartInfo]::new('powershell.exe')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.Arguments = '-NoProfile -NonInteractive -Sta -EncodedCommand ' + [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    [Diagnostics.Process]::Start($start)
}

function StartTarget([string] $log, [string] $expectedFile = '') {
    $area = [PasteDesk]::WorkArea([PasteDesk]::Foreground())
    $process = StartPowerShell $targetScript @($log, [string]($area[0] + 100), [string]($area[1] + 40), $expectedFile)
    $target = $null
    try {
        $null = WaitFor { (Lines $log | Where-Object { $_ -like 'ready *' }).Count -gt 0 } 60000
        $ready = @(Lines $log | Where-Object { $_ -like 'ready *' })
        if ($ready.Count -eq 0) { throw 'the target window did not start' }
        $target = @{ Process = $process; Window = [IntPtr][int64]$ready[0].Substring(6); Log = $log }
        $target
    }
    finally {
        if ($null -eq $target -and -not $process.HasExited) {
            $process.Kill()
            $null = $process.WaitForExit(5000)
        }
    }
}

function ClickToFront($target) {
    $bounds = [PasteDesk]::WindowRect($target.Window)
    [PasteDesk]::Click([int](($bounds[0] + $bounds[2]) / 2), $bounds[1] + 12)
    WaitFor { [PasteDesk]::Foreground() -eq $target.Window } 5000
}

# The key messages, and the characters and pastes, the target received after line <after> of its log.
function KeysAfter($target, [int] $after) { @(Lines $target.Log | Select-Object -Skip $after | Where-Object { $_ -like 'key *' } | ForEach-Object { ($_ -split ' ')[1] }) }
function OthersAfter($target, [int] $after) { @(Lines $target.Log | Select-Object -Skip $after | Where-Object { $_ -like 'other *' } | ForEach-Object { ($_ -split ' ')[1] }) }

# What one Ctrl+V and nothing else makes an edit box receive: the four key messages, the character Ctrl+V translates
# to, and the paste the edit box makes of it.
$oneCtrlV = 'WM_KEYDOWN:0x11,WM_KEYDOWN:0x56,WM_KEYUP:0x56,WM_KEYUP:0x11'
$ctrlVMakes = 'WM_CHAR:0x16,WM_PASTE:0x0'
