using System.Diagnostics;
using System.Runtime.InteropServices;
using ImageStash.Seams;

namespace ImageStash.Windows.Seams;

/// <summary>
/// The Recycle Bin through the shell's <c>IFileOperation</c> with recycle-on-delete, whose progress sink reports the
/// recycled item (S9), and without it to remove a recycled entry for good (S9's <c>DeleteItem</c>). One operation per
/// file, so a file that cannot be recycled or removed fails alone. A folder goes the same way, whole, as one entry, and
/// its entry is removed whole (a stash folder an import replaced, G29).
/// </summary>
public sealed class WindowsRecycleBin : IRecycleBin
{
    private const string Action = "Move to the Recycle Bin";
    private const string RemoveAction = "Remove from the Recycle Bin";
    private const string LeftInPlace = "Windows left it where it was";
    private static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(100);

    // The identities Recycle gave back in this run and Remove has not removed: the only entries Remove takes, so no
    // stash file and no entry of anyone else's can be deleted through it (G29).
    private readonly HashSet<string> given = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Outcome<RecycledFile>> Recycle(IReadOnlyList<string> paths, TimeSpan heldFileRetry)
    {
        // IFileOperation works only in a single-threaded apartment, whatever thread the caller is on.
        var outcomes = OnStaThread(() => paths.Select(path => RecycleHeldFile(path, heldFileRetry)).ToList());
        lock (given)
        {
            given.UnionWith(outcomes.Where(o => o.Succeeded).Select(o => o.Value!.RecycledIdentity).OfType<string>());
        }

        return outcomes;
    }

    public IReadOnlyList<Outcome> Remove(IReadOnlyList<string> recycledIdentities, TimeSpan heldFileRetry)
    {
        bool Given(string identity)
        {
            lock (given)
            {
                return given.Contains(identity);
            }
        }

        var outcomes = OnStaThread(() => recycledIdentities
            .Select(identity => Given(identity) && IsRecycledEntry(identity)
                ? RemoveEntry(identity, heldFileRetry)
                : new Failure(RemoveAction, Path.GetFileName(identity), "it is not an entry ImageStash put in the Recycle Bin"))
            .ToList());
        lock (given)
        {
            given.ExceptWith(recycledIdentities.Zip(outcomes).Where(r => r.Second.Succeeded).Select(r => r.First));
        }

        return outcomes;
    }

    /// <summary>
    /// Deletes the entry's <c>$R</c> file, then the <c>$I</c> record beside it, which holds the entry's original name and
    /// date and which removing the <c>$R</c> file alone leaves on disk (probed on Windows 11 build 26300). An entry whose
    /// <c>$R</c> file an earlier removal deleted before its record was refused as held is finished by deleting the record
    /// alone, so a removal asked again once the record is let go never answers that the entry does not exist (G35). A
    /// recycled folder's <c>$R</c> entry is a folder, removed with everything in it.
    /// </summary>
    private static Outcome RemoveEntry(string identity, TimeSpan heldFileRetry)
    {
        var name = Path.GetFileName(identity);
        var record = Path.Combine(Path.GetDirectoryName(identity)!, "$I" + name[2..]);
        var halfRemoved = !Path.Exists(identity) && File.Exists(record);
        var error = halfRemoved ? 0 : DeleteHeldFile(identity, heldFileRetry, Shell.FofNoUi).Error;
        if (error >= 0 && File.Exists(record))
        {
            error = DeleteHeldFile(record, heldFileRetry, Shell.FofNoUi).Error;
        }

        return error >= 0 ? Outcome.Done : new Failure(RemoveAction, name, ReasonFor(error));
    }

    /// <summary>
    /// The shape <see cref="Recycle"/> gives back, <c>&lt;drive&gt;:\$Recycle.Bin\&lt;SID&gt;\$R…</c>, which the <c>$I</c>
    /// record's name is derived from.
    /// </summary>
    private static bool IsRecycledEntry(string identity)
    {
        if (!Path.IsPathFullyQualified(identity) || Path.GetFullPath(identity) != identity)
        {
            return false;
        }

        var user = Path.GetDirectoryName(identity);
        var bin = user is null ? null : Path.GetDirectoryName(user);
        var root = bin is null ? null : Path.GetDirectoryName(bin);
        return Path.GetFileName(identity) is { Length: > 2 } name && name.StartsWith("$R", StringComparison.Ordinal)
            && Path.GetFileName(user)?.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase) == true
            && string.Equals(Path.GetFileName(bin), "$Recycle.Bin", StringComparison.OrdinalIgnoreCase)
            && root is not null && Path.GetPathRoot(root) == root;
    }

    private static Outcome<RecycledFile> RecycleHeldFile(string path, TimeSpan heldFileRetry)
    {
        var (identity, error) = DeleteHeldFile(path, heldFileRetry, Shell.FofNoUi | Shell.FofAllowUndo | Shell.FofxRecycleOnDelete);
        if (error < 0)
        {
            return new Failure(Action, Path.GetFileName(path), ReasonFor(error));
        }

        // A delete that succeeded with no recycled item is a file the shell removed for good because the bin could not
        // take it (S9: PostDeleteItem's new item is null then); it is done, with no identity (G29). One still there was
        // not removed at all, and stays a failure, so no row is ended over a file left in the stash (G28).
        return identity is null && Path.Exists(path)
            ? new Failure(Action, Path.GetFileName(path), LeftInPlace)
            : Outcome<RecycledFile>.Of(new RecycledFile(path, identity));
    }

    /// <summary>Deletes <paramref name="path"/> with <paramref name="flags"/>, again while it is held, for at most the retry.</summary>
    private static (string? Identity, int Error) DeleteHeldFile(string path, TimeSpan heldFileRetry, uint flags)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var result = DeleteOnce(path, flags);
            if (!IsHeld(result.Error) && !HoldsAnOpenFile(path, result.Error))
            {
                return result;
            }

            if (clock.Elapsed >= heldFileRetry)
            {
                // A folder holding a file another program has open is answered as held, which the caller retries.
                return IsHeld(result.Error) ? result : (null, Shell.CopyEngineSharingViolationSource);
            }

            Thread.Sleep(Spacing);
        }
    }

    /// <summary>One delete through the shell: the recycled item's identity when it went to the Recycle Bin, and the HRESULT.</summary>
    private static (string? Identity, int Error) DeleteOnce(string path, uint flags)
    {
        var created = Shell.SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out var item);
        if (created < 0)
        {
            return (null, created);
        }

        IFileOperation? operation = null;
        try
        {
            operation = (IFileOperation)new FileOperation();
            var sink = new DeleteSink();
            var step = operation.SetOperationFlags(flags);
            if (step >= 0)
            {
                step = operation.DeleteItem(item, sink);
            }

            if (step >= 0)
            {
                step = operation.PerformOperations();
            }

            // The shell can stop an operation and still answer success; only this says it did (GetAnyOperationsAborted).
            if (step >= 0 && operation.GetAnyOperationsAborted(out var aborted) >= 0 && aborted)
            {
                step = Shell.Aborted;
            }

            if (sink.Error < 0)
            {
                return (null, sink.Error);
            }

            if (step < 0)
            {
                return (null, step);
            }

            return (sink.RecycledIdentity, 0);
        }
        catch (Exception e)
        {
            // Creating the shell's file operation reports an HRESULT that the runtime maps to one of many exception
            // types (access denied, out of memory, no such interface); each is this file's failure, never a crash.
            return (null, e.HResult);
        }
        finally
        {
            if (operation is not null)
            {
                Marshal.ReleaseComObject(operation);
            }

            Marshal.ReleaseComObject(item);
        }
    }

    // The shell refuses to move a folder while any file inside it is open, whatever its share mode, as access denied
    // (observed: COPYENGINE_E_ACCESS_DENIED_SRC), moving nothing of it; only a file open elsewhere tells that from a
    // refusal of the folder itself.
    private static bool HoldsAnOpenFile(string path, int hresult) =>
        hresult is (Shell.CopyEngineAccessDeniedSource or Shell.AccessDenied) && Files.HeldFileRetry.HoldsAnOpenFile(path);

    private static bool IsHeld(int hresult) =>
        hresult is Shell.CopyEngineSharingViolationSource or Shell.CopyEngineSharingViolationDestination
        || (IsWin32(hresult) && (hresult & 0xFFFF) is 32 or 33);

    private static string ReasonFor(int hresult)
    {
        if (IsHeld(hresult))
        {
            return Files.HeldFileRetry.HeldReason;
        }

        if (IsWin32(hresult) && (hresult & 0xFFFF) is 2 or 3)
        {
            return "the file does not exist";
        }

        return Marshal.GetExceptionForHR(hresult)?.Message ?? $"Windows reported error 0x{hresult:X8}";
    }

    private static bool IsWin32(int hresult) => (hresult & 0xFFFF0000) == 0x80070000;

    private static T OnStaThread<T>(Func<T> work)
    {
        T result = default!;
        var thread = new Thread(() => result = work()) { IsBackground = true, Name = "Recycle Bin" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    /// <summary>Receives the one delete's result and the item it became in the Recycle Bin.</summary>
    private sealed class DeleteSink : IFileOperationProgressSink
    {
        public int Error { get; private set; }

        public string? RecycledIdentity { get; private set; }

        public int PostDeleteItem(uint flags, IShellItem item, int hrDelete, IShellItem? newlyCreated)
        {
            Error = hrDelete;
            if (hrDelete >= 0 && newlyCreated is not null
                && newlyCreated.GetDisplayName(Shell.SigdnDesktopAbsoluteParsing, out var name) >= 0)
            {
                RecycledIdentity = Marshal.PtrToStringUni(name);
                Marshal.FreeCoTaskMem(name);
            }

            return 0;
        }

        public int StartOperations() => 0;
        public int FinishOperations(int hrResult) => 0;
        public int PreRenameItem(uint flags, IShellItem item, string? newName) => 0;
        public int PostRenameItem(uint flags, IShellItem item, string? newName, int hrRename, IShellItem? newlyCreated) => 0;
        public int PreMoveItem(uint flags, IShellItem item, IShellItem destinationFolder, string? newName) => 0;
        public int PostMoveItem(uint flags, IShellItem item, IShellItem destinationFolder, string? newName, int hrMove, IShellItem? newlyCreated) => 0;
        public int PreCopyItem(uint flags, IShellItem item, IShellItem destinationFolder, string? newName) => 0;
        public int PostCopyItem(uint flags, IShellItem item, IShellItem destinationFolder, string? newName, int hrCopy, IShellItem? newlyCreated) => 0;
        public int PreDeleteItem(uint flags, IShellItem item) => 0;
        public int PreNewItem(uint flags, IShellItem destinationFolder, string? newName) => 0;
        public int PostNewItem(uint flags, IShellItem destinationFolder, string? newName, string? templateName, uint fileAttributes, int hrNew, IShellItem? newItem) => 0;
        public int UpdateProgress(uint workTotal, uint workSoFar) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }

    private static class Shell
    {
        public const uint FofNoUi = 0x0614;
        public const uint FofAllowUndo = 0x0040;
        public const uint FofxRecycleOnDelete = 0x00080000;
        public const uint SigdnDesktopAbsoluteParsing = 0x80028000;
        // COPYENGINE_E_SHARING_VIOLATION_SRC and _DEST (winerror.h; observed for a file held with FileShare.None).
        public const int CopyEngineSharingViolationSource = unchecked((int)0x80270027);
        public const int CopyEngineSharingViolationDestination = unchecked((int)0x80270028);
        // COPYENGINE_E_ACCESS_DENIED_SRC (winerror.h; observed for a folder holding a file another program has open) and E_ACCESSDENIED.
        public const int CopyEngineAccessDeniedSource = unchecked((int)0x80270021);
        public const int AccessDenied = unchecked((int)0x80070005);
        // E_ABORT (winerror.h).
        public const int Aborted = unchecked((int)0x80004004);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, in Guid riid, out IShellItem item);
    }

    [ComImport]
    [Guid("3ad05575-8857-4850-9277-11b85bdb8e09")]
    private class FileOperation
    {
    }

    [ComImport]
    [Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig] int Advise(IFileOperationProgressSink sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(IntPtr dialog);
        [PreserveSig] int SetProperties(IntPtr properties);
        [PreserveSig] int SetOwnerWindow(IntPtr owner);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem item);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
        [PreserveSig] int RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, IFileOperationProgressSink? sink);
        [PreserveSig] int RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        [PreserveSig] int MoveItem(IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName, IFileOperationProgressSink? sink);
        [PreserveSig] int MoveItems(IntPtr items, IShellItem destinationFolder);
        [PreserveSig] int CopyItem(IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? copyName, IFileOperationProgressSink? sink);
        [PreserveSig] int CopyItems(IntPtr items, IShellItem destinationFolder);
        [PreserveSig] int DeleteItem(IShellItem item, IFileOperationProgressSink? sink);
        [PreserveSig] int DeleteItems(IntPtr items);
        [PreserveSig] int NewItem(IShellItem destinationFolder, uint fileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? templateName, IFileOperationProgressSink? sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComImport]
    [Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int hrResult);
        [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
        [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int hrRename, IShellItem? newlyCreated);
        [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
        [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int hrMove, IShellItem? newlyCreated);
        [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
        [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int hrCopy, IShellItem? newlyCreated);
        [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
        [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int hrDelete, IShellItem? newlyCreated);
        [PreserveSig] int PreNewItem(uint flags, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
        [PreserveSig] int PostNewItem(uint flags, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName, [MarshalAs(UnmanagedType.LPWStr)] string? templateName, uint fileAttributes, int hrNew, IShellItem? newItem);
        [PreserveSig] int UpdateProgress(uint workTotal, uint workSoFar);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr bindContext, in Guid handler, in Guid riid, out IntPtr result);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint sigdn, out IntPtr name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
    }
}
