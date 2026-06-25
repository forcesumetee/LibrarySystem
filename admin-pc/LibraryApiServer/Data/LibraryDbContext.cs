using LibraryApiServer.Entities;
using Microsoft.EntityFrameworkCore;

namespace LibraryApiServer.Data;

public class LibraryDbContext : DbContext
{
    public LibraryDbContext(DbContextOptions<LibraryDbContext> options) : base(options)
    {
    }

    public DbSet<Book> Books => Set<Book>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Book>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.RegNo).IsRequired();
            entity.Property(x => x.Title).IsRequired();
            entity.Property(x => x.Category).IsRequired();
            entity.Property(x => x.Shelf).IsRequired();
            entity.Property(x => x.Publisher).IsRequired();

            entity.HasIndex(x => x.RegNo).IsUnique();
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).IsRequired();
            entity.Property(x => x.Value).IsRequired();
        });
    }
}