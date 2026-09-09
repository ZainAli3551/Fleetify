using Microsoft.EntityFrameworkCore;
using Fleetify.Models.Entities;

namespace Fleetify.Data
{
    public class FleetifyDbContext : DbContext
    {
        public FleetifyDbContext(DbContextOptions<FleetifyDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Admin> Admins { get; set; }
        public DbSet<Driver> Drivers { get; set; }
        public DbSet<Vehicle> Vehicles { get; set; }
        public DbSet<DeliveryRequest> DeliveryRequests { get; set; }
        public DbSet<Assignment> Assignments { get; set; }
        public DbSet<StatusUpdate> StatusUpdates { get; set; }
        public DbSet<CostEstimate> CostEstimates { get; set; }
        public DbSet<MaintenanceAlert> MaintenanceAlerts { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<SupportConversation> SupportConversations { get; set; }
        public DbSet<SupportMessage> SupportMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Table mappings
            modelBuilder.Entity<User>().ToTable("Users");
            modelBuilder.Entity<Admin>().ToTable("Admins");
            modelBuilder.Entity<Driver>().ToTable("Drivers");
            modelBuilder.Entity<Vehicle>().ToTable("Vehicles");
            modelBuilder.Entity<DeliveryRequest>().ToTable("DeliveryRequests");
            modelBuilder.Entity<Assignment>().ToTable("Assignments");
            modelBuilder.Entity<StatusUpdate>().ToTable("StatusUpdates");
            modelBuilder.Entity<CostEstimate>().ToTable("CostEstimates");
            modelBuilder.Entity<MaintenanceAlert>().ToTable("MaintenanceAlerts");
            modelBuilder.Entity<Notification>().ToTable("Notifications");
            modelBuilder.Entity<SupportConversation>().ToTable("SupportConversations");
            modelBuilder.Entity<SupportMessage>().ToTable("SupportMessages");

            // User Email uniqueness
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<Admin>()
                .HasIndex(a => a.Email)
                .IsUnique();

            modelBuilder.Entity<Driver>()
                .HasIndex(d => d.Email)
                .IsUnique();

            // Vehicle Number uniqueness
            modelBuilder.Entity<Vehicle>()
                .HasIndex(v => v.VehicleNumber)
                .IsUnique();

            // DeliveryRequest TrackingNumber uniqueness
            modelBuilder.Entity<DeliveryRequest>()
                .HasIndex(dr => dr.TrackingNumber)
                .IsUnique();

            // Relationships & Cascades
            modelBuilder.Entity<DeliveryRequest>()
                .HasOne(dr => dr.User)
                .WithMany(u => u.DeliveryRequests)
                .HasForeignKey(dr => dr.UserID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.DeliveryRequest)
                .WithOne(dr => dr.Assignment)
                .HasForeignKey<Assignment>(a => a.RequestID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.Driver)
                .WithMany(d => d.Assignments)
                .HasForeignKey(a => a.DriverID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.Vehicle)
                .WithMany(v => v.Assignments)
                .HasForeignKey(a => a.VehicleID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StatusUpdate>()
                .HasOne(su => su.Assignment)
                .WithMany(a => a.StatusUpdates)
                .HasForeignKey(su => su.AssignmentID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<MaintenanceAlert>()
                .HasOne(ma => ma.Vehicle)
                .WithMany(v => v.MaintenanceAlerts)
                .HasForeignKey(ma => ma.VehicleID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SupportConversation>()
                .HasIndex(sc => sc.SessionToken)
                .IsUnique();

            modelBuilder.Entity<SupportMessage>()
                .HasOne(sm => sm.Conversation)
                .WithMany(sc => sc.Messages)
                .HasForeignKey(sm => sm.ConversationID)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
