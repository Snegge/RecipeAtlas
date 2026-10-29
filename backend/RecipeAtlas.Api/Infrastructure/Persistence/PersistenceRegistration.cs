using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Recipoke.Api.Infrastructure.Persistence;

public static class PersistenceRegistration
{
    public static bool AddPersistence(this WebApplicationBuilder builder, bool administration)
    {
        var config = builder.Configuration;
        var provider = config["Database:Provider"] ?? (config["RENDER"] is not null ? "" : "Sqlite");
        var keys = builder.Services.AddDataProtection().SetApplicationName("RecipeAtlas");
        if (provider == "Sqlite")
        {
            if (config["RENDER"] is not null) throw new InvalidOperationException("Render requires Database:Provider=PostgreSql.");
            var directory = Path.GetFullPath(config["DataDirectory"] ?? "data", builder.Environment.ContentRootPath);
            Directory.CreateDirectory(directory);
            var connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "recipes.db"), ForeignKeys = true }.ToString();
            builder.Services.AddDbContext<RecipeDb>(o => o.UseSqlite(connection));
            keys.PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(directory, "keys")));
            return false;
        }
        if (provider != "PostgreSql") throw new InvalidOperationException("Database:Provider must be Sqlite or PostgreSql.");
        var setting = administration ? "Database:MigrationConnection" : "Database:Connection";
        var value = config[setting] ?? throw new InvalidOperationException($"Missing {setting}.");
        NpgsqlConnectionStringBuilder connectionString;
        try { connectionString = new(value); }
        catch { throw new InvalidOperationException($"Invalid Npgsql syntax in {setting}."); }
        if (string.IsNullOrWhiteSpace(connectionString.Host) || string.IsNullOrWhiteSpace(connectionString.Database))
            throw new InvalidOperationException($"Host and Database required in {setting}.");
        if (connectionString.Host is not ("localhost" or "127.0.0.1" or "postgres") && connectionString.SslMode != SslMode.VerifyFull)
            throw new InvalidOperationException("Remote PostgreSQL requires SSL Mode=VerifyFull.");
        if (administration && connectionString.Host.Contains("-pooler"))
            throw new InvalidOperationException("Administration requires the direct PostgreSQL endpoint.");
        connectionString.IncludeErrorDetail = false;
        connectionString.Timeout = 15;
        connectionString.CommandTimeout = 30;
        builder.Services.AddDbContext<PostgresRecipeDb>(o => o.UseNpgsql(connectionString.ToString()));
        builder.Services.AddScoped<RecipeDb>(s => s.GetRequiredService<PostgresRecipeDb>());
        if (!administration)
        {
            var certificate = LoadCertificate(config["DataProtection:Certificate"], config["DataProtection:Password"]);
            keys.PersistKeysToDbContext<PostgresRecipeDb>().ProtectKeysWithCertificate(certificate);
            var old = config.GetSection("DataProtection:PreviousCertificates").GetChildren()
                .Select(c => LoadCertificate(c["Certificate"], c["Password"])).Prepend(certificate).ToArray();
            keys.UnprotectKeysWithAnyCertificate(old);
        }
        return true;
    }
    private static X509Certificate2 LoadCertificate(string? encoded, string? password)
    {
        try
        {
            var cert = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(encoded ?? ""), password,
                X509KeyStorageFlags.EphemeralKeySet);
            if (!cert.HasPrivateKey) throw new InvalidOperationException();
            return cert;
        }
        catch { throw new InvalidOperationException("DataProtection requires a base64 PKCS#12 certificate with private key and its password."); }
    }
}
