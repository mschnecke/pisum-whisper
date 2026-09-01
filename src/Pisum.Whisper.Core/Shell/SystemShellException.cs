namespace Pisum.Whisper.Core.Shell;

/// <summary>
/// The file browser could not be launched. Carried as a type rather than left as whatever
/// <c>Process.Start</c> raises, so the one caller can report it and keep the window usable.
/// </summary>
public sealed class SystemShellException(string message, Exception? innerException = null)
    : Exception(message, innerException);
