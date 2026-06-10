using QuestPDF.Drawing;

namespace Backend.Services;

internal static class InvoiceFontSetup
{
    public const string Family = "Cambria";

    private static int _initialized;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
            return;

        var fontsDir = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Fonts");
        QuestPDF.Settings.UseEnvironmentFonts = false;
        QuestPDF.Settings.FontDiscoveryPaths.Add(fontsDir);

        foreach (var file in new[]
                 {
                     "Cambria-Regular.ttf",
                     "Cambria-Bold.ttf",
                     "Cambria-Italic.ttf",
                     "Cambria-BoldItalic.ttf"
                 })
        {
            var path = Path.Combine(fontsDir, file);
            if (!File.Exists(path))
                continue;

            using var stream = File.OpenRead(path);
            FontManager.RegisterFont(stream);
        }
    }
}
