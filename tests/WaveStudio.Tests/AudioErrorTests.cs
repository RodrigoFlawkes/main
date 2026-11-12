using System.Runtime.InteropServices;
using WaveStudio.Audio;
using Xunit;

namespace WaveStudio.Tests;

public sealed class AudioErrorTests
{
    [Theory]
    [InlineData(unchecked((int)0x80070490), "não está mais disponível")]
    [InlineData(unchecked((int)0x88890004), "desconectado")]
    [InlineData(unchecked((int)0x80004005), "0x80004005")]
    public void NativeErrorsHaveActionableMessages(int hresult, string expected)
    {
        var error = new COMException("internal message", hresult);
        Assert.Contains(expected, AudioError.Describe(error));
        Assert.DoesNotContain("internal message", AudioError.Describe(error));
    }

    [Fact]
    public void AccessDeniedExplainsPermissions()
    {
        Assert.Contains("permissões", AudioError.Describe(new UnauthorizedAccessException()));
    }
}
