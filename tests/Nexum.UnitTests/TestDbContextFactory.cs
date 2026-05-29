using Microsoft.EntityFrameworkCore;
using Nexum.Modules.Auth.Infrastructure.Persistence;

public static class TestDbContextFactory
{
    public static NexumDbContext Create()
    {
        var options = new DbContextOptionsBuilder<NexumDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new NexumDbContext(options);
    }
}
