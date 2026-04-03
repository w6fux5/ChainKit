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
    public void GetBytecode_ReturnsNonEmptyBytes()
    {
        var bytecode = Trc20Template.GetBytecode(DefaultOptions);

        Assert.NotNull(bytecode);
        Assert.NotEmpty(bytecode);
    }

    [Fact]
    public void GetBytecode_StartsWithValidEvmDeploymentPrefix()
    {
        var bytecode = Trc20Template.GetBytecode(DefaultOptions);

        // EVM deployment bytecode typically starts with 0x60 0x80 0x60 0x40 0x52
        // (PUSH1 0x80 PUSH1 0x40 MSTORE) which initializes the free memory pointer
        Assert.True(bytecode.Length > 5, "Bytecode too short to be a valid contract");
        Assert.Equal(0x60, bytecode[0]); // PUSH1
        Assert.Equal(0x80, bytecode[1]); // 0x80
        Assert.Equal(0x60, bytecode[2]); // PUSH1
        Assert.Equal(0x40, bytecode[3]); // 0x40
        Assert.Equal(0x52, bytecode[4]); // MSTORE
    }

    [Fact]
    public void GetBytecode_DifferentNameProducesDifferentBytecode()
    {
        var bytecodeA = Trc20Template.GetBytecode(DefaultOptions);
        var bytecodeB = Trc20Template.GetBytecode(DefaultOptions with { Name = "OtherToken" });

        Assert.NotEqual(bytecodeA, bytecodeB);
    }

    [Fact]
    public void GetBytecode_DifferentSymbolProducesDifferentBytecode()
    {
        var bytecodeA = Trc20Template.GetBytecode(DefaultOptions);
        var bytecodeB = Trc20Template.GetBytecode(DefaultOptions with { Symbol = "OTH" });

        Assert.NotEqual(bytecodeA, bytecodeB);
    }

    [Fact]
    public void GetBytecode_SameOptionsProduceIdenticalBytecode()
    {
        var bytecodeA = Trc20Template.GetBytecode(DefaultOptions);
        var bytecodeB = Trc20Template.GetBytecode(DefaultOptions);

        Assert.Equal(bytecodeA, bytecodeB);
    }

    [Fact]
    public void GetBytecode_IncludesConstructorArgsAfterBaseBytecode()
    {
        var bytecode = Trc20Template.GetBytecode(DefaultOptions);

        // Base bytecode is 4000 bytes; with constructor args appended it must be larger
        Assert.True(bytecode.Length > 4000,
            $"Expected bytecode larger than base 4000 bytes, got {bytecode.Length}");
    }

    [Fact]
    public void GetBytecode_MintableFlagDoesNotChangeBytecode()
    {
        // Mintable/Burnable flags only affect ABI, not bytecode
        var bytecodeA = Trc20Template.GetBytecode(DefaultOptions with { Mintable = true });
        var bytecodeB = Trc20Template.GetBytecode(DefaultOptions with { Mintable = false });

        Assert.Equal(bytecodeA, bytecodeB);
    }

    [Fact]
    public void GetBytecode_BurnableFlagDoesNotChangeBytecode()
    {
        var bytecodeA = Trc20Template.GetBytecode(DefaultOptions with { Burnable = true });
        var bytecodeB = Trc20Template.GetBytecode(DefaultOptions with { Burnable = false });

        Assert.Equal(bytecodeA, bytecodeB);
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
