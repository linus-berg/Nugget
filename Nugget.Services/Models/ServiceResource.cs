using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class ServiceResource
{
  [JsonPropertyName("@id")]
  public string id { get; set; } = "";
  [JsonPropertyName("@type")]
  public string type { get; set; } = "";
}