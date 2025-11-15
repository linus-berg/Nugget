using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class SearchHit
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = "";
  [JsonPropertyName("version")]
  public string Version { get; set; } = "";
  [JsonPropertyName("description")]
  public string Description { get; set; } = "";
  [JsonPropertyName("authors")]
  public string[] Authors { get; set; } = Array.Empty<string>();
  [JsonPropertyName("versions")]
  public List<SearchVersion> Versions { get; set; } = new();
}