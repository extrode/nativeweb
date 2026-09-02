using System.Text.Json.Serialization;

namespace NativeWeb.Sample;

/// <summary>
/// The source-generated serializer for everything this app answers as JSON.
/// </summary>
/// <remarks>
/// <see cref="NativeWeb.Render.FormatResults.Json{T}"/> takes a
/// <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}"/> and
/// nothing else, so there is no reflection path to fall back on and no
/// trim warning to suppress: a type not listed here cannot be serialized, and
/// the build says so.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IReadOnlyList<PublishedArticle>))]
internal sealed partial class SampleJson : JsonSerializerContext
{
}
