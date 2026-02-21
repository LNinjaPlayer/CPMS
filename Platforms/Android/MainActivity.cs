using Android.App;
using Android.Content.PM;
using Android.OS;

#pragma warning disable IDE0130
namespace CPMS
#pragma warning restore IDE0130
{
	[Activity(Theme = "@style/MyTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
    }
}
