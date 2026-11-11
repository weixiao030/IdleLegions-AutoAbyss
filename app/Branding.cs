using System.Drawing;
using System.Reflection;

namespace FrostAbyss;

static class Branding
{
    internal static string Version => Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "未知";

    public static Icon LoadIcon(Size size)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico")
            ?? throw new InvalidOperationException("程序图标资源缺失。");
        using var source = new Icon(stream, size);
        // A clone owns its data independently of the disposed resource stream.
        return (Icon)source.Clone();
    }
}
