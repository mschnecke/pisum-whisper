namespace Pisum.Whisper.Spikes;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var spike = args.FirstOrDefault();
        return spike switch
        {
            "hook" => await HookSpike.RunAsync(),
            "api" => ApiDump.Run(args),
            "paste" => await PasteSpike.RunAsync(),
            "audio" => await AudioSpike.RunAsync(),
            "opus" => await OpusSpike.RunAsync(),
            "tray" => await TraySpike.RunAsync(),
            "combined" => await CombinedSpike.RunAsync(),
            _ => Usage(spike),
        };
    }

    private static int Usage(string? given)
    {
        Console.WriteLine($"unknown spike '{given}'. try: hook");
        return 2;
    }
}
