// Persistence/SearchEngineContext.cs
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using SearchEngine.Persistence.Entities;

namespace SearchEngine.Persistence;

public class SearchEngineContext : DbContext
{
    public DbSet<DocumentEntity> Documents { get; set; }
    public DbSet<DocumentTermEntity> DocumentTerms { get; set; }

    public SearchEngineContext(DbContextOptions<SearchEngineContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<DocumentEntity>(b =>
        {
            b.HasKey(d => d.Id);

            b.Property(d => d.Title)
             .IsRequired()
             .HasMaxLength(200);
             
            b.Property(d => d.CompressedContent)
             .IsRequired(false);

            b.HasMany(d => d.Terms)
             .WithOne(t => t.Document)
             .HasForeignKey(t => t.DocumentId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DocumentTermEntity>(b =>
        {
            b.HasKey(t => new { t.DocumentId, t.Term });

            b.Property(t => t.Term)
             .IsRequired()
             .HasMaxLength(200);

            b.Property(t => t.PositionsJson)
             .IsRequired();
        });
    }
}

