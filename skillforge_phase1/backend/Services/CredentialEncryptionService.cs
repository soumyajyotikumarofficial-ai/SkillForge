using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SkillForge.Services;

/// <summary>
/// Provides encryption/decryption services for sensitive credentials and user data.
/// Uses AES-256-GCM for authenticated encryption.
/// </summary>
public interface ICredentialEncryptionService
{
    /// <summary>
    /// Encrypts plaintext using AES-256-GCM.
    /// </summary>
    /// <param name="plaintext">Data to encrypt</param>
    /// <returns>Base64-encoded ciphertext with IV and auth tag</returns>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decrypts AES-256-GCM encrypted data.
    /// </summary>
    /// <param name="encryptedData">Base64-encoded ciphertext with IV and auth tag</param>
    /// <returns>Decrypted plaintext</returns>
    string Decrypt(string encryptedData);

    /// <summary>
    /// Checks if a plaintext matches an encrypted value.
    /// </summary>
    bool VerifyEncrypted(string plaintext, string encrypted);
}

public class CredentialEncryptionService : ICredentialEncryptionService
{
    private readonly IConfiguration _config;
    private readonly ILogger<CredentialEncryptionService> _logger;
    private readonly byte[] _encryptionKey;
    private const int KeySize = 32; // 256-bit key for AES-256
    private const int NonceSizeBytes = 12; // 96-bit nonce for GCM
    private const int TagSizeBytes = 16; // 128-bit auth tag

    public CredentialEncryptionService(IConfiguration config, ILogger<CredentialEncryptionService> logger)
    {
        _config = config;
        _logger = logger;

        // Get encryption key from configuration
        var keyString = _config["Encryption:Key"] ?? throw new InvalidOperationException("Encryption:Key not configured in appsettings.json");
        
        // Convert hex string to bytes (must be 64 hex characters for 32-byte key)
        if (keyString.Length != 64)
            throw new InvalidOperationException("Encryption:Key must be 64 hex characters (32 bytes)");

        try
        {
            _encryptionKey = Convert.FromHexString(keyString);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse encryption key from configuration");
            throw;
        }
    }

    /// <summary>
    /// Encrypts plaintext using AES-256-GCM.
    /// Format: [IV (12 bytes)] + [Ciphertext] + [Tag (16 bytes)]
    /// Returned as Base64 string.
    /// </summary>
    public string Encrypt(string plaintext)
    {
        try
        {
            if (string.IsNullOrEmpty(plaintext))
                throw new ArgumentException("Plaintext cannot be empty", nameof(plaintext));

            using (var aes = new AesGcm(_encryptionKey, TagSizeBytes))
            {
                // Generate random nonce (IV)
                var nonce = new byte[NonceSizeBytes];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(nonce);
                }

                var plainBytes = Encoding.UTF8.GetBytes(plaintext);
                var ciphertext = new byte[plainBytes.Length];
                var tag = new byte[TagSizeBytes];

                // Encrypt and generate auth tag
                aes.Encrypt(nonce, plainBytes, Array.Empty<byte>(), ciphertext, tag);

                // Combine: nonce + ciphertext + tag
                var encryptedBytes = new byte[nonce.Length + ciphertext.Length + tag.Length];
                Buffer.BlockCopy(nonce, 0, encryptedBytes, 0, nonce.Length);
                Buffer.BlockCopy(ciphertext, 0, encryptedBytes, nonce.Length, ciphertext.Length);
                Buffer.BlockCopy(tag, 0, encryptedBytes, nonce.Length + ciphertext.Length, tag.Length);

                // Return as Base64
                return Convert.ToBase64String(encryptedBytes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Encryption failed");
            throw;
        }
    }

    /// <summary>
    /// Decrypts AES-256-GCM encrypted data.
    /// Expected format: [IV (12 bytes)] + [Ciphertext] + [Tag (16 bytes)]
    /// </summary>
    public string Decrypt(string encryptedData)
    {
        try
        {
            if (string.IsNullOrEmpty(encryptedData))
                throw new ArgumentException("Encrypted data cannot be empty", nameof(encryptedData));

            var encryptedBytes = Convert.FromBase64String(encryptedData);

            // Minimum size: nonce (12) + tag (16) = 28 bytes
            if (encryptedBytes.Length < NonceSizeBytes + TagSizeBytes)
                throw new InvalidOperationException("Encrypted data is too short");

            using (var aes = new AesGcm(_encryptionKey, TagSizeBytes))
            {
                // Extract components
                var nonce = new byte[NonceSizeBytes];
                var ciphertextLength = encryptedBytes.Length - NonceSizeBytes - TagSizeBytes;
                var ciphertext = new byte[ciphertextLength];
                var tag = new byte[TagSizeBytes];

                Buffer.BlockCopy(encryptedBytes, 0, nonce, 0, NonceSizeBytes);
                Buffer.BlockCopy(encryptedBytes, NonceSizeBytes, ciphertext, 0, ciphertextLength);
                Buffer.BlockCopy(encryptedBytes, NonceSizeBytes + ciphertextLength, tag, 0, TagSizeBytes);

                // Decrypt and verify auth tag
                var plaintext = new byte[ciphertext.Length];
                aes.Decrypt(nonce, ciphertext, tag, Array.Empty<byte>(), plaintext);

                return Encoding.UTF8.GetString(plaintext);
            }
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Decryption failed - data may be corrupted or tampered");
            throw new InvalidOperationException("Failed to decrypt data - invalid credentials or corrupted data", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Decryption error");
            throw;
        }
    }

    /// <summary>
    /// Verifies if plaintext matches an encrypted value without decrypting.
    /// </summary>
    public bool VerifyEncrypted(string plaintext, string encrypted)
    {
        try
        {
            if (string.IsNullOrEmpty(plaintext) || string.IsNullOrEmpty(encrypted))
                return false;

            var decrypted = Decrypt(encrypted);
            return decrypted == plaintext;
        }
        catch
        {
            return false;
        }
    }
}
