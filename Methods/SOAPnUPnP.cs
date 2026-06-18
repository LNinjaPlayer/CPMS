using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Xml;

namespace CPMS.Methods
{
	public enum Protocol
	{
		Tcp,
		Udp
	}

	public sealed class Mapping
	{
		public Protocol Protocol { get; }
		public int PublicPort { get; }
		public int PrivatePort { get; }
		public IPAddress PrivateIP { get; }
		public string Description { get; }
		public int Lifetime { get; }

		public Mapping(Protocol protocol, int privatePort, int publicPort, IPAddress privateIP, string description = "Open.Nat", int lifetime = 0)
		{
			Protocol = protocol;
			PrivatePort = privatePort;
			PublicPort = publicPort;
			PrivateIP = privateIP;
			Description = description;
			Lifetime = lifetime;
		}

		public override string ToString()
		{
			return $"{Protocol} {PublicPort}->{PrivateIP}:{PrivatePort} ({Description})";
		}
	}

	public abstract class NatDevice
	{
		public abstract Task<IPAddress> GetExternalIPAsync(CancellationToken cancellationToken = default);
		public abstract Task CreatePortMapAsync(Mapping mapping, CancellationToken cancellationToken = default);
		public abstract Task DeletePortMapAsync(Mapping mapping, CancellationToken cancellationToken = default);
	}

	public sealed class UpnpNatDevice : NatDevice
	{
		private readonly Uri _controlUrl;
		private readonly string _serviceType;
		private readonly IPAddress _localAddress;

		public UpnpNatDevice(Uri controlUrl, string serviceType, IPAddress localAddress)
		{
			_controlUrl = controlUrl;
			_serviceType = serviceType;
			_localAddress = localAddress;
		}

		public override async Task<IPAddress> GetExternalIPAsync(CancellationToken cancellationToken = default)
		{
			var doc = await SendSoapAsync("GetExternalIPAddress", null, cancellationToken).ConfigureAwait(false);
			var ns = new XmlNamespaceManager(doc.NameTable);
			ns.AddNamespace("s", "urn:schemas-upnp-org:service:WANIPConnection:1");
			var node = doc.SelectSingleNode("//NewExternalIPAddress");
			if (node == null || string.IsNullOrWhiteSpace(node.InnerText))
				throw new InvalidOperationException("External IP not found in response.");
			return IPAddress.Parse(node.InnerText.Trim());
		}

		public override async Task CreatePortMapAsync(Mapping mapping, CancellationToken cancellationToken = default)
		{
			var args = new Dictionary<string, string>
			{
				{ "NewRemoteHost", "" },
				{ "NewExternalPort", mapping.PublicPort.ToString() },
				{ "NewProtocol", mapping.Protocol == Protocol.Tcp ? "TCP" : "UDP" },
				{ "NewInternalPort", mapping.PrivatePort.ToString() },
				{ "NewInternalClient", mapping.PrivateIP.ToString() },
				{ "NewEnabled", "1" },
				{ "NewPortMappingDescription", mapping.Description },
				{ "NewLeaseDuration", mapping.Lifetime.ToString() }
			};

			await SendSoapAsync("AddPortMapping", args, cancellationToken).ConfigureAwait(false);
		}

		public override async Task DeletePortMapAsync(Mapping mapping, CancellationToken cancellationToken = default)
		{
			var args = new Dictionary<string, string>
			{
				{ "NewRemoteHost", "" },
				{ "NewExternalPort", mapping.PublicPort.ToString() },
				{ "NewProtocol", mapping.Protocol == Protocol.Tcp ? "TCP" : "UDP" }
			};

			await SendSoapAsync("DeletePortMapping", args, cancellationToken).ConfigureAwait(false);
		}

		private async Task<XmlDocument> SendSoapAsync(string action, IDictionary<string, string> arguments, CancellationToken cancellationToken)
		{
			var req = (HttpWebRequest)WebRequest.Create(_controlUrl);
			req.Method = "POST";
			req.ContentType = "text/xml; charset=\"utf-8\"";
			req.Headers.Add("SOAPACTION", $"\"{_serviceType}#{action}\"");
			req.Timeout = 10000;

			var body = BuildSoapBody(action, arguments);
			var bytes = Encoding.UTF8.GetBytes(body);

			req.ContentLength = bytes.Length; // avoid chunked transfer
			req.KeepAlive = false;
			req.ServicePoint.Expect100Continue = false;

			try
			{
				using (var reqStream = await req.GetRequestStreamAsync().ConfigureAwait(false))
				{
					await reqStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
				}

				using (var resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
				using (var stream = resp.GetResponseStream())
				{
					var doc = new XmlDocument();
					doc.Load(stream);
					return doc;
				}
			}
			catch (WebException ex) when (ex.Response != null)
			{
				using (var resp = (HttpWebResponse)ex.Response)
				using (var rs = resp.GetResponseStream())
				using (var sr = new StreamReader(rs))
				{
					var serverText = await sr.ReadToEndAsync().ConfigureAwait(false);
					// Log resp.StatusCode, resp.StatusDescription and serverText for diagnosis
					throw new InvalidOperationException($"UPnP error {(int)resp.StatusCode} {resp.StatusDescription}: {serverText}", ex);
				}
			}
		}

		private string BuildSoapBody(string action, IDictionary<string, string> arguments)
		{
			var sb = new StringBuilder();
			sb.Append("<?xml version=\"1.0\"?>");
			sb.Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" ");
			sb.Append("s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">");
			sb.Append("<s:Body>");
			sb.Append($"<u:{action} xmlns:u=\"{_serviceType}\">");

			if (arguments != null)
			{
				foreach (var kv in arguments)
				{
					sb.AppendFormat("<{0}>{1}</{0}>", kv.Key, SecurityElement.Escape(kv.Value));
				}
			}

			sb.Append($"</u:{action}>");
			sb.Append("</s:Body>");
			sb.Append("</s:Envelope>");
			return sb.ToString();
		}
	}

	public sealed class NatDiscoverer
	{
		private const string SsdpMulticastAddress = "239.255.255.250";
		private const int SsdpPort = 1900;
		private const string SearchTarget = "urn:schemas-upnp-org:device:InternetGatewayDevice:1";

		public async Task<(NatDevice device, IPAddress localAddress)> DiscoverDeviceAsync(CancellationToken cancellationToken = default)
		{
			var LocalIPs = GetLocalIPAddresses();
			foreach (var local in LocalIPs)
			{
				var responses = await SendSsdpDiscoverAsync(local, cancellationToken).ConfigureAwait(false);
				foreach (var resp in responses)
				{
					var device = await TryCreateUpnpDeviceAsync(resp, local, cancellationToken).ConfigureAwait(false);
					if (device != null)
						return (device, local);
				}
			}
			throw new InvalidOperationException("No UPnP NAT device found.");
		}

		private static IPAddress[] GetLocalIPAddresses()
		{
			var host = Dns.GetHostEntry(Dns.GetHostName());
			var ips = host.AddressList;
			if (ips == null)
				throw new InvalidOperationException("No network adapters with an IPv4 address in the system!");
			return ips;
		}

		private async Task<List<string>> SendSsdpDiscoverAsync(IPAddress localAddress, CancellationToken cancellationToken)
		{
			var list = new List<string>();
			if (localAddress.AddressFamily != AddressFamily.InterNetwork)
				return list;
			using (var udp = new UdpClient(new IPEndPoint(localAddress, 0)))
			{
				udp.EnableBroadcast = true;
				udp.MulticastLoopback = false;

				var request =
					"M-SEARCH * HTTP/1.1\r\n" +
					$"HOST: {SsdpMulticastAddress}:{SsdpPort}\r\n" +
					"MAN: \"ssdp:discover\"\r\n" +
					$"ST: {SearchTarget}\r\n" +
					"MX: 2\r\n" +
					"\r\n";

				var data = Encoding.ASCII.GetBytes(request);
				var ep = new IPEndPoint(IPAddress.Parse(SsdpMulticastAddress), SsdpPort);
				await udp.SendAsync(data, data.Length, ep).ConfigureAwait(false);

				var timeout = Task.Delay(3000, cancellationToken);
				while (!timeout.IsCompleted)
				{
					if (udp.Available > 0)
					{
						var result = await udp.ReceiveAsync().ConfigureAwait(false);
						var text = Encoding.ASCII.GetString(result.Buffer);
						list.Add(text);
					}
					else
					{
						await Task.Delay(100, cancellationToken).ConfigureAwait(false);
					}
				}
			}

			return list;
		}

		private async Task<NatDevice> TryCreateUpnpDeviceAsync(string ssdpResponse, IPAddress localAddress, CancellationToken cancellationToken)
		{
			var location = ParseHeader(ssdpResponse, "LOCATION");
			if (string.IsNullOrWhiteSpace(location))
				return null;

			var descUri = new Uri(location.Trim());
			var (controlUrl, serviceType) = await GetControlUrlAsync(descUri, cancellationToken).ConfigureAwait(false);
			if (controlUrl == null || string.IsNullOrEmpty(serviceType))
				return null;

			return new UpnpNatDevice(controlUrl, serviceType, localAddress);
		}

		private static string ParseHeader(string response, string headerName)
		{
			var lines = response.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var line in lines)
			{
				var idx = line.IndexOf(':');
				if (idx <= 0) continue;
				var name = line.Substring(0, idx).Trim();
				if (!name.Equals(headerName, StringComparison.OrdinalIgnoreCase))
					continue;
				return line.Substring(idx + 1).Trim();
			}
			return null;
		}

		private async Task<(Uri controlUrl, string serviceType)> GetControlUrlAsync(Uri descriptionUrl, CancellationToken cancellationToken)
		{
			var req = (HttpWebRequest)WebRequest.Create(descriptionUrl);
			req.Method = "GET";
			req.Timeout = 5000;

			using (var resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
			using (var stream = resp.GetResponseStream())
			{
				var doc = new XmlDocument();
				doc.Load(stream);

				var ns = new XmlNamespaceManager(doc.NameTable);
				ns.AddNamespace("tns", "urn:schemas-upnp-org:device-1-0");

				// Find WANIPConnection service
				var serviceList = doc.GetElementsByTagName("service");
				foreach (XmlNode service in serviceList)
				{
					var serviceTypeNode = service["serviceType"];
					var controlUrlNode = service["controlURL"];
					if (serviceTypeNode == null || controlUrlNode == null)
						continue;

					var st = serviceTypeNode.InnerText.Trim();
					if (!st.Contains("WANIPConnection"))
						continue;

					var ctrl = controlUrlNode.InnerText.Trim();
					var baseUri = new Uri(descriptionUrl.GetLeftPart(UriPartial.Authority));
					Uri ctrlUri;
					if (Uri.TryCreate(ctrl, UriKind.Absolute, out ctrlUri))
					{
						// ok
					}
					else
					{
						ctrlUri = new Uri(baseUri, ctrl);
					}

					return (ctrlUri, st);
				}
			}

			return (null, null);

		}
	}
}
