using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class SearchVersion
{
  [JsonPropertyName("version")]
  public string Version { get; set; } = "";
  [JsonPropertyName("@id")]
  public string Id { get; set; } = "";
}