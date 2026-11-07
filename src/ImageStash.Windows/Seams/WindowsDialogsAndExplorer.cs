using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ImageStash.Seams;
using Microsoft.Win32.SafeHandles;

namespace ImageStash.Windows.Seams;

/// <summary>
/// The standard Windows file and folder dialogs through Avalonia's storage provider, which on Windows shows them owned by the
/// given window and keeps to files with a path on this PC; the shell's Open-with dialog; and File Explorer opened on a
/// file's folder with the file selected, or on a folder itself, through the shell.
/// </summary>
public sealed class WindowsDialogsAndExplorer : IDialogsAndExplorer
{
    private const string RevealAction = "Reveal in File Explorer";

    private const string OpenWithAction = "Open with…";

    private const string OpenFolderAction = "Open folder";

    /// <summary>SFGAO_FOLDER: the item is a folder.</summary>
    private const uint ShellFolder = 0x20000000;

    /// <summary>SEE_MASK_IDLIST: the item is given by its identifier list.</summary>
    private const uint UseIdList = 0x4;

    /// <summary>SEE_MASK_FLAG_NO_UI: a failure is answered, never shown as the shell's own error dialog.</summary>
    private const uint NoErrorDialog = 0x400;

    private const int ShowNormal = 1;

    /// <summary>OAIF_EXEC: the chosen program is started on the file (S16).</summary>
    private const int ExecuteChosen = 0x4;

    /// <summary>HRESULT_FROM_WIN32(ERROR_CANCELLED): the dialog closed without a choice, which is no failure.</summary>
    private const int Cancelled = unchecked((int)0x800704C7);

    public async Task<IReadOnlyList<string>?> OpenFiles(TopLevel owner, string title, IReadOnlyList<PickerFilter> filters)
    {
        var picked = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = FileTypes(filters),
        });
        try
        {
            return picked.Count == 0 ? null : picked.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
        }
        finally
        {
            foreach (var file in picked)
            {
                file.Dispose();
            }
        }
    }

    /// <summary>The open picker for one file; a file with no local path, which the picker offers for a phone, answers null as a cancel would.</summary>
    public async Task<string?> OpenFile(TopLevel owner, string title, IReadOnlyList<PickerFilter> filters)
    {
        var picked = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = FileTypes(filters),
        });
        try
        {
            return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        }
        finally
        {
            foreach (var file in picked)
            {
                file.Dispose();
            }
        }
    }

    public async Task<string?> SaveFile(TopLevel owner, string title, string suggestedFileName, PickerFilter fileType)
    {
        using var chosen = await owner.StorageProvider.SaveFilePickerAsync(SaveOptions(title, suggestedFileName, fileType));
        return chosen?.TryGetLocalPath() is { } path ? FinalPath(path) : null;
    }

    /// <summary>
    /// The folder picker, one folder at a time; the chosen folder spelled as <see cref="FinalFolder"/> spells it, so Move
    /// judges the folder it would copy into, not how the picker spelled it (G30). A folder with no local path, which the
    /// picker offers for a library or a phone, answers null as a cancel would.
    /// </summary>
    public async Task<string?> PickFolder(TopLevel owner, string title)
    {
        var picked = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        try
        {
            return picked.Count > 0 && picked[0].TryGetLocalPath() is { } path ? FinalFolder(path) : null;
        }
        finally
        {
            foreach (var folder in picked)
            {
                folder.Dispose();
            }
        }
    }

    /// <summary>
    /// <paramref name="path"/> with its folder spelled as the folder it reaches, every link followed and a substituted
    /// drive or a short name spelled out (GetFinalPathNameByHandle), so Save a copy… judges where a file would land, not
    /// how the picker spelled it (G9, G29). A folder that cannot be opened keeps its spelling.
    /// </summary>
    public static string FinalPath(string path) =>
        Path.GetDirectoryName(path) is { } folder ? Path.Combine(FinalFolder(folder), Path.GetFileName(path)) : path;

    /// <summary>
    /// <paramref name="folder"/> spelled as the folder it reaches, every link followed and a substituted drive or a short
    /// name spelled out (GetFinalPathNameByHandle); a folder that cannot be opened keeps its spelling.
    /// </summary>
    public static string FinalFolder(string folder)
    {
        using var handle = CreateFileW(folder, 0, FileShareAll, 0, OpenExisting, BackupSemantics, 0);
        if (handle.IsInvalid)
        {
            return folder;
        }

        var buffer = new char[260];
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (length > buffer.Length)
        {
            buffer = new char[length];
            length = GetFinalPathNameByHandleW(handle, buffer, length, 0);
        }

        if (length == 0 || length > buffer.Length)
        {
            return folder;
        }

        var final = new string(buffer, 0, (int)length);
        return final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + final[8..]
            : final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..]
            : final;
    }

    /// <summary>The picker's file types, in the order given, each listing its patterns.</summary>
    public static IReadOnlyList<FilePickerFileType> FileTypes(IReadOnlyList<PickerFilter> filters) =>
        filters.Select(filter => new FilePickerFileType(filter.Name) { Patterns = filter.Patterns }).ToList();

    /// <summary>
    /// The save picker as Save a copy… opens it: the suggested name, its one file type, that type's extension added to
    /// a name typed without one, and the dialog's own question before an existing file is replaced (G9, S16).
    /// </summary>
    public static FilePickerSaveOptions SaveOptions(string title, string suggestedFileName, PickerFilter fileType) => new()
    {
        Title = title,
        SuggestedFileName = suggestedFileName,
        DefaultExtension = Path.GetExtension(suggestedFileName).TrimStart('.'),
        FileTypeChoices = FileTypes([fileType]),
        ShowOverwritePrompt = true,
    };

    /// <summary>
    /// SHOpenWithDialog with OAIF_EXEC and no class, so the file's extension decides the programs listed and the chosen
    /// one is started on it (S16). The call blocks its thread until the dialog, a window of the shell's own host
    /// process, has closed, so it runs on a thread of its own, in a single-threaded apartment as the shell's dialogs
    /// need, and the window keeps drawing and answering meanwhile. The window's handle, read here on the UI thread, is
    /// the dialog's parent: the shell disables that window while the dialog is up, without owning the dialog by it, and
    /// gives it the foreground back once the dialog has closed. A cancel answers done.
    /// </summary>
    public Task<Outcome> OpenWith(TopLevel owner, string path)
    {
        var parent = owner.TryGetPlatformHandle()?.Handle ?? 0;
        var closed = new TaskCompletionSource<Outcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var info = new OpenAsInfo { File = Path.GetFullPath(path), Class = null, Flags = ExecuteChosen };
                var result = SHOpenWithDialog(parent, ref info);
                closed.SetResult(result >= 0 || result == Cancelled ? Outcome.Done : new Failure(OpenWithAction, Path.GetFileName(path), ReasonForResult(result)));
            }
            catch (Exception e)
            {
                closed.SetResult(new Failure(OpenWithAction, Path.GetFileName(path), e.Message));
            }
        })
        {
            IsBackground = true,
            Name = "Open with dialog",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return closed.Task;
    }

    /// <summary>
    /// SHOpenFolderAndSelectItems with the file's own full identifier list and no children: Explorer opens the parent
    /// folder and selects the file (S16). Run on the UI thread, which Avalonia has initialised for COM, as the call needs.
    /// </summary>
    public Outcome Reveal(string path)
    {
        var parsed = SHParseDisplayName(Path.GetFullPath(path), 0, out var idList, 0, out _);
        if (parsed < 0)
        {
            return new Failure(RevealAction, Path.GetFileName(path), ReasonForResult(parsed));
        }

        try
        {
            var opened = SHOpenFolderAndSelectItems(idList, 0, 0, 0);
            return opened < 0 ? new Failure(RevealAction, Path.GetFileName(path), ReasonForResult(opened)) : Outcome.Done;
        }
        finally
        {
            CoTaskMemFree(idList);
        }
    }

    /// <summary>
    /// The folder's own identifier list, asked whether it is a folder, then ShellExecuteEx on that list with the open
    /// verb and no error dialog, so Explorer opens the folder itself. Not SHOpenFolderAndSelectItems: given a folder and no
    /// children it opens the folder's parent with the folder selected (SHOpenFolderAndSelectItems, cidl). A path that is a
    /// file is refused, so the verb can never start a program. Run on the UI thread, which Avalonia has initialised for COM.
    /// </summary>
    public Outcome OpenFolder(string folder)
    {
        var full = Path.GetFullPath(folder);
        var parsed = SHParseDisplayName(full, 0, out var idList, ShellFolder, out var attributes);
        if (parsed < 0)
        {
            return new Failure(OpenFolderAction, FolderName(full), ReasonForResult(parsed));
        }

        try
        {
            if ((attributes & ShellFolder) == 0)
            {
                return new Failure(OpenFolderAction, FolderName(full), "it is not a folder");
            }

            var info = new ShellExecuteInfo
            {
                Size = Marshal.SizeOf<ShellExecuteInfo>(),
                Mask = UseIdList | NoErrorDialog,
                Verb = "open",
                Show = ShowNormal,
                IdList = idList,
            };
            return ShellExecuteExW(ref info) ? Outcome.Done : new Failure(OpenFolderAction, FolderName(full), new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }
        finally
        {
            CoTaskMemFree(idList);
        }
    }

    /// <summary>The folder's own name, or the whole path for a drive's root, which has none.</summary>
    private static string FolderName(string full) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(full)) is { Length: > 0 } name ? name : full;

    /// <summary>A failed HRESULT in words; one that carries a Win32 error, by that error's words.</summary>
    private static string ReasonForResult(int result) =>
        new Win32Exception((result & 0xFFFF0000) == 0x80070000 ? result & 0xFFFF : result).Message;

    private const uint FileShareAll = 7;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] path, uint length, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint idList, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHOpenFolderAndSelectItems(nint folder, uint count, nint children, uint flags);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoTaskMemFree(nint memory);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHOpenWithDialog(nint parent, ref OpenAsInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteExW(ref ShellExecuteInfo info);

    /// <summary>SHELLEXECUTEINFOW, in its declared order.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size;
        public uint Mask;
        public nint Window;
        public string? Verb;
        public string? File;
        public string? Parameters;
        public string? Directory;
        public int Show;
        public nint InstApp;
        public nint IdList;
        public string? Class;
        public nint ClassKey;
        public uint HotKey;
        public nint IconOrMonitor;
        public nint Process;
    }

    /// <summary>OPENASINFO: the file, its class (null: by the file's extension) and the OAIF flags.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo
    {
        public string File;
        public string? Class;
        public int Flags;
    }
}
