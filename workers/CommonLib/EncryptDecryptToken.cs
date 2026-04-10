using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace CommonLib
{
    public interface ITokenEncryptionService
    {
        string Encrypt(string plainText);
        string Decrypt(string cipherText);
    }

    public sealed class EncryptDecryptToken : ITokenEncryptionService
    {
        private readonly byte[] _keyBytes;

        public EncryptDecryptToken(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));

            _keyBytes = Convert.FromBase64String(key);

            if (_keyBytes.Length is not (16 or 24 or 32))
                throw new ArgumentException(
                    "Encryption key must be 128, 192, or 256 bits (base64-encoded).");
        }


        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) throw new ArgumentNullException(nameof(plainText));

            using var aes = Aes.Create();
            aes.Key = _keyBytes; //Encoding.UTF8.GetBytes(_key);
            aes.GenerateIV();

           

            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();
            ms.Write(aes.IV, 0, aes.IV.Length); // prepend IV
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs, Encoding.UTF8))
            {
                sw.Write(plainText);               
            }

            return Convert.ToBase64String(ms.ToArray());
        }

        public string Decrypt(string cipherText)
        {
            if (string.IsNullOrWhiteSpace(cipherText)) throw new ArgumentException("Cipher text cannot be empty", nameof(cipherText));

            var bytes = Convert.FromBase64String(cipherText);

            if (bytes.Length <= 16)
                throw new CryptographicException("Cipher text does not contain encrypted data.");                      

            using var aes = Aes.Create();
            aes.Key = _keyBytes;      //Encoding.UTF8.GetBytes(_key);

            var iv = new byte[16]; // AES block size
            //if (bytes.Length < iv.Length) return string.Empty;
            Array.Copy(bytes, iv, iv.Length);

            using var decryptor = aes.CreateDecryptor(aes.Key, iv);
            using var ms = new MemoryStream(bytes, iv.Length, bytes.Length - iv.Length);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);
            try
            {
                return sr.ReadToEnd();
            }
            catch
            {
                // If decryption fails, return empty to preserve previous behavior but caller should log/throw as needed
                return string.Empty;
            }
        }
    }
}
