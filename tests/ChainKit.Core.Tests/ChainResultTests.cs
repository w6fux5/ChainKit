using ChainKit.Core;
using Xunit;

namespace ChainKit.Core.Tests;

public class ChainResultTests
{
    [Fact]
    public void Ok_SetsSuccessAndData()
    {
        var result = ChainResult<string>.Ok("hello");
        Assert.True(result.Success);
        Assert.Equal("hello", result.Data);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Fail_SetsErrorAndNoData()
    {
        var error = new ChainError("ERR_TEST", "test error", null);
        var result = ChainResult<string>.Fail(error);
        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.NotNull(result.Error);
        Assert.Equal("ERR_TEST", result.Error.Code);
        Assert.Equal("test error", result.Error.Message);
    }

    [Fact]
    public void Fail_WithRawMessage_PreservesIt()
    {
        var error = new ChainError("ERR", "msg", "raw node output");
        var result = ChainResult<int>.Fail(error);
        Assert.Equal("raw node output", result.Error!.RawMessage);
    }
}
