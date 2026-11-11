using System.Text.Json.Serialization;

namespace Radiacode.App;

/// <summary>
/// Compile-time JSON metadata for the files the app stores. The Release build is trimmed, and trimmed
/// apps cannot use reflection-based System.Text.Json, so settings and the background would silently
/// fail to load and save without this.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(BackgroundSpectrum))]
internal sealed partial class AppJsonContext : JsonSerializerContext;