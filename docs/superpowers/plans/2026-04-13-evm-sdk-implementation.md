# ChainKit.Evm SDK Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add EVM-compatible chain support (Ethereum + Polygon) to ChainKit, refactoring shared crypto into Core.

**Architecture:** `ChainKit.Core` gains shared crypto (Keccak256, AbiEncoder, Mnemonic, TokenConverter). `ChainKit.Evm` follows the same per-chain pattern as `ChainKit.Tron` — Crypto/Protocol/Providers/Contracts/Watching layers with an `EvmClient` facade. No Nethereum dependency; RLP encoding is self-written.

**Tech Stack:** .NET 10, C#, xUnit, NSubstitute, NBitcoin.Secp256k1, Anvil (local EVM node for integration tests)

**Spec:** `docs/superpowers/specs/2026-04-13-evm-sdk-design.md`

---

## Phase 1: Core Refactoring

### Task 1: Move Keccak256 to Core

**Files:**
- Create: `src/ChainKit.Core/Crypto/Keccak256.cs`
- Delete: `src/ChainKit.Tron/Crypto/Keccak256.cs`
- Modify: `tests/ChainKit.Tron.Tests/Crypto/Keccak256Tests.cs` (update namespace)
- Create: `tests/ChainKit.Core.Tests/Crypto/Keccak256Tests.cs`

- [ ] **Step 1: Copy Keccak256 to Core with new namespace**

Create `src/ChainKit.Core/Crypto/Keccak256.cs`:

```csharp
namespace ChainKit.Core.Crypto;

/// <summary>
/// Keccak-256 hash function (pre-NIST variant, padding byte 0x01).
/// Used for Ethereum/Tron address generation and ABI function selectors.
/// This is NOT SHA3-256, which uses padding byte 0x06.
/// </summary>
public static class Keccak256
{
    // (exact same implementation as current src/ChainKit.Tron/Crypto/Keccak256.cs, lines 9-140)
    // Only the namespace changes from ChainKit.Tron.Crypto → ChainKit.Core.Crypto
}
```

- [ ] **Step 2: Add Core Keccak256 unit tests**

Create `tests/ChainKit.Core.Tests/Crypto/Keccak256Tests.cs`:

```csharp
using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;
using Xunit;

namespace ChainKit.Core.Tests.Crypto;

public class Keccak256Tests
{
    [Fact]
    public void Hash_EmptyInput_KnownVector()
    {
        var result = Keccak256.Hash(Array.Empty<byte>());
        Assert.Equal("c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470", result.ToHex());
    }

    [Fact]
    public void Hash_TransferSelector_KnownVector()
    {
        var input = System.Text.Encoding.UTF8.GetBytes("transfer(address,uint256)");
        var result = Keccak256.Hash(input);
        Assert.Equal("a9059cbb", result[..4].ToHex());
    }

    [Fact]
    public void Hash_AlwaysReturns32Bytes()
    {
        var result = Keccak256.Hash(new byte[] { 0x41 });
        Assert.Equal(32, result.Length);
    }
}
```

- [ ] **Step 3: Run Core tests**

Run: `dotnet test tests/ChainKit.Core.Tests --filter "FullyQualifiedName~Keccak256" -v n`
Expected: 3 tests PASS

- [ ] **Step 4: Delete Tron's Keccak256 and update references**

Delete `src/ChainKit.Tron/Crypto/Keccak256.cs`.

Update all files in `src/ChainKit.Tron/` that `using ChainKit.Tron.Crypto;` and reference `Keccak256` — add `using ChainKit.Core.Crypto;`. Key files:
- `src/ChainKit.Tron/Crypto/AbiEncoder.cs` — uses `Keccak256.Hash` in `EncodeFunctionSelector`
- `src/ChainKit.Tron/Crypto/TronAccount.cs` — uses `Keccak256.Hash` in `FromPrivateKey`

Update test file `tests/ChainKit.Tron.Tests/Crypto/Keccak256Tests.cs` — change `using ChainKit.Tron.Crypto;` to `using ChainKit.Core.Crypto;`.

- [ ] **Step 5: Run all tests to confirm no regression**

Run: `dotnet test --filter "Category!=Integration" -v n`
Expected: ALL tests PASS

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "refactor: move Keccak256 to ChainKit.Core.Crypto"
```

---

### Task 2: Move Mnemonic to Core

**Files:**
- Create: `src/ChainKit.Core/Crypto/Mnemonic.cs`
- Delete: `src/ChainKit.Tron/Crypto/Mnemonic.cs`
- Modify: `src/ChainKit.Core/ChainKit.Core.csproj` (add NBitcoin package)
- Modify: `tests/ChainKit.Tron.Tests/Crypto/MnemonicTests.cs` (update namespace)
- Create: `tests/ChainKit.Core.Tests/Crypto/MnemonicTests.cs`

- [ ] **Step 1: Add NBitcoin to Core.csproj**

Add to `src/ChainKit.Core/ChainKit.Core.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="NBitcoin" Version="9.0.5" />
</ItemGroup>
```

- [ ] **Step 2: Copy Mnemonic to Core with new namespace**

Create `src/ChainKit.Core/Crypto/Mnemonic.cs`:

```csharp
namespace ChainKit.Core.Crypto;

/// <summary>
/// BIP-39 mnemonic generation, seed derivation, and validation.
/// Chain-specific BIP-44 derivation paths are handled by each chain's Account class.
/// </summary>
public static class Mnemonic
{
    public static string Generate(int wordCount = 12)
    {
        var entropy = wordCount switch
        {
            12 => NBitcoin.WordCount.Twelve,
            24 => NBitcoin.WordCount.TwentyFour,
            _ => throw new ArgumentException("wordCount must be 12 or 24")
        };
        return new NBitcoin.Mnemonic(NBitcoin.Wordlist.English, entropy).ToString();
    }

    public static byte[] ToSeed(string mnemonic, string passphrase = "")
        => new NBitcoin.Mnemonic(mnemonic).DeriveSeed(passphrase);

    public static bool Validate(string mnemonic)
    {
        try { return new NBitcoin.Mnemonic(mnemonic).IsValidChecksum; }
        catch { return false; }
    }
}
```

- [ ] **Step 3: Add Core Mnemonic unit tests**

Create `tests/ChainKit.Core.Tests/Crypto/MnemonicTests.cs`:

```csharp
using ChainKit.Core.Crypto;
using Xunit;

namespace ChainKit.Core.Tests.Crypto;

public class MnemonicTests
{
    [Fact]
    public void Generate_12Words_Returns12Words()
    {
        var mnemonic = Mnemonic.Generate(12);
        Assert.Equal(12, mnemonic.Split(' ').Length);
    }

    [Fact]
    public void Generate_TwoCalls_DifferentResults()
    {
        Assert.NotEqual(Mnemonic.Generate(12), Mnemonic.Generate(12));
    }

    [Fact]
    public void Validate_ValidMnemonic_ReturnsTrue()
    {
        Assert.True(Mnemonic.Validate(Mnemonic.Generate(12)));
    }

    [Fact]
    public void Validate_InvalidMnemonic_ReturnsFalse()
    {
        Assert.False(Mnemonic.Validate("invalid words that are not a real mnemonic phrase at all ever"));
    }

    [Fact]
    public void ToSeed_SameMnemonic_SameSeed()
    {
        var m = Mnemonic.Generate(12);
        Assert.Equal(Mnemonic.ToSeed(m), Mnemonic.ToSeed(m));
    }

    [Fact]
    public void ToSeed_Returns64Bytes()
    {
        Assert.Equal(64, Mnemonic.ToSeed(Mnemonic.Generate(12)).Length);
    }
}
```

- [ ] **Step 4: Run Core tests**

Run: `dotnet test tests/ChainKit.Core.Tests --filter "FullyQualifiedName~Mnemonic" -v n`
Expected: 6 tests PASS

- [ ] **Step 5: Delete Tron's Mnemonic and update references**

Delete `src/ChainKit.Tron/Crypto/Mnemonic.cs`.

Update `src/ChainKit.Tron/Crypto/TronAccount.cs` — the `FromMnemonic` method uses `NBitcoin.Mnemonic` directly (not our wrapper), so no `using` change needed for the class itself, but update tests.

Update `tests/ChainKit.Tron.Tests/Crypto/MnemonicTests.cs` — change `using ChainKit.Tron.Crypto;` to `using ChainKit.Core.Crypto;`.

- [ ] **Step 6: Run all tests**

Run: `dotnet test --filter "Category!=Integration" -v n`
Expected: ALL tests PASS

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "refactor: move Mnemonic to ChainKit.Core.Crypto"
```

---

### Task 3: Extract AbiEncoder generic methods to Core

**Files:**
- Create: `src/ChainKit.Core/Crypto/AbiEncoder.cs`
- Modify: `src/ChainKit.Tron/Crypto/AbiEncoder.cs` → rename to `TronAbiEncoder.cs`
- Create: `tests/ChainKit.Core.Tests/Crypto/AbiEncoderTests.cs`
- Modify: `tests/ChainKit.Tron.Tests/Crypto/AbiEncoderTests.cs`

- [ ] **Step 1: Create Core AbiEncoder with generic methods**

Create `src/ChainKit.Core/Crypto/AbiEncoder.cs`:

```csharp
using System.Numerics;
using System.Text;
using ChainKit.Core.Extensions;

namespace ChainKit.Core.Crypto;

/// <summary>
/// Solidity ABI encoding/decoding utilities (chain-agnostic).
/// Address encoding is chain-specific — see TronAbiEncoder / EvmAbiEncoder.
/// </summary>
public static class AbiEncoder
{
    /// <summary>
    /// Computes the 4-byte function selector: Keccak256(signature)[..4].
    /// </summary>
    public static byte[] EncodeFunctionSelector(string signature)
        => Keccak256.Hash(Encoding.UTF8.GetBytes(signature))[..4];

    /// <summary>
    /// ABI-encodes a uint256 value as 32-byte big-endian, left-padded with zeros.
    /// </summary>
    public static byte[] EncodeUint256(BigInteger value)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        var padded = new byte[32];
        Buffer.BlockCopy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);
        return padded;
    }

    /// <summary>
    /// ABI-decodes a uint256 value from 32-byte big-endian data.
    /// </summary>
    public static BigInteger DecodeUint256(byte[] data)
    {
        var slice = data.Length > 32 ? data[^32..] : data;
        return new BigInteger(slice, isUnsigned: true, isBigEndian: true);
    }

    /// <summary>
    /// ABI-decodes a dynamic string (offset + length + UTF-8 data).
    /// </summary>
    public static string DecodeString(byte[] data)
    {
        if (data.Length < 64) return string.Empty;
        var length = (int)new BigInteger(data[32..64], isUnsigned: true, isBigEndian: true);
        return Encoding.UTF8.GetString(data, 64, length);
    }
}
```

- [ ] **Step 2: Write Core AbiEncoder tests**

Create `tests/ChainKit.Core.Tests/Crypto/AbiEncoderTests.cs`:

```csharp
using System.Numerics;
using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;
using Xunit;

namespace ChainKit.Core.Tests.Crypto;

public class AbiEncoderTests
{
    [Fact]
    public void EncodeFunctionSelector_Transfer()
    {
        var selector = AbiEncoder.EncodeFunctionSelector("transfer(address,uint256)");
        Assert.Equal("a9059cbb", selector.ToHex());
    }

    [Fact]
    public void EncodeFunctionSelector_BalanceOf()
    {
        var selector = AbiEncoder.EncodeFunctionSelector("balanceOf(address)");
        Assert.Equal("70a08231", selector.ToHex());
    }

    [Fact]
    public void EncodeUint256_SmallValue()
    {
        var encoded = AbiEncoder.EncodeUint256(new BigInteger(1));
        Assert.Equal(32, encoded.Length);
        Assert.Equal(1, encoded[31]);
        Assert.Equal(0, encoded[0]);
    }

    [Fact]
    public void DecodeUint256_Roundtrip()
    {
        var original = new BigInteger(123456789);
        var encoded = AbiEncoder.EncodeUint256(original);
        Assert.Equal(original, AbiEncoder.DecodeUint256(encoded));
    }

    [Fact]
    public void DecodeString_ValidData()
    {
        // ABI-encoded "USDT": offset(32) + length(32) + data(32)
        var strBytes = System.Text.Encoding.UTF8.GetBytes("USDT");
        var data = new byte[96];
        data[31] = 0x20; // offset = 32
        data[63] = 0x04; // length = 4
        Buffer.BlockCopy(strBytes, 0, data, 64, strBytes.Length);

        Assert.Equal("USDT", AbiEncoder.DecodeString(data));
    }

    [Fact]
    public void DecodeString_TooShort_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, AbiEncoder.DecodeString(new byte[32]));
    }
}
```

- [ ] **Step 3: Run Core AbiEncoder tests**

Run: `dotnet test tests/ChainKit.Core.Tests --filter "FullyQualifiedName~AbiEncoder" -v n`
Expected: 6 tests PASS

- [ ] **Step 4: Rename Tron's AbiEncoder to TronAbiEncoder**

Rename `src/ChainKit.Tron/Crypto/AbiEncoder.cs` to `src/ChainKit.Tron/Crypto/TronAbiEncoder.cs`.

Replace the file contents — keep only `EncodeAddress`, `DecodeAddress`, and the convenience methods that use addresses. Delegate generic methods to `Core.Crypto.AbiEncoder`:

```csharp
using System.Numerics;
using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;

namespace ChainKit.Tron.Crypto;

/// <summary>
/// Tron-specific ABI encoding — handles 41-prefix hex addresses.
/// Generic ABI methods delegate to ChainKit.Core.Crypto.AbiEncoder.
/// </summary>
public static class TronAbiEncoder
{
    public static byte[] EncodeAddress(string hexAddress)
    {
        var addr = hexAddress;
        if (addr.StartsWith("41") && addr.Length == 42)
            addr = addr[2..];
        var bytes = addr.FromHex();
        var padded = new byte[32];
        Buffer.BlockCopy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);
        return padded;
    }

    public static string DecodeAddress(byte[] data)
    {
        var slice = data.Length > 32 ? data[^32..] : data;
        return "41" + slice[12..].ToHex();
    }

    public static byte[] EncodeTransfer(string toHex, BigInteger amount)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("transfer(address,uint256)");
        var addr = EncodeAddress(toHex);
        var amt = AbiEncoder.EncodeUint256(amount);
        return ConcatBytes(selector, addr, amt);
    }

    public static byte[] EncodeBalanceOf(string addressHex)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("balanceOf(address)");
        var addr = EncodeAddress(addressHex);
        return ConcatBytes(selector, addr);
    }

    public static byte[] EncodeApprove(string spenderHex, BigInteger amount)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("approve(address,uint256)");
        var spender = EncodeAddress(spenderHex);
        var amt = AbiEncoder.EncodeUint256(amount);
        return ConcatBytes(selector, spender, amt);
    }

    public static byte[] EncodeMint(string toHex, BigInteger amount)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("mint(address,uint256)");
        var addr = EncodeAddress(toHex);
        var amt = AbiEncoder.EncodeUint256(amount);
        return ConcatBytes(selector, addr, amt);
    }

    public static byte[] EncodeBurn(BigInteger amount)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("burn(uint256)");
        var amt = AbiEncoder.EncodeUint256(amount);
        return ConcatBytes(selector, amt);
    }

    public static byte[] EncodeBurnFrom(string fromHex, BigInteger amount)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("burnFrom(address,uint256)");
        var addr = EncodeAddress(fromHex);
        var amt = AbiEncoder.EncodeUint256(amount);
        return ConcatBytes(selector, addr, amt);
    }

    public static byte[] EncodeAllowance(string ownerHex, string spenderHex)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("allowance(address,address)");
        var owner = EncodeAddress(ownerHex);
        var spender = EncodeAddress(spenderHex);
        return ConcatBytes(selector, owner, spender);
    }

    private static byte[] ConcatBytes(params byte[][] arrays)
    {
        var totalLength = arrays.Sum(a => a.Length);
        var result = new byte[totalLength];
        var offset = 0;
        foreach (var arr in arrays)
        {
            Buffer.BlockCopy(arr, 0, result, offset, arr.Length);
            offset += arr.Length;
        }
        return result;
    }
}
```

- [ ] **Step 5: Update all Tron references from `AbiEncoder` to `TronAbiEncoder`**

Update every file in `src/ChainKit.Tron/` that references the old `AbiEncoder` class name. Key files:
- `src/ChainKit.Tron/Contracts/Trc20Contract.cs` — change `AbiEncoder.EncodeTransfer` → `TronAbiEncoder.EncodeTransfer`, etc.
- `src/ChainKit.Tron/Contracts/TokenInfoCache.cs` — change `AbiEncoder.DecodeString` → `AbiEncoder.DecodeString` (this now comes from Core via `using ChainKit.Core.Crypto;`), change `AbiEncoder.DecodeUint256` → `AbiEncoder.DecodeUint256` (same)
- `src/ChainKit.Tron/Contracts/Trc20Template.cs` — if it uses AbiEncoder methods
- `src/ChainKit.Tron/Watching/TronTransactionWatcher.cs` — if it uses AbiEncoder

For files that only use `DecodeString`/`DecodeUint256`/`EncodeFunctionSelector` (generic methods), add `using ChainKit.Core.Crypto;` and use `AbiEncoder` directly.

For files that use `EncodeAddress`/`DecodeAddress`/`EncodeTransfer`/etc. (Tron-specific), change to `TronAbiEncoder`.

- [ ] **Step 6: Update Tron AbiEncoder tests**

Update `tests/ChainKit.Tron.Tests/Crypto/AbiEncoderTests.cs`:
- Rename class to `TronAbiEncoderTests`
- Change references from `AbiEncoder` to `TronAbiEncoder` for address-specific tests
- Change references from `AbiEncoder` to `ChainKit.Core.Crypto.AbiEncoder` for generic tests (or add using)
- Add `using ChainKit.Core.Crypto;` for generic methods

- [ ] **Step 7: Run all tests**

Run: `dotnet test --filter "Category!=Integration" -v n`
Expected: ALL tests PASS

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "refactor: extract generic AbiEncoder to Core, rename Tron's to TronAbiEncoder"
```

---

### Task 4: Extract TokenConverter to Core

**Files:**
- Create: `src/ChainKit.Core/Converters/TokenConverter.cs`
- Modify: `src/ChainKit.Tron/Crypto/TronConverter.cs`
- Create: `tests/ChainKit.Core.Tests/Converters/TokenConverterTests.cs`
- Modify: `tests/ChainKit.Tron.Tests/Crypto/TronConverterTests.cs`

- [ ] **Step 1: Create Core TokenConverter**

Create `src/ChainKit.Core/Converters/TokenConverter.cs`:

```csharp
using System.Numerics;

namespace ChainKit.Core.Converters;

/// <summary>
/// Chain-agnostic token amount conversion utilities.
/// Uses decimal loop multiplication (not Math.Pow) to avoid double precision loss.
/// </summary>
public static class TokenConverter
{
    /// <summary>
    /// Computes 10^exp using decimal multiplication to avoid double precision loss from Math.Pow.
    /// </summary>
    public static decimal DecimalPow10(int exp)
    {
        var result = 1m;
        for (int i = 0; i < exp; i++) result *= 10;
        return result;
    }

    /// <summary>
    /// Converts raw token amount to human-readable amount (divide by 10^decimals).
    /// Example: rawAmount=1000000, decimals=6 → 1.0
    /// </summary>
    public static decimal ToTokenAmount(BigInteger rawAmount, int decimals)
    {
        if (decimals <= 0) return (decimal)rawAmount;
        var divisor = BigInteger.Pow(10, decimals);
        var wholePart = BigInteger.DivRem(rawAmount, divisor, out var remainder);
        return (decimal)wholePart + (decimal)remainder / (decimal)divisor;
    }

    /// <summary>
    /// Converts human-readable amount to raw token amount (multiply by 10^decimals).
    /// Example: amount=1.0, decimals=6 → 1000000
    /// </summary>
    public static BigInteger ToRawAmount(decimal amount, int decimals)
    {
        if (decimals <= 0) return new BigInteger(amount);
        var multiplier = DecimalPow10(decimals);
        return new BigInteger(amount * multiplier);
    }
}
```

- [ ] **Step 2: Write Core TokenConverter tests**

Create `tests/ChainKit.Core.Tests/Converters/TokenConverterTests.cs`:

```csharp
using System.Numerics;
using ChainKit.Core.Converters;
using Xunit;

namespace ChainKit.Core.Tests.Converters;

public class TokenConverterTests
{
    [Fact]
    public void DecimalPow10_6_ReturnsOneMillion()
    {
        Assert.Equal(1_000_000m, TokenConverter.DecimalPow10(6));
    }

    [Fact]
    public void DecimalPow10_18_ReturnsCorrect()
    {
        Assert.Equal(1_000_000_000_000_000_000m, TokenConverter.DecimalPow10(18));
    }

    [Fact]
    public void ToTokenAmount_Usdt_6Decimals()
    {
        Assert.Equal(20.2m, TokenConverter.ToTokenAmount(new BigInteger(20_200_000), 6));
    }

    [Fact]
    public void ToTokenAmount_Eth_18Decimals()
    {
        var oneEth = BigInteger.Parse("1000000000000000000");
        Assert.Equal(1.0m, TokenConverter.ToTokenAmount(oneEth, 18));
    }

    [Fact]
    public void ToRawAmount_Roundtrip()
    {
        var original = 1.5m;
        var raw = TokenConverter.ToRawAmount(original, 6);
        Assert.Equal(original, TokenConverter.ToTokenAmount(raw, 6));
    }

    [Fact]
    public void ToTokenAmount_ZeroDecimals_ReturnsSameValue()
    {
        Assert.Equal(42m, TokenConverter.ToTokenAmount(new BigInteger(42), 0));
    }
}
```

- [ ] **Step 3: Run Core TokenConverter tests**

Run: `dotnet test tests/ChainKit.Core.Tests --filter "FullyQualifiedName~TokenConverter" -v n`
Expected: 6 tests PASS

- [ ] **Step 4: Update TronConverter to delegate to Core**

Replace `src/ChainKit.Tron/Crypto/TronConverter.cs`:

```csharp
using System.Numerics;
using ChainKit.Core.Converters;

namespace ChainKit.Tron.Crypto;

/// <summary>
/// Tron-specific unit conversions: Sun ↔ TRX.
/// Token amount conversions delegate to ChainKit.Core.Converters.TokenConverter.
/// </summary>
public static class TronConverter
{
    private const long SunPerTrx = 1_000_000;

    /// <summary>
    /// Sun 轉換為 TRX。1 TRX = 1,000,000 Sun。
    /// </summary>
    public static decimal SunToTrx(long sun) => (decimal)sun / SunPerTrx;

    /// <summary>
    /// TRX 轉換為 Sun。1 TRX = 1,000,000 Sun。
    /// </summary>
    /// <exception cref="OverflowException">金額超出 long 範圍。</exception>
    public static long TrxToSun(decimal trx) => checked((long)(trx * SunPerTrx));

    /// <summary>
    /// 將代幣原始值轉換為人類可讀金額。
    /// </summary>
    public static decimal ToTokenAmount(BigInteger rawAmount, int decimals)
        => TokenConverter.ToTokenAmount(rawAmount, decimals);

    /// <summary>
    /// 將人類可讀金額轉換為代幣原始值。
    /// </summary>
    public static BigInteger ToRawAmount(decimal amount, int decimals)
        => TokenConverter.ToRawAmount(amount, decimals);

    /// <summary>
    /// Computes 10^exp using decimal multiplication to avoid double precision loss.
    /// </summary>
    internal static decimal DecimalPow10(int exp)
        => TokenConverter.DecimalPow10(exp);
}
```

- [ ] **Step 5: Run all tests**

Run: `dotnet test --filter "Category!=Integration" -v n`
Expected: ALL tests PASS

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "refactor: extract TokenConverter to ChainKit.Core.Converters"
```

---

## Phase 2: EVM Project Scaffolding

### Task 5: Create ChainKit.Evm and ChainKit.Evm.Tests projects

**Files:**
- Create: `src/ChainKit.Evm/ChainKit.Evm.csproj`
- Create: `tests/ChainKit.Evm.Tests/ChainKit.Evm.Tests.csproj`
- Modify: `ChainKit.slnx`

- [ ] **Step 1: Create ChainKit.Evm project**

Create `src/ChainKit.Evm/ChainKit.Evm.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <RootNamespace>ChainKit.Evm</RootNamespace>
    <PackageId>W6fux5.ChainKit.Evm</PackageId>
    <Description>EVM-compatible blockchain SDK for ChainKit — Ethereum, Polygon, and other EVM chains</Description>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="ChainKit.Evm.Tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ChainKit.Core\ChainKit.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.5" />
    <PackageReference Include="NBitcoin.Secp256k1" Version="3.2.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create ChainKit.Evm.Tests project**

Create `tests/ChainKit.Evm.Tests/ChainKit.Evm.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\ChainKit.Evm\ChainKit.Evm.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="8.0.1">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="NSubstitute" Version="5.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Update solution file**

Replace `ChainKit.slnx`:

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/ChainKit.Core/ChainKit.Core.csproj" />
    <Project Path="src/ChainKit.Tron/ChainKit.Tron.csproj" />
    <Project Path="src/ChainKit.Evm/ChainKit.Evm.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/ChainKit.Core.Tests/ChainKit.Core.Tests.csproj" />
    <Project Path="tests/ChainKit.Tron.Tests/ChainKit.Tron.Tests.csproj" />
    <Project Path="tests/ChainKit.Evm.Tests/ChainKit.Evm.Tests.csproj" />
  </Folder>
  <Folder Name="/sandbox/">
    <Project Path="sandbox/ChainKit.Sandbox/ChainKit.Sandbox.csproj" />
  </Folder>
</Solution>
```

- [ ] **Step 4: Verify build**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: scaffold ChainKit.Evm and ChainKit.Evm.Tests projects"
```

---

### Task 6: EVM Models

**Files:**
- Create: `src/ChainKit.Evm/Models/EvmErrorCode.cs`
- Create: `src/ChainKit.Evm/Models/EvmResult.cs`
- Create: `src/ChainKit.Evm/Models/TransactionModels.cs`
- Create: `src/ChainKit.Evm/Models/AccountModels.cs`
- Create: `src/ChainKit.Evm/Models/WatcherModels.cs`
- Create: `tests/ChainKit.Evm.Tests/Models/EvmResultTests.cs`

- [ ] **Step 1: Create all model files**

Create the five model files as defined in the spec (Section 6). Each is a straightforward record/enum definition. See spec for exact code.

Key: `EvmResult<T>` must extend `ChainResult<T>` with the same pattern as `TronResult<T>`:

```csharp
using ChainKit.Core;

namespace ChainKit.Evm.Models;

public record EvmResult<T> : ChainResult<T>
{
    private EvmResult() { }

    public EvmErrorCode? ErrorCode { get; init; }

    public new static EvmResult<T> Ok(T data) => new()
    {
        Success = true,
        Data = data,
        Error = null
    };

    public static EvmResult<T> Fail(EvmErrorCode code, string message, string? rawMessage = null) => new()
    {
        Success = false,
        Data = default,
        ErrorCode = code,
        Error = new ChainError(code.ToString(), message, rawMessage)
    };

    public new static EvmResult<T> Fail(ChainError error) => new()
    {
        Success = false,
        Data = default,
        Error = error
    };
}
```

Note: `ITransaction.Timestamp` in Core is `DateTimeOffset`, so `EvmTransactionDetail.Timestamp` must also be `DateTimeOffset` (not `DateTime` as written in spec — fix this).

- [ ] **Step 2: Write EvmResult tests**

Create `tests/ChainKit.Evm.Tests/Models/EvmResultTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run tests and verify build**

Run: `dotnet test tests/ChainKit.Evm.Tests -v n`
Expected: 3 tests PASS

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat(evm): add Models layer — EvmResult, EvmErrorCode, transaction/account/watcher DTOs"
```

---

## Phase 3: EVM Crypto

### Task 7: RlpEncoder

**Files:**
- Create: `src/ChainKit.Evm/Protocol/RlpEncoder.cs`
- Create: `tests/ChainKit.Evm.Tests/Protocol/RlpEncoderTests.cs`

- [ ] **Step 1: Write RLP tests first (TDD)**

Create `tests/ChainKit.Evm.Tests/Protocol/RlpEncoderTests.cs`:

```csharp
using ChainKit.Core.Extensions;
using ChainKit.Evm.Protocol;
using Xunit;

namespace ChainKit.Evm.Tests.Protocol;

public class RlpEncoderTests
{
    // Ethereum RLP spec test vectors: https://ethereum.org/en/developers/docs/data-structures-and-encoding/rlp/

    [Fact]
    public void EncodeElement_EmptyBytes_Returns0x80()
    {
        var result = RlpEncoder.EncodeElement(Array.Empty<byte>());
        Assert.Equal("80", result.ToHex());
    }

    [Fact]
    public void EncodeElement_SingleByteLessThan0x80_ReturnsByteSelf()
    {
        // Single byte in [0x00, 0x7f] is its own RLP encoding
        var result = RlpEncoder.EncodeElement(new byte[] { 0x0f });
        Assert.Equal("0f", result.ToHex());
    }

    [Fact]
    public void EncodeElement_SingleByte0x80_Returns8180()
    {
        // 0x80 is >= 0x80, so needs length prefix
        var result = RlpEncoder.EncodeElement(new byte[] { 0x80 });
        Assert.Equal("8180", result.ToHex());
    }

    [Fact]
    public void EncodeElement_ShortString_Dog()
    {
        // "dog" = [0x64, 0x6f, 0x67], length=3, prefix=0x80+3=0x83
        var result = RlpEncoder.EncodeElement(System.Text.Encoding.ASCII.GetBytes("dog"));
        Assert.Equal("83646f67", result.ToHex());
    }

    [Fact]
    public void EncodeElement_55Bytes_ShortStringPrefix()
    {
        var data = new byte[55];
        Array.Fill(data, (byte)0xAA);
        var result = RlpEncoder.EncodeElement(data);
        Assert.Equal(0x80 + 55, result[0]); // prefix
        Assert.Equal(56, result.Length);     // 1 prefix + 55 data
    }

    [Fact]
    public void EncodeElement_56Bytes_LongStringPrefix()
    {
        var data = new byte[56];
        Array.Fill(data, (byte)0xBB);
        var result = RlpEncoder.EncodeElement(data);
        Assert.Equal(0xb8, result[0]);  // 0xb7 + 1 (length of length)
        Assert.Equal(56, result[1]);    // length = 56
        Assert.Equal(58, result.Length); // 2 prefix + 56 data
    }

    [Fact]
    public void EncodeList_Empty_Returns0xC0()
    {
        var result = RlpEncoder.EncodeList();
        Assert.Equal("c0", result.ToHex());
    }

    [Fact]
    public void EncodeList_CatDog()
    {
        // ["cat", "dog"] per Ethereum spec
        var cat = RlpEncoder.EncodeElement(System.Text.Encoding.ASCII.GetBytes("cat"));
        var dog = RlpEncoder.EncodeElement(System.Text.Encoding.ASCII.GetBytes("dog"));
        var result = RlpEncoder.EncodeList(cat, dog);
        Assert.Equal("c88363617483646f67", result.ToHex());
    }

    [Fact]
    public void EncodeElement_Zero_Returns0x80()
    {
        // Integer 0 encodes as empty byte array
        var result = RlpEncoder.EncodeElement(Array.Empty<byte>());
        Assert.Equal("80", result.ToHex());
    }

    [Fact]
    public void EncodeElement_Integer15_Returns0x0f()
    {
        var result = RlpEncoder.EncodeElement(new byte[] { 0x0f });
        Assert.Equal("0f", result.ToHex());
    }

    [Fact]
    public void EncodeElement_Integer1024_Returns820400()
    {
        // 1024 = 0x0400
        var result = RlpEncoder.EncodeElement(new byte[] { 0x04, 0x00 });
        Assert.Equal("820400", result.ToHex());
    }

    [Fact]
    public void EncodeList_Nested_SetTheoreticRepresentation()
    {
        // [ [], [[]], [ [], [[]] ] ] per Ethereum spec
        var empty = RlpEncoder.EncodeList();
        var innerNested = RlpEncoder.EncodeList(empty);
        var last = RlpEncoder.EncodeList(empty, innerNested);
        var result = RlpEncoder.EncodeList(empty, innerNested, last);
        Assert.Equal("c7c0c1c0c3c0c1c0", result.ToHex());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ChainKit.Evm.Tests --filter "FullyQualifiedName~RlpEncoder" -v n`
Expected: FAIL — `RlpEncoder` does not exist

- [ ] **Step 3: Implement RlpEncoder**

Create `src/ChainKit.Evm/Protocol/RlpEncoder.cs`:

```csharp
namespace ChainKit.Evm.Protocol;

/// <summary>
/// Recursive Length Prefix (RLP) encoding for Ethereum transaction serialization.
/// Spec: https://ethereum.org/en/developers/docs/data-structures-and-encoding/rlp/
/// </summary>
public static class RlpEncoder
{
    /// <summary>
    /// RLP-encodes a single byte array element.
    /// </summary>
    public static byte[] EncodeElement(byte[] data)
    {
        if (data.Length == 1 && data[0] < 0x80)
            return data;

        return EncodeWithPrefix(data, 0x80);
    }

    /// <summary>
    /// RLP-encodes a list of already-encoded items.
    /// </summary>
    public static byte[] EncodeList(params byte[][] items)
    {
        var totalLength = items.Sum(item => item.Length);
        var payload = new byte[totalLength];
        var offset = 0;
        foreach (var item in items)
        {
            Buffer.BlockCopy(item, 0, payload, offset, item.Length);
            offset += item.Length;
        }

        return EncodeWithPrefix(payload, 0xc0);
    }

    private static byte[] EncodeWithPrefix(byte[] data, byte shortBase)
    {
        if (data.Length <= 55)
        {
            var result = new byte[1 + data.Length];
            result[0] = (byte)(shortBase + data.Length);
            Buffer.BlockCopy(data, 0, result, 1, data.Length);
            return result;
        }

        var lengthBytes = EncodeLengthBytes(data.Length);
        var longBase = (byte)(shortBase + 55 + lengthBytes.Length);
        var output = new byte[1 + lengthBytes.Length + data.Length];
        output[0] = longBase;
        Buffer.BlockCopy(lengthBytes, 0, output, 1, lengthBytes.Length);
        Buffer.BlockCopy(data, 0, output, 1 + lengthBytes.Length, data.Length);
        return output;
    }

    private static byte[] EncodeLengthBytes(int length)
    {
        if (length < 256) return new byte[] { (byte)length };
        if (length < 65536) return new byte[] { (byte)(length >> 8), (byte)length };
        if (length < 16777216) return new byte[] { (byte)(length >> 16), (byte)(length >> 8), (byte)length };
        return new byte[] { (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length };
    }

    /// <summary>
    /// Helper: RLP-encodes a BigInteger as a minimal big-endian byte array.
    /// Strips leading zero bytes. Zero encodes as empty.
    /// </summary>
    public static byte[] EncodeUint(System.Numerics.BigInteger value)
    {
        if (value.IsZero) return EncodeElement(Array.Empty<byte>());
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        return EncodeElement(bytes);
    }

    /// <summary>
    /// Helper: RLP-encodes a long as a minimal big-endian byte array.
    /// </summary>
    public static byte[] EncodeLong(long value)
    {
        if (value == 0) return EncodeElement(Array.Empty<byte>());
        return EncodeUint(new System.Numerics.BigInteger(value));
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/ChainKit.Evm.Tests --filter "FullyQualifiedName~RlpEncoder" -v n`
Expected: ALL 12 tests PASS

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(evm): add RlpEncoder with Ethereum spec test vectors"
```

---

### Task 8: EvmAddress

**Files:**
- Create: `src/ChainKit.Evm/Crypto/EvmAddress.cs`
- Create: `tests/ChainKit.Evm.Tests/Crypto/EvmAddressTests.cs`

- [ ] **Step 1: Write EvmAddress tests (TDD)**

Create `tests/ChainKit.Evm.Tests/Crypto/EvmAddressTests.cs`:

```csharp
using ChainKit.Core.Extensions;
using ChainKit.Evm.Crypto;
using Xunit;

namespace ChainKit.Evm.Tests.Crypto;

public class EvmAddressTests
{
    [Theory]
    [InlineData("0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045", true)]  // vitalik.eth checksum
    [InlineData("0xd8da6bf26964af9d7eed9e03e53415d37aa96045", true)]  // all lowercase valid
    [InlineData("0xD8DA6BF26964AF9D7EED9E03E53415D37AA96045", true)]  // all uppercase valid
    [InlineData("0x123", false)]                                         // too short
    [InlineData("not an address", false)]
    [InlineData("", false)]
    public void IsValid_VariousCases(string address, bool expected)
    {
        Assert.Equal(expected, EvmAddress.IsValid(address));
    }

    [Fact]
    public void ToChecksumAddress_EIP55_KnownVector()
    {
        // EIP-55 test vector
        var input = "0xfb6916095ca1df60bb79ce92ce3ea74c37c5d359";
        var expected = "0xfB6916095ca1df60bB79Ce92cE3Ea74c37c5d359";
        Assert.Equal(expected, EvmAddress.ToChecksumAddress(input));
    }

    [Fact]
    public void ToChecksumAddress_AllLowercase_ReturnsChecksum()
    {
        var input = "0xd8da6bf26964af9d7eed9e03e53415d37aa96045";
        var result = EvmAddress.ToChecksumAddress(input);
        Assert.StartsWith("0x", result);
        Assert.Equal(42, result.Length);
    }

    [Fact]
    public void FromPublicKey_KnownVector()
    {
        // Well-known test: private key = 1 → known public key → known address
        var ecKey = NBitcoin.Secp256k1.ECPrivKey.Create(
            "0000000000000000000000000000000000000000000000000000000000000001".FromHex());
        var pubKey = ecKey.CreatePubKey();
        var uncompressed = new byte[65];
        pubKey.WriteToSpan(false, uncompressed, out _);

        var address = EvmAddress.FromPublicKey(uncompressed);
        Assert.StartsWith("0x", address);
        Assert.Equal(42, address.Length);
        // Private key 1's Ethereum address is 0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf
        Assert.Equal("0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf", address);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ChainKit.Evm.Tests --filter "FullyQualifiedName~EvmAddress" -v n`
Expected: FAIL

- [ ] **Step 3: Implement EvmAddress**

Create `src/ChainKit.Evm/Crypto/EvmAddress.cs`:

```csharp
using System.Text;
using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;

namespace ChainKit.Evm.Crypto;

/// <summary>
/// EVM address utilities: validation, EIP-55 checksum, and derivation from public key.
/// </summary>
public static class EvmAddress
{
    /// <summary>
    /// Validates that the string is a valid EVM address (0x + 40 hex characters).
    /// </summary>
    public static bool IsValid(string address)
    {
        if (string.IsNullOrEmpty(address)) return false;
        if (!address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return false;
        if (address.Length != 42) return false;
        return address[2..].All(c => Uri.IsHexDigit(c));
    }

    /// <summary>
    /// Converts an address to EIP-55 mixed-case checksum format.
    /// </summary>
    public static string ToChecksumAddress(string address)
    {
        var addr = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? address[2..].ToLowerInvariant()
            : address.ToLowerInvariant();

        var hash = Keccak256.Hash(Encoding.UTF8.GetBytes(addr)).ToHex();
        var result = new StringBuilder("0x", 42);

        for (int i = 0; i < 40; i++)
        {
            result.Append(hash[i] >= '8' ? char.ToUpperInvariant(addr[i]) : addr[i]);
        }

        return result.ToString();
    }

    /// <summary>
    /// Derives an EVM address from an uncompressed public key (65 bytes: 04 + x + y).
    /// Returns EIP-55 checksummed address.
    /// </summary>
    public static string FromPublicKey(byte[] uncompressedPublicKey)
    {
        var hash = Keccak256.Hash(uncompressedPublicKey[1..]);
        var addressBytes = hash[12..]; // last 20 bytes
        return ToChecksumAddress("0x" + addressBytes.ToHex());
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/ChainKit.Evm.Tests --filter "FullyQualifiedName~EvmAddress" -v n`
Expected: ALL tests PASS

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(evm): add EvmAddress — validation, EIP-55 checksum, public key derivation"
```

---

### Task 9: EvmSigner

**Files:**
- Create: `src/ChainKit.Evm/Crypto/EvmSigner.cs`
- Create: `tests/ChainKit.Evm.Tests/Crypto/EvmSignerTests.cs`

- [ ] **Step 1: Write EvmSigner tests (TDD)**

Create `tests/ChainKit.Evm.Tests/Crypto/EvmSignerTests.cs`:

```csharp
using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;
using ChainKit.Evm.Crypto;
using Xunit;

namespace ChainKit.Evm.Tests.Crypto;

public class EvmSignerTests
{
    private static readonly byte[] TestPrivateKey =
        "0000000000000000000000000000000000000000000000000000000000000001".FromHex();

    [Fact]
    public void SignTyped_Returns65Bytes()
    {
        var hash = Keccak256.Hash(new byte[] { 0x01, 0x02, 0x03 });
        var sig = EvmSigner.SignTyped(hash, TestPrivateKey);
        Assert.Equal(65, sig.Length);
    }

    [Fact]
    public void SignTyped_RecoveryIdIsZeroOrOne()
    {
        var hash = Keccak256.Hash(new byte[] { 0x01 });
        var sig = EvmSigner.SignTyped(hash, TestPrivateKey);
        Assert.True(sig[64] == 0 || sig[64] == 1);
    }

    [Fact]
    public void SignLegacy_VIncludesChainId()
    {
        var hash = Keccak256.Hash(new byte[] { 0x01 });
        long chainId = 1; // Ethereum mainnet
        var sig = EvmSigner.SignLegacy(hash, TestPrivateKey, chainId);
        var v = sig[64];
        // EIP-155: v = chainId * 2 + 35 + recoveryId
        // For chainId=1: v is 37 or 38
        Assert.True(v == 37 || v == 38, $"Expected v=37 or v=38, got v={v}");
    }

    [Fact]
    public void SignLegacy_DifferentChainId_DifferentV()
    {
        var hash = Keccak256.Hash(new byte[] { 0x01 });
        var sigEth = EvmSigner.SignLegacy(hash, TestPrivateKey, 1);   // Ethereum
        var sigPoly = EvmSigner.SignLegacy(hash, TestPrivateKey, 137); // Polygon
        // Different chain IDs produce different v values
        Assert.NotEqual(sigEth[64], sigPoly[64]);
    }

    [Fact]
    public void Verify_ValidSignature_ReturnsTrue()
    {
        var ecKey = NBitcoin.Secp256k1.ECPrivKey.Create(TestPrivateKey);
        var pubKey = ecKey.CreatePubKey();
        var compressed = new byte[33];
        pubKey.WriteToSpan(true, compressed, out _);

        var hash = Keccak256.Hash(new byte[] { 0x42 });
        var sig = EvmSigner.SignTyped(hash, TestPrivateKey);

        Assert.True(EvmSigner.Verify(hash, sig, compressed));
    }

    [Fact]
    public void Verify_WrongData_ReturnsFalse()
    {
        var ecKey = NBitcoin.Secp256k1.ECPrivKey.Create(TestPrivateKey);
        var pubKey = ecKey.CreatePubKey();
        var compressed = new byte[33];
        pubKey.WriteToSpan(true, compressed, out _);

        var hash = Keccak256.Hash(new byte[] { 0x42 });
        var wrongHash = Keccak256.Hash(new byte[] { 0x43 });
        var sig = EvmSigner.SignTyped(hash, TestPrivateKey);

        Assert.False(EvmSigner.Verify(wrongHash, sig, compressed));
    }
}
```

- [ ] **Step 2: Implement EvmSigner**

Create `src/ChainKit.Evm/Crypto/EvmSigner.cs`:

```csharp
using NBitcoin.Secp256k1;

namespace ChainKit.Evm.Crypto;

/// <summary>
/// ECDSA signing for EVM transactions using secp256k1.
/// Supports EIP-155 (legacy) and EIP-1559 (typed) transactions.
/// </summary>
public static class EvmSigner
{
    /// <summary>
    /// Signs a transaction hash for EIP-1559 (Type 2) transactions.
    /// Returns 65 bytes: r(32) + s(32) + v(1), where v = recoveryId (0 or 1).
    /// </summary>
    public static byte[] SignTyped(byte[] txHash, byte[] privateKey)
    {
        var ecKey = ECPrivKey.Create(privateKey);
        if (!ecKey.TrySignRecoverable(txHash, out var sig) || sig is null)
            throw new InvalidOperationException("Failed to create recoverable signature.");

        var output = new byte[65];
        sig.WriteToSpanCompact(output.AsSpan(0, 64), out var recId);
        output[64] = (byte)recId;
        return output;
    }

    /// <summary>
    /// Signs a transaction hash for legacy (EIP-155) transactions.
    /// Returns 65 bytes: r(32) + s(32) + v(1), where v = chainId * 2 + 35 + recoveryId.
    /// </summary>
    public static byte[] SignLegacy(byte[] txHash, byte[] privateKey, long chainId)
    {
        var ecKey = ECPrivKey.Create(privateKey);
        if (!ecKey.TrySignRecoverable(txHash, out var sig) || sig is null)
            throw new InvalidOperationException("Failed to create recoverable signature.");

        var output = new byte[65];
        sig.WriteToSpanCompact(output.AsSpan(0, 64), out var recId);
        output[64] = (byte)(chainId * 2 + 35 + recId);
        return output;
    }

    /// <summary>
    /// Verifies a signature against the expected public key.
    /// </summary>
    public static bool Verify(byte[] data, byte[] signature, byte[] publicKey)
    {
        if (signature.Length != 65) return false;
        var recId = signature[64] >= 27 ? signature[64] - 27 : signature[64];
        if (!SecpRecoverableECDSASignature.TryCreateFromCompact(signature.AsSpan(0, 64), recId, out var recSig))
            return false;
        if (!ECPubKey.TryRecover(Context.Instance, recSig, data, out var recoveredPubKey))
            return false;
        var recoveredBytes = new byte[33];
        recoveredPubKey.WriteToSpan(true, recoveredBytes, out _);
        return recoveredBytes.AsSpan().SequenceEqual(publicKey);
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/ChainKit.Evm.Tests --filter "FullyQualifiedName~EvmSigner" -v n`
Expected: ALL 6 tests PASS

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat(evm): add EvmSigner — EIP-155 legacy and EIP-1559 typed signing"
```

---

### Task 10: EvmAbiEncoder

**Files:**
- Create: `src/ChainKit.Evm/Crypto/EvmAbiEncoder.cs`
- Create: `tests/ChainKit.Evm.Tests/Crypto/EvmAbiEncoderTests.cs`

- [ ] **Step 1: Write tests (TDD)**

Create `tests/ChainKit.Evm.Tests/Crypto/EvmAbiEncoderTests.cs`:

```csharp
using System.Numerics;
using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;
using ChainKit.Evm.Crypto;
using Xunit;

namespace ChainKit.Evm.Tests.Crypto;

public class EvmAbiEncoderTests
{
    [Fact]
    public void EncodeAddress_0xPrefix_PadsTo32Bytes()
    {
        var address = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";
        var encoded = EvmAbiEncoder.EncodeAddress(address);
        Assert.Equal(32, encoded.Length);
        // First 12 bytes should be zero (left-pad)
        Assert.Equal(new byte[12], encoded[..12]);
    }

    [Fact]
    public void DecodeAddress_Returns0xChecksumAddress()
    {
        var address = "0xd8da6bf26964af9d7eed9e03e53415d37aa96045";
        var encoded = EvmAbiEncoder.EncodeAddress(address);
        var decoded = EvmAbiEncoder.DecodeAddress(encoded);
        Assert.StartsWith("0x", decoded);
        Assert.Equal(42, decoded.Length);
    }

    [Fact]
    public void EncodeTransfer_CorrectSelectorAndLength()
    {
        var to = "0xd8da6bf26964af9d7eed9e03e53415d37aa96045";
        var amount = new BigInteger(1000000);
        var encoded = EvmAbiEncoder.EncodeTransfer(to, amount);
        Assert.Equal(4 + 32 + 32, encoded.Length); // selector + address + uint256
        Assert.Equal("a9059cbb", encoded[..4].ToHex()); // transfer selector
    }

    [Fact]
    public void EncodeBalanceOf_CorrectSelector()
    {
        var addr = "0xd8da6bf26964af9d7eed9e03e53415d37aa96045";
        var encoded = EvmAbiEncoder.EncodeBalanceOf(addr);
        Assert.Equal(4 + 32, encoded.Length);
        Assert.Equal("70a08231", encoded[..4].ToHex()); // balanceOf selector
    }

    [Fact]
    public void EncodeApprove_CorrectSelector()
    {
        var spender = "0xd8da6bf26964af9d7eed9e03e53415d37aa96045";
        var encoded = EvmAbiEncoder.EncodeApprove(spender, BigInteger.One);
        Assert.Equal(4 + 32 + 32, encoded.Length);
        Assert.Equal("095ea7b3", encoded[..4].ToHex()); // approve selector
    }
}
```

- [ ] **Step 2: Implement EvmAbiEncoder**

Create `src/ChainKit.Evm/Crypto/EvmAbiEncoder.cs`:

```csharp
using System.Numerics;
using ChainKit.Core.Crypto;
using ChainKit.Core.Extensions;

namespace ChainKit.Evm.Crypto;

/// <summary>
/// EVM-specific ABI encoding — handles 0x-prefix hex addresses.
/// Generic ABI methods come from ChainKit.Core.Crypto.AbiEncoder.
/// </summary>
public static class EvmAbiEncoder
{
    /// <summary>
    /// ABI-encodes an EVM address (strips 0x, left-pads to 32 bytes).
    /// </summary>
    public static byte[] EncodeAddress(string address)
    {
        var hex = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? address[2..] : address;
        var bytes = hex.FromHex();
        var padded = new byte[32];
        Buffer.BlockCopy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);
        return padded;
    }

    /// <summary>
    /// ABI-decodes an address (takes last 20 bytes, returns 0x-prefix checksum).
    /// </summary>
    public static string DecodeAddress(byte[] data)
    {
        var slice = data.Length > 32 ? data[^32..] : data;
        return EvmAddress.ToChecksumAddress("0x" + slice[12..].ToHex());
    }

    public static byte[] EncodeTransfer(string toAddress, BigInteger amount)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("transfer(address,uint256)");
        var addr = EncodeAddress(toAddress);
        var amt = AbiEncoder.EncodeUint256(amount);
        return ConcatBytes(selector, addr, amt);
    }

    public static byte[] EncodeBalanceOf(string address)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("balanceOf(address)");
        var addr = EncodeAddress(address);
        return ConcatBytes(selector, addr);
    }

    public static byte[] EncodeApprove(string spender, BigInteger amount)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("approve(address,uint256)");
        var spAddr = EncodeAddress(spender);
        var amt = AbiEncoder.EncodeUint256(amount);
        return ConcatBytes(selector, spAddr, amt);
    }

    public static byte[] EncodeAllowance(string owner, string spender)
    {
        var selector = AbiEncoder.EncodeFunctionSelector("allowance(address,address)");
        var ownerAddr = EncodeAddress(owner);
        var spenderAddr = EncodeAddress(spender);
        return ConcatBytes(selector, ownerAddr, spenderAddr);
    }

    private static byte[] ConcatBytes(params byte[][] arrays)
    {
        var totalLength = arrays.Sum(a => a.Length);
        var result = new byte[totalLength];
        var offset = 0;
        foreach (var arr in arrays)
        {
            Buffer.BlockCopy(arr, 0, result, offset, arr.Length);
            offset += arr.Length;
        }
        return result;
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/ChainKit.Evm.Tests --filter "FullyQualifiedName~EvmAbiEncoder" -v n`
Expected: ALL 5 tests PASS

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat(evm): add EvmAbiEncoder — 0x address encoding, transfer/balanceOf/approve"
```

---

### Task 11: EvmAccount

**Files:**
- Create: `src/ChainKit.Evm/Crypto/EvmAccount.cs`
- Create: `tests/ChainKit.Evm.Tests/Crypto/EvmAccountTests.cs`

- [ ] **Step 1: Write tests (TDD)**

Create `tests/ChainKit.Evm.Tests/Crypto/EvmAccountTests.cs`:

```csharp
using ChainKit.Core.Extensions;
using ChainKit.Evm.Crypto;
using Xunit;

namespace ChainKit.Evm.Tests.Crypto;

public class EvmAccountTests
{
    [Fact]
    public void Create_ReturnsValidAccount()
    {
        var account = EvmAccount.Create();
        Assert.True(EvmAddress.IsValid(account.Address));
        Assert.Equal(33, account.PublicKey.Length);  // compressed
        Assert.Equal(32, account.PrivateKey.Length);
    }

    [Fact]
    public void FromPrivateKey_KnownVector()
    {
        var key = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        var account = EvmAccount.FromPrivateKey(key);
        Assert.Equal("0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf", account.Address);
    }

    [Fact]
    public void FromPrivateKey_TwoCallsSameKey_SameAddress()
    {
        var key = "0000000000000000000000000000000000000000000000000000000000000002".FromHex();
        var a1 = EvmAccount.FromPrivateKey(key);
        var a2 = EvmAccount.FromPrivateKey(key);
        Assert.Equal(a1.Address, a2.Address);
    }

    [Fact]
    public void FromMnemonic_ReturnsValidAccount()
    {
        var mnemonic = ChainKit.Core.Crypto.Mnemonic.Generate(12);
        var account = EvmAccount.FromMnemonic(mnemonic);
        Assert.True(EvmAddress.IsValid(account.Address));
    }

    [Fact]
    public void FromMnemonic_DifferentIndex_DifferentAddress()
    {
        var mnemonic = ChainKit.Core.Crypto.Mnemonic.Generate(12);
        var a0 = EvmAccount.FromMnemonic(mnemonic, 0);
        var a1 = EvmAccount.FromMnemonic(mnemonic, 1);
        Assert.NotEqual(a0.Address, a1.Address);
    }

    [Fact]
    public void Dispose_ZerosPrivateKey()
    {
        var key = "0000000000000000000000000000000000000000000000000000000000000003".FromHex();
        var account = EvmAccount.FromPrivateKey(key);
        account.Dispose();
        Assert.All(account.PrivateKey, b => Assert.Equal(0, b));
    }

    [Fact]
    public void ImplementsIAccount()
    {
        var account = EvmAccount.Create();
        ChainKit.Core.IAccount iAccount = account;
        Assert.NotNull(iAccount.Address);
        Assert.NotNull(iAccount.PublicKey);
    }
}
```

- [ ] **Step 2: Implement EvmAccount**

Create `src/ChainKit.Evm/Crypto/EvmAccount.cs`:

```csharp
using System.Security.Cryptography;
using ChainKit.Core;
using NBitcoin;

namespace ChainKit.Evm.Crypto;

/// <summary>
/// EVM account — secp256k1 key pair with EIP-55 checksummed address.
/// BIP-44 derivation path: m/44'/60'/0'/0/{index}
/// </summary>
public sealed class EvmAccount : IAccount, IDisposable
{
    public string Address { get; }
    public byte[] PublicKey { get; }
    public byte[] PrivateKey { get; }

    private EvmAccount(byte[] privateKey, byte[] publicKey, string address)
    {
        PrivateKey = privateKey;
        PublicKey = publicKey;
        Address = address;
    }

    /// <summary>Zeroes private key material from memory.</summary>
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(PrivateKey);
    }

    public static EvmAccount Create()
        => FromPrivateKey(RandomNumberGenerator.GetBytes(32));

    public static EvmAccount FromPrivateKey(byte[] privateKey)
    {
        var ecKey = NBitcoin.Secp256k1.ECPrivKey.Create(privateKey);
        var pubKey = ecKey.CreatePubKey();

        var uncompressed = new byte[65];
        pubKey.WriteToSpan(false, uncompressed, out _);

        var address = EvmAddress.FromPublicKey(uncompressed);

        var compressed = new byte[33];
        pubKey.WriteToSpan(true, compressed, out _);

        return new EvmAccount(privateKey, compressed, address);
    }

    public static EvmAccount FromMnemonic(string mnemonic, int index = 0)
    {
        var m = new NBitcoin.Mnemonic(mnemonic);
        var seed = m.DeriveSeed();
        var masterKey = ExtKey.CreateFromSeed(seed);
        var derived = masterKey.Derive(new KeyPath($"m/44'/60'/0'/0/{index}"));
        return FromPrivateKey(derived.PrivateKey.ToBytes());
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/ChainKit.Evm.Tests --filter "FullyQualifiedName~EvmAccount" -v n`
Expected: ALL 7 tests PASS

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat(evm): add EvmAccount — key management, BIP-44 m/44'/60', EIP-55 address"
```

---

## Phase 4: EVM Provider + Protocol

### Task 12: IEvmProvider, EvmNetwork, EvmHttpProvider

**Files:**
- Create: `src/ChainKit.Evm/Providers/IEvmProvider.cs`
- Create: `src/ChainKit.Evm/Providers/EvmNetwork.cs`
- Create: `src/ChainKit.Evm/Providers/EvmHttpProvider.cs`
- Create: `tests/ChainKit.Evm.Tests/Providers/EvmHttpProviderTests.cs`

- [ ] **Step 1: Create IEvmProvider interface**

Create `src/ChainKit.Evm/Providers/IEvmProvider.cs` — exact interface from spec Section 5.

- [ ] **Step 2: Create EvmNetwork**

Create `src/ChainKit.Evm/Providers/EvmNetwork.cs` — exact code from spec Section 5.

- [ ] **Step 3: Implement EvmHttpProvider**

Create `src/ChainKit.Evm/Providers/EvmHttpProvider.cs` — JSON-RPC 2.0 client. Core pattern:

```csharp
using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChainKit.Evm.Providers;

public sealed class EvmHttpProvider : IEvmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _rpcUrl;
    private readonly ILogger<EvmHttpProvider> _logger;
    private long _requestId;

    public EvmHttpProvider(string rpcUrl, ILogger<EvmHttpProvider>? logger = null)
    {
        _rpcUrl = rpcUrl ?? throw new ArgumentNullException(nameof(rpcUrl));
        _httpClient = new HttpClient();
        _logger = logger ?? NullLogger<EvmHttpProvider>.Instance;
    }

    public EvmHttpProvider(EvmNetworkConfig network, ILogger<EvmHttpProvider>? logger = null)
        : this(network.RpcUrl, logger) { }

    private async Task<JsonElement> RpcAsync(string method, object[]? parameters = null, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _requestId);
        var request = new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters ?? Array.Empty<object>(),
            id
        };

        var json = JsonSerializer.Serialize(request);
        _logger.LogDebug("RPC request: {Method} id={Id}", method, id);

        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(_rpcUrl, content, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        if (doc.RootElement.TryGetProperty("error", out var error))
        {
            var msg = error.GetProperty("message").GetString() ?? "RPC error";
            throw new InvalidOperationException($"JSON-RPC error: {msg}");
        }

        return doc.RootElement.GetProperty("result").Clone();
    }

    // Each method implementation follows this pattern:
    public async Task<BigInteger> GetBalanceAsync(string address, CancellationToken ct = default)
    {
        var result = await RpcAsync("eth_getBalance", new object[] { address, "latest" }, ct);
        return ParseHexBigInteger(result.GetString()!);
    }

    // ... (implement all IEvmProvider methods following the same RpcAsync pattern)
    // Key hex parsing helper:
    private static BigInteger ParseHexBigInteger(string hex)
    {
        var clean = hex.StartsWith("0x") ? hex[2..] : hex;
        if (clean.Length == 0) return BigInteger.Zero;
        return BigInteger.Parse("0" + clean, System.Globalization.NumberStyles.HexNumber);
    }

    private static long ParseHexLong(string hex)
    {
        var clean = hex.StartsWith("0x") ? hex[2..] : hex;
        return Convert.ToInt64(clean, 16);
    }

    public void Dispose() => _httpClient.Dispose();
}
```

Implement all `IEvmProvider` methods using the `RpcAsync` helper. Each method maps to an Ethereum JSON-RPC method:
- `GetBalanceAsync` → `eth_getBalance`
- `GetTransactionCountAsync` → `eth_getTransactionCount`
- `GetCodeAsync` → `eth_getCode`
- `GetBlockByNumberAsync` → `eth_getBlockByNumber`
- `GetBlockNumberAsync` → `eth_blockNumber`
- `SendRawTransactionAsync` → `eth_sendRawTransaction`
- `GetTransactionByHashAsync` → `eth_getTransactionByHash`
- `GetTransactionReceiptAsync` → `eth_getTransactionReceipt`
- `CallAsync` → `eth_call`
- `EstimateGasAsync` → `eth_estimateGas`
- `GetGasPriceAsync` → `eth_gasPrice`
- `GetEip1559FeesAsync` → `eth_getBlockByNumber("latest")` for baseFee + `eth_maxPriorityFeePerGas` for priorityFee
- `GetLogsAsync` → `eth_getLogs`

- [ ] **Step 4: Write unit tests for EvmHttpProvider (mock HttpClient is complex; test with NSubstitute on IEvmProvider for consumers)**

Create `tests/ChainKit.Evm.Tests/Providers/EvmHttpProviderTests.cs` — basic construction and validation tests.

- [ ] **Step 5: Verify build**

Run: `dotnet build src/ChainKit.Evm`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(evm): add IEvmProvider, EvmNetwork, EvmHttpProvider — JSON-RPC 2.0 client"
```

---

### Task 13: TransactionBuilder and TransactionUtils

**Files:**
- Create: `src/ChainKit.Evm/Protocol/TransactionBuilder.cs`
- Create: `src/ChainKit.Evm/Protocol/TransactionUtils.cs`
- Create: `tests/ChainKit.Evm.Tests/Protocol/TransactionBuilderTests.cs`

- [ ] **Step 1: Write TransactionBuilder tests (TDD)**

Test with known Ethereum transaction vectors. Key test: build an EIP-1559 unsigned tx, verify the RLP output matches expected bytes.

- [ ] **Step 2: Implement TransactionBuilder**

Create `src/ChainKit.Evm/Protocol/TransactionBuilder.cs` — implements `BuildEip1559` and `BuildLegacy` as defined in spec Section 4. Uses `RlpEncoder` for serialization.

Key implementation detail for EIP-1559:
```csharp
// Unsigned (for signing hash): 0x02 || RLP([chainId, nonce, maxPriorityFee, maxFee, gasLimit, to, value, data, accessList])
// Signed: 0x02 || RLP([chainId, nonce, maxPriorityFee, maxFee, gasLimit, to, value, data, accessList, v, r, s])
```

- [ ] **Step 3: Implement TransactionUtils**

Create `src/ChainKit.Evm/Protocol/TransactionUtils.cs` — `ComputeSigningHash` (Keccak256 of unsigned tx), `ComputeTxHash` (Keccak256 of signed tx), and `SignTransaction` convenience method.

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/ChainKit.Evm.Tests --filter "FullyQualifiedName~TransactionBuilder" -v n`
Expected: ALL tests PASS

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(evm): add TransactionBuilder and TransactionUtils — EIP-1559 + legacy tx encoding"
```

---

## Phase 5: EVM Contracts

### Task 14: TokenInfoCache

**Files:**
- Create: `src/ChainKit.Evm/Contracts/TokenInfoCache.cs`
- Create: `tests/ChainKit.Evm.Tests/Contracts/TokenInfoCacheTests.cs`

- [ ] **Step 1: Write tests (TDD)**

Test three-layer cache: known token hits immediately, unknown token calls resolver, second call uses memory cache.

```csharp
using ChainKit.Evm.Contracts;
using ChainKit.Evm.Models;
using Xunit;

namespace ChainKit.Evm.Tests.Contracts;

public class TokenInfoCacheTests
{
    [Fact]
    public async Task GetOrResolveAsync_KnownToken_DoesNotCallResolver()
    {
        var cache = new TokenInfoCache();
        var resolverCalled = false;
        var info = await cache.GetOrResolveAsync(
            "0xdAC17F958D2ee523a2206206994597C13D831ec7", 1,
            _ => { resolverCalled = true; return Task.FromResult<TokenInfo?>(null); });
        Assert.False(resolverCalled);
        Assert.Equal("USDT", info!.Symbol);
    }

    [Fact]
    public async Task GetOrResolveAsync_UnknownToken_CallsResolverAndCaches()
    {
        var cache = new TokenInfoCache();
        var callCount = 0;
        var info = await cache.GetOrResolveAsync(
            "0x1234567890abcdef1234567890abcdef12345678", 1,
            _ => { callCount++; return Task.FromResult<TokenInfo?>(new TokenInfo("0x1234...", "TEST", "TST", 18, default, null)); });
        Assert.Equal(1, callCount);
        Assert.Equal("TST", info!.Symbol);

        // Second call should NOT invoke resolver
        await cache.GetOrResolveAsync("0x1234567890abcdef1234567890abcdef12345678", 1,
            _ => { callCount++; return Task.FromResult<TokenInfo?>(null); });
        Assert.Equal(1, callCount); // still 1
    }
}
```

- [ ] **Step 2: Implement TokenInfoCache**

Create `src/ChainKit.Evm/Contracts/TokenInfoCache.cs` — three-layer cache with `ConcurrentDictionary`, keyed by `{chainId}:{normalizedAddress}`. Include known tokens for Ethereum mainnet and Polygon mainnet (USDT, USDC).

- [ ] **Step 3: Run tests and commit**

```bash
git add -A && git commit -m "feat(evm): add TokenInfoCache — three-layer ERC20 metadata cache"
```

---

### Task 15: Erc20Contract

**Files:**
- Create: `src/ChainKit.Evm/Contracts/Erc20Contract.cs`
- Create: `tests/ChainKit.Evm.Tests/Contracts/Erc20ContractTests.cs`

- [ ] **Step 1: Write tests (TDD with NSubstitute mock IEvmProvider)**

Test read-only methods (name, symbol, decimals, balanceOf, GetTokenInfo) and write methods (transfer, approve). Follow the same mock pattern as `Trc20ContractTests`.

- [ ] **Step 2: Implement Erc20Contract**

Follow the `Trc20Contract` pattern. Key differences:
- Read-only calls use `CallAsync` (not `TriggerConstantContract`)
- Write calls build tx internally: ABI encode → estimateGas → getEip1559Fees → getNonce → buildTx → sign → sendRawTransaction
- No `ownerAddress` needed for read-only calls
- Address encoding uses `EvmAbiEncoder` (0x prefix)

- [ ] **Step 3: Run tests and commit**

```bash
git add -A && git commit -m "feat(evm): add Erc20Contract — read/write ERC20 operations with Result pattern"
```

---

## Phase 6: EVM Watching

### Task 16: IEvmBlockStream and PollingBlockStream

**Files:**
- Create: `src/ChainKit.Evm/Watching/IEvmBlockStream.cs`
- Create: `src/ChainKit.Evm/Watching/PollingBlockStream.cs`
- Create: `tests/ChainKit.Evm.Tests/Watching/PollingBlockStreamTests.cs`

- [ ] **Step 1: Create IEvmBlockStream interface**

```csharp
using ChainKit.Evm.Models;

namespace ChainKit.Evm.Watching;

public interface IEvmBlockStream : IAsyncDisposable
{
    IAsyncEnumerable<EvmBlock> GetBlocksAsync(long startBlock, CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement PollingBlockStream**

Polls `IEvmProvider.GetBlockByNumberAsync` sequentially, incrementing block number. Sleeps `pollInterval` between polls (default 3s). Waits when caught up with chain tip.

- [ ] **Step 3: Write unit tests with mock provider**

Test that it yields blocks in order, waits when at chain tip, handles provider errors gracefully.

- [ ] **Step 4: Run tests and commit**

```bash
git add -A && git commit -m "feat(evm): add IEvmBlockStream and PollingBlockStream"
```

---

### Task 17: WebSocketBlockStream

**Files:**
- Create: `src/ChainKit.Evm/Watching/WebSocketBlockStream.cs`
- Create: `tests/ChainKit.Evm.Tests/Watching/WebSocketBlockStreamTests.cs`

- [ ] **Step 1: Implement WebSocketBlockStream**

Uses `System.Net.WebSockets.ClientWebSocket` to connect to an EVM node's WebSocket endpoint. Sends `eth_subscribe("newHeads")`, receives block notifications, fetches full block data via `IEvmProvider.GetBlockByNumberAsync`.

Features:
- Automatic reconnection with exponential backoff
- Gap detection: on reconnect, poll missed blocks from last processed + 1

- [ ] **Step 2: Write unit tests**

Basic tests: construction, that it implements `IEvmBlockStream`. Full WebSocket testing requires integration tests (Anvil with WebSocket).

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat(evm): add WebSocketBlockStream — eth_subscribe with auto-reconnect"
```

---

### Task 18: EvmTransactionWatcher

**Files:**
- Create: `src/ChainKit.Evm/Watching/EvmTransactionWatcher.cs`
- Create: `tests/ChainKit.Evm.Tests/Watching/EvmTransactionWatcherTests.cs`

- [ ] **Step 1: Write tests (TDD)**

Test with mock `IEvmBlockStream` and `IEvmProvider`:
- Native ETH received/sent events fire for watched addresses
- ERC20 Transfer log detection (topic `0xddf252ad...`)
- Address filtering (unwatched addresses ignored)
- Transaction confirmation (receipt status + N block confirmations)
- Transaction failure (receipt status 0x0)

- [ ] **Step 2: Implement EvmTransactionWatcher**

Follow `TronTransactionWatcher` architecture:
- `HashSet<string>` for watched addresses (case-insensitive)
- Process each block from stream: check native transfers + parse receipt logs for ERC20 Transfer events
- Confirmation loop: track unconfirmed tx hashes, periodically check receipt + block depth
- Six events: `OnNativeReceived`, `OnNativeSent`, `OnErc20Received`, `OnErc20Sent`, `OnTransactionConfirmed`, `OnTransactionFailed`
- Three-stage lifecycle: `StartAsync`, `StopAsync`, `DisposeAsync`

Key difference from Tron: ERC20 detection uses receipt logs (not input data parsing).

ERC20 Transfer event detection:
```csharp
// Transfer(address indexed from, address indexed to, uint256 value)
private static readonly string TransferTopic = "0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef";

// Parse from receipt logs:
// topics[0] = TransferTopic
// topics[1] = from address (32 bytes, left-padded)
// topics[2] = to address (32 bytes, left-padded)
// data = amount (uint256)
```

- [ ] **Step 3: Run tests and commit**

```bash
git add -A && git commit -m "feat(evm): add EvmTransactionWatcher — six events, log-based ERC20 detection"
```

---

## Phase 7: EVM Client Facade

### Task 19: EvmClient

**Files:**
- Create: `src/ChainKit.Evm/EvmClient.cs`
- Create: `tests/ChainKit.Evm.Tests/EvmClientTests.cs`

- [ ] **Step 1: Write tests (TDD with mock IEvmProvider)**

Test:
- `TransferAsync`: validate amount > 0, overflow protection, mock full tx flow
- `GetBalanceAsync`: converts Wei to ETH/POL correctly
- `GetTransactionDetailAsync`: merges tx + receipt data, determines status
- `GetErc20Contract`: returns configured `Erc20Contract` instance
- Error handling: provider failure → `EvmResult.Fail`

- [ ] **Step 2: Implement EvmClient**

Follow `TronClient` facade pattern. Key methods:
- `TransferAsync`: validate → getNonce → estimateGas → getFees → buildTx → sign → broadcast
- `GetBalanceAsync`: getBalance → convert Wei to native currency decimal
- `GetTransactionDetailAsync`: getTx + getReceipt → merge into `EvmTransactionDetail`
- `GetErc20Contract`: factory method returning `Erc20Contract` with shared `TokenCache`

```csharp
public sealed class EvmClient : IDisposable
{
    public IEvmProvider Provider { get; }
    public TokenInfoCache TokenCache { get; }
    public EvmNetworkConfig Network { get; }

    public EvmClient(IEvmProvider provider, EvmNetworkConfig network, ILogger<EvmClient>? logger = null)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Network = network ?? throw new ArgumentNullException(nameof(network));
        TokenCache = new TokenInfoCache(logger);
        _logger = logger ?? NullLogger<EvmClient>.Instance;
    }
    // ... (implement all methods)
}
```

- [ ] **Step 3: Run tests and commit**

```bash
git add -A && git commit -m "feat(evm): add EvmClient facade — transfer, balance, tx detail, ERC20 factory"
```

---

## Phase 8: Integration Tests (Anvil)

### Task 20: Anvil Integration Test Infrastructure

**Files:**
- Create: `tests/ChainKit.Evm.Tests/Integration/AnvilFixture.cs`
- Create: `tests/ChainKit.Evm.Tests/Integration/TransferIntegrationTests.cs`
- Create: `tests/ChainKit.Evm.Tests/Integration/Erc20IntegrationTests.cs`
- Create: `tests/ChainKit.Evm.Tests/Integration/WatcherIntegrationTests.cs`

- [ ] **Step 1: Create Anvil test fixture**

```csharp
using System.Diagnostics;
using ChainKit.Evm.Crypto;
using ChainKit.Evm.Providers;
using Xunit;

namespace ChainKit.Evm.Tests.Integration;

public class AnvilFixture : IAsyncLifetime
{
    private Process? _anvilProcess;
    public EvmHttpProvider Provider { get; private set; } = null!;
    public EvmNetworkConfig Network { get; } = new("http://127.0.0.1:8545", 31337, "Anvil", "ETH");

    // Anvil's default pre-funded accounts (deterministic)
    public EvmAccount Account0 { get; } = EvmAccount.FromPrivateKey(
        Convert.FromHexString("ac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80"));
    public EvmAccount Account1 { get; } = EvmAccount.FromPrivateKey(
        Convert.FromHexString("59c6995e998f97a5a0044966f0945389dc9e86dae88c7a8412f4603b6b78690d"));

    public async Task InitializeAsync()
    {
        _anvilProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "anvil",
                Arguments = "--silent",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };
        _anvilProcess.Start();
        Provider = new EvmHttpProvider(Network);

        // Wait for Anvil to be ready
        for (int i = 0; i < 30; i++)
        {
            try
            {
                await Provider.GetBlockNumberAsync();
                return;
            }
            catch { await Task.Delay(200); }
        }
        throw new TimeoutException("Anvil did not start within 6 seconds");
    }

    public Task DisposeAsync()
    {
        Provider.Dispose();
        if (_anvilProcess is { HasExited: false })
        {
            _anvilProcess.Kill();
            _anvilProcess.WaitForExit(3000);
        }
        _anvilProcess?.Dispose();
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Write transfer integration tests**

```csharp
[Trait("Category", "Integration")]
public class TransferIntegrationTests : IClassFixture<AnvilFixture>
{
    private readonly AnvilFixture _anvil;

    public TransferIntegrationTests(AnvilFixture anvil) => _anvil = anvil;

    [Fact]
    public async Task Transfer_1Eth_Success()
    {
        var client = new EvmClient(_anvil.Provider, _anvil.Network);
        var result = await client.TransferAsync(_anvil.Account0, _anvil.Account1.Address, 1.0m);
        Assert.True(result.Success);
        Assert.NotEmpty(result.Data!.TxId);
    }

    [Fact]
    public async Task GetBalance_AfterTransfer_Updated()
    {
        var client = new EvmClient(_anvil.Provider, _anvil.Network);
        var before = await client.GetBalanceAsync(_anvil.Account1.Address);
        await client.TransferAsync(_anvil.Account0, _anvil.Account1.Address, 0.5m);
        var after = await client.GetBalanceAsync(_anvil.Account1.Address);
        Assert.True(after.Data!.Balance > before.Data!.Balance);
    }
}
```

- [ ] **Step 3: Write ERC20 integration tests**

Deploy a test ERC20 contract on Anvil, then test transfer/balanceOf/approve through `Erc20Contract`.

- [ ] **Step 4: Write Watcher integration tests**

Start watcher on Anvil, send a transaction, verify events fire.

- [ ] **Step 5: Run integration tests (requires Anvil installed)**

Run: `dotnet test tests/ChainKit.Evm.Tests --filter "Category=Integration" -v n`
Expected: ALL tests PASS (if Anvil is available)

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "test(evm): add Anvil integration tests — transfer, ERC20, watcher"
```

---

## Phase 9: Sandbox Extension

### Task 21: Sandbox EVM Endpoints

**Files:**
- Modify: `sandbox/ChainKit.Sandbox/ChainKit.Sandbox.csproj` (add ChainKit.Evm reference)
- Create: `sandbox/ChainKit.Sandbox/Endpoints/EvmEndpoints.cs` (or add to existing Program.cs pattern)

- [ ] **Step 1: Add project reference**

Add to `sandbox/ChainKit.Sandbox/ChainKit.Sandbox.csproj`:
```xml
<ProjectReference Include="..\..\src\ChainKit.Evm\ChainKit.Evm.csproj" />
```

- [ ] **Step 2: Add EVM endpoints**

Add endpoints matching the spec (Section 11):
- `GET /evm/balance/{address}` → `EvmClient.GetBalanceAsync`
- `POST /evm/transfer` → `EvmClient.TransferAsync`
- `GET /evm/transaction/{txHash}` → `EvmClient.GetTransactionDetailAsync`
- `GET /evm/erc20/{contract}/info` → `Erc20Contract.GetTokenInfoAsync`
- `GET /evm/erc20/{contract}/balance/{address}` → `Erc20Contract.BalanceOfAsync`
- `POST /evm/erc20/{contract}/transfer` → `Erc20Contract.TransferAsync`
- `GET /evm/block-number` → `EvmClient.GetBlockNumberAsync`

All endpoints accept `?network=sepolia|ethereum-mainnet|polygon-mainnet|polygon-amoy` query parameter (default: sepolia).

- [ ] **Step 3: Verify sandbox builds and starts**

Run: `dotnet build sandbox/ChainKit.Sandbox`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat(sandbox): add EVM endpoints — balance, transfer, ERC20, block-number"
```

---

## Phase 10: Documentation and Cleanup

### Task 22: Update CLAUDE.md and documentation

**Files:**
- Modify: `CLAUDE.md`
- Modify: `sandbox/README.md`

- [ ] **Step 1: Update CLAUDE.md**

Add `ChainKit.Evm` to project structure section, add EVM development notes, update package tags, add Anvil test instructions.

- [ ] **Step 2: Update sandbox README**

Add EVM endpoint documentation to the existing sandbox README.

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "docs: update CLAUDE.md and sandbox README for EVM support"
```

---

## Phase 11: E2E Tests (Sepolia)

### Task 23: Sepolia E2E Tests

**Files:**
- Create: `tests/ChainKit.Evm.Tests/E2E/SepoliaTests.cs`

- [ ] **Step 1: Write Sepolia E2E tests**

```csharp
[Trait("Category", "E2E")]
public class SepoliaTests
{
    private readonly EvmHttpProvider _provider = new(EvmNetwork.Sepolia);
    private readonly EvmClient _client;

    public SepoliaTests()
    {
        _client = new EvmClient(_provider, EvmNetwork.Sepolia);
    }

    [Fact]
    public async Task GetBlockNumber_ReturnsPositive()
    {
        var result = await _client.GetBlockNumberAsync();
        Assert.True(result.Success);
        Assert.True(result.Data > 0);
    }

    [Fact]
    public async Task GetBalance_KnownAddress_ReturnsResult()
    {
        // Use a known Sepolia address (e.g., a faucet address)
        var result = await _client.GetBalanceAsync("0x0000000000000000000000000000000000000000");
        Assert.True(result.Success);
    }
}
```

- [ ] **Step 2: Run E2E tests**

Run: `dotnet test tests/ChainKit.Evm.Tests --filter "Category=E2E" -v n`
Expected: PASS (requires internet)

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "test(evm): add Sepolia E2E tests — block number, balance query"
```

---

## Final Verification

### Task 24: Full Build + Test Suite

- [ ] **Step 1: Full build**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

- [ ] **Step 2: All unit tests**

Run: `dotnet test --filter "Category!=Integration&Category!=E2E" -v n`
Expected: ALL PASS

- [ ] **Step 3: Integration tests (if Anvil available)**

Run: `dotnet test --filter "Category=Integration" -v n`
Expected: ALL PASS

- [ ] **Step 4: Coverage report**

Run: `rm -rf coverage-results coverage-report && dotnet test --filter "Category!=Integration&Category!=E2E" --collect:"XPlat Code Coverage" --results-directory ./coverage-results && reportgenerator -reports:"coverage-results/*/coverage.cobertura.xml" -targetdir:coverage-report -reporttypes:TextSummary && cat coverage-report/Summary.txt`

- [ ] **Step 5: Final commit**

```bash
git add -A && git commit -m "chore: final verification — all tests pass, coverage report generated"
```
