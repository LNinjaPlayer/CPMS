using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Timers;
using System.Xml.Serialization;
using static CPMS.CP;

namespace CPMS
{
	public class SettingsList // Remember to update the Settings, SettingsList and the SettingsPage if you add new settings
	{
		public int TimeoutMs { get; set; } = 300000; // 5 minutes
		public bool PasswordPromptCPPageEnter { get; set; } = true;
	}
	public static class Settings
	{
#pragma warning disable CA2211 // Needs to be public static mutable since those are the values used and modified by the rest of the app, part laziness here
		public static int TimeoutMs; 
		public static bool PasswordPromptCPPageEnter;
#pragma warning restore CA2211 // Can't save to file with static class so we use SettingsList as a middleman for file operations
		public static void Apply(SettingsList s)
		{
			TimeoutMs = s.TimeoutMs;
			PasswordPromptCPPageEnter = s.PasswordPromptCPPageEnter;
		}
		public static SettingsList ToSettings()
		{
			return new SettingsList
			{
				TimeoutMs = TimeoutMs,
				PasswordPromptCPPageEnter = PasswordPromptCPPageEnter
			};
		}
	}
	public partial class MainPage : ContentPage
    {
		public MainPage()
        {
			InitializeComponent();
			var loaded = SettingsManager.Load();
			Settings.Apply(loaded);
			PromptPassword();
			ReloadCPList();
			Task.Run(() => StartPasswordLoop());
		}
		private async Task StartPasswordLoop()
		{
			while (true)
			{
				if (PasswordPromptLoopVariables.TimeKeep > Settings.TimeoutMs && !PasswordPromptLoopVariables.PausePasswordLoop)
				{
					PasswordPromptLoopVariables.TimeKeep = 0;
					await MainThread.InvokeOnMainThreadAsync(async () => PromptPassword());
				}
				if (PasswordPromptLoopVariables.PausePasswordLoop == false) PasswordPromptLoopVariables.TimeKeep += 100;
				Thread.Sleep(100);
			}
		}
		private async void PromptPassword()
		{
			PasswordPromptLoopVariables.PausePasswordLoop = true;
			PasswordStore.Clear();
			var passwordPage = new PasswordPage();
			await Navigation.PushAsync(passwordPage);
			passwordPage.Disappearing += (s, e) =>
			{
				PasswordStore.Set(passwordPage.Password);
				PasswordPromptLoopVariables.PausePasswordLoop = false;
			};
		}

		private async void Settings_Clicked(object s, EventArgs e)
		{
			var settingsPage = new SettingsPage();
			settingsPage.Disappearing += (s, e) => SettingsManager.Save(Settings.ToSettings());
			await Navigation.PushAsync(settingsPage);
		}

		private async void CreateNewCP(object s, EventArgs e)
        {
            Page newCPPage = new NewCPPage();
            await Navigation.PushAsync(newCPPage); // just using "await" doesn't work for some reason, so use .Disappearing to reload the list after the page is closed
			newCPPage.Disappearing += (s, e) => ReloadCPList();
        }

        private void ReloadCPList()
        {
            foreach (var child in CPStackLayout.Children.ToList()) CPStackLayout.Children.Remove(child);
			string CPFolder = Path.Combine(FileSystem.Current.AppDataDirectory, "CPs");
            if (!Directory.Exists(CPFolder)) Directory.CreateDirectory(CPFolder);
            foreach (var Folder in Directory.GetDirectories(CPFolder))
            {
                string CPName = Path.GetFileName(Folder);
                Button CPButton = new() { Text = CPName };
                CPStackLayout.Children.Add(CPButton);
				CPButton.Clicked += async (s, e) =>
				{
					Page CPPage = new CP(CPName);
					CPPage.Disappearing += (s, e) => { ReloadCPList(); };

					if (Settings.PasswordPromptCPPageEnter) { await Navigation.PushAsync(CPPage); PromptPassword(); }
					else { await Navigation.PushAsync(CPPage); }
				};
			}
        }

		public class PasswordPromptLoopVariables
		{
			public static bool PausePasswordLoop { get; set; } = false;
			public static int TimeKeep { get; set; } = 0;
		}
	}

	public static class SettingsManager
	{
		private static readonly string filePath = Path.Combine(FileSystem.Current.AppDataDirectory, "Settings.xml");
		public static void Save(SettingsList settings)
		{
			var serializer = new XmlSerializer(typeof(SettingsList));
			using var writer = new StreamWriter(filePath, false);
			serializer.Serialize(writer, settings);
			writer.Close();
		}

		public static SettingsList Load()
		{
			if (!File.Exists(filePath))
			{
				System.IO.File.Create(filePath).Dispose();
				var defaults = new SettingsList();
				Save(defaults);
			}
			var serializer = new XmlSerializer(typeof(SettingsList));
			using var reader = new StreamReader(filePath);
			var result = serializer.Deserialize(reader);
			if (result is SettingsList s) return s;
			return new SettingsList();
		}
	}

	internal static class PasswordStore
    {
        private static readonly System.Threading.Lock _lock = new();
        private static string? _value;
        private static System.Timers.Timer? _timer;
        private readonly static int _timeout = Settings.TimeoutMs;
        public static string? Current
        {
            get
            {
                _lock.Enter();
                try { return _value; }
                finally { _lock.Exit(); }
            }
        }
        public static void Set(string? pw)
        {
            _lock.Enter();
            try
            {
                _value = pw;
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Elapsed -= TimerElapsed;
                    _timer.Dispose();
                    _timer = null;
                }
                if (string.IsNullOrEmpty(pw) || _timeout <= 0) {  return; }
                _timer = new System.Timers.Timer(_timeout) { AutoReset = false, };
                _timer.Elapsed += TimerElapsed;
                _timer.Start();
            }
            finally { _lock.Exit(); }
        }

        private static void TimerElapsed(object? s, ElapsedEventArgs e)
        {
            _lock.Enter();
            try
            {
                _value = null;
                if (_timer != null)
                {
                    _timer.Elapsed -= TimerElapsed;
                    _timer.Dispose();
                    _timer = null;
                }
            }
            finally { _lock.Exit(); }
        }
        public static void Clear() { Set(null); }
    }
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
