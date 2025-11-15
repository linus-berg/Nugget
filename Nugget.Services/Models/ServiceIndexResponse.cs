using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class ServiceIndexResponse
{
  [JsonPropertyName("version")]
  public string Version { get; set; } = "3.0.0";
  [JsonPropertyName("resources")]
  public List<ServiceResource> Resources { get; set; } = new();
}