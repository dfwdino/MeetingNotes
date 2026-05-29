using System.Security.Cryptography;
using System.Text;

namespace MeetingNotes.Services;

public class EncryptionService
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int Iterations = 200_000;

    public byte[] GenerateDataKey()
    {
        var key = new byte[KeySize];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    public byte[] GenerateSalt()
    {
        var salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    public byte[] DeriveKeyFromPassword(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeySize);

    /// <summary>Encrypts a 32-byte data key using the KEK. Returns nonce+ciphertext+tag as bytes.</summary>
    public byte[] WrapKey(byte[] dataKey, byte[] kek)
    {
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[dataKey.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(kek, TagSize);
        aes.Encrypt(nonce, dataKey, ciphertext, tag);

        var result = new byte[NonceSize + ciphertext.Length + TagSize];
        nonce.CopyTo(result, 0);
        ciphertext.CopyTo(result, NonceSize);
        tag.CopyTo(result, NonceSize + ciphertext.Length);
        return result;
    }

    /// <summary>Decrypts a wrapped data key. Throws <see cref="CryptographicException"/> if the password is wrong.</summary>
    public byte[] UnwrapKey(byte[] wrappedKey, byte[] kek)
    {
        var nonce = wrappedKey[..NonceSize];
        var ciphertext = wrappedKey[NonceSize..(wrappedKey.Length - TagSize)];
        var tag = wrappedKey[(wrappedKey.Length - TagSize)..];

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(kek, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    /// <summary>Encrypts text with the data key. Returns base64(nonce+ciphertext+tag), or null for empty input.</summary>
    public string? EncryptText(string? text, byte[] dataKey)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var plaintext = Encoding.UTF8.GetBytes(text);
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(dataKey, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var result = new byte[NonceSize + ciphertext.Length + TagSize];
        nonce.CopyTo(result, 0);
        ciphertext.CopyTo(result, NonceSize);
        tag.CopyTo(result, NonceSize + ciphertext.Length);
        return Convert.ToBase64String(result);
    }

    /// <summary>Decrypts a base64 blob produced by <see cref="EncryptText"/>. Throws <see cref="CryptographicException"/> if tampered.</summary>
    public string? DecryptText(string? encryptedBase64, byte[] dataKey)
    {
        if (string.IsNullOrEmpty(encryptedBase64)) return null;

        var data = Convert.FromBase64String(encryptedBase64);
        var nonce = data[..NonceSize];
        var ciphertext = data[NonceSize..(data.Length - TagSize)];
        var tag = data[(data.Length - TagSize)..];

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(dataKey, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }
}
