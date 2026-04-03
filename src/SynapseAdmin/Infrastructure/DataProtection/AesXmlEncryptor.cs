using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace SynapseAdmin.Infrastructure.DataProtection;

public class AesXmlEncryptor(string passphrase) : IXmlEncryptor
{
    private const int KeySize = 32; // 256 bits
    private const int NonceSize = 12; // Standard for AES-GCM
    private const int TagSize = 16; // Standard for AES-GCM
    private const int Iterations = 100_000;

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var ciphertext = new byte[plaintext.Length];

        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        using var aesGcm = new AesGcm(key, TagSize);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        var element = new XElement("encryptedKey",
            new XAttribute("v", "1"),
            new XElement("salt", Convert.ToBase64String(salt)),
            new XElement("nonce", Convert.ToBase64String(nonce)),
            new XElement("tag", Convert.ToBase64String(tag)),
            new XElement("ciphertext", Convert.ToBase64String(ciphertext))
        );

        return new EncryptedXmlInfo(element, typeof(AesXmlDecryptor));
    }
}
