using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var dbName = Guid.NewGuid().ToString();
var services = new ServiceCollection();
services.AddDbContext<TestDb>(opt => opt.UseInMemoryDatabase(dbName));
var sp = services.BuildServiceProvider();

// Scope 1: seed
using (var scope1 = sp.CreateScope())
{
    var db = scope1.ServiceProvider.GetRequiredService<TestDb>();
    db.Items.Add(new Item { Id = 1, Name = "test", Status = "active" });
    await db.SaveChangesAsync();
    Console.WriteLine("Scope1 seeded");
}

// Scope 2: update
using (var scope2 = sp.CreateScope())
{
    var db = scope2.ServiceProvider.GetRequiredService<TestDb>();
    var item = await db.Items.FindAsync(1);
    Console.WriteLine($"Scope2 found: {item?.Status}");
    if (item != null)
    {
        item.Status = "suspended";
        await db.SaveChangesAsync();
        Console.WriteLine("Scope2 updated");
    }
}

// Scope 3: verify
using (var scope3 = sp.CreateScope())
{
    var db = scope3.ServiceProvider.GetRequiredService<TestDb>();
    var item = await db.Items.FindAsync(1);
    Console.WriteLine($"Scope3 read: {item?.Status}");
}

public class TestDb(DbContextOptions<TestDb> opts) : DbContext(opts)
{
    public DbSet<Item> Items => Set<Item>();
}

public class Item
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
}
