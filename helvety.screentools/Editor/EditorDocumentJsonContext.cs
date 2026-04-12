using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace helvety.screentools.Editor
{
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(EditorDocumentPayload))]
    [JsonSerializable(typeof(EditorStatePayload))]
    [JsonSerializable(typeof(LayerPayload))]
    [JsonSerializable(typeof(RegionPayload))]
    [JsonSerializable(typeof(List<LayerPayload>))]
    internal partial class EditorDocumentJsonContext : JsonSerializerContext
    {
    }
}
