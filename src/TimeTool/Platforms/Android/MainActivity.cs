using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace TimeTool;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // отступы под системные бары (статус-бар сверху, жесты снизу)
        var content = FindViewById(Android.Resource.Id.Content);
        if (content != null)
        {
            content.SetOnApplyWindowInsetsListener(new SystemBarsInsetsListener());
            content.RequestApplyInsets();
        }
    }

    private sealed class SystemBarsInsetsListener : Java.Lang.Object, global::Android.Views.View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(global::Android.Views.View view, WindowInsets insets)
        {
            int top;
            int bottom;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                var bars = insets.GetInsets(WindowInsets.Type.SystemBars());
                top = bars.Top;
                bottom = bars.Bottom;
            }
            else
            {
#pragma warning disable CS0618
                top = insets.SystemWindowInsetTop;
                bottom = insets.SystemWindowInsetBottom;
#pragma warning restore CS0618
            }
            view.SetPadding(view.PaddingLeft, top, view.PaddingRight, bottom);
            return insets;
        }
    }
}
