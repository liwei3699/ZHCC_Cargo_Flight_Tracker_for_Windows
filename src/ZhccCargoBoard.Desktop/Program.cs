using Avalonia;

namespace ZhccCargoBoard.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

        // RK3588（ARM 闭源 DDK）只有 GLES：GLX 初始化会失败，必须显式走 EGL，再退软件渲染
        if (OperatingSystem.IsLinux())
        {
            builder = builder.With(new X11PlatformOptions
            {
                RenderingMode = new[] { X11RenderingMode.Egl, X11RenderingMode.Software }
            });
        }

        return builder;
    }
}
