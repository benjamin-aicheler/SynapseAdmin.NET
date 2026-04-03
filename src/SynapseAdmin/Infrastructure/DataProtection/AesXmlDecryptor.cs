using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

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

        var salt = Convert.FromBase64String(encryptedElement.Element("salt")!.Value);
        var nonce = Convert.FromBase64String(encryptedElement.Element("nonce")!.Value);
        var tag = Convert.FromBase64String(encryptedElement.Element("tag")!.Value);
        var ciphertext = Convert.FromBase64String(encryptedElement.Element("ciphertext")!.Value);

        var plaintext = new byte[ciphertext.Length];

        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        using var aesGcm = new AesGcm(key, tag.Length);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return XElement.Parse(Encoding.UTF8.GetString(plaintext));
    }
}
