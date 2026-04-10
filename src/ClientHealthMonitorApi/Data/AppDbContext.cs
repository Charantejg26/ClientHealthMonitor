using Microsoft.EntityFrameworkCore;
using ClientHealthMonitor.Data.Domain;


namespace ClientHealthMonitor.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options): base(options)
        {
        }

        // Define DbSets for your entities here
        public DbSet<Users> Users { get; set; }
        public DbSet<Clients> Clients { get; set; }
        public DbSet<AuthTokens> AuthTokens { get; set; }
        public DbSet<ClientInteraction> ClientInteractions { get; set; }
        public DbSet<ClientScore> ClientScores { get; set; }
        public DbSet<Feedback> Feedbacks { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Convert all table and column names to lowercase
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                entity.SetTableName(entity.GetTableName().ToLower());

                foreach (var property in entity.GetProperties())
                {
                    property.SetColumnName(property.GetColumnName().ToLower());
                }
            }

            //Table names
            modelBuilder.Entity<Users>().ToTable("users");
            modelBuilder.Entity<AuthTokens>().ToTable("authtokens");
            modelBuilder.Entity<Clients>().ToTable("clients");
            modelBuilder.Entity<ClientInteraction>().ToTable("clientinteractions");
            modelBuilder.Entity<ClientScore>().ToTable("clientscores");
            modelBuilder.Entity<Feedback>().ToTable("feedback");

            // Configure entity properties and relationships here if needed
            modelBuilder.Entity<Users>(b =>
            {
                b.HasIndex(u => u.Email).IsUnique();
                b.HasIndex(u => u.GoogleId).IsUnique();
            });

            modelBuilder.Entity<AuthTokens>(b =>
            {
                // configure one-to-one relationship: User has one AuthTokens, AuthTokens has one User with FK UserId
                b.HasOne(t => t.User)
                 .WithOne(u => u.AuthToken)
                 .HasForeignKey<AuthTokens>(t => t.UserId)
                 .OnDelete(DeleteBehavior.Cascade);

                // ensure UserId is unique to enforce one-to-one
                b.HasIndex(t => t.UserId).IsUnique();
            });

            modelBuilder.Entity<Clients>(b =>
            {
                b.HasIndex(c => new { c.UserId, c.Client_Email }).IsUnique();
                b.HasIndex(c => new { c.UserId, c.Client_Name });
                b.Property(c => c.Client_Name).HasMaxLength(255).IsRequired();
                b.HasOne(c => c.User).WithMany(u => u.Clients).HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ClientInteraction>(b =>
            {
                b.HasIndex(i => new { i.UserId, i.gmail_message_id }).IsUnique();
                b.Property(i => i.InteractionType).HasMaxLength(50).IsRequired();
                b.HasOne(i => i.Client).WithMany(c => c.Interactions).HasForeignKey(i => i.ClientId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(i => i.User).WithMany(u => u.ClientInteractions).HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ClientScore>(b =>
            {
                b.HasIndex(s => s.ClientId).IsUnique();
                b.HasIndex(s => new { s.ClientId, s.CalculatedAt });
                b.Property(s => s.Score).HasColumnType("int");
                b.HasOne(s => s.Client).WithOne(c => c.Score).HasForeignKey<ClientScore>(s => s.ClientId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Feedback>(b =>
            {
                b.HasOne(f => f.Client).WithMany(c => c.Feedbacks).HasForeignKey(f => f.ClientId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(f => f.User).WithMany(u => u.Feedbacks).HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(f => f.Interaction).WithMany().HasForeignKey(f => f.InteractionId).OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}
