using Radiacode.Core;

namespace Radiacode.Core.Tests;

public class NotificationAssemblerTests
{
    [Fact]
    public void ReassemblesPayloadSplitAcrossChunks()
    {
        var a = new NotificationAssembler();
        Assert.Null(a.Append([5, 0, 0, 0, 1, 2]));
        Assert.Null(a.Append([3, 4]));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, a.Append([5]));
        // ready for the next response
        Assert.Equal(new byte[] { 9 }, a.Append([1, 0, 0, 0, 9]));
    }

    [Fact]
    public void RejectsOverrunAndImplausibleLength()
    {
        var a = new NotificationAssembler();
        Assert.Null(a.Append([2, 0, 0, 0, 1]));
        Assert.Throws<ProtocolException>(() => a.Append([2, 3]));
        a.Reset();
        Assert.Throws<ProtocolException>(() => a.Append([0xFF, 0xFF, 0xFF, 0x7F]));
    }
}
