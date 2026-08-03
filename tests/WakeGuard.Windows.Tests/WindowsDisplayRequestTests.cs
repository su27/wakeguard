using WakeGuard.Windows;

namespace WakeGuard.Windows.Tests;

public sealed class WindowsDisplayRequestTests
{
    [Fact]
    public void InteractiveProcessCanSetAndClearDisplayRequest()
    {
        using var request = new WindowsDisplayRequest();

        request.SetActive(true);
        Assert.True(request.IsActive);

        request.SetActive(false);
        Assert.False(request.IsActive);
    }
}
