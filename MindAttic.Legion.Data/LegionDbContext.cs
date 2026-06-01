using Microsoft.EntityFrameworkCore;

namespace MindAttic.Legion.Data;

/// <summary>
/// EF Core context for Legion's SQL Server store: the persona library, every
/// versioned <see cref="AssessmentRunEntity"/>, the scored
/// <see cref="PsychometricProfileEntity"/> rows (five frameworks as owned
/// columns), and the optional raw item-response audit trail.
/// </summary>
public class LegionDbContext : DbContext
{
    public LegionDbContext(DbContextOptions<LegionDbContext> options) : base(options) { }

    public DbSet<PersonaEntity> Personas => Set<PersonaEntity>();
    public DbSet<AssessmentRunEntity> AssessmentRuns => Set<AssessmentRunEntity>();
    public DbSet<PsychometricProfileEntity> PsychometricProfiles => Set<PsychometricProfileEntity>();
    public DbSet<AssessmentItemResponseEntity> ItemResponses => Set<AssessmentItemResponseEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<PersonaEntity>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasMaxLength(64);
            e.Property(p => p.Name).HasMaxLength(128);
            e.Property(p => p.Archetype).HasMaxLength(64);
            e.Property(p => p.Worldview).HasMaxLength(64);
            e.Property(p => p.Background).HasMaxLength(64);
            e.Property(p => p.Pronouns).HasMaxLength(32);
            e.Property(p => p.ProviderId).HasMaxLength(32);
            e.HasIndex(p => p.Archetype);
            e.HasIndex(p => p.IsDefault);
        });

        b.Entity<AssessmentRunEntity>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Provider).HasMaxLength(32);
            e.Property(r => r.Model).HasMaxLength(128);
            e.Property(r => r.Tier).HasMaxLength(16);
            e.Property(r => r.InstrumentSetVersion).HasMaxLength(32);
            e.Property(r => r.Notes).HasMaxLength(256);
        });

        b.Entity<PsychometricProfileEntity>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.PersonaId).HasMaxLength(64);
            e.Property(p => p.AdministeredByProvider).HasMaxLength(32);
            e.Property(p => p.AdministeredByModel).HasMaxLength(128);
            e.Property(p => p.InstrumentSetVersion).HasMaxLength(32);

            e.HasOne(p => p.Persona)
                .WithMany(p => p.Profiles)
                .HasForeignKey(p => p.PersonaId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(p => p.AssessmentRun)
                .WithMany(r => r.Profiles)
                .HasForeignKey(p => p.AssessmentRunId)
                .OnDelete(DeleteBehavior.Cascade);

            // One profile per persona per run.
            e.HasIndex(p => new { p.PersonaId, p.AssessmentRunId }).IsUnique();

            // Five frameworks as owned (flat, prefixed) columns.
            e.OwnsOne(p => p.Ocean);
            e.OwnsOne(p => p.Hexaco);
            e.OwnsOne(p => p.Disc, d => d.Property(x => x.PrimaryStyle).HasMaxLength(4));
            e.OwnsOne(p => p.Mbti, m => m.Property(x => x.Type).HasMaxLength(4));
            e.OwnsOne(p => p.Enneagram, en => en.Property(x => x.Triad).HasMaxLength(16));

            e.Navigation(p => p.Ocean).IsRequired();
            e.Navigation(p => p.Hexaco).IsRequired();
            e.Navigation(p => p.Disc).IsRequired();
            e.Navigation(p => p.Mbti).IsRequired();
            e.Navigation(p => p.Enneagram).IsRequired();
        });

        b.Entity<AssessmentItemResponseEntity>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.PersonaId).HasMaxLength(64);
            e.Property(r => r.Instrument).HasMaxLength(16);
            e.HasOne(r => r.AssessmentRun)
                .WithMany()
                .HasForeignKey(r => r.AssessmentRunId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => new { r.AssessmentRunId, r.PersonaId });
        });
    }
}
