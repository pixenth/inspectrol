using System.Globalization;

namespace Inspectrol.Core.Formatting;

public static class RussianPlural
{
    // forms holds the noun for 1, for 2 to 4 and for 5 or more, separated by semicolons: "запись;записи;записей".
    public static string Format(long count, string forms) =>
        string.Create(CultureInfo.CurrentCulture, $"{count} {Form(count, forms)}");

    public static string Form(long count, string forms)
    {
        var words = forms.Split(';');
        if (words.Length != 3)
            throw new ArgumentException("Expected three forms separated by semicolons.", nameof(forms));

        var lastTwo = Math.Abs(count) % 100;
        var last = lastTwo % 10;

        if (lastTwo is >= 11 and <= 14)
            return words[2];

        return last switch
        {
            1 => words[0],
            >= 2 and <= 4 => words[1],
            _ => words[2],
        };
    }
}
