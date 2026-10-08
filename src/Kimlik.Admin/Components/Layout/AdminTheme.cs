using MudBlazor;

namespace Kimlik.Admin.Components.Layout;

internal static class AdminTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight { Primary = "#4f46e5", AppbarBackground = "#111827" },
        PaletteDark = new PaletteDark { Primary = "#818cf8" },
    };
}
