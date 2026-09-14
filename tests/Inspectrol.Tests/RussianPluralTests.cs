using Inspectrol.Core.Formatting;

namespace Inspectrol.Tests;

public class RussianPluralTests
{
    private const string Recordings = "запись;записи;записей";

    [Theory]
    [InlineData(0, "0 записей")]
    [InlineData(1, "1 запись")]
    [InlineData(2, "2 записи")]
    [InlineData(4, "4 записи")]
    [InlineData(5, "5 записей")]
    [InlineData(11, "11 записей")]
    [InlineData(12, "12 записей")]
    [InlineData(14, "14 записей")]
    [InlineData(21, "21 запись")]
    [InlineData(22, "22 записи")]
    [InlineData(111, "111 записей")]
    [InlineData(1001, "1001 запись")]
    public void Picks_the_form_that_matches_the_count(long count, string expected)
    {
        Assert.Equal(expected, RussianPlural.Format(count, Recordings));
    }

    [Fact]
    public void Rejects_forms_that_are_not_three_words()
    {
        Assert.Throws<ArgumentException>(() => RussianPlural.Format(1, "запись;записи"));
    }
}
