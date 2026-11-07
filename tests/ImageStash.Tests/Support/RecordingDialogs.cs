using Avalonia.Controls;
using ImageStash.Seams;

namespace ImageStash.Tests.Support;

/// <summary>
/// The dialogs seam for the portable checks: keeps what each save picker was asked to show, each file the Open-with
/// dialog was asked on, each file revealed and each folder opened, and answers with what the check sets: the picker's
/// chosen path, null for a cancel, the dialog's outcome, or a task the check completes, so the dialog stays "up" until
/// it does.
/// </summary>
public sealed class RecordingDialogs : IDialogsAndExplorer
{
    public List<(string Title, string SuggestedFileName, PickerFilter FileType)> SavePickers { get; } = [];

    /// <summary>What the next save pickers answer: the chosen path, or null when cancelled.</summary>
    public Func<Task<string?>> SaveAnswer { get; set; } = () => Task.FromResult<string?>(null);

    /// <summary>The files the Open-with dialog was asked on, in order.</summary>
    public List<string> OpenedWith { get; } = [];

    /// <summary>What the next Open-with dialogs answer once closed: done at once, unless the check sets another.</summary>
    public Func<Task<Outcome>> OpenWithAnswer { get; set; } = () => Task.FromResult(Outcome.Done);

    public List<string> Revealed { get; } = [];

    public Outcome RevealAnswer { get; set; } = Outcome.Done;

    /// <summary>The titles the folder picker was shown with, in order.</summary>
    public List<string> FolderPickers { get; } = [];

    /// <summary>What the next folder pickers answer: the chosen folder, or null when cancelled.</summary>
    public Func<Task<string?>> PickFolderAnswer { get; set; } = () => Task.FromResult<string?>(null);

    public Task<string?> PickFolder(TopLevel owner, string title)
    {
        FolderPickers.Add(title);
        return PickFolderAnswer();
    }

    /// <summary>The titles and file types the one-file picker was shown with, in order.</summary>
    public List<(string Title, IReadOnlyList<PickerFilter> Filters)> FilePickers { get; } = [];

    /// <summary>What the next one-file pickers answer: the chosen file, or null when cancelled.</summary>
    public Func<Task<string?>> OpenFileAnswer { get; set; } = () => Task.FromResult<string?>(null);

    public Task<string?> OpenFile(TopLevel owner, string title, IReadOnlyList<PickerFilter> filters)
    {
        FilePickers.Add((title, filters));
        return OpenFileAnswer();
    }

    /// <summary>The checks hand an import its files directly.</summary>
    public Task<IReadOnlyList<string>?> OpenFiles(TopLevel owner, string title, IReadOnlyList<PickerFilter> filters) =>
        throw new NotSupportedException("The checks hand an import its files themselves.");

    public Task<string?> SaveFile(TopLevel owner, string title, string suggestedFileName, PickerFilter fileType)
    {
        SavePickers.Add((title, suggestedFileName, fileType));
        return SaveAnswer();
    }

    public Task<Outcome> OpenWith(TopLevel owner, string path)
    {
        lock (OpenedWith)
        {
            OpenedWith.Add(path);
        }

        return OpenWithAnswer();
    }

    public Outcome Reveal(string path)
    {
        Revealed.Add(path);
        return RevealAnswer;
    }

    /// <summary>The folders the file browser was asked to open, in order.</summary>
    public List<string> OpenedFolders { get; } = [];

    public Outcome OpenFolderAnswer { get; set; } = Outcome.Done;

    public Outcome OpenFolder(string folder)
    {
        OpenedFolders.Add(folder);
        return OpenFolderAnswer;
    }
}
