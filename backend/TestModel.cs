using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ecommerce.Api.Infrastructure;

class Program2 {
    static void Main() {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=db;Username=u;Password=p")
            .Options;
        using var db = new ApplicationDbContext(options);
        var entity = db.Model.FindEntityType(typeof(Ecommerce.Api.Domain.User));
        if (entity != null) {
            foreach(var prop in entity.GetProperties()) {
                Console.WriteLine(prop.Name + " -> " + prop.GetColumnName());
            }
        } else {
            Console.WriteLine("User entity not found.");
        }
    }
}
