using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using CPMS.Methods;
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
			public static string LocalCPPath = ""; // LocalCPName in CPProprietiesList
			public static string Username = "";
			public static string CPI = "";
			public static string CPKey = "";
			public static int ClientPort;
			public static string ServerIP = "";
			public static int ServerPort;

			public static void Apply(CPProprietiesList s)
			{
				LocalCPPath = s.LocalCPName;
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
			CPProprieties.LocalCPPath = Path.Combine(FileSystem.Current.AppDataDirectory, "CPs", CPName);
			// System.IO.Path.Combine(FileSystem.Current.AppDataDirectory, "CPs", CPName, "Proprieties.txt");
			// CP_MSG[] files = info.GetFiles().OrderBy(p => p.CreationTime).ToArray();
			LoadChatLog();
			var listener = new UDPListener(this);
			listener.StartListener();
		}

		private async void Delete_Clicked(object s, EventArgs e)
		{
			Directory.Delete(CPProprieties.LocalCPPath, recursive: true);
			await Navigation.PopAsync();
		}

		private async void SendMSG_Clicked(object s, EventArgs e)
		{
			if (string.IsNullOrEmpty(CPMSGBox.Text)) return;
			byte[] message = MakeMessage(Encoding.UTF8.GetBytes(CPMSGBox.Text), MessageType.txt);
			UDPClient.SendBroadcast(message, CPProprieties.ClientPort, CPProprieties.ServerIP); // TEMP
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
		private static Grid MSGViewBuilderFromBin(string? timeStamp)
		{
			if (string.IsNullOrEmpty(timeStamp)) return [];
			byte[] bytes = File.ReadAllBytes(Path.Combine(CPProprieties.LocalCPPath, timeStamp));
			MessageType messagetype = (MessageType)(bytes[0]);
			bytes = bytes[1..];
			var UTF8bytes = Encoding.UTF8.GetString(bytes);
			var username = UTF8bytes[0..UTF8bytes.IndexOf(':')];
			var message = bytes[(UTF8bytes.IndexOf(':') + 1)..];
			Grid MSGBoundingBox = new() { RowDefinitions = { new RowDefinition(), new RowDefinition() } };

			MSGBoundingBox.Add(new Label { Text = username }, 0, 0);
			if (messagetype == MessageType.txt)
			{
				MSGBoundingBox.Add(new Label { Text = Encoding.UTF8.GetString(Crypto.Decrypt(message, CPProprieties.CPKey)), Margin = 5 }, 0, 1);
			}
			else if (messagetype == MessageType.image)
			{
				var image = new Microsoft.Maui.Controls.Image { Source = ImageSource.FromStream(() => new MemoryStream(Crypto.Decrypt((byte[])message, CPProprieties.CPKey))), Margin = 5 };
				MSGBoundingBox.Add(image, 0, 1);
				image.MaximumHeightRequest = 2000;
				image.MaximumWidthRequest = 2000;
				image.Aspect = Aspect.AspectFit;
				image.IsAnimationPlaying = true;
				// MSGBoundingBox.Add(new Label { Text = "Image messages not supported yet", Margin = 5 }, 0, 1);
			}
			return MSGBoundingBox;
		}

		private async void ChatLogUpdate(Grid[] MSGViewList, int index)
		{
			CPMSGStackLayout.Children.Clear();
			foreach (var MSGView in MSGViewList)
			{
				CPMSGStackLayout.Children.Insert(index, MSGView);
				index++;
			}
			await CPView.ScrollToAsync(CPMSGStackLayout, ScrollToPosition.End, false);
		}

		private void LoadChatLog()
		{
			CPMSGStackLayout.Children.Clear();
			int index = 0;
			var MSGViewList = Directory.EnumerateFiles(CPProprieties.LocalCPPath).Where(f => Path.GetFileName(f) != "Proprieties.xml")
				.Select(Path.GetFileName).OrderBy(name => name).Select(MSGViewBuilderFromBin).Where(grid => grid is not null).ToArray();
			ChatLogUpdate(MSGViewList, index);
		}

		

		public class UDPListener(CP CP)
		{
			private readonly CP _CP = CP;
			private static CancellationTokenSource? cts = null;
			public async void StartListener()
			{
				cts = new CancellationTokenSource();

				var discoverer = new CPMS.Methods.NatDiscoverer();
				var (device, localIPv4) = await discoverer.DiscoverDeviceAsync(cts.Token);
				var externalIP = await device.GetExternalIPAsync(cts.Token);
				var portMapping = new CPMS.Methods.Mapping(Protocol.Udp, privatePort: CPProprieties.ClientPort, publicPort: CPProprieties.ClientPort, privateIP: localIPv4, description: "UDP for CPMS", lifetime: 300);
				await device.CreatePortMapAsync(portMapping, cts.Token);
				// await device.DeletePortMapAsync(mapping, cts.Token); // To delete the mapping when done

				IPEndPoint serverEP = new(IPAddress.Parse(CPProprieties.ServerIP), CPProprieties.ServerPort);
				UdpClient udpServer = new() { ExclusiveAddressUse = false };
				udpServer.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
				udpServer.Client.Bind(new IPEndPoint(localIPv4, CPProprieties.ClientPort));
				udpServer.Connect(serverEP);
				
				UDPClient.SendBroadcast(Crypto.Encrypt(Encoding.UTF8.GetBytes($"{CPProprieties.Username}:{externalIP}:{CPProprieties.ClientPort}"), CPProprieties.CPI), CPProprieties.ServerPort, CPProprieties.ServerIP);
				
				UdpClient listener = new(CPProprieties.ClientPort);

				try
				{
					while (cts != null)
					{
						UdpReceiveResult Receiveresult = await listener.ReceiveAsync().WithCancellation(cts.Token);
						var remote = Receiveresult.RemoteEndPoint;
						Debug.WriteLine($"RECEIVED SOMETHING from : {remote.Address}:{remote.Port}");
						byte[] bytes = Receiveresult.Buffer;
						var timeStamp = Encoding.ASCII.GetString(bytes)[0..18];
						var result = new byte[bytes.Length - 18];
						Buffer.BlockCopy(bytes, 18, result, 0, result.Length);
						bytes = result;
						if (bytes[0] != (byte)MessageType.server)
						{
							File.Create(Path.Combine(CPProprieties.LocalCPPath, timeStamp)).Close();
							var binWriter = new BinaryWriter(new FileStream(Path.Combine(CPProprieties.LocalCPPath, timeStamp), FileMode.Create, FileAccess.ReadWrite));
							binWriter.Write(bytes);
							binWriter.Flush();
							binWriter.Close();
							MainThread.BeginInvokeOnMainThread(async () => { Grid[] MSGViewList = [MSGViewBuilderFromBin(timeStamp)]; _CP.LoadChatLog(); });
						}
						else
						{
							Debug.WriteLine($"RECEIVED SERVER MSG at {timeStamp}");
						}
					}
					listener.Close();
					listener.Dispose();
					udpServer.Close();
					udpServer.Dispose();
				}
				catch (OperationCanceledException) { listener.Close(); udpServer.Close(); }
				catch (SocketException e) { Debug.WriteLine(e); }
				finally { Close(); }
			}
			public static void Close() { cts?.Cancel(); cts = null; }
		}

		public class UDPClient
		{
			public static async void SendBroadcast(byte[] message, int listenPort, string broadcastIP)
			{
				Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true };
				socket.SendTo(message, new IPEndPoint(IPAddress.Parse(broadcastIP), listenPort));
			}
		}

		public enum MessageType
		{
			txt = (byte)1,
			image = (byte)2,
			file = (byte)8,
			server = (byte)128
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
		{ // Not going to do it for other platforms, no u Apple, Linux and Android, you will have to use **A BUTTON** to send files \(°O°)/
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
					UDPClient.SendBroadcast(message, CPProprieties.ServerPort, CPProprieties.ServerIP);
				}
			}
#endif
		}
	}
	public static class AsyncExtensions
	{
		public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
		{
			var tcs = new TaskCompletionSource<bool>();
			using (cancellationToken.Register(s => (s as TaskCompletionSource<bool>)?.TrySetResult(true), tcs)) {
				if (task != await Task.WhenAny(task, tcs.Task)) throw new OperationCanceledException(cancellationToken); }
			return task.Result;
		}
	}
}
