using Appota.Models;
using Microsoft.EntityFrameworkCore;

namespace Appota.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        { 

        }
  
        public DbSet<UsersPay> UsersPays { get; set; }
        public DbSet<Payment> Payments { get; set;}
        public DbSet<PaymentFee> PaymentsFee { get; set; }

    }
}
