using Microsoft.Extensions.Options;
using TemporaryDiagnostics.AspNetCore.Security;
using TemporaryDiagnostics.Core.Configuration;
using TemporaryDiagnostics.Infrastructure.FileSystem;
using Xunit;

namespace TemporaryDiagnostics.Tests;

public class SecurityAndPathTests
{
    [Fact]
    public void Options_Validation_ShouldThrowOnEmptyOrShortKey()
    {
        var options = new TemporaryDiagnosticsOptions();

        Assert.Throws<ArgumentException>(() => options.AccessKey = "");
        Assert.Throws<ArgumentException>(() => options.AccessKey = "123");

        options.AccessKey = "ValidKey@123456";
        Assert.Equal("ValidKey@123456", options.AccessKey);
    }

    [Fact]
    public void Options_RoutePrefix_ShouldBeNormalized()
    {
        var options = new TemporaryDiagnosticsOptions
        {
            AccessKey = "ValidKey@123456",
            RoutePrefix = "my-diag/"
        };

        Assert.Equal("/my-diag", options.RoutePrefix);
    }

    [Fact]
    public void PathTraversalGuard_ShouldBlockUncAndNtfsStream()
    {
        var options = Options.Create(new TemporaryDiagnosticsOptions
        {
            AccessKey = "SecretKey@12345",
            AllowAllDrivesInDiagnostics = true
        });

        var guard = new PathTraversalGuard(options);

        var uncResult = guard.NormalizeAndValidate(@"\\192.168.1.1\share\test.txt");
        Assert.False(uncResult.IsSuccess);
        Assert.Equal(403, uncResult.StatusCode);

        var streamResult = guard.NormalizeAndValidate(@"C:\Windows\System32\notepad.exe::$DATA");
        Assert.False(streamResult.IsSuccess);
        Assert.Equal(403, streamResult.StatusCode);
    }

    [Fact]
    public void PathTraversalGuard_WhitelistMode_ShouldRejectExternalPaths()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "DiagTestWhitelist_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var options = Options.Create(new TemporaryDiagnosticsOptions
            {
                AccessKey = "SecretKey@12345",
                AllowAllDrivesInDiagnostics = false,
                AllowedFileRoots = new List<string> { tempRoot }
            });

            var guard = new PathTraversalGuard(options);

            // 允许白名单内路径
            var validFile = Path.Combine(tempRoot, "test.log");
            var validResult = guard.NormalizeAndValidate(validFile);
            Assert.True(validResult.IsSuccess);

            // 拒绝白名单外路径
            var outsidePath = Path.GetTempPath();
            if (outsidePath.EndsWith(Path.DirectorySeparatorChar))
            {
                outsidePath = outsidePath.Substring(0, outsidePath.Length - 1);
            }
            var invalidFile = Path.Combine(outsidePath, "evil.txt");
            var invalidResult = guard.NormalizeAndValidate(invalidFile);
            Assert.False(invalidResult.IsSuccess);
            Assert.Equal(403, invalidResult.StatusCode);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void DiagnosticSessionStore_AuthAndRateLimit_ShouldWork()
    {
        var options = Options.Create(new TemporaryDiagnosticsOptions
        {
            AccessKey = "SecretKey@12345",
            SessionTimeoutMinutes = 10
        });

        var store = new DiagnosticSessionStore(options);
        const string clientIp = "192.168.1.99";

        // 错误尝试 5 次
        for (var i = 0; i < 5; i++)
        {
            var failedToken = store.AuthenticateKey("WrongKey", clientIp);
            Assert.Null(failedToken);
        }

        // 第 6 次应被 RateLimit 阻断
        Assert.True(store.IsRateLimited(clientIp));

        // 其它客户端 IP 正常访问
        var validToken = store.AuthenticateKey("SecretKey@12345", "10.0.0.1");
        Assert.NotNull(validToken);
        Assert.True(store.ValidateToken(validToken, "10.0.0.1"));
    }
}
