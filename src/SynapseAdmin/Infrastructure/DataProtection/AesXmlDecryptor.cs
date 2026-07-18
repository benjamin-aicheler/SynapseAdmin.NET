using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace SynapseAdmin.Infrastructure.DataProtection;

public class AesXmlDecryptor(IServiceProvider services) : IXmlDecryptor
{
    private const int KeySize = 32; // 256 bits
    private const int Iterations = 100_000;

    public XElement Decrypt(XElement encryptedElement)
    {
        var passphrase = services.GetRequiredService<IConfiguration>()["DP_PASSPHRASE"];
        if (string.IsNullOrEmpty(passphrase))
        {
            throw new InvalidOperationException("DP_PASSPHRASE environment variable is missing for decryption.");
        }

        var saltEl = encryptedElement.Element("salt") ?? throw new CryptographicException("Malformed Data Protection XML: missing 'salt' element.");
        var nonceEl = encryptedElement.Element("nonce") ?? throw new CryptographicException("Malformed Data Protection XML: missing 'nonce' element.");
        var tagEl = encryptedElement.Element("tag") ?? throw new CryptographicException("Malformed Data Protection XML: missing 'tag' element.");
        var ciphertextEl = encryptedElement.Element("ciphertext") ?? throw new CryptographicException("Malformed Data Protection XML: missing 'ciphertext' element.");

        var salt = Convert.FromBase64String(saltEl.Value);
        var nonce = Convert.FromBase64String(nonceEl.Value);
        var tag = Convert.FromBase64String(tagEl.Value);
        var ciphertext = Convert.FromBase64String(ciphertextEl.Value);

        var plaintext = new byte[ciphertext.Length];

        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        using var aesGcm = new AesGcm(key, tag.Length);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return XElement.Parse(Encoding.UTF8.GetString(plaintext));
    }
}
