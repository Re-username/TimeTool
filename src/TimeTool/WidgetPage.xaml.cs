#if WINDOWS
using Microsoft.Web.WebView2.Core;
#endif

namespace TimeTool;

public partial class WidgetPage : ContentPage
{
    public WidgetPage()
    {
        InitializeComponent();
#if WINDOWS
        blazorWebView.BlazorWebViewInitialized += (_, args) =>
        {
            // до создания документа сообщаем виджету, какую вкладку показывать
            var tab = Services.WidgetContext.Tab.Replace("'", "");
            args.WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                $"window.__widget = '{tab}';");
        };
#endif
    }
}
