using System.Security.Cryptography;
using System.Text;

namespace Escalated.Services;

public class TwoFactorService
{
    private const string Base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>
    /// Generate a random 16-character base32 secret.
    /// </summary>
    public string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        var sb = new StringBuilder(16);
        for (int i = 0; i < 16; i++)
        {
            sb.Append(Base32Chars[bytes[i] % 32]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Generate an otpauth:// URI for QR code display.
    /// </summary>
    public string GenerateQrUri(string secret, string email, string issuer = "Escalated")
    {
        var label = Uri.EscapeDataString($"{issuer}:{email}");
        var parameters = $"secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";
        return $"otpauth://totp/{label}?{parameters}";
    }

    /// <summary>
    /// Verify a TOTP code against a secret with +/- 1 time period tolerance.
    /// </summary>
    public bool Verify(string secret, string code)
    {
        var timeSlice = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

        for (int i = -1; i <= 1; i++)
        {
            var calculated = GenerateTotp(secret, timeSlice + i);
            if (CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(calculated),
                Encoding.UTF8.GetBytes(code)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Generate 8 random recovery codes.
    /// </summary>
    public string[] GenerateRecoveryCodes()
    {
        var codes = new string[8];
        for (int i = 0; i < 8; i++)
        {
            var part1 = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToUpper();
            var part2 = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToUpper();
            codes[i] = $"{part1}-{part2}";
        }
        return codes;
    }

    private string GenerateTotp(string secret, long timeSlice)
    {
        var secretKey = Base32Decode(secret);
        var timeBytes = BitConverter.GetBytes(timeSlice);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(timeBytes);

        using var hmac = new HMACSHA1(secretKey);
        var hash = hmac.ComputeHash(timeBytes);

        var offset = hash[^1] & 0x0F;
        var code = (
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF)
        ) % 1000000;

        return code.ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        var map = new Dictionary<char, int>();
        for (int i = 0; i < Base32Chars.Length; i++)
            map[Base32Chars[i]] = i;

        input = input.ToUpper().TrimEnd('=');
        var bits = new StringBuilder();
        foreach (var c in input)
        {
            if (map.TryGetValue(c, out var val))
                bits.Append(Convert.ToString(val, 2).PadLeft(5, '0'));
        }

        var bytes = new List<byte>();
        for (int i = 0; i + 8 <= bits.Length; i += 8)
        {
            bytes.Add(Convert.ToByte(bits.ToString(i, 8), 2));
        }

        return bytes.ToArray();
    }
}
