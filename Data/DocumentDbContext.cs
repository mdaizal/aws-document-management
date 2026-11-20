using aws_document_management.Documents;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace aws_document_management.Data;

public sealed class DocumentDbContext(DbContextOptions<DocumentDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Document>(entity =>
        {
            entity.Property(document => document.Title).HasMaxLength(200);
            entity.Property(document => document.FileName).HasMaxLength(255);
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(document => document.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(document => new { document.OwnerId, document.Ready, document.CreatedAt });
        });
        builder.Entity<DocumentTag>(entity =>
        {
            entity.HasKey(tag => new { tag.DocumentId, tag.Value });
            entity.Property(tag => tag.Value).HasMaxLength(40);
            entity.HasIndex(tag => tag.Value);
        });
    }
}
