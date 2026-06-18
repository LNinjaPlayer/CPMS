using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Timers;
using System.Xml.Serialization;
using CPMS.Methods;
using static CPMS.CP;

namespace CPMS
{

	public partial class MainPage : ContentPage
    {
		public MainPage()
        {
			InitializeComponent();
			var loaded = SettingsManager.Load();
			Settings.Apply(loaded);
			PromptPassword(null);
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
					await MainThread.InvokeOnMainThreadAsync(async () => PromptPassword(null));
				}
				if (PasswordPromptLoopVariables.PausePasswordLoop == false) PasswordPromptLoopVariables.TimeKeep += 100;
				await Task.Delay(100);
			}
		}
		private async void PromptPassword(string? CPPageAfterPrompt)
		{
			PasswordPromptLoopVariables.PausePasswordLoop = true;
			PasswordStore.Clear();
			var passwordPage = new PasswordPage();
			await Navigation.PushAsync(passwordPage);
			passwordPage.Disappearing += (s, e) =>
			{
				PasswordStore.Set(passwordPage.Password);
				PasswordPromptLoopVariables.PausePasswordLoop = false;
				if (CPPageAfterPrompt != null) Task.Run(() => OpenCPPage(CPPageAfterPrompt));
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
					if (Settings.PasswordPromptCPPageEnter) PromptPassword(CPName);
					else await OpenCPPage(CPName);
				};
			}
        }

		private async Task OpenCPPage(string CPName)
		{
			var CPPage = new CP(CPName);
			CPPage.Disappearing += (s, e) => { UDPListener.Close(); ReloadCPList(); };
			await MainThread.InvokeOnMainThreadAsync(async () => await Navigation.PushAsync(CPPage));
		}

		public class PasswordPromptLoopVariables
		{
			public static bool PausePasswordLoop { get; set; } = false;
			public static int TimeKeep { get; set; } = 0;
		}
	}
}
