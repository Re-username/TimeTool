namespace TimeTool;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
#if ANDROID
        // На Android BlazorWebView стартует раньше готовности нативного моста
        // (window.external.receiveMessage) и падает с "Blazor has already started" —
        // экран навсегда остаётся на спиннере. Android-страница стартует Blazor
        // вручную только после готовности моста (autostart="false").
        blazorWebView.HostPage = "wwwroot/index.android.html";
#endif
    }
}
