using System;
using System.Security.Cryptography;
using System.Text;

namespace ScriptableWithID
{
    public static class MD5HashGenerator
    {
        public static int GenerateIntHash(string seed)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
                return BitConverter.ToInt32(hashBytes, 0);
            }
        }
    }
}
