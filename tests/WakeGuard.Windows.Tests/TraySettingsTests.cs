using WakeGuard.Tray;

namespace WakeGuard.Windows.Tests;

public sealed class TraySettingsTests
{
    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void SettingsRoundTrip(bool startWithWindows, int languageValue)
    {
        var language = (UiLanguage)languageValue;
        var expected = new TraySettings(startWithWindows, language);

        var json = TraySettingsStore.Serialize(expected);
        var actual = TraySettingsStore.Deserialize(json);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void InvalidLanguageFallsBackToValidDefaults()
    {
        var settings = TraySettingsStore.Deserialize(
            """
            {
              "StartWithWindows": false,
              "Language": 99
            }
            """);

        Assert.True(settings.StartWithWindows);
        Assert.True(Enum.IsDefined(settings.Language));
    }
}
