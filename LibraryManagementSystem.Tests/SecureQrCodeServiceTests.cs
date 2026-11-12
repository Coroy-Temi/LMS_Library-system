using LibraryManagementSystem.Services;
using Xunit;

namespace LibraryManagementSystem.Tests;

public sealed class SecureQrCodeServiceTests
{
    [Fact]
    public void Token_RoundTripsOnlyInsideMatchingInstallation()
    {
        var service=new SecureQrCodeService(new MemorySecretStore(Enumerable.Range(1,32).Select(x=>(byte)x).ToArray(),"11111111111111111111111111111111"));
        var token=service.CreateBookCopyToken(42,"LF-000042-0001");
        var decoded=service.Decode(token);
        Assert.True(decoded.Success);
        Assert.Equal(42,decoded.Payload!.EntityId);
        Assert.Equal("LF-000042-0001",decoded.Payload.Code);
        Assert.StartsWith("LF1.",token);
        Assert.DoesNotContain("LF-000042-0001",token);
    }

    [Fact]
    public void Token_IsRejectedByAnotherInstallation()
    {
        var source=new SecureQrCodeService(new MemorySecretStore(new byte[32],"11111111111111111111111111111111"));
        var other=new SecureQrCodeService(new MemorySecretStore(Enumerable.Repeat((byte)9,32).ToArray(),"22222222222222222222222222222222"));
        Assert.False(other.Decode(source.CreateBookCopyToken(1,"LF-000001-0001")).Success);
    }

    [Fact]
    public void Token_TamperingIsDetected()
    {
        var service=new SecureQrCodeService(new MemorySecretStore(new byte[32],"11111111111111111111111111111111"));
        var token=service.CreateBookCopyToken(1,"LF-000001-0001");
        var tampered=token[..^1]+(token[^1]=='A'?'B':'A');
        Assert.False(service.Decode(tampered).Success);
    }

    private sealed class MemorySecretStore(byte[] key,string id):IInstallationSecretStore
    {
        public InstallationSecret GetOrCreate()=>new(key,id);
    }
}
