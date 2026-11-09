using System.Drawing;
using System.Reflection;

namespace FrostAbyss;

static class Branding
{
    public static Icon LoadIcon(Size size)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico")
            ?? throw new InvalidOperationException("程序图标资源缺失。");
        using var source = new Icon(stream, size);
        // A clone owns its data independently of the disposed resource stream.
        return (Icon)source.Clone();
    }
}
