namespace Pisum.Whisper.Core.Tests.Autostart;

using Pisum.Whisper.Core.Autostart;

/// <summary>
/// A login registration in memory, counting every read and every write so that "wrote nothing"
/// is assertable rather than inferred.
/// </summary>
/// <remarks>
/// <see cref="Registration"/> is the whole state, and it is the three-valued one rather than a flag:
/// the case this fake exists to let a test reach is
/// <see cref="AutostartRegistration.Stale"/> — registered, and pointing somewhere else.
/// </remarks>
public sealed class FakeAutostartService : IAutostartService
{
    public AutostartRegistration Registration { get; set; }

    public int Reads { get; private set; }

    public int Enables { get; private set; }

    public int Disables { get; private set; }

    /// <summary>Writes, of either kind. The reconciler's whole contract is about this being zero.</summary>
    public int Writes => Enables + Disables;

    /// <summary>Set to make every call throw, as a locked registry key or a machine policy does.</summary>
    public AutostartException? Failure { get; set; }

    public AutostartRegistration Read()
    {
        Reads++;
        Throw();

        return Registration;
    }

    public void Enable()
    {
        Enables++;
        Throw();

        // Enable overwrites, so it lands on Current from Absent and from Stale alike — which is what
        // makes repointing one call rather than a delete and a create.
        Registration = AutostartRegistration.Current;
    }

    public void Disable()
    {
        Disables++;
        Throw();

        Registration = AutostartRegistration.Absent;
    }

    private void Throw()
    {
        if (Failure is { } failure)
        {
            throw failure;
        }
    }
}
