namespace Pisum.Whisper.Core.Settings;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(CamelCaseEnumConverter<AudioFormat>), typeof(CamelCaseEnumConverter<RecordingMode>)],
    WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Preset))]
[JsonSerializable(typeof(ProviderConfig))]
public sealed partial class SettingsJsonContext : JsonSerializerContext
{
    /// <summary>
    /// The context of the settings file is read and written through. It is <see cref="Default"/> plus
    /// a relaxed encoder, which the source-generation attribute has no way to express: the default
    /// encoder escapes every non-ASCII character, which would write the German built-in prompts as
    /// a wall of <c>ü</c> and undo the hand-editability camelCase is there to preserve. What
    /// the relaxed encoder relaxes is escaping meant for HTML, and a settings file is not HTML.
    /// </summary>
    /// <remarks>
    /// Built lazily because it reads <see cref="Default"/>, which the generator declares in the
    /// same class and which is therefore not yet assigned while this class is being initialized.
    /// </remarks>
    public static SettingsJsonContext OnDisk => LazyOnDisk.Value;

    private static readonly Lazy<SettingsJsonContext> LazyOnDisk = new(CreateOnDiskContext);

    private static SettingsJsonContext CreateOnDiskContext()
    {
        var options = new JsonSerializerOptions(Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

            // Cleared so the base constructor points it at the context being built rather than
            // leaving it aimed at Default, whose options carry the escaping encoder.
            TypeInfoResolver = null,
        };

        return new SettingsJsonContext(options);
    }
}

/// <summary>
/// One camelCase enum converter serves both settings enums. The reference is inconsistent —
/// <c>AudioFormat</c> is lowercase while <c>RecordingMode</c> is camelCase — but every
/// <see cref="AudioFormat"/> name is a single word, so both render identically under camelCase.
/// It is closed over each enum rather than used open because the non-generic converter cannot be
/// statically analyzed, which would rule out trimming and AOT later.
/// </summary>
public sealed class CamelCaseEnumConverter<TEnum>()
    : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase)
    where TEnum : struct, Enum;
