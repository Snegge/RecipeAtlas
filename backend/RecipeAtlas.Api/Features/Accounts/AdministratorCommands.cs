using System.Text;
using Recipoke.Api.Features.Accounts.Application;
namespace Recipoke.Api.Features.Accounts;
public static class AdministratorCommands
{
    public static async Task RunAsync(IServiceProvider services, bool recover)
    {
        using var scope = services.CreateScope();
        var setup = scope.ServiceProvider.GetRequiredService<AdministratorSetup>();
        if (!recover && await setup.IsCompleteAsync(default)) { Console.WriteLine("Setup is already complete; no changes made."); return; }
        Console.Write("Username: "); var username = Console.ReadLine() ?? "";
        string displayName = "";
        if (!recover) { Console.Write("Display name: "); displayName = Console.ReadLine() ?? ""; }
        Console.Write("New password (12–256 characters): ");
        string password;
        if (Console.IsInputRedirected) password = Console.ReadLine() ?? "";
        else
        {
            var buffer = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(true); if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace) { if (buffer.Length > 0) buffer.Length--; }
                else if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
            }
            Console.WriteLine(); password = buffer.ToString();
        }
        if (recover) await setup.RecoverAsync(username, password, default);
        else await setup.BootstrapAsync(username, displayName, password, default);
        Console.WriteLine(recover ? "Administrator recovered; previous sessions revoked." : "Administrator setup complete. Sign in with the new account.");
    }
}
