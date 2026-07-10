using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class RegistrationPage
{
  [JsonPropertyName("@id")]
  public string id { get; set; } = "";
  [JsonPropertyName("lower")]
  public string lower { get; set; } = "";
  [JsonPropertyName("upper")]
  public string upper { get; set; } = "";
  [JsonPropertyName("count")]
  public int count { get; set; }
  [JsonPropertyName("items")]
  public List<RegistrationPageItem> items { get; set; } = new();
}