using Microsoft.EntityFrameworkCore;
using NpApi.Api.Features.People;
using NpApi.Api.Features.Photos;

namespace NpApi.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<PhotoPerson> PhotoPeople => Set<PhotoPerson>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Picks up every IEntityTypeConfiguration<T> in this assembly (e.g. NoteConfiguration).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
