using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Serialization;
using static CPMS.NewCPPage;

#if WINDOWS
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
#endif

namespace CPMS
{
	public partial class CP : ContentPage
	{
		class CPProprieties
		{
			public static string LocalCPName = "";
			public static string Username = "TestUsername";
			public static string CPI = "";
			public static string CPKey = "TestKey";
			public static int ClientPort = 42000;
			public static string ServerIP = "82.225.58.132";
			public static int ServerPort = 42000;

			public static void Apply(CPProprietiesList s)
			{
				LocalCPName = s.LocalCPName;
				Username = Encoding.UTF8.GetString(Crypto.Decrypt(s.Username, PasswordStore.Current));
				CPI = Encoding.UTF8.GetString(Crypto.Decrypt(s.CPI, PasswordStore.Current));
				CPKey = Encoding.UTF8.GetString(Crypto.Decrypt(s.CPKey, PasswordStore.Current));
#pragma warning disable CA1806 // int.TryParse won't be null, NewCPPage forced you to enter a valid int and defaults to 42000 if null, except if someone touches the Proprieties.xml in which case this is now a feature not a bug, if you dare enter the wrong password on the app or modify the file, things just won't work and crash, 100% intended.
				int.TryParse(Encoding.UTF8.GetString(Crypto.Decrypt(s.ClientPort, PasswordStore.Current)), out ClientPort);
				ServerIP = Encoding.UTF8.GetString(Crypto.Decrypt(s.ServerIP, PasswordStore.Current));
				int.TryParse(Encoding.UTF8.GetString(Crypto.Decrypt(s.ServerPort, PasswordStore.Current)), out ServerPort);
#pragma warning restore CA1806
			}
		}
		public CP(string CPName)
		{
			InitializeComponent();
			CPPageTitle.Title = CPName;
			CPProprieties.Apply(LoadCPProprieties(CPName));
			CPProprieties.LocalCPName = Path.Combine(FileSystem.Current.AppDataDirectory, "CPs", CPName);
			// System.IO.Path.Combine(FileSystem.Current.AppDataDirectory, "CPs", CPName, "Proprieties.txt");
			// CP_MSG[] files = info.GetFiles().OrderBy(p => p.CreationTime).ToArray();
			var listener = new UDPListener(this, CPProprieties.ServerPort);
			listener.StartListener();
		}

		private async void Delete_Clicked(object s, EventArgs e)
		{
			Directory.Delete(CPProprieties.LocalCPName, recursive: true);
			await Navigation.PopAsync();
		}

		private async void SendMSG_Clicked(object s, EventArgs e)
		{
			if (string.IsNullOrEmpty(CPMSGBox.Text)) return;
			byte[] message = MakeMessage(Encoding.UTF8.GetBytes(CPMSGBox.Text), MessageType.txt);
			UDPClientExample.SendBroadcast(message, CPProprieties.ServerPort, CPProprieties.ServerIP);
		}
		private static byte[] MakeMessage(byte[] message, MessageType messageType)
		{
			byte[] result;
			using (var ms = new MemoryStream())
			{
				ms.Write(Encoding.ASCII.GetBytes(DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssffff")));
				ms.WriteByte((byte)messageType);
				ms.Write(Encoding.UTF8.GetBytes(CPProprieties.Username + ":"));
				ms.Write(Crypto.Encrypt(message, CPProprieties.CPKey));
				result = ms.ToArray();
			}
			return result;
		}
		private async void MSG(string User, byte[] message, MessageType _messagetype)
		{
			Grid MSGBoundingBox = new() { RowDefinitions = { new RowDefinition(), new RowDefinition() } };

			MSGBoundingBox.Add(new Label { Text = User }, 0, 0);
			if (_messagetype == MessageType.txt)
			{
				MSGBoundingBox.Add(new Label { Text = Encoding.UTF8.GetString(Crypto.Decrypt(message, CPProprieties.CPKey)), Margin = 5 }, 0, 1);
			}
			else if (_messagetype == MessageType.image)
			{
				var image = new Image { Source = ImageSource.FromStream(() => new MemoryStream(Crypto.Decrypt((byte[])message, CPProprieties.CPKey))), Margin = 5 };
				MSGBoundingBox.Add(image, 0, 1);
				image.MaximumHeightRequest = 2000;
				image.MaximumWidthRequest = 2000;
				image.Aspect = Aspect.AspectFit;
				image.IsAnimationPlaying = true;
				// MSGBoundingBox.Add(new Label { Text = "Image messages not supported yet", Margin = 5 }, 0, 1);
			}
			CPMSGStackLayout.Children.Add(MSGBoundingBox);
			await CPView.ScrollToAsync(CPMSGStackLayout, ScrollToPosition.End, false);
		}

		public class UDPListener(CP CP, int listenPort)
		{
			private readonly CP _CP = CP;
			private static readonly int _listenPort = CPProprieties.ServerPort;
			private static readonly UdpClient listener = new(_listenPort);
			public async void StartListener()
			{
				var groupEP = new IPEndPoint(IPAddress.Any, listenPort);
				//var binWriter = new BinaryWriter(new FileStream(CPProprieties.CPName, FileMode.Create, FileAccess.ReadWrite));
				//var binReader = new BinaryReader(binWriter.BaseStream);
				try
				{
					while (true)
					{
						UdpReceiveResult Receiveresult = await listener.ReceiveAsync();
						byte[] bytes = Receiveresult.Buffer;
						var timeStamp = Encoding.ASCII.GetString(bytes)[0..18];
						var result = new byte[bytes.Length - 18];
						Buffer.BlockCopy(bytes, 18, result, 0, result.Length);
						bytes = result;
						File.Create(Path.Combine(CPProprieties.LocalCPName, timeStamp)).Close();
						var binWriter = new BinaryWriter(new FileStream(Path.Combine(CPProprieties.LocalCPName, timeStamp), FileMode.Create, FileAccess.ReadWrite));
						binWriter.Write(bytes);
						MessageType messagetype = (MessageType)(bytes[0]);
						bytes = bytes[1..];
						var UTF8bytes = Encoding.UTF8.GetString(bytes);
						var username = UTF8bytes[0..UTF8bytes.IndexOf(':')];
						var message = bytes[(UTF8bytes.IndexOf(':')+1)..];
						MainThread.BeginInvokeOnMainThread( () => _CP.MSG(username, message, messagetype) );
					}
				}
				catch (SocketException e)
				{
					Debug.WriteLine(e);
				}
				finally
				{
					listener.Close();
				}
			}
			public static void Close()
			{
				listener.Close();
			}
		}
		public class UDPClientExample
		{
			public static async void SendBroadcast(byte[] message, int listenPort, string broadcastIP)
			{
				Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true };
				var broadcast = IPAddress.Parse(broadcastIP);
				var groupEP = new IPEndPoint(broadcast, listenPort);
				socket.SendTo(message, groupEP);
			}
		}
		public enum MessageType
		{
			txt = (byte)1,
			image = (byte)2,
			file = (byte)64
		}
		private static CPProprietiesList LoadCPProprieties(string CPName)
		{
			var filePath = System.IO.Path.Combine(FileSystem.Current.AppDataDirectory, "CPs", CPName, "Proprieties.xml");
			var serializer = new XmlSerializer(typeof(CPProprietiesList));
			using var reader = new StreamReader(filePath);
			var result = serializer.Deserialize(reader);
			if (result is CPProprietiesList s) return s;
			return new CPProprietiesList();
		}

		private async void FileDrop(object s, DropEventArgs e)
		{ // Not going to do it for other platforms, no u Apple, Linux and Android, you will have to use **A BUTTON** to send files °O°
#if WINDOWS
			var items = await e.PlatformArgs?.DragEventArgs.DataView.GetStorageItemsAsync();
			var filePaths = new List<string>();
			if (items == null) return;
			if (items.Any()) { foreach (var item in items) { if (item is StorageFile file) filePaths.Add(item.Path); } }
			if (filePaths.Count > 0)
			{
				var ImageExtensions = new List<string> { ".JPG", ".JPEG", ".JPE", ".BMP", ".GIF", ".PNG" };
				foreach (string filepath in filePaths)
				{
					byte[] message;
					if (ImageExtensions.Contains(Path.GetExtension(filepath).ToUpperInvariant())) message = MakeMessage(File.ReadAllBytes(filepath), MessageType.image); 
					else message = MakeMessage(File.ReadAllBytes(filepath), MessageType.file);
					UDPClientExample.SendBroadcast(message, CPProprieties.ServerPort, CPProprieties.ServerIP);
				}
			}
#endif
		}
	}
}
