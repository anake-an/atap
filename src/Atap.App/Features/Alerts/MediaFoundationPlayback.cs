using System.IO;
using Atap.Core.Alerts;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Atap.Features.Alerts;

/// <summary>
/// Plays one file or bundled sound to its end, once, on the default audio device. Media Foundation reads
/// MP3, WAV, WMA and AAC, and OGG and FLAC with the media extensions Windows comes with. This is the only
/// class that uses NAudio, so NAudio loads only when such a sound plays.
/// </summary>
internal sealed class MediaFoundationPlayback : ISoundHandle
{
    private readonly object _gate = new();
    private readonly Action<Exception?> _ended;
    private Stream? _stream;
    private WaveStream? _reader;
    private WasapiPlayer? _output;
    private bool _stopped;
    private int _reported;

    private MediaFoundationPlayback(Action<Exception?> ended) => _ended = ended;

    public static MediaFoundationPlayback Start(AlertSound sound, Action<Exception?> ended)
    {
        var playback = new MediaFoundationPlayback(ended);
        // Opening the file and the audio device can take a moment, so it never runs on the UI thread.
        _ = Task.Run(() => playback.Open(sound));
        return playback;
    }

    public void Stop()
    {
        WasapiPlayer? output;
        lock (_gate)
        {
            _stopped = true;
            output = _output;
        }
        if (output is null)
            return; // Still opening: Open sees the stop and reports the end.

        // Stopping waits for the audio thread, so it is not done on the UI thread either.
        _ = Task.Run(() =>
        {
            Exception? error = null;
            try
            {
                output.Stop();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            try
            {
                Close();
            }
            finally
            {
                Report(error);
            }
        });
    }

    /// <summary>The largest sound file Atap plays. An azan is a few megabytes.</summary>
    public const long MaxFileBytes = 50L * 1024 * 1024;

    /// <summary>
    /// Opens a sound file the user picked as a plain stream, so Media Foundation never resolves a path or a
    /// web address itself. Refuses network drives (a mapped Z: is still another computer), Windows device
    /// names such as COM1, and very large files.
    /// </summary>
    /// <param name="path">A full local path, already checked by <see cref="AlertSounds"/>.</param>
    /// <param name="driveType">Tells the type of a drive root. The real drive when null.</param>
    /// <param name="maxBytes">The size limit. <see cref="MaxFileBytes"/> when null.</param>
    internal static FileStream OpenLocalFile(string path, Func<string, DriveType>? driveType = null, long? maxBytes = null)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
            throw new IOException("The sound file has no drive.");
        var type = (driveType ?? (r => new DriveInfo(r).DriveType))(root);
        if (type is DriveType.Network or DriveType.NoRootDirectory or DriveType.Unknown)
            throw new IOException("Sound files on network drives are not played.");
        if (IsDeviceName(Path.GetFileNameWithoutExtension(path)))
            throw new IOException("This file name is reserved by Windows.");

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        if (stream.Length > (maxBytes ?? MaxFileBytes))
        {
            stream.Dispose();
            throw new IOException("The sound file is too large.");
        }
        return stream;
    }

    private static bool IsDeviceName(string name)
    {
        var upper = name.Trim().TrimEnd('.').ToUpperInvariant();
        if (upper is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$")
            return true;
        return upper.Length == 4 && (upper.StartsWith("COM", StringComparison.Ordinal) || upper.StartsWith("LPT", StringComparison.Ordinal))
            && (char.IsAsciiDigit(upper[3]) || upper[3] is '¹' or '²' or '³');
    }

    private void Open(AlertSound sound)
    {
        try
        {
            if (sound.Kind == AlertSoundKind.Bundled)
            {
                _stream = typeof(MediaFoundationPlayback).Assembly.GetManifestResourceStream("Atap.Data." + sound.Value)
                    ?? throw new FileNotFoundException("Atap has no bundled sound with this name.", sound.Value);
                _reader = new StreamMediaFoundationReader(_stream, new MediaFoundationReader.MediaFoundationReaderSettings(), originName: sound.Value);
            }
            else
            {
                _stream = OpenLocalFile(sound.Value);
                _reader = new StreamMediaFoundationReader(_stream, new MediaFoundationReader.MediaFoundationReaderSettings(), originName: Path.GetFileName(sound.Value));
            }

            // Shared mode, so Windows converts the sample rate and mixes it with other sounds.
            var output = new WasapiPlayerBuilder().WithSharedMode().WithCategory(AudioStreamCategory.Alerts).Build();
            try
            {
                output.PlaybackStopped += OnPlaybackStopped;
                output.Init(_reader);
            }
            catch
            {
                output.PlaybackStopped -= OnPlaybackStopped;
                output.Dispose();
                throw;
            }
            lock (_gate)
            {
                _output = output;
                if (!_stopped)
                {
                    // Plays once to the end of the reader. Nothing loops it.
                    output.Play();
                    return;
                }
            }
            CloseThenReport(null);
        }
        catch (Exception ex)
        {
            CloseThenReport(ex);
        }
    }

    // Raised on the audio thread, which cannot dispose its own player.
    private void OnPlaybackStopped(object? sender, StoppedEventArgs e) =>
        _ = Task.Run(() => CloseThenReport(e.Exception));

    /// <summary>The end is reported even when releasing the device throws, or the alert would wait for it forever.</summary>
    private void CloseThenReport(Exception? error)
    {
        try
        {
            Close();
        }
        finally
        {
            Report(error);
        }
    }

    private void Close()
    {
        WasapiPlayer? output;
        WaveStream? reader;
        Stream? stream;
        lock (_gate)
        {
            output = _output;
            reader = _reader;
            stream = _stream;
            _output = null;
            _reader = null;
            _stream = null;
        }
        if (output is not null)
        {
            output.PlaybackStopped -= OnPlaybackStopped;
            output.Dispose();
        }
        reader?.Dispose();
        stream?.Dispose();
    }

    private void Report(Exception? error)
    {
        if (Interlocked.Exchange(ref _reported, 1) == 0)
            _ended(error);
    }
}
