using ChainKit.Evm.Models;
using Xunit;

namespace ChainKit.Evm.Tests.Models;

public class EvmResultTests
{
    [Fact]
    public void Ok_SetsSuccessAndData()
    {
        var result = EvmResult<string>.Ok("0xabc123");
        Assert.True(result.Success);
        Assert.Equal("0xabc123", result.Data);
        Assert.Null(result.Error);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void Fail_SetsErrorCode()
    {
        var result = EvmResult<string>.Fail(EvmErrorCode.InsufficientBalance, "not enough ETH");
        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Equal(EvmErrorCode.InsufficientBalance, result.ErrorCode);
        Assert.Contains("InsufficientBalance", result.Error!.Code);
    }

    [Fact]
    public void Fail_WithRawMessage_PreservesIt()
    {
        var result = EvmResult<int>.Fail(EvmErrorCode.ContractReverted, "revert", "0x08c379a0...");
        Assert.Equal("0x08c379a0...", result.Error!.RawMessage);
    }
}
