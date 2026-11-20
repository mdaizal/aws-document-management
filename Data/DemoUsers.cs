using Microsoft.AspNetCore.Identity;

namespace aws_document_management.Data;

internal static class DemoUsers
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var accounts = new[] { (Name: "alice", Key: "DemoUsers:AlicePassword"),
            (Name: "bob", Key: "DemoUsers:BobPassword") };
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        foreach (var (name, key) in accounts)
        {
            if (await users.FindByNameAsync(name) is not null)
                continue;

            var password = configuration[key]
                ?? throw new InvalidOperationException($"Set {key.Replace(":", "__")} to provision {name}.");
            var result = await users.CreateAsync(new IdentityUser { UserName = name }, password);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Could not provision {name}: " +
                    string.Join(", ", result.Errors.Select(error => error.Code)));
        }
    }
}
