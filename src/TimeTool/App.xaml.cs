namespace TimeTool;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "TimeTool — таймер и расчёт времени" };
#if WINDOWS
        // закрытие окна → трей (выход — через меню значка в трее)
        window.Created += (_, _) => Platforms.Windows.TrayService.Attach(window);
#endif
        return window;
    }
}
