using System.Text;
using System.Xml.Serialization;

namespace CPMS
{
    public partial class NewCPPage : ContentPage
    {
		public class CPProprietiesList
		{ // Remember to update this and the CPProprieties class in CP.xaml.cs and Save_Clicked Method under here
			public string LocalCPName = "";
			public byte[] Username = [];
			public byte[] CPI = [];
			public byte[] CPKey = [];
			public byte[] ClientPort = [];
			public byte[] ServerIP = [];
			public byte[] ServerPort = [];
		}
		public NewCPPage()
        {
            InitializeComponent();
			string CPlocation = FileSystem.Current.AppDataDirectory.ToString();
			CPLocation.Text = "CP saved at : " + CPlocation;
		}
		private async void Save_Clicked(object sender, EventArgs e)
        {
			if (!IsValidFolderName(CPName.Text, out var reason)) await DisplayAlertAsync("Error", reason, "OK");
			else if (string.IsNullOrEmpty(Username.Text)) await DisplayAlertAsync("Error", "You have to have a username", "OK");
			else if (Username.Text.IndexOf(':') >= 0) await DisplayAlertAsync("Error", $"Username cannot contain ':'\nEverything else is allowed", "OK"); // .Contains() is slower than .IndexOf(), just a random micro-optimization, for that 0.1ms diffenrence, probably not even with complier optimizations but I'm making fun of optimization for my crappy code
			else if (string.IsNullOrEmpty(CPI.Text)) await DisplayAlertAsync("Error", "You have to have a CP identifier", "OK");
			else if (string.IsNullOrEmpty(CPKey.Text)) await DisplayAlertAsync("Error", "You have to have a CP Encryption key", "OK");
			else if (!int.TryParse(ClientPort.Text, out _)) if (!string.IsNullOrEmpty(ClientPort.Text)) await DisplayAlertAsync("Error", "You have to have a valid Client Port", "OK");
			else if (string.IsNullOrEmpty(ServerIP.Text)) await DisplayAlertAsync("Error", "You have to have a Server IP", "OK");
			else if (!int.TryParse(ServerPort.Text, out _)) if (!string.IsNullOrEmpty(ServerPort.Text)) await DisplayAlertAsync("Error", "You have to have a valid Server Port", "OK");
			else
			{
				var dir = System.IO.Path.Combine(FileSystem.Current.AppDataDirectory, "CPs", CPName.Text);
				System.IO.Directory.CreateDirectory(dir);
				CPProprietieManager.Save(dir, new CPProprietiesList
				{
					LocalCPName = CPName.Text,
					Username = Crypto.Encrypt(Encoding.UTF8.GetBytes(Username.Text), PasswordStore.Current),
					CPI = Crypto.Encrypt(Encoding.UTF8.GetBytes(CPI.Text), PasswordStore.Current),
					CPKey = Crypto.Encrypt(Encoding.UTF8.GetBytes(CPKey.Text), PasswordStore.Current),
					ClientPort = Crypto.Encrypt(Encoding.UTF8.GetBytes((int.TryParse(ClientPort.Text, out var cport) ? cport : 42000).ToString()), PasswordStore.Current),
					ServerIP = Crypto.Encrypt(Encoding.UTF8.GetBytes(ServerIP.Text), PasswordStore.Current),
					ServerPort = Crypto.Encrypt(Encoding.UTF8.GetBytes((int.TryParse(ServerPort.Text, out var sport) ? sport : 42000).ToString()), PasswordStore.Current)

				});
				await Navigation.PopAsync();
			}
		}


		private static class CPProprietieManager
		{
			public static void Save(string CPdir, CPProprietiesList proprieties)
			{
				string filePath = Path.Combine(CPdir, "Proprieties.xml");
				File.Create(filePath).Close();
				var serializer = new XmlSerializer(typeof(CPProprietiesList));
				using var writer = new StreamWriter(filePath, false);
				serializer.Serialize(writer, proprieties);
				writer.Close();
			}
		}


			// Conservative validation for folder names all (most) OSes will accept, with user-friendly reasons for failure
			private static bool IsValidFolderName(string name, out string reason)
		{
			reason = string.Empty;

			if (string.IsNullOrEmpty(name))
			{
				reason = "Name cannot be empty.";
				return false;
			}
			if (name.All(c => c == '.'))
			{
				reason = "Name cannot consist only of periods.";
				return false;
			}
			if (name.Length > 255)
			{
				reason = "Name is too long (max 255 characters).";
				return false;
			}
			if (!name.Equals(name.Trim(), StringComparison.Ordinal))
			{
				reason = "Name must not have leading or trailing whitespace.";
				return false;
			}
			if (name.EndsWith('.') || name.StartsWith('.')) // using StringComparison.Ordinal causes CA1865 and annoyed me
			{
				reason = "Name must not end with a period ('.').";
				return false;
			}
			if (name.Any(c => char.IsControl(c)))
			{
				reason = "Name contains invalid control characters.";
				return false;
			}

			// (Hopefully) Full conservative list of invalid filename characters:
			var platformInvalid = Path.GetInvalidFileNameChars();
			var additionalInvalid = new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*', '\0' }; // include NUL explicitly
			var invalidChars = platformInvalid.Concat(additionalInvalid).Distinct().ToArray();

			var foundChars = name.Where(c => invalidChars.Contains(c)).Distinct().ToArray();
			if (foundChars.Length > 0)
			{
				// Convert found chars to a readable representation
				var readable = foundChars.Select(c =>
				{
					if (c == '\0') return "\\0";
					if (char.IsControl(c)) return $"\\u{((int)c):X4}";
					if (c == ' ') return "' '";
					return c.ToString();
				}).ToArray();

				reason = $"Name contains invalid characters: {string.Join(" ", readable)}";
				return false;
			}

			// Disallow reserved Windows device names (applies even with extensions, e.g. "CON.txt")
			string[] reserved = [ "CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9" ];

			// Trim trailing spaces and dots when checking device names (Windows rule)
			var nameForDeviceCheck = name.TrimEnd(' ', '.').Split('.').FirstOrDefault()?.ToUpperInvariant() ?? string.Empty;
			if (reserved.Contains(nameForDeviceCheck))
			{
				reason = "Name is a reserved system name.";
				return false;
			}

			// Passed all checks
			return true;
		}
	}
}
