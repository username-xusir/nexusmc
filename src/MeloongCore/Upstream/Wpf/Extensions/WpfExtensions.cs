using System.Windows.Controls;

namespace MeloongCore.Wpf.Extensions;
public static class WpfExtensions {

    public static bool Any(this UIElementCollection? Arr) => Arr?.Count > 0;

}
