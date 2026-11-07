using Avalonia.Controls;

namespace ImageStash.Seams;

/// <summary>The platform's standard file dialogs and its file browser (F4, F13): Import files…'s, Move…'s, Save a copy…'s, Export stash…'s and Import stash…'s pickers, Open with…'s dialog, Reveal in File Explorer, and Settings' Open folder.</summary>
public interface IDialogsAndExplorer
{
    /// <summary>
    /// Shows the platform's open picker, owned by <paramref name="owner"/>, for choosing several files, offering
    /// <paramref name="filters"/> in order; the chosen files' local paths, or null when it was cancelled.
    /// </summary>
    Task<IReadOnlyList<string>?> OpenFiles(TopLevel owner, string title, IReadOnlyList<PickerFilter> filters);

    /// <summary>
    /// Shows the platform's open picker, owned by <paramref name="owner"/>, for choosing one file (Import stash…, G36),
    /// offering <paramref name="filters"/> in order; the chosen file's local path, or null when it was cancelled.
    /// </summary>
    Task<string?> OpenFile(TopLevel owner, string title, IReadOnlyList<PickerFilter> filters);

    /// <summary>
    /// Shows the platform's open picker in its folder mode, owned by <paramref name="owner"/>, for choosing one folder
    /// (Move…, G30, G32): the chosen folder's local path spelled as the folder it reaches (every link followed, another
    /// drive's or a short name's spelling of it spelled out), or null when it was cancelled.
    /// </summary>
    Task<string?> PickFolder(TopLevel owner, string title);

    /// <summary>
    /// Shows the platform's save picker, owned by <paramref name="owner"/>, suggesting
    /// <paramref name="suggestedFileName"/> and offering <paramref name="fileType"/> alone; it asks before a chosen
    /// existing file is replaced (G9, S16). The chosen local path, its folder spelled as the folder it reaches (every
    /// link followed, another drive's or a short name's spelling of it spelled out), or null when it was cancelled.
    /// </summary>
    Task<string?> SaveFile(TopLevel owner, string title, string suggestedFileName, PickerFilter fileType);

    /// <summary>
    /// Shows the platform's dialog for opening the file at <paramref name="path"/> with a program the user chooses, owned
    /// by <paramref name="owner"/>, and completes once the dialog has closed, the chosen program started (G27, S16): done
    /// when a program was chosen or the dialog cancelled; a failure naming the file otherwise. It never says whether a
    /// program started or wrote the file.
    /// </summary>
    Task<Outcome> OpenWith(TopLevel owner, string path);

    /// <summary>Opens the folder holding <paramref name="path"/> in the platform's file browser with that file selected (G9); a failure names the file.</summary>
    Outcome Reveal(string path);

    /// <summary>
    /// Opens <paramref name="folder"/> itself in the platform's file browser (G32); done once the browser was asked, a
    /// failure naming the folder otherwise, a folder that is not there, or is no folder, included.
    /// </summary>
    Outcome OpenFolder(string folder);
}

/// <summary>A picker's file type: its name as listed, and the name patterns it shows (<c>*.png</c>).</summary>
public sealed record PickerFilter(string Name, IReadOnlyList<string> Patterns);
