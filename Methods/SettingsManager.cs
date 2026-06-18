using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;

namespace CPMS.Methods
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
}
