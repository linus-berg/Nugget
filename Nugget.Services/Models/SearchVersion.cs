using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class SearchVersion {
  [JsonPropertyName("version")]
  public string version { get; set; } = "";

  [JsonPropertyName("@id")]
  public string id { get; set; } = "";
}