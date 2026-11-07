using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using ImageStash.Media;
using ImageStash.Seams;
using Microsoft.Win32.SafeHandles;

namespace ImageStash.Windows.Seams;

/// <summary>
/// A drop as Avalonia 12.1.4 hands it over on Windows: File Explorer's files and folders (CF_HDROP) as storage items
/// with local paths; a browser's or chat's picture as a shell virtual file (FileGroupDescriptorW and FileContents),
/// a file item with no local path whose contents open once; and a link dragged out of a browser as text alone.
/// The clipboard is written with the Win32 clipboard API, every format rendered at the write as memory the system
/// then owns: CF_HDROP, the registered "PNG", CF_DIBV5 with alpha, and "Preferred DropEffect" as Copy (How G8 holds).
/// Avalonia's own clipboard renders on request, which a hidden window or a quit ImageStash would no longer answer.
/// A drag out of the window is the shell's own file drag, offering a copy only. The clipboard is read with the same
/// Win32 API by <see cref="Read"/> alone for the paste, files, else "PNG", else a bitmap, else text, and by
/// <see cref="ReadImage"/> alone for Stash clipboard image, "PNG", else a bitmap, as How G13 holds says, each copying
/// out only the format it takes (N9, R5). The write remembered for G26's refresh is in <c>WindowsClipboardRefresh.cs</c>.
/// </summary>
public sealed partial class WindowsClipboardAndDrops : IClipboardAndDrops
{
    private const string Action = "Copy";
    private const string ReadAction = "Paste";
    private const string ImageReadAction = "Stash clipboard image";
    private const string Subject = "the clipboard";
    private const uint CfBitmap = 2;
    private const uint CfDib = 8;
    private const uint CfUnicodeText = 13;
    private const uint CfHdrop = 15;
    private const uint CfDibV5 = 17;
    private const uint DropEffectCopy = 1;
    private const int ErrorAccessDenied = 5;
    private const uint GmemMoveable = 0x0002;
    private const string DragAction = "Drag out";
    private const int DragDropSDrop = 0x00040100;
    private const uint FileReadAttributes = 0x0080;
    private const uint FileShareAll = 0x0007;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const int FileIdInfoClass = 18;
    private static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// ImageStash's own opens of the clipboard take turns: Windows opens it for a second thread of a process while
    /// another of its threads holds it, and the second one's close then closes it under the first, whose reads find
    /// nothing or a handle already let go (probed: the first thread's next EnumClipboardFormats failed with error 1418).
    /// </summary>
    private static readonly Lock OneOpenAtATime = new();
    private static readonly Guid DataObjectId = new("0000010e-0000-0000-c000-000000000046");

    public DropContents ReadDrop(IDataTransfer data)
    {
        if (!data.Contains(DataFormat.File))
        {
            // No file item is built for such a payload, so judging it fetches nothing (N2).
            return new DropContents([], IsLink(data.TryGetText()), filesUnavailable: false, []);
        }

        // A virtual file stays readable after the drop returns, until it is disposed (Avalonia's OleVirtualFileData).
        var storageItems = data.TryGetFiles() ?? [];
        var items = new List<DroppedItem>(storageItems.Length);
        foreach (var storageItem in storageItems)
        {
            var path = storageItem.TryGetLocalPath();
            items.Add(storageItem switch
            {
                IStorageFolder => new DroppedItem(storageItem.Name, path, null, IsFolder: true),
                IStorageFile file when path is null => new DroppedItem(storageItem.Name, null, file.OpenReadAsync, IsFolder: false),
                _ => new DroppedItem(storageItem.Name, path, null, IsFolder: false),
            });
        }

        return new DropContents(items, onlyLink: false, filesUnavailable: items.Count == 0, storageItems);
    }

    public Task<Outcome> Write(TopLevel owner, ClipboardContent content, TimeSpan heldClipboardRetry)
    {
        // The stash window owns what it puts there: an open with no window keeps no other program out, and the
        // documentation says a write with no owner fails. Its handle is read here, on the UI thread, and used from
        // the pool thread, which the clipboard allows (probed on Windows 11 build 26300).
        var window = owner.TryGetPlatformHandle()?.Handle ?? 0;
        return Task.Run(() => WriteNow(window, content, heldClipboardRetry, onlyWhile: null) is { Succeeded: false } refused ? refused.Failure : Outcome.Done);
    }

    public Task<Outcome> WriteText(TopLevel owner, string action, string text, TimeSpan heldClipboardRetry)
    {
        var window = owner.TryGetPlatformHandle()?.Handle ?? 0;
        return Task.Run(() =>
        {
            if (Open(window, heldClipboardRetry, action) is { Succeeded: false } held)
            {
                return held;
            }

            try
            {
                Forget();
                if (!EmptyClipboard() || Set(CfUnicodeText, Encoding.Unicode.GetBytes(text + '\0')) is not null)
                {
                    return new Failure(action, Subject, WindowsRefusal.ReasonFor(Marshal.GetLastPInvokeError()));
                }

                return Outcome.Done;
            }
            finally
            {
                Close();
            }
        });
    }

    // A read keeps nobody out and needs no owner, so it opens with no window, from a pool thread (probed).
    public Task<Outcome<ClipboardReading>> Read(TimeSpan heldClipboardRetry) => Task.Run(() => ReadNow(ReadAction, imageOnly: false, heldClipboardRetry));

    public Task<Outcome<ClipboardImage?>> ReadImage(TimeSpan heldClipboardRetry) => Task.Run(() =>
    {
        var read = ReadNow(ImageReadAction, imageOnly: true, heldClipboardRetry);
        return !read.Succeeded ? read.Failure
            : Outcome<ClipboardImage?>.Of(read.Value is { Png: null, Image: null } ? null : new ClipboardImage(read.Value!.Png, read.Value.Image));
    });

    /// <summary>A failure of the paste's read of the clipboard, in words; the image-only read names its own act instead (G35).</summary>
    internal static Failure ReadFailure(string reason) => new(ReadAction, Subject, reason);

    public Task<Outcome<bool>> StartDrag(TopLevel owner, PointerPressedEventArgs press, IReadOnlyList<string> filePaths)
    {
        var window = owner.TryGetPlatformHandle()?.Handle ?? 0;
        // The drag takes the mouse from here on, so the pressed control lets go of it, as Avalonia's own drag source does.
        press.Pointer.Capture(null);
        return Task.FromResult(DragNow(window, filePaths));
    }

    /// <summary>
    /// The shell's own drag of the files, as File Explorer starts one: its data object for the files (CF_HDROP among its
    /// formats) and its drop source, with the files' drag image, offering a copy only. Run through the shell rather than
    /// Avalonia's drag source, which drops the native call's result, so a drag that could not run is named rather than
    /// read as cancelled (G35).
    /// </summary>
    private static Outcome<bool> DragNow(nint window, IReadOnlyList<string> filePaths)
    {
        var paths = filePaths.Select(Path.GetFullPath).ToList();
        var folder = Path.GetDirectoryName(paths[0])!;
        var subject = paths.Count == 1 ? Path.GetFileName(paths[0]) : $"{paths.Count} files";
        if (paths.FirstOrDefault(path => !string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase)) is { } elsewhere)
        {
            return new Failure(DragAction, Path.GetFileName(elsewhere), "it is not in the folder of the other files");
        }

        var idLists = new List<nint>();
        nint data = 0;
        try
        {
            foreach (var path in paths.Prepend(folder))
            {
                // A file no longer there is refused by the shell by name, rather than dragged as nothing.
                var parsed = SHParseDisplayName(path, 0, out var idList, 0, out _);
                if (parsed < 0)
                {
                    return new Failure(DragAction, Path.GetFileName(path), ReasonForResult(parsed));
                }

                idLists.Add(idList);
            }

            var children = idLists.Skip(1).Select(ILFindLastID).ToArray();
            var created = SHCreateDataObject(idLists[0], (uint)children.Length, children, 0, DataObjectId, out data);
            if (created < 0)
            {
                return new Failure(DragAction, subject, ReasonForResult(created));
            }

            var dragged = SHDoDragDrop(window, data, 0, DropEffectCopy, out var effect);
            if (dragged < 0)
            {
                return new Failure(DragAction, subject, ReasonForResult(dragged));
            }

            // The effect is set only when the drop was not cancelled, and a destination that took nothing reports none.
            return Outcome<bool>.Of(dragged == DragDropSDrop && (effect & DropEffectCopy) != 0);
        }
        finally
        {
            if (data != 0)
            {
                Marshal.Release(data);
            }

            foreach (var idList in idLists)
            {
                CoTaskMemFree(idList);
            }
        }
    }

    /// <summary>
    /// The whole write in one open of the clipboard, every format made before it opens so another program waits on
    /// it as briefly as possible. A format refused once the clipboard is emptied empties it again, so no part of a
    /// write is left looking like a finished copy (G35). With <paramref name="onlyWhile"/>, it writes only while that
    /// holds once the clipboard is open, before anything else; what it wrote is remembered after its close (G26).
    /// </summary>
    private Outcome<bool> WriteNow(nint window, ClipboardContent content, TimeSpan heldClipboardRetry, Func<bool>? onlyWhile)
    {
        var formats = new List<(uint Format, byte[] Bytes)> { (CfHdrop, DropFiles(content.FilePaths)) };
        if (content is { Png: { } png, Bitmap: { } bitmap })
        {
            if (Registered("PNG") is not { } pngFormat)
            {
                return Refused();
            }

            formats.Add((pngFormat, png));
            formats.Add((CfDibV5, DibV5(bitmap)));
        }

        // A paste into a folder copies the stash's file, never moves it out of the stash (G29).
        if (Registered("Preferred DropEffect") is not { } effectFormat)
        {
            return Refused();
        }

        formats.Add((effectFormat, BitConverter.GetBytes(DropEffectCopy)));

        var chosen = content.FilePaths.Select(FileIdentity).ToList();
        var opened = Open(window, heldClipboardRetry, Action);
        if (!opened.Succeeded)
        {
            return opened.Failure;
        }

        var written = false;
        try
        {
            if (onlyWhile is not null && !onlyWhile())
            {
                return Outcome<bool>.Of(false);
            }

            // A file gone while the clipboard was held, its item removed meanwhile, would paste as nothing (G35).
            if (content.FilePaths.FirstOrDefault(path => !File.Exists(path)) is { } gone)
            {
                return new Failure(Action, Subject, $"{Path.GetFileName(gone)} no longer exists");
            }

            // Another file at the same path, its item removed and another of the same name added meanwhile, would hand
            // over a picture nobody chose (G8). The file's identity, not its times: the file system may give a file made
            // under a name just freed that name's creation time.
            if (content.FilePaths.Where((path, i) => FileIdentity(path) != chosen[i]).FirstOrDefault() is { } replaced)
            {
                return new Failure(Action, Subject, $"{Path.GetFileName(replaced)} was replaced by another file");
            }

            Forget();
            if (!EmptyClipboard())
            {
                return Refused();
            }

            foreach (var (format, bytes) in formats)
            {
                if (Set(format, bytes) is { } error)
                {
                    EmptyClipboard();
                    return new Failure(Action, Subject, WindowsRefusal.ReasonFor(error));
                }
            }

            written = true;
            return Outcome<bool>.Of(true);
        }
        finally
        {
            Close(written ? () => Remember(window, content.Items) : null);
        }
    }

    /// <summary>
    /// The read in one open of the clipboard: its formats enumerated in the order they were placed, Windows' own
    /// conversions after the format they come from, then the first of files, "PNG", a bitmap and text copied out, or
    /// with <paramref name="imageOnly"/> the first of "PNG" and a bitmap, nothing else, and the clipboard closed before
    /// a bitmap is converted, so another program waits on it briefly. A format Windows cannot hand over is read as
    /// absent and the order goes on. Its failures name <paramref name="action"/>, the act the read is for (G35).
    /// </summary>
    private static Outcome<ClipboardReading> ReadNow(string action, bool imageOnly, TimeSpan heldClipboardRetry)
    {
        var pngFormat = Registered("PNG");
        var opened = Open(0, heldClipboardRetry, action);
        if (!opened.Succeeded)
        {
            return opened.Failure;
        }

        (IReadOnlyList<DroppedItem> Files, byte[]? Png, byte[]? Dib, bool OnlyLink, string? Text) copied;
        try
        {
            copied = CopyOut(pngFormat, imageOnly);
        }
        catch (OutOfMemoryException)
        {
            return new Failure(action, Subject, "what it holds is too large to hold in memory");
        }
        finally
        {
            Close();
        }

        if (copied.Dib is not { } dib)
        {
            return Outcome<ClipboardReading>.Of(new ClipboardReading(copied.Files, copied.Png, null, copied.OnlyLink, copied.Text));
        }

        var frame = WindowsDib.Convert(dib);
        return frame.Succeeded ? Outcome<ClipboardReading>.Of(new ClipboardReading([], null, frame.Value, false)) : frame.Failure with { Action = action };
    }

    /// <summary>
    /// What the open clipboard holds, in G13's order: CF_HDROP's paths, no file's bytes read; else the "PNG" bytes; else
    /// the bitmap a program placed itself, CF_DIBV5 or CF_DIB, whichever is enumerated first, since Windows enumerates
    /// its conversions after it, or CF_DIB as Windows makes it from a CF_BITMAP; else its text and whether that is a link (N2).
    /// With <paramref name="imageOnly"/>, neither CF_HDROP nor the text is asked for: the "PNG" bytes, else the bitmap.
    /// </summary>
    private static (IReadOnlyList<DroppedItem> Files, byte[]? Png, byte[]? Dib, bool OnlyLink, string? Text) CopyOut(uint? pngFormat, bool imageOnly)
    {
        var formats = new List<uint>();
        for (var format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
        {
            formats.Add(format);
        }

        if (!imageOnly && formats.Contains(CfHdrop) && DroppedFiles() is { Count: > 0 } files)
        {
            return (files, null, null, false, null);
        }

        if (pngFormat is { } png && formats.Contains(png) && Copy(png) is { } pngBytes)
        {
            return ([], pngBytes, null, false, null);
        }

        var bitmap = formats.FirstOrDefault(format => format is CfDibV5 or CfDib);
        if (bitmap == 0 && formats.Contains(CfBitmap))
        {
            bitmap = CfDib;
        }

        if (bitmap != 0 && Copy(bitmap) is { } dib)
        {
            return ([], null, dib, false, null);
        }

        var text = !imageOnly && formats.Contains(CfUnicodeText) ? Text() : null;
        return ([], null, null, IsLink(text), text);
    }

    /// <summary>CF_HDROP's paths in their order, each a file or a folder as it is now; null when Windows hands over none.</summary>
    private static List<DroppedItem>? DroppedFiles()
    {
        var drop = GetClipboardData(CfHdrop);
        if (drop == 0)
        {
            return null;
        }

        var count = DragQueryFileW(drop, uint.MaxValue, null, 0);
        var files = new List<DroppedItem>();
        for (var i = 0u; i < count; i++)
        {
            var path = new StringBuilder((int)DragQueryFileW(drop, i, null, 0) + 1);
            if (DragQueryFileW(drop, i, path, (uint)path.Capacity) == 0)
            {
                continue;
            }

            var full = path.ToString();
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(full));
            files.Add(new DroppedItem(name.Length > 0 ? name : full, full, null, IsFolder: Directory.Exists(full)));
        }

        return files;
    }

    /// <summary>
    /// One format's bytes, copied at once, as the clipboard keeps its memory; null when Windows hands over none. A block
    /// larger than an array can hold is out of memory, which the read names.
    /// </summary>
    private static byte[]? Copy(uint format)
    {
        var memory = GetClipboardData(format);
        var size = memory == 0 ? 0 : GlobalSize(memory);
        if (size == 0)
        {
            return null;
        }

        if (size > (nuint)Array.MaxLength)
        {
            throw new OutOfMemoryException();
        }

        var pointer = GlobalLock(memory);
        if (pointer == 0)
        {
            return null;
        }

        try
        {
            var bytes = new byte[(int)size];
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            GlobalUnlock(memory);
        }
    }

    /// <summary>CF_UNICODETEXT up to its first NUL; null when Windows hands over none.</summary>
    private static string? Text() =>
        Copy(CfUnicodeText) is { } bytes ? Encoding.Unicode.GetString(bytes).Split('\0')[0] : null;

    /// <summary>
    /// Opens the clipboard for this thread once no other open of ImageStash's holds it, again every 50 ms while it is
    /// refused, for at most <paramref name="retry"/>: another program holding it open refuses it with "access denied"
    /// (probed). Once it has opened, the caller closes it with <see cref="Close"/>.
    /// </summary>
    private static Outcome Open(nint window, TimeSpan retry, string action)
    {
        OneOpenAtATime.Enter();
        var clock = Stopwatch.StartNew();
        while (true)
        {
            if (OpenClipboard(window))
            {
                return Outcome.Done;
            }

            var error = Marshal.GetLastPInvokeError();
            var left = retry - clock.Elapsed;
            if (left <= TimeSpan.Zero)
            {
                OneOpenAtATime.Exit();
                return new Failure(action, Subject, error == ErrorAccessDenied ? HeldClipboard.Reason : WindowsRefusal.ReasonFor(error));
            }

            Thread.Sleep(left < Spacing ? left : Spacing);
        }
    }

    /// <summary>Closes the clipboard <see cref="Open"/> opened on this thread, runs <paramref name="closed"/>, and lets ImageStash's next open go ahead.</summary>
    private static void Close(Action? closed = null)
    {
        CloseClipboard();
        closed?.Invoke();
        OneOpenAtATime.Exit();
    }

    /// <summary>
    /// One format, its bytes in movable memory that the clipboard owns once it has taken them; null when it took
    /// them, else the Win32 error that refused them.
    /// </summary>
    private static int? Set(uint format, byte[] bytes)
    {
        var memory = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
        if (memory == 0)
        {
            return Marshal.GetLastPInvokeError();
        }

        var pointer = GlobalLock(memory);
        if (pointer == 0)
        {
            var refused = Marshal.GetLastPInvokeError();
            GlobalFree(memory);
            return refused;
        }

        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        GlobalUnlock(memory);
        if (SetClipboardData(format, memory) == 0)
        {
            var refused = Marshal.GetLastPInvokeError();
            GlobalFree(memory);
            return refused;
        }

        return null;
    }

    /// <summary>
    /// CF_HDROP: a DROPFILES header (the list at offset 20, wide characters) and every full path, each ending in a
    /// NUL, with one more NUL after the last.
    /// </summary>
    private static byte[] DropFiles(IReadOnlyList<string> paths)
    {
        const int header = 20;
        var list = string.Concat(paths.Select(path => Path.GetFullPath(path) + "\0")) + "\0";
        var bytes = new byte[header + Encoding.Unicode.GetByteCount(list)];
        BitConverter.GetBytes(header).CopyTo(bytes, 0);
        BitConverter.GetBytes(1).CopyTo(bytes, 16);
        Encoding.Unicode.GetBytes(list).CopyTo(bytes, header);
        return bytes;
    }

    /// <summary>
    /// CF_DIBV5: a BITMAPV5HEADER for 32 bits per pixel with an alpha mask, in sRGB, then the frame's rows as they are,
    /// BGRA with straight alpha, only reordered bottom-up as a DIB stores them.
    /// </summary>
    private static byte[] DibV5(MediaFrame frame)
    {
        const int header = 124;
        var rowBytes = frame.Width * 4;
        var bytes = new byte[header + rowBytes * frame.Height];
        BitConverter.GetBytes(header).CopyTo(bytes, 0);
        BitConverter.GetBytes(frame.Width).CopyTo(bytes, 4);
        BitConverter.GetBytes(frame.Height).CopyTo(bytes, 8);
        BitConverter.GetBytes((ushort)1).CopyTo(bytes, 12);
        BitConverter.GetBytes((ushort)32).CopyTo(bytes, 14);
        BitConverter.GetBytes(3u).CopyTo(bytes, 16); // BI_BITFIELDS
        BitConverter.GetBytes((uint)(rowBytes * frame.Height)).CopyTo(bytes, 20);
        BitConverter.GetBytes(0x00FF0000u).CopyTo(bytes, 40);
        BitConverter.GetBytes(0x0000FF00u).CopyTo(bytes, 44);
        BitConverter.GetBytes(0x000000FFu).CopyTo(bytes, 48);
        BitConverter.GetBytes(0xFF000000u).CopyTo(bytes, 52);
        BitConverter.GetBytes(0x73524742u).CopyTo(bytes, 56); // LCS_sRGB
        BitConverter.GetBytes(4u).CopyTo(bytes, 108); // LCS_GM_IMAGES
        for (var y = 0; y < frame.Height; y++)
        {
            Array.Copy(frame.Pixels, y * rowBytes, bytes, header + (frame.Height - 1 - y) * rowBytes, rowBytes);
        }

        return bytes;
    }

    /// <summary>
    /// The file at <paramref name="path"/> as the file system knows it: its volume and file identifier, the 128-bit one
    /// where the file system gives it (ReFS's 64-bit index is not unique), else the 64-bit index, which with the volume
    /// tells whether two paths reach the same file (GetFileInformationByHandle); null when it cannot be read. Opened
    /// for its attributes alone, so a program holding the file open without sharing does not stand in the way, and by
    /// its extended-length path, since a stash path can be longer than MAX_PATH, the limit of a plain one.
    /// </summary>
    private static (ulong Volume, UInt128 Id)? FileIdentity(string path)
    {
        using var file = CreateFileW(ExtendedLength(path), FileReadAttributes, FileShareAll, 0, OpenExisting, FileFlagBackupSemantics, 0);
        if (file.IsInvalid)
        {
            return null;
        }

        if (GetFileInformationByHandleEx(file, FileIdInfoClass, out var id, (uint)Marshal.SizeOf<FileIdInfo>()))
        {
            return (id.VolumeSerialNumber, new UInt128(id.IdHigh, id.IdLow));
        }

        return GetFileInformationByHandle(file, out var information)
            ? (information.VolumeSerialNumber, (ulong)information.FileIndexHigh << 32 | information.FileIndexLow)
            : null;
    }

    /// <summary>A full path as Win32 takes it past MAX_PATH: <c>\\?\</c> before a drive's path, <c>\\?\UNC\</c> before a share's.</summary>
    private static string ExtendedLength(string path)
    {
        var full = Path.GetFullPath(path);
        return full.StartsWith(@"\\?\", StringComparison.Ordinal) ? full
            : full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..]
            : @"\\?\" + full;
    }

    private static uint? Registered(string name)
    {
        var format = RegisterClipboardFormatW(name);
        return format == 0 ? null : format;
    }

    private static Failure Refused() => new(Action, Subject, WindowsRefusal.ReasonFor(Marshal.GetLastPInvokeError()));

    /// <summary>A failed HRESULT in words; one that carries a Win32 error, by that error's words.</summary>
    private static string ReasonForResult(int result) =>
        (result & 0xFFFF0000) == 0x80070000 ? WindowsRefusal.ReasonFor(result & 0xFFFF) : new Win32Exception(result).Message;

    /// <summary>A browser's link drag carries its address as text: an absolute http or https address, and nothing else.</summary>
    private static bool IsLink(string? text) =>
        Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var address) && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps);

    // BY_HANDLE_FILE_INFORMATION, its FILETIMEs as their two DWORDs each.
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow, CreationTimeHigh, LastAccessTimeLow, LastAccessTimeHigh, LastWriteTimeLow, LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh, FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

    // FILE_ID_INFO: the volume's 64-bit serial number and the file's 128-bit identifier, its low half first in memory.
    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        public ulong VolumeSerialNumber;
        public ulong IdLow, IdHigh;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass, out FileIdInfo information, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint format);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint DragQueryFileW(nint drop, uint index, StringBuilder? path, uint capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint RegisterClipboardFormatW(string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalFree(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint GlobalSize(nint memory);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint idList, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern nint ILFindLastID(nint idList);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHCreateDataObject(nint folder, uint count, nint[] children, nint inner, in Guid interfaceId, out nint dataObject);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHDoDragDrop(nint window, nint dataObject, nint dropSource, uint allowedEffects, out uint effect);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoTaskMemFree(nint memory);
}
