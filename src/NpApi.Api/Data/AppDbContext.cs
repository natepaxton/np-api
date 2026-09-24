using Microsoft.EntityFrameworkCore;
using NpApi.Api.Features.People;
using NpApi.Api.Features.Photos;
using NpApi.Api.Features.Places;

namespace NpApi.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<PhotoPerson> PhotoPeople => Set<PhotoPerson>();
    public DbSet<Place> Places => Set<Place>();
    public DbSet<Country> Countries => Set<Country>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Picks up every IEntityTypeConfiguration<T> in this assembly (e.g. NoteConfiguration).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
