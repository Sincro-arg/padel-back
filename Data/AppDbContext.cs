using Microsoft.EntityFrameworkCore;
using Padel.Api.Models;

namespace Padel.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Court> Courts => Set<Court>();
    public DbSet<PriceRule> PriceRules => Set<PriceRule>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<RecurringBooking> RecurringBookings => Set<RecurringBooking>();
    public DbSet<BookingPayment> BookingPayments => Set<BookingPayment>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductSale> ProductSales => Set<ProductSale>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<MemberPayment> MemberPayments => Set<MemberPayment>();
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<Pair> Pairs => Set<Pair>();
    public DbSet<Match> Matches => Set<Match>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<PriceRule>().Property(p => p.PricePerHour).HasPrecision(10, 2);

        modelBuilder.Entity<Booking>().Property(b => b.TotalAmount).HasPrecision(10, 2);
        modelBuilder.Entity<Booking>().Property(b => b.PaidAmount).HasPrecision(10, 2);
        modelBuilder.Entity<Booking>().Property(b => b.CancellationFee).HasPrecision(10, 2);
        modelBuilder.Entity<BookingPayment>().Property(p => p.Amount).HasPrecision(10, 2);
        modelBuilder.Entity<Product>().Property(p => p.Price).HasPrecision(10, 2);
        modelBuilder.Entity<ProductSale>().Property(s => s.Amount).HasPrecision(10, 2);
        modelBuilder.Entity<Member>().Property(m => m.MembershipFee).HasPrecision(10, 2);
        modelBuilder.Entity<Member>().Property(m => m.DiscountPercent).HasPrecision(5, 2);
        modelBuilder.Entity<MemberPayment>().Property(p => p.Amount).HasPrecision(10, 2);
        modelBuilder.Entity<Tournament>().Property(t => t.RegistrationFee).HasPrecision(10, 2);
    }
}
