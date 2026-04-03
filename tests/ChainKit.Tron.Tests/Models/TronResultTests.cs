using ChainKit.Tron.Models;
using Xunit;

namespace ChainKit.Tron.Tests.Models;

public class TronResultTests
{
    [Fact]
    public void Ok_SetsSuccessAndData()
    {
        var result = TronResult<string>.Ok("tx123");
        Assert.True(result.Success);
        Assert.Equal("tx123", result.Data);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Fail_SetsErrorCode()
    {
        var result = TronResult<string>.Fail(TronErrorCode.InsufficientBalance, "not enough TRX");
        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Contains("InsufficientBalance", result.Error!.Code);
        Assert.Equal("not enough TRX", result.Error.Message);
    }

    [Fact]
    public void Fail_WithNodeMessage_PreservesIt()
    {
        var result = TronResult<int>.Fail(TronErrorCode.ContractExecutionFailed, "failed", "CONTRACT_EXE_ERROR");
        Assert.Equal("CONTRACT_EXE_ERROR", result.Error!.RawMessage);
    }
}
