using Atendefy.API.Modules.AI.Models;
using Atendefy.API.Modules.Chatbot.Models;
using Atendefy.API.Modules.WhatsApp.Models;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Infrastructure.Database;

public class TenantDbContext(DbContextOptions<TenantDbContext> options, string schema) : DbContext(options)
{
    public string SchemaName => schema;
    public DbSet<WhatsAppAccount> WhatsAppAccounts => Set<WhatsAppAccount>();
    public DbSet<AiConfig> AiConfigs => Set<AiConfig>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> Messages => Set<ConversationMessage>();
    public DbSet<UsageCounter> UsageCounters => Set<UsageCounter>();
    public DbSet<Contact> Contacts => Set<Contact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(schema);

        modelBuilder.Entity<WhatsAppAccount>(e =>
        {
            e.ToTable("whatsapp_accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.ConfigJson).HasColumnType("jsonb");
            e.Property(x => x.Status).HasMaxLength(50);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<AiConfig>(e =>
        {
            e.ToTable("ai_configs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            e.Property(x => x.Model).HasMaxLength(100);
        });

        modelBuilder.Entity<Conversation>(e =>
        {
            e.ToTable("conversations");
            e.HasKey(x => x.Id);
            e.Property(x => x.ContactPhone).HasMaxLength(30).IsRequired();
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.ConversationId);
            e.HasQueryFilter(x => !x.IsDeleted);
            e.Property(x => x.BotPaused).HasDefaultValue(false);
            e.Property(x => x.AccountId);
        });

        modelBuilder.Entity<ConversationMessage>(e =>
        {
            e.ToTable("messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Role).HasMaxLength(20).IsRequired();
            e.Property(x => x.Content).IsRequired();
        });

        modelBuilder.Entity<UsageCounter>(e =>
        {
            e.ToTable("usage_counters");
            e.HasKey(x => x.Month);
            e.Property(x => x.Month).HasMaxLength(7);
            e.Property(x => x.CostUsd).HasColumnType("decimal(10,4)");
        });

        modelBuilder.Entity<Contact>(e =>
        {
            e.ToTable("contacts");
            e.HasKey(x => x.Phone);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Name).HasMaxLength(200);
        });
    }
}
