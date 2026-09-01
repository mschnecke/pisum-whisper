namespace Pisum.Whisper.Core.Autostart;

/// <summary>
/// The login registration could not be read or written. Carried as a type rather than left as
/// whatever the registry or the file system raises, mirroring <c>SystemShellException</c>, so that
/// <see cref="AutostartReconciler"/> can catch exactly this and let the application come up anyway.
/// </summary>
public sealed class AutostartException(string message, Exception? innerException = null)
    : Exception(message, innerException);
