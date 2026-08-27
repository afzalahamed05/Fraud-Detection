using FraudDetection.Api.Services;
using Xunit;

namespace FraudDetection.Api.Tests.Unit;

public class PasswordHasherTests
{
    [Fact]
    public void Verify_ReturnsTrue_ForCorrectPassword()
    {
        var salt = "EL9TY5QZpagCVdJBOAxCgA==";
        var hash = "2vie7/QfimSCRBZ4fKyS5BiL5cgnW7Fpm/tePA4yfFs=";

        Assert.True(PasswordHasher.Verify("admin123", salt, hash));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForIncorrectPassword()
    {
        var salt = "EL9TY5QZpagCVdJBOAxCgA==";
        var hash = "2vie7/QfimSCRBZ4fKyS5BiL5cgnW7Fpm/tePA4yfFs=";

        Assert.False(PasswordHasher.Verify("wrong-password", salt, hash));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForEmptyPassword()
    {
        var salt = "EL9TY5QZpagCVdJBOAxCgA==";
        var hash = "2vie7/QfimSCRBZ4fKyS5BiL5cgnW7Fpm/tePA4yfFs=";

        Assert.False(PasswordHasher.Verify(string.Empty, salt, hash));
    }
}
