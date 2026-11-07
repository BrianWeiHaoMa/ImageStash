namespace ImageStash.Media;

/// <summary>
/// A clip being written frame by frame, as the recorder sees Media's one GIF encoder (R4): full frames with their times
/// on the clip's timeline in, the time of Stop, Cancel, and the bytes written so far. <see cref="GifEncoder"/> is the one
/// implementation; the interface lets the recorder's checks stand a slow or failing encoder in for the library.
/// </summary>
public interface IClipEncoder
{
    /// <summary>The bytes written so far, readable from any thread while another call runs.</summary>
    long BytesWritten { get; }

    /// <summary>Adds a full frame shown from <paramref name="atMs"/>, no earlier than the previous frame's time; the frame is the encoder's until the call returns.</summary>
    Outcome Append(MediaFrame frame, long atMs);

    /// <summary>Ends the clip at <paramref name="atMs"/>: the clip written whole, or null when it holds no frame and so left no file.</summary>
    Outcome<GifClip?> Stop(long atMs);

    /// <summary>Finishes the clip without Stop, its file closed and left for the caller to discard through Store.</summary>
    void Cancel();
}

/// <summary>Opens a clip of <paramref name="width"/> × <paramref name="height"/> physical pixels at <paramref name="workPath"/>, a <c>.work\</c> path Store allocated; failures name <paramref name="name"/>.</summary>
public delegate Outcome<IClipEncoder> ClipOpener(string workPath, int width, int height, string name);
