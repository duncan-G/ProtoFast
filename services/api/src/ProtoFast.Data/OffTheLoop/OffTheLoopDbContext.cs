using Microsoft.EntityFrameworkCore;
using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop;

/// <summary>OffTheLoop's slice of the <c>protofast</c> database, in the <c>loop</c> schema with its own migrations history.</summary>
public sealed class OffTheLoopDbContext(
    DbContextOptions<OffTheLoopDbContext> options,
    QueryFilterService queryFilterService,
    UserContext userContext)
    : DbContextBase(options, queryFilterService, userContext)
{
    public const string Schema = "loop";

    private const int UserIdLength = 255;

    /// <summary>Enums are stored by name, so renaming a member needs a data migration.</summary>
    private const int EnumLength = 32;

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Application> Applications => Set<Application>();

    public DbSet<Note> Notes => Set<Note>();

    public DbSet<Todo> Todos => Set<Todo>();

    public DbSet<Goal> Goals => Set<Goal>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<ModelRun> ModelRuns => Set<ModelRun>();

    public DbSet<Resume> Resumes => Set<Resume>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(OffTheLoopLimits.Name);
            entity.Property(c => c.About).IsRequired().HasMaxLength(OffTheLoopLimits.Line);
            entity.Property(c => c.Location).IsRequired().HasMaxLength(OffTheLoopLimits.Label);
            entity.Property(c => c.WhatItDoes).IsRequired().HasMaxLength(OffTheLoopLimits.Summary);
            entity.Property(c => c.HowItMakesMoney).IsRequired().HasMaxLength(OffTheLoopLimits.Summary);
            entity.Property(c => c.Revenue).IsRequired().HasMaxLength(OffTheLoopLimits.Label);
            entity.Property(c => c.Growth).IsRequired().HasMaxLength(OffTheLoopLimits.Label);
            entity.Property(c => c.Headcount).IsRequired().HasMaxLength(OffTheLoopLimits.Label);
            entity.Property(c => c.Funding).IsRequired().HasMaxLength(OffTheLoopLimits.Label);
            entity.Property(c => c.LastRaised).IsRequired().HasMaxLength(OffTheLoopLimits.Label);

            entity.OwnsMany(c => c.Contacts, contact => contact.ToJson());

            entity.HasIndex(c => c.UserId);
        });

        modelBuilder.Entity<Application>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(a => a.Role).IsRequired().HasMaxLength(OffTheLoopLimits.Name);
            entity.Property(a => a.Stage).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(a => a.Source).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(a => a.SourceDetail).IsRequired().HasMaxLength(OffTheLoopLimits.Label);
            entity.Property(a => a.Resume).IsRequired().HasMaxLength(OffTheLoopLimits.Label);
            entity.Property(a => a.PostingUrl).IsRequired().HasMaxLength(OffTheLoopLimits.Url);
            entity.Property(a => a.Posting).IsRequired().HasMaxLength(OffTheLoopLimits.Posting);
            entity.Property(a => a.NextStep).IsRequired().HasMaxLength(OffTheLoopLimits.Line);
            entity.Property(a => a.PostedPay).IsRequired().HasMaxLength(OffTheLoopLimits.Label);
            entity.Property(a => a.AskedPay).IsRequired().HasMaxLength(OffTheLoopLimits.Label);
            entity.Property(a => a.OfferedPay).IsRequired().HasMaxLength(OffTheLoopLimits.Label);
            entity.Property(a => a.OfferTerms).IsRequired().HasMaxLength(OffTheLoopLimits.Line);
            entity.Property(a => a.Team).IsRequired().HasMaxLength(OffTheLoopLimits.Line);
            entity.Property(a => a.ReportsTo).IsRequired().HasMaxLength(OffTheLoopLimits.Name);
            entity.Property(a => a.Arrangement).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(a => a.Location).IsRequired().HasMaxLength(OffTheLoopLimits.Label);
            entity.Property(a => a.Hours).IsRequired().HasMaxLength(OffTheLoopLimits.Label);

            entity.OwnsMany(a => a.Rounds, round =>
            {
                round.ToJson();
                round.Property(r => r.State).HasConversion<string>();
            });
            entity.OwnsMany(a => a.Asked, asked => asked.ToJson());
            entity.OwnsMany(a => a.Learnings, learning => learning.ToJson());

            entity.HasOne(a => a.Company)
                .WithMany()
                .HasForeignKey(a => a.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Document>()
                .WithMany()
                .HasForeignKey(a => a.ResumeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<Document>()
                .WithMany()
                .HasForeignKey(a => a.CvId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(a => a.UserId);
            entity.HasIndex(a => a.CompanyId);
            entity.HasIndex(a => a.ResumeId);
            entity.HasIndex(a => a.CvId);
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(d => d.Kind).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(d => d.Name).IsRequired().HasMaxLength(OffTheLoopLimits.Label);
            entity.Property(d => d.FileName).IsRequired().HasMaxLength(OffTheLoopLimits.FileName);
            entity.Property(d => d.MediaType).IsRequired().HasMaxLength(OffTheLoopLimits.Name);
            entity.Property(d => d.StorageKey).IsRequired().HasMaxLength(OffTheLoopLimits.Line);

            entity.HasIndex(d => new { d.UserId, d.DateCreated });
        });

        modelBuilder.Entity<Note>(entity =>
        {
            entity.HasKey(n => n.Id);
            entity.Property(n => n.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(n => n.Kind).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(n => n.Title).IsRequired().HasMaxLength(OffTheLoopLimits.Name);
            entity.Property(n => n.Tag).IsRequired().HasMaxLength(OffTheLoopLimits.Label);
            entity.Property(n => n.Body).IsRequired().HasMaxLength(OffTheLoopLimits.Body);

            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(n => n.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(n => new { n.UserId, n.DateLastModified });
            entity.HasIndex(n => n.CompanyId);
        });

        modelBuilder.Entity<Todo>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(t => t.Label).IsRequired().HasMaxLength(OffTheLoopLimits.Line);

            entity.HasIndex(t => t.UserId);
        });

        modelBuilder.Entity<Goal>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.Property(g => g.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(g => g.Kind).IsRequired().HasMaxLength(OffTheLoopLimits.Label);
            entity.Property(g => g.Target).IsRequired().HasMaxLength(OffTheLoopLimits.Label);
            entity.Property(g => g.Current).IsRequired().HasMaxLength(OffTheLoopLimits.Label);

            entity.HasIndex(g => g.UserId);
        });

        modelBuilder.Entity<Resume>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(r => r.Name).IsRequired().HasMaxLength(OffTheLoopLimits.Name);
            entity.Property(r => r.Headline).IsRequired().HasMaxLength(OffTheLoopLimits.Line);
            entity.Property(r => r.Email).IsRequired().HasMaxLength(OffTheLoopLimits.Name);
            entity.Property(r => r.Phone).IsRequired().HasMaxLength(OffTheLoopLimits.Label);
            entity.Property(r => r.Location).IsRequired().HasMaxLength(OffTheLoopLimits.Label);
            entity.Property(r => r.Summary).IsRequired().HasMaxLength(OffTheLoopLimits.Text);

            entity.OwnsMany(r => r.Links, link => link.ToJson());
            entity.OwnsMany(r => r.Experience, role =>
            {
                role.ToJson();
                role.OwnsMany(x => x.Evidence);
            });
            entity.OwnsMany(r => r.Projects, project =>
            {
                project.ToJson();
                project.OwnsMany(x => x.Evidence);
            });
            entity.OwnsMany(r => r.Skills, group => group.ToJson());
            entity.OwnsMany(r => r.Education, school => school.ToJson());
            entity.OwnsMany(r => r.Extras, group => group.ToJson());

            entity.HasOne<Application>()
                .WithMany()
                .HasForeignKey(r => r.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            // One general resume per user, and one per application.
            entity.HasIndex(r => new { r.UserId, r.ApplicationId }).IsUnique().AreNullsDistinct(false);
            entity.HasIndex(r => r.ApplicationId);
        });

        modelBuilder.Entity<ModelRun>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.UserId).IsRequired().HasMaxLength(UserIdLength);
            entity.Property(r => r.Kind).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(EnumLength);
            entity.Property(r => r.Label).IsRequired().HasMaxLength(OffTheLoopLimits.Line);
            entity.Property(r => r.Work).IsRequired();
            entity.Property(r => r.Error).HasMaxLength(OffTheLoopLimits.Line);
            entity.Property(r => r.TraceParent).HasMaxLength(OffTheLoopLimits.Label);

            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(r => r.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Application>()
                .WithMany()
                .HasForeignKey(r => r.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Note>()
                .WithMany()
                .HasForeignKey(r => r.NoteId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(r => new { r.UserId, r.DateCreated });
            entity.HasIndex(r => new { r.Status, r.DateCreated });
            entity.HasIndex(r => r.CompanyId);
            entity.HasIndex(r => r.ApplicationId);
            entity.HasIndex(r => r.NoteId);
        });
    }
}
