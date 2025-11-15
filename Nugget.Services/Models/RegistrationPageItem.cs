using System.Text.Json.Serialization;

namespace Nugget.Services.Models;

public class RegistrationPageItem {
  [JsonPropertyName("catalogEntry")]
  public RegistrationCatalogEntry catalog_entry { get; set; } = new();
}