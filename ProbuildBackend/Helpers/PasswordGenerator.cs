using System.Security.Cryptography;
using System.Text;

namespace ProbuildBackend.Helpers
{
    public static class PasswordGenerator
    {
        public static string GenerateRandomPassword(int length = 12)
        {
            const string chars =
                "ABCDEFGHJKLMNPQRSTUVWXYZ" + // no confusing chars
                "abcdefghijkmnopqrstuvwxyz" +
                "23456789" +
                "!@#$%^&*_-+=";

            var result = new StringBuilder(length);
            var bytes = new byte[length];

            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            for (int i = 0; i < length; i++)
            {
                result.Append(chars[bytes[i] % chars.Length]);
            }

            return result.ToString();
        }
    }
}
