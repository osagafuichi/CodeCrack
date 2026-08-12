using System.Globalization;

namespace CodeCrack.App.Core.Session;

/// The main window frame (L/T/W/H). Persisted as four invariant-culture strings.
public sealed record WindowFrame(double Left, double Top, double Width, double Height)
{
    private const string Key = "sessionWindowFrame";

    public bool IsValid => Width > 0 && Height > 0;

    /// Persist — but never persist a degenerate frame (width/height <= 0).
    public void Save(IKeyValueStore store)
    {
        if (!IsValid) return;
        store.SetStringArray(Key, new[]
        {
            Left.ToString(CultureInfo.InvariantCulture),
            Top.ToString(CultureInfo.InvariantCulture),
            Width.ToString(CultureInfo.InvariantCulture),
            Height.ToString(CultureInfo.InvariantCulture),
        });
    }

    public static WindowFrame? Load(IKeyValueStore store)
    {
        var a = store.GetStringArray(Key);
        if (a is not { Length: 4 }) return null;
        static double P(string s) => double.Parse(s, CultureInfo.InvariantCulture);
        var f = new WindowFrame(P(a[0]), P(a[1]), P(a[2]), P(a[3]));
        return f.IsValid ? f : null;
    }
}
