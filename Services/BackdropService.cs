using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3.Services;

public enum BackdropType
{
    Mica,
    MicaAlt,
    Acrylic
}

public static class BackdropService
{
    public static event Action? BackdropChanged;

    public static BackdropType GetBackdropType()
    {
        var val = AppSettings.Get("BackdropType");
        return Enum.TryParse<BackdropType>(val, out var t) ? t : BackdropType.Mica;
    }

    public static void SetBackdropType(BackdropType type)
    {
        AppSettings.Set("BackdropType", type.ToString());
        BackdropChanged?.Invoke();
    }

    public static void ApplyBackdrop(Window window)
    {
        var type = GetBackdropType();

        switch (type)
        {
            case BackdropType.Mica:
                window.SystemBackdrop = new MicaBackdrop();
                break;

            case BackdropType.MicaAlt:
                var micaAlt = new MicaBackdrop();
                micaAlt.Kind = MicaKind.BaseAlt;
                window.SystemBackdrop = micaAlt;
                break;

            case BackdropType.Acrylic:
                window.SystemBackdrop = new DesktopAcrylicBackdrop();
                break;
        }
    }
}
