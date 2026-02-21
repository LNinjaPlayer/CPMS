using System;
using System.IO;
namespace CPMS;

public partial class SettingsPage : ContentPage
{
	public SettingsPage()
	{
		InitializeComponent();
		PwdPromptTimer.Text = (Settings.TimeoutMs/1000).ToString();
		PwdPromptCPPageEnter.IsToggled = Settings.PasswordPromptCPPageEnter;
	}

	private async void Save_Clicked(object s, EventArgs e)
	{
		if (!int.TryParse(PwdPromptTimer.Text, out var seconds) || seconds < 0)
		{
			if (string.IsNullOrEmpty(PwdPromptTimer.Text)) { return; } // No need to do anything, all Peferences calls has a Default value: i.e Peferences.Get("PasswordTimeoutSeconds", (int)_defaultTimeout)
			await DisplayAlertAsync("Invalid value", "Enter a non-negative integer for the password prompt timer.", "OK"); return;
		}
		if (seconds > 2147483) { await DisplayAlertAsync("Invalid value", "Enter a value up to 2147483sec (24.86 days).", "OK"); return; } // Prevent integer overflow in ms: 2,147,483,647 / 1000 = 2,147,483
		if (seconds < 30) {	await DisplayAlertAsync("Error", "Password Timeout shouldn't be less than 30 sec; you'll be annoyed by the password prompter too much.", "OK");	return; }

		Settings.TimeoutMs = seconds*1000;
		Settings.PasswordPromptCPPageEnter = PwdPromptCPPageEnter.IsToggled;
		// SettingsManager.Save(Settings.ToSettings()); // This has been done on MainPage with settingsPage.OnDisappearing, so just exit the page
		await Navigation.PopAsync();
	} // Remember to update the Settings, SettingsList and the SettingsPage if you add new settings
}