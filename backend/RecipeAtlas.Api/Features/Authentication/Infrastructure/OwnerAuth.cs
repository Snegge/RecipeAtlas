using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Recipoke.Api.Features.Authentication.Infrastructure;

public sealed class OwnerAuth
{
    private static readonly object Owner = new();
    private static readonly PasswordHasher<object> Hasher = new(
        Options.Create(new PasswordHasherOptions { IterationCount = 210000 }));
    private readonly string _hash;
    public string Stamp { get; }

    public OwnerAuth(string hash)
    {
        _hash = hash;
        // Validate configuration immediately. Never log the hash or password.
        try { Hasher.VerifyHashedPassword(Owner, hash, "configuration-check"); }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        { throw new InvalidOperationException("Owner:PasswordHash is not a valid password hash."); }
        Stamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash)));
    }

    public bool Verify(string? password) => password is { Length: >= 1 and <= 256 } &&
        Hasher.VerifyHashedPassword(Owner, _hash, password) != PasswordVerificationResult.Failed;

    public static void PrintPasswordHash()
    {
        Console.Error.Write("New owner password (12 to 256 characters): ");
        string password;
        if (Console.IsInputRedirected)
            password = Console.ReadLine() ?? "";
        else
        {
            var buffer = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace)
                { if (buffer.Length > 0) buffer.Length--; }
                else if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
            }
            password = buffer.ToString();
            Console.Error.WriteLine();
        }
        if (password.Length is < 12 or > 256)
            throw new InvalidOperationException("Use a password with 12 to 256 characters.");
        Console.WriteLine(Hasher.HashPassword(Owner, password));
    }

}
