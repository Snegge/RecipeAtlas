using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Recipoke.Api.Infrastructure.Hosting;

public static class ProtectionCertificate
{
    public static void Generate(string path)
    {
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=Recipoke Data Protection", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var file = new FileStream(path, options);
        JsonSerializer.Serialize(file, new Dictionary<string, string> {
            ["DataProtection__Certificate"] = Convert.ToBase64String(cert.Export(X509ContentType.Pfx, password)),
            ["DataProtection__Password"] = password
        });
        Console.WriteLine("Protection material created. Store securely and back up; do not commit or publish this file.");
    }
}
