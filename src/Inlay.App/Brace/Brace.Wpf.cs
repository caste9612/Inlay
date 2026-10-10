// GENERATO da strumenti/genera_piattaforme.py a partire da strumenti/modelli/wpf/Brace.Wpf.cs: modifica quello, non questo file.
// Brace per WPF: codice di supporto agli stili di Brace.Controls.xaml.
#nullable enable
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Brace;

/// <summary>Barra del titolo di Windows nei colori di Brace: brace:Chrome.Dark="True" su una finestra.</summary>
public static class Chrome
{
    public static readonly DependencyProperty DarkProperty = DependencyProperty.RegisterAttached(
        "Dark", typeof(bool), typeof(Chrome), new PropertyMetadata(false, OnDarkChanged));

    public static bool GetDark(Window window) => (bool)window.GetValue(DarkProperty);

    public static void SetDark(Window window, bool value) => window.SetValue(DarkProperty, value);

    private static void OnDarkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window || e.NewValue is not true)
            return;
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Apply(window);
        else
            window.SourceInitialized += (_, _) => Apply(window);
    }

    // Tema scuro della cornice (20) da Windows 10 20H1; colori di bordo (34), titolo (35) e testo (36) da Windows 11.
    // Sui sistemi che non li conoscono le chiamate falliscono senza effetti.
    private static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
        SetColor(window, hwnd, 34, "Brace.BorderDefaultColor");
        SetColor(window, hwnd, 35, "Brace.SurfaceBaseColor");
        SetColor(window, hwnd, 36, "Brace.TextPrimaryColor");
    }

    private static void SetColor(Window window, IntPtr hwnd, int attribute, string key)
    {
        if (window.TryFindResource(key) is not Color c)
            return;
        var colorref = c.R | (c.G << 8) | (c.B << 16);
        DwmSetWindowAttribute(hwnd, attribute, ref colorref, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

/// <summary>Rientro delle voci di TreeView: la selezione copre tutta la riga, il contenuto rientra di Step per livello.</summary>
public sealed class TreeIndentConverter : IValueConverter
{
    public double Base { get; set; }

    public double Step { get; set; } = 16;

    public double Right { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var depth = 0;
        if (value is DependencyObject item)
        {
            for (var parent = ItemsControl.ItemsControlFromItemContainer(item); parent is TreeViewItem node;
                 parent = ItemsControl.ItemsControlFromItemContainer(node))
                depth++;
        }
        return new Thickness(Base + depth * Step, 0, Right, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Avanzamento di un movimento da 0 a 1, animato dagli stati visivi; gli stili lo traducono in posizione.</summary>
public static class Motion
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.RegisterAttached(
        "Progress", typeof(double), typeof(Motion), new PropertyMetadata(0.0));

    public static double GetProgress(DependencyObject element) => (double)element.GetValue(ProgressProperty);

    public static void SetProgress(DependencyObject element, double value) => element.SetValue(ProgressProperty, value);
}

/// <summary>
/// Margine del cursore dell'interruttore: (avanzamento, larghezza della traccia, larghezza del cursore).
/// Con le animazioni di Windows disattivate il cursore salta da un capo all'altro invece di scorrere.
/// </summary>
public sealed class SwitchThumbMarginConverter : IMultiValueConverter
{
    /// <summary>Distanza del cursore dal bordo interno della traccia.</summary>
    public double Inset { get; set; } = 2;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values[0] is not double progress || values[1] is not double track || values[2] is not double thumb)
            return new Thickness(Inset, 0, 0, 0);
        if (!SystemParameters.ClientAreaAnimation)
            progress = progress < 0.5 ? 0 : 1;
        var corsa = Math.Max(0, track - 2 - 2 * Inset - thumb); // 2: il bordo di 1 px della traccia, a sinistra e a destra
        return new Thickness(Inset + progress * corsa, 0, 0, 0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Curva di Bézier cubica dei token di movimento come funzione di easing: Spline="0,0 0.58,1".</summary>
public sealed class BezierEase : EasingFunctionBase
{
    public static readonly DependencyProperty SplineProperty = DependencyProperty.Register(
        nameof(Spline), typeof(KeySpline), typeof(BezierEase), new PropertyMetadata(null));

    static BezierEase()
    {
        // La curva è già scritta per intero: nessuna trasformazione EaseOut o EaseInOut sopra.
        EasingModeProperty.OverrideMetadata(typeof(BezierEase), new PropertyMetadata(EasingMode.EaseIn));
    }

    public KeySpline? Spline
    {
        get => (KeySpline?)GetValue(SplineProperty);
        set => SetValue(SplineProperty, value);
    }

    protected override double EaseInCore(double normalizedTime) => Spline?.GetSplineProgress(normalizedTime) ?? normalizedTime;

    protected override Freezable CreateInstanceCore() => new BezierEase();
}

/// <summary>Scheda di un documento con modifiche non salvate: brace:DocumentTab.IsModified="True" su un TabItem.</summary>
public static class DocumentTab
{
    public static readonly DependencyProperty IsModifiedProperty = DependencyProperty.RegisterAttached(
        "IsModified", typeof(bool), typeof(DocumentTab), new PropertyMetadata(false));

    public static bool GetIsModified(DependencyObject element) => (bool)element.GetValue(IsModifiedProperty);

    public static void SetIsModified(DependencyObject element, bool value) => element.SetValue(IsModifiedProperty, value);
}
