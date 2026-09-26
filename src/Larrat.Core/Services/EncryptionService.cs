using System.Security.Cryptography;
using System.Text;

namespace Larrat.Core.Services;

/// <summary>
/// Serviço de criptografia AES-256-GCM para mensagens ponta-a-ponta.
/// A chave é derivada do JWT secret + ID da conversa, garantindo que
/// apenas usuários autenticados com o token correto possam descriptografar.
/// </summary>
public interface IEncryptionService
{
    EncryptedPayload Encrypt(string plainText, string conversationKey);
    string Decrypt(EncryptedPayload payload, string conversationKey);
    string DeriveConversationKey(string jwtSecret, string conversationId);
}

public class EncryptedPayload
{
    public string CipherText { get; set; } = string.Empty;  // Base64
    public string IV { get; set; } = string.Empty;          // Base64 - 12 bytes para GCM
    public string AuthTag { get; set; } = string.Empty;     // Base64 - 16 bytes GCM tag
}

public class EncryptionService : IEncryptionService
{
    /// <summary>
    /// Deriva uma chave AES-256 para uma conversa específica usando HKDF.
    /// Input: JWT secret (master key) + conversationId (contexto).
    /// </summary>
    public string DeriveConversationKey(string jwtSecret, string conversationId)
    {
        var masterKeyBytes = Encoding.UTF8.GetBytes(jwtSecret);
        var infoBytes = Encoding.UTF8.GetBytes($"larrat-chat-{conversationId}");
        var saltBytes = SHA256.HashData(Encoding.UTF8.GetBytes("larrat-salt-v1"));

        // HKDF-SHA256 para derivar 32 bytes (256 bits)
        var derivedKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKeyBytes, 32, saltBytes, infoBytes);
        return Convert.ToBase64String(derivedKey);
    }

    /// <summary>
    /// Criptografa texto usando AES-256-GCM.
    /// </summary>
    public EncryptedPayload Encrypt(string plainText, string conversationKeyBase64)
    {
        var keyBytes = Convert.FromBase64String(conversationKeyBase64);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);

        // IV de 12 bytes (96 bits) - recomendado para GCM
        var iv = new byte[12];
        RandomNumberGenerator.Fill(iv);

        var cipherText = new byte[plainBytes.Length];
        var authTag = new byte[16]; // 128-bit tag GCM

        using var aesGcm = new AesGcm(keyBytes, 16);
        aesGcm.Encrypt(iv, plainBytes, cipherText, authTag);

        return new EncryptedPayload
        {
            CipherText = Convert.ToBase64String(cipherText),
            IV = Convert.ToBase64String(iv),
            AuthTag = Convert.ToBase64String(authTag)
        };
    }

    /// <summary>
    /// Descriptografa e verifica autenticidade da mensagem.
    /// Lança exceção se a tag GCM não bater (mensagem adulterada).
    /// </summary>
    public string Decrypt(EncryptedPayload payload, string conversationKeyBase64)
    {
        var keyBytes = Convert.FromBase64String(conversationKeyBase64);
        var cipherBytes = Convert.FromBase64String(payload.CipherText);
        var iv = Convert.FromBase64String(payload.IV);
        var authTag = Convert.FromBase64String(payload.AuthTag);

        var plainBytes = new byte[cipherBytes.Length];

        using var aesGcm = new AesGcm(keyBytes, 16);
        aesGcm.Decrypt(iv, cipherBytes, authTag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
