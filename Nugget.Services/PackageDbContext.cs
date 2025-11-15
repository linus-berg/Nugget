using Microsoft.EntityFrameworkCore;
using Nugget.Services.DatabaseModels;

namespace Nugget.Services;

public class PackageDbContext : DbContext
{
    public PackageDbContext(DbContextOptions<PackageDbContext> options) : base(options) { }

    public DbSet<PackageVersion> package_versions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder model_builder)
    {
        // Ensure that a package ID + version is unique
        model_builder.Entity<PackageVersion>()
            .HasIndex(p => new {
                PackageId = p.package_id,
                Version = p.version })
            .IsUnique();
        
        // Add an index on PackageId for faster searches
        model_builder.Entity<PackageVersion>()
            .HasIndex(p => p.package_id);
    }
}
