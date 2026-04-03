using System.Numerics;
using ChainKit.Tron.Contracts;
using ChainKit.Tron.Models;
using Xunit;

namespace ChainKit.Tron.Tests.Contracts;

public class Trc20TemplateTests
{
    private static readonly Trc20TokenOptions DefaultOptions = new(
        Name: "TestToken",
        Symbol: "TTK",
        Decimals: 18,
        InitialSupply: BigInteger.Parse("1000000000000000000000000"), // 1M tokens
        Mintable: true,
        Burnable: true);

    [Fact]
    public void GetBytecode_ThrowsNotImplementedException()
    {
        var ex = Assert.Throws<NotImplementedException>(() =>
            Trc20Template.GetBytecode(DefaultOptions));

        Assert.Contains("not yet compiled", ex.Message);
    }

    [Fact]
    public void GetAbi_ReturnsValidJson()
    {
        var abi = Trc20Template.GetAbi(DefaultOptions);

        Assert.StartsWith("[", abi);
        Assert.EndsWith("]", abi);
        Assert.Contains("\"name\"", abi);
        Assert.Contains("\"symbol\"", abi);
        Assert.Contains("\"decimals\"", abi);
        Assert.Contains("\"totalSupply\"", abi);
        Assert.Contains("\"balanceOf\"", abi);
        Assert.Contains("\"transfer\"", abi);
        Assert.Contains("\"approve\"", abi);
        Assert.Contains("\"allowance\"", abi);
    }

    [Fact]
    public void GetAbi_Mintable_IncludesMint()
    {
        var options = DefaultOptions with { Mintable = true, Burnable = false };
        var abi = Trc20Template.GetAbi(options);

        Assert.Contains("\"mint\"", abi);
        Assert.DoesNotContain("\"burn\"", abi);
    }

    [Fact]
    public void GetAbi_Burnable_IncludesBurnAndBurnFrom()
    {
        var options = DefaultOptions with { Mintable = false, Burnable = true };
        var abi = Trc20Template.GetAbi(options);

        Assert.DoesNotContain("\"mint\"", abi);
        Assert.Contains("\"burn\"", abi);
        Assert.Contains("\"burnFrom\"", abi);
    }

    [Fact]
    public void GetAbi_NotMintableNotBurnable_ExcludesBothMintAndBurn()
    {
        var options = DefaultOptions with { Mintable = false, Burnable = false };
        var abi = Trc20Template.GetAbi(options);

        Assert.DoesNotContain("\"mint\"", abi);
        Assert.DoesNotContain("\"burn\"", abi);
        Assert.DoesNotContain("\"burnFrom\"", abi);
    }
}
