using System;
using System.Threading.Tasks;

namespace CPMS;

public partial class PasswordPage : ContentPage
{
	public PasswordPage()
	{
		InitializeComponent();
		NavigationPage.SetHasBackButton(this, false);
		Shell.SetBackButtonBehavior(this, new BackButtonBehavior { IsVisible = false });
	}

	public string? Password { get; private set; }

	protected override bool OnBackButtonPressed()
	{
		return true;
	}

	private async void Confirm_Clicked(object s, EventArgs e)
	{
		Password = PasswordEntry?.Text;
		await Navigation.PopAsync();
	}

}