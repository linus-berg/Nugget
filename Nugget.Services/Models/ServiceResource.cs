using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class ServiceResource
{
  [JsonPropertyName("@id")]
  public string Id { get; set; } = "";
  [JsonPropertyName("@type")]
  public string Type { get; set; } = "";
}