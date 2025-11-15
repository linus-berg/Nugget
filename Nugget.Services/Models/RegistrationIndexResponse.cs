using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class RegistrationIndexResponse
{
  [JsonPropertyName("count")]
  public int Count { get; set; }
  [JsonPropertyName("items")]
  public List<RegistrationPage> Items { get; set; } = new();
}