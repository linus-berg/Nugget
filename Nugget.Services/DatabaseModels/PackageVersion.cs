namespace Nugget.Services.DatabaseModels;

public class PackageVersion {
  public int id { get; set; } // Primary Key
  public string package_id { get; set; } = "";
  public string version { get; set; } = "";
  public string description { get; set; } = "";
  public string authors { get; set; } = "";
  public DateTime published { get; set; } 
}