using Microsoft.EntityFrameworkCore;

namespace MiniMonitor.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ActivitySample> ActivitySamples => Set<ActivitySample>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ActivitySample>(entity =>
        {
            // Nomes em snake_case. O padrão do EF seria PascalCase, o que no
            // Postgres obriga a usar aspas em toda query escrita na mão.
            entity.ToTable("activity_samples");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");

            entity.Property(e => e.Hostname)
                .HasColumnName("hostname")
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(e => e.Username)
                .HasColumnName("username")
                .HasMaxLength(255)
                .IsRequired();

            // DateTimeOffset vira timestamptz no Postgres, que guarda o instante
            // absoluto. Sem limite de tamanho no título porque nome de janela
            // varia demais e truncar perderia informação da agregação.
            entity.Property(e => e.CapturedAtUtc).HasColumnName("captured_at_utc");
            entity.Property(e => e.ReceivedAtUtc).HasColumnName("received_at_utc");

            entity.Property(e => e.WindowTitle)
                .HasColumnName("window_title")
                .IsRequired();
        });
    }
}
