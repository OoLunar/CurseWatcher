using Microsoft.EntityFrameworkCore;

namespace CurseWatcher
{
    public class CurseWatcherContext : DbContext
    {
        /// <summary>
        /// A list of projects that are being watched.
        /// </summary>
        public DbSet<Project> Projects { get; init; }
        public CurseWatcherContext(DbContextOptions<CurseWatcherContext> options) : base(options) { }
    }
}