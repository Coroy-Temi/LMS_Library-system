using QRCoder;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace LibraryManagementSystem.Services;

public sealed record InstallationSecret(byte[] Key, string InstallationId);
public interface IInstallationSecretStore { InstallationSecret GetOrCreate(); }

public sealed class WindowsInstallationSecretStore : IInstallationSecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LibraryForge.QR.Key.v1");
    private readonly object sync = new();
    private readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibraryForge", "Security");

    public InstallationSecret GetOrCreate()
    {
        lock (sync)
        {
            Directory.CreateDirectory(folder);
            var keyPath = Path.Combine(folder, "qr-key.bin");
            var idPath = Path.Combine(folder, "installation-id.txt");
            if (!File.Exists(keyPath))
                File.WriteAllBytes(keyPath, ProtectedData.Protect(RandomNumberGenerator.GetBytes(32), Entropy, DataProtectionScope.CurrentUser));
            if (!File.Exists(idPath))
                File.WriteAllText(idPath, Guid.NewGuid().ToString("N"), Encoding.UTF8);
            var key = ProtectedData.Unprotect(File.ReadAllBytes(keyPath), Entropy, DataProtectionScope.CurrentUser);
            var installationId = File.ReadAllText(idPath, Encoding.UTF8).Trim();
            if (key.Length != 32 || installationId.Length != 32)
                throw new CryptographicException("The LibraryForge QR installation secret is invalid.");
            return new(key, installationId);
        }
    }
}

public sealed record SecureQrPayload(string Signature, string InstallationId, string EntityType, int EntityId, string Code, DateTime IssuedAtUtc);
public sealed record SecureQrDecodeResult(bool Success, string Message, SecureQrPayload? Payload = null);

public sealed class SecureQrCodeService(IInstallationSecretStore secrets)
{
    private const string Prefix = "LF1.";
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("LibraryForge|QR|v1");

    public string CreateBookCopyToken(int copyId, string accessionCode)
    {
        var secret = secrets.GetOrCreate();
        var payload = new SecureQrPayload("LibraryForge-v1", secret.InstallationId, "book-copy", copyId, accessionCode, DateTime.UtcNow);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[plaintext.Length];
        using var aes = new AesGcm(secret.Key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);
        var envelope = new byte[nonce.Length + tag.Length + ciphertext.Length];
        nonce.CopyTo(envelope, 0); tag.CopyTo(envelope, nonce.Length); ciphertext.CopyTo(envelope, nonce.Length + tag.Length);
        return Prefix + Base64UrlEncode(envelope);
    }

    public SecureQrDecodeResult Decode(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith(Prefix, StringComparison.Ordinal))
            return new(false, "This is not a LibraryForge QR code.");
        try
        {
            var envelope = Base64UrlDecode(token[Prefix.Length..]);
            if (envelope.Length < 29) return new(false, "The QR payload is incomplete.");
            var secret = secrets.GetOrCreate();
            var nonce = envelope.AsSpan(0, 12);
            var tag = envelope.AsSpan(12, 16);
            var ciphertext = envelope.AsSpan(28);
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(secret.Key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
            var payload = JsonSerializer.Deserialize<SecureQrPayload>(plaintext);
            if (payload is null || payload.Signature != "LibraryForge-v1" || payload.EntityType != "book-copy")
                return new(false, "The QR payload type is not supported.");
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(payload.InstallationId), Encoding.UTF8.GetBytes(secret.InstallationId)))
                return new(false, "This QR code belongs to a different LibraryForge installation.");
            return new(true, "Secure LibraryForge QR code validated.", payload);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return new(false, "The QR code is invalid, corrupted, or was created by another installation.");
        }
    }

    public byte[] RenderPng(string token, int pixelsPerModule = 12)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(token, QRCodeGenerator.ECCLevel.H);
        using var qr = new PngByteQRCode(data);
        return qr.GetGraphic(Math.Clamp(pixelsPerModule, 4, 30), new byte[] { 0, 109, 119 }, new byte[] { 255, 255, 255 }, true);
    }

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }
}
