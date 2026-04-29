using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class PackageVersionsResponse
{
    [JsonPropertyName("versions")]
    public List<string> versions { get; set; } = new();
}
