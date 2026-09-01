namespace Pisum.Whisper.Core.Tests.Autostart;

using Pisum.Whisper.Core.Autostart;

/// <summary>
/// A login registration in memory, counting every read and every write so that "wrote nothing"
/// is assertable rather than inferred.
/// </summary>
public sealed class FakeAutostartService : IAutostartService
{
    public bool Registered { get; set; }

    public int Reads { get; private set; }

    public int Enables { get; private set; }

    public int Disables { get; private set; }

    /// <summary>Writes, of either kind. The reconciler's whole contract is about this being zero.</summary>
    public int Writes => Enables + Disables;

    /// <summary>Set to make every call throw, as a locked registry key or a machine policy does.</summary>
    public AutostartException? Failure { get; set; }

    public bool IsEnabled()
    {
        Reads++;
        Throw();

        return Registered;
    }

    public void Enable()
    {
        Enables++;
        Throw();

        Registered = true;
    }

    public void Disable()
    {
        Disables++;
        Throw();

        Registered = false;
    }

    private void Throw()
    {
        if (Failure is { } failure)
        {
            throw failure;
        }
    }
}
