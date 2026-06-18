using System.Security.Cryptography;
using System.Text;

namespace CPMS.Methods
{
	public static class Crypto
	{
		private static byte XOR(byte a, byte b) => (byte)(a ^ b);
		private static byte RollR(byte a, byte b) => (byte)((a >> b) | (a << (8 - b))); // DO NOT ROLL MORE THAN 8 BITS YOU LOSE DATA
		private static byte RollL(byte a, byte b) => (byte)((a << b) | (a >> (8 - b))); // DO NOT ROLL MORE THAN 8 BITS YOU LOSE DATA
		public static byte[] Encrypt(byte[] CleanBytes, string? key)
		{
			if (string.IsNullOrEmpty(key)) return CleanBytes;
			var HashKey = SHA256.HashData(Encoding.UTF8.GetBytes(key));
			while (HashKey.Length < CleanBytes.Length)
			{
				var doubled = new byte[HashKey.Length * 2]; ;
				Buffer.BlockCopy(HashKey, 0, doubled, 0, HashKey.Length);
				Buffer.BlockCopy(HashKey, 0, doubled, HashKey.Length, HashKey.Length);
				HashKey = doubled;
			}
			for (int i = 0; i < CleanBytes.Length; i++)
			{ // DO NOT ROLL MORE THAN 8 BITS YOU LOSE DATA
				CleanBytes[i] = XOR(CleanBytes[i], HashKey[i]);
				CleanBytes[i] = RollL(CleanBytes[i], 3);
				CleanBytes[i] = XOR(CleanBytes[i], HashKey[i]);
				CleanBytes[i] = RollR(CleanBytes[i], 2);
			}
			return CleanBytes;
		}
		public static byte[] Decrypt(byte[] CryptedBytes, string? key)
		{
			if (string.IsNullOrEmpty(key)) return CryptedBytes;
			var HashKey = SHA256.HashData(Encoding.UTF8.GetBytes(key));
			while (HashKey.Length < CryptedBytes.Length)
			{
				var doubled = new byte[HashKey.Length * 2]; ;
				Buffer.BlockCopy(HashKey, 0, doubled, 0, HashKey.Length);
				Buffer.BlockCopy(HashKey, 0, doubled, HashKey.Length, HashKey.Length);
				HashKey = doubled;
			}
			for (int i = 0; i < CryptedBytes.Length; i++)
			{ // DO NOT ROLL MORE THAN 8 BITS YOU LOSE DATA
				CryptedBytes[i] = RollL(CryptedBytes[i], 2);
				CryptedBytes[i] = XOR(CryptedBytes[i], HashKey[i]);
				CryptedBytes[i] = RollR(CryptedBytes[i], 3);
				CryptedBytes[i] = XOR(CryptedBytes[i], HashKey[i]);
			}
			return CryptedBytes;
		}
	}
}
