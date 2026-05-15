using System;
using System.Threading;
using Avalonia;
using UnlockFps.Utils;

namespace UnlockFps;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        int? headlessFps = null;
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--fps" || args[i] == "-fps") && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out var fps))
                {
                    headlessFps = fps;
                }
            }
            else if (args.Length == 1 && int.TryParse(args[0], out var fpsOnly))
            {
                headlessFps = fpsOnly;
            }
        }

        using (new Mutex(true, @"GenshinFPSUnlocker", out var createdNew))
        {
            DuplicatedInstance = !createdNew;

            if (headlessFps.HasValue)
            {
                if (DuplicatedInstance)
                {
                    Console.WriteLine("Another instance of the unlocker is already running.");
                    return;
                }

                RunHeadless(headlessFps.Value);
                return;
            }

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
    }

    private static void RunHeadless(int fps)
    {
        Console.WriteLine($"Starting in headless mode with FPS limit: {fps}");
        
        var configService = new UnlockFps.Services.ConfigService();
        configService.Config.FpsTarget = fps;
        
        var gameService = new UnlockFps.Services.GameInstanceService(configService);
        gameService.Start();
        
        Console.WriteLine("Waiting for game... Press Ctrl+C to exit.");
        
        Thread.Sleep(Timeout.Infinite);
    }

    public static bool DuplicatedInstance { get; private set; }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var appBuilder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithNativeFonts()
            .LogToTrace();
        if (WineHelper.DetectWine(out _, out _))
        {
            return appBuilder
                .With(new Win32PlatformOptions
                {
                    CompositionMode = [Win32CompositionMode.RedirectionSurface],
                    RenderingMode = [Win32RenderingMode.Software],
                    OverlayPopups = true
                });
        }
        else
        {
            return appBuilder;
        }
    }
}