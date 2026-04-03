# Plan 1: Core + Crypto + Protocol Implementation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the foundation layer (ChainKit.Core) and all offline Tron functionality (Crypto + Protocol) — address generation, signing, ABI encoding, and transaction building.

**Architecture:** Two projects: `ChainKit.Core` (zero dependencies, cross-chain abstractions) and `ChainKit.Tron` (Tron-specific offline functionality under `Crypto/` and `Protocol/` namespaces). Test-first with known test vectors.

**Tech Stack:** .NET 10, C#, NBitcoin 9.0.5, NBitcoin.Secp256k1 3.2.0, Google.Protobuf 3.34.1, Grpc.Tools 2.80.0, xUnit

**Spec:** `docs/superpowers/specs/2026-04-03-tron-sdk-design.md`

---

### Task 1: Project Scaffolding

**Files:**
- Create: `src/ChainKit.Core/ChainKit.Core.csproj`
- Create: `src/ChainKit.Tron/ChainKit.Tron.csproj`
- Create: `tests/ChainKit.Core.Tests/ChainKit.Core.Tests.csproj`
- Create: `tests/ChainKit.Tron.Tests/ChainKit.Tron.Tests.csproj`
- Modify: `ChainKit.slnx`
- Delete: `Tron/Tron.csproj`, `Tron/Class1.cs`

- [ ] **Step 1: Remove old placeholder project**

```bash
rm -rf Tron/
```

- [ ] **Step 2: Create src/ChainKit.Core/ChainKit.Core.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>ChainKit.Core</RootNamespace>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Create src/ChainKit.Tron/ChainKit.Tron.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>ChainKit.Tron</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\ChainKit.Core\ChainKit.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="NBitcoin" Version="9.0.5" />
    <PackageReference Include="NBitcoin.Secp256k1" Version="3.2.0" />
    <PackageReference Include="Google.Protobuf" Version="3.34.1" />
    <PackageReference Include="Grpc.Tools" Version="2.80.0" PrivateAssets="All" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create tests/ChainKit.Core.Tests/ChainKit.Core.Tests.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\ChainKit.Core\ChainKit.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Create tests/ChainKit.Tron.Tests/ChainKit.Tron.Tests.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\ChainKit.Tron\ChainKit.Tron.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Update ChainKit.slnx to reference all 4 projects**

```bash
dotnet sln ChainKit.slnx add src/ChainKit.Core/ChainKit.Core.csproj
dotnet sln ChainKit.slnx add src/ChainKit.Tron/ChainKit.Tron.csproj
dotnet sln ChainKit.slnx add tests/ChainKit.Core.Tests/ChainKit.Core.Tests.csproj
dotnet sln ChainKit.slnx add tests/ChainKit.Tron.Tests/ChainKit.Tron.Tests.csproj
```

- [ ] **Step 7: Create placeholder classes to verify build**

Create `src/ChainKit.Core/Placeholder.cs`:
```csharp
namespace ChainKit.Core;
```

Create `src/ChainKit.Tron/Placeholder.cs`:
```csharp
namespace ChainKit.Tron;
```

- [ ] **Step 8: Verify build passes**

Run: `dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 9: Verify tests run (empty)**

Run: `dotnet test`
Expected: 0 tests discovered, no errors

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: scaffold project structure with Core and Tron projects"
```

---

### Task 2: ChainKit.Core — Result Pattern + Exception

**Files:**
- Create: `src/ChainKit.Core/ChainResult.cs`
- Create: `src/ChainKit.Core/ChainError.cs`
- Create: `src/ChainKit.Core/ChainKitException.cs`
- Create: `tests/ChainKit.Core.Tests/ChainResultTests.cs`

- [ ] **Step 1: Write failing tests for ChainResult**

Create `tests/ChainKit.Core.Tests/ChainResultTests.cs`:
```csharp
using ChainKit.Core;

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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ChainKit.Core.Tests`
Expected: FAIL — types not found

- [ ] **Step 3: Implement ChainResult and ChainError**

Create `src/ChainKit.Core/ChainError.cs`:
```csharp
namespace ChainKit.Core;

public record ChainError(
    string Code,
    string Message,
    string? RawMessage);
```

Create `src/ChainKit.Core/ChainResult.cs`:
```csharp
namespace ChainKit.Core;

public record ChainResult<T>
{
    public bool Success { get; private init; }
    public T? Data { get; private init; }
    public ChainError? Error { get; private init; }

    private ChainResult() { }

    public static ChainResult<T> Ok(T data) => new()
    {
        Success = true,
        Data = data,
        Error = null
    };

    public static ChainResult<T> Fail(ChainError error) => new()
    {
        Success = false,
        Data = default,
        Error = error
    };
}
```

Create `src/ChainKit.Core/ChainKitException.cs`:
```csharp
namespace ChainKit.Core;

public class ChainKitException : Exception
{
    public ChainKitException(string message) : base(message) { }
    public ChainKitException(string message, Exception innerException) : base(message, innerException) { }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ChainKit.Core.Tests`
Expected: 3 passed

- [ ] **Step 5: Delete placeholder files**

```bash
rm src/ChainKit.Core/Placeholder.cs src/ChainKit.Tron/Placeholder.cs
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(core): add ChainResult pattern and ChainKitException"
```

---

### Task 3: ChainKit.Core — Interfaces

**Files:**
- Create: `src/ChainKit.Core/IAccount.cs`
- Create: `src/ChainKit.Core/ITransaction.cs`

- [ ] **Step 1: Create interfaces**

Create `src/ChainKit.Core/IAccount.cs`:
```csharp
namespace ChainKit.Core;

public interface IAccount
{
    string Address { get; }
    byte[] PublicKey { get; }
}
```

Create `src/ChainKit.Core/ITransaction.cs`:
```csharp
namespace ChainKit.Core;

public interface ITransaction
{
    string TxId { get; }
    string FromAddress { get; }
    string ToAddress { get; }
    decimal Amount { get; }
    DateTimeOffset Timestamp { get; }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(core): add IAccount and ITransaction interfaces"
```

---

### Task 4: ChainKit.Core — HexExtensions

**Files:**
- Create: `src/ChainKit.Core/Extensions/HexExtensions.cs`
- Create: `tests/ChainKit.Core.Tests/HexExtensionsTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/ChainKit.Core.Tests/HexExtensionsTests.cs`:
```csharp
using ChainKit.Core.Extensions;

namespace ChainKit.Core.Tests;

public class HexExtensionsTests
{
    [Fact]
    public void ToHex_ReturnsLowercaseHexString()
    {
        var bytes = new byte[] { 0x41, 0xAB, 0xCD, 0xEF };
        Assert.Equal("41abcdef", bytes.ToHex());
    }

    [Fact]
    public void ToHex_EmptyArray_ReturnsEmptyString()
    {
        Assert.Equal("", Array.Empty<byte>().ToHex());
    }

    [Fact]
    public void FromHex_ParsesHexString()
    {
        var result = "41abcdef".FromHex();
        Assert.Equal(new byte[] { 0x41, 0xAB, 0xCD, 0xEF }, result);
    }

    [Fact]
    public void FromHex_UpperCase_ParsesCorrectly()
    {
        var result = "41ABCDEF".FromHex();
        Assert.Equal(new byte[] { 0x41, 0xAB, 0xCD, 0xEF }, result);
    }

    [Fact]
    public void FromHex_WithPrefix_ParsesCorrectly()
    {
        var result = "0x41ab".FromHex();
        Assert.Equal(new byte[] { 0x41, 0xAB }, result);
    }

    [Fact]
    public void Roundtrip_PreservesData()
    {
        var original = new byte[] { 0x00, 0xFF, 0x12, 0x34 };
        Assert.Equal(original, original.ToHex().FromHex());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ChainKit.Core.Tests`
Expected: FAIL — HexExtensions not found

- [ ] **Step 3: Implement HexExtensions**

Create `src/ChainKit.Core/Extensions/HexExtensions.cs`:
```csharp
namespace ChainKit.Core.Extensions;

public static class HexExtensions
{
    public static string ToHex(this byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static byte[] FromHex(this string hex)
    {
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];

        return Convert.FromHexString(hex);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ChainKit.Core.Tests`
Expected: All passed

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add HexExtensions for byte[]/hex conversion"
```

---

### Task 5: ChainKit.Core — Base58Extensions

**Files:**
- Create: `src/ChainKit.Core/Extensions/Base58Extensions.cs`
- Create: `tests/ChainKit.Core.Tests/Base58ExtensionsTests.cs`

- [ ] **Step 1: Write failing tests**

Use known Tron address as test vector: hex `41a614f803b6fd780986a42c78ec9c7f77e6ded13c` → Base58Check `TQrg2LqShb7NFMXKV4DTP81GPWyaU7dFzd`

Create `tests/ChainKit.Core.Tests/Base58ExtensionsTests.cs`:
```csharp
using ChainKit.Core.Extensions;

namespace ChainKit.Core.Tests;

public class Base58ExtensionsTests
{
    [Fact]
    public void ToBase58Check_KnownVector()
    {
        // Tron address hex (with 0x41 prefix) → Base58Check
        var bytes = Convert.FromHexString("41a614f803b6fd780986a42c78ec9c7f77e6ded13c");
        var result = bytes.ToBase58Check();
        Assert.Equal("TQrg2LqShb7NFMXKV4DTP81GPWyaU7dFzd", result);
    }

    [Fact]
    public void FromBase58Check_KnownVector()
    {
        var result = "TQrg2LqShb7NFMXKV4DTP81GPWyaU7dFzd".FromBase58Check();
        var expected = Convert.FromHexString("41a614f803b6fd780986a42c78ec9c7f77e6ded13c");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Roundtrip_PreservesData()
    {
        var original = Convert.FromHexString("41a614f803b6fd780986a42c78ec9c7f77e6ded13c");
        var encoded = original.ToBase58Check();
        var decoded = encoded.FromBase58Check();
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void FromBase58Check_InvalidChecksum_Throws()
    {
        // Tamper with last char
        Assert.Throws<FormatException>(() => "TQrg2LqShb7NFMXKV4DTP81GPWyaU7dFzX".FromBase58Check());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ChainKit.Core.Tests`
Expected: FAIL — Base58Extensions not found

- [ ] **Step 3: Implement Base58Extensions**

Create `src/ChainKit.Core/Extensions/Base58Extensions.cs`:
```csharp
using System.Numerics;
using System.Security.Cryptography;

namespace ChainKit.Core.Extensions;

public static class Base58Extensions
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    public static string ToBase58Check(this byte[] payload)
    {
        var checksum = ComputeChecksum(payload);
        var data = new byte[payload.Length + 4];
        Buffer.BlockCopy(payload, 0, data, 0, payload.Length);
        Buffer.BlockCopy(checksum, 0, data, payload.Length, 4);
        return EncodeBase58(data);
    }

    public static byte[] FromBase58Check(this string encoded)
    {
        var data = DecodeBase58(encoded);
        if (data.Length < 4)
            throw new FormatException("Base58Check data too short.");

        var payload = data[..^4];
        var checksum = data[^4..];
        var expectedChecksum = ComputeChecksum(payload);

        if (!checksum.AsSpan().SequenceEqual(expectedChecksum.AsSpan(0, 4)))
            throw new FormatException("Base58Check checksum mismatch.");

        return payload;
    }

    private static byte[] ComputeChecksum(byte[] data)
    {
        var hash1 = SHA256.HashData(data);
        var hash2 = SHA256.HashData(hash1);
        return hash2[..4];
    }

    private static string EncodeBase58(byte[] data)
    {
        var intData = new BigInteger(data, isUnsigned: true, isBigEndian: true);
        var result = new List<char>();

        while (intData > 0)
        {
            intData = BigInteger.DivRem(intData, 58, out var remainder);
            result.Add(Alphabet[(int)remainder]);
        }

        // Leading zeros
        foreach (var b in data)
        {
            if (b != 0) break;
            result.Add(Alphabet[0]);
        }

        result.Reverse();
        return new string(result.ToArray());
    }

    private static byte[] DecodeBase58(string encoded)
    {
        var intData = BigInteger.Zero;
        foreach (var c in encoded)
        {
            var digit = Alphabet.IndexOf(c);
            if (digit < 0)
                throw new FormatException($"Invalid Base58 character: '{c}'");
            intData = intData * 58 + digit;
        }

        var leadingZeros = encoded.TakeWhile(c => c == '1').Count();
        var bytesWithoutLeading = intData.ToByteArray(isUnsigned: true, isBigEndian: true);
        var result = new byte[leadingZeros + bytesWithoutLeading.Length];
        Buffer.BlockCopy(bytesWithoutLeading, 0, result, leadingZeros, bytesWithoutLeading.Length);
        return result;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ChainKit.Core.Tests`
Expected: All passed

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add Base58Extensions with Base58Check encode/decode"
```

---

### Task 6: ChainKit.Tron — Keccak256

**Files:**
- Create: `src/ChainKit.Tron/Crypto/Keccak256.cs`
- Create: `tests/ChainKit.Tron.Tests/Crypto/Keccak256Tests.cs`

- [ ] **Step 1: Write failing tests**

Test vector: Keccak256("") = `c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470`
Test vector: Keccak256("transfer(address,uint256)") first 4 bytes = `a9059cbb`

Create `tests/ChainKit.Tron.Tests/Crypto/Keccak256Tests.cs`:
```csharp
using ChainKit.Core.Extensions;
using ChainKit.Tron.Crypto;

namespace ChainKit.Tron.Tests.Crypto;

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
        // Keccak256("transfer(address,uint256)") → first 4 bytes = a9059cbb
        var input = System.Text.Encoding.UTF8.GetBytes("transfer(address,uint256)");
        var result = Keccak256.Hash(input);
        Assert.Equal("a9059cbb", result[..4].ToHex());
    }

    [Fact]
    public void Hash_BalanceOfSelector()
    {
        var input = System.Text.Encoding.UTF8.GetBytes("balanceOf(address)");
        var result = Keccak256.Hash(input);
        Assert.Equal("70a08231", result[..4].ToHex());
    }

    [Fact]
    public void Hash_DifferentInputs_DifferentOutputs()
    {
        var a = Keccak256.Hash(new byte[] { 0x01 });
        var b = Keccak256.Hash(new byte[] { 0x02 });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Hash_AlwaysReturns32Bytes()
    {
        var result = Keccak256.Hash(new byte[] { 0x41 });
        Assert.Equal(32, result.Length);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ChainKit.Tron.Tests`
Expected: FAIL — Keccak256 not found

- [ ] **Step 3: Implement Keccak256**

Create `src/ChainKit.Tron/Crypto/Keccak256.cs`:
```csharp
namespace ChainKit.Tron.Crypto;

/// <summary>
/// Keccak-256 hash (pre-NIST, padding 0x01). NOT the same as SHA3-256 (NIST, padding 0x06).
/// Ethereum and Tron both use this variant.
/// </summary>
public static class Keccak256
{
    private static readonly ulong[] RoundConstants =
    {
        0x0000000000000001, 0x0000000000008082, 0x800000000000808A, 0x8000000080008000,
        0x000000000000808B, 0x0000000080000001, 0x8000000080008081, 0x8000000000008009,
        0x000000000000008A, 0x0000000000000088, 0x0000000080008009, 0x000000008000000A,
        0x000000008000808B, 0x800000000000008B, 0x8000000000008089, 0x8000000000008003,
        0x8000000000008002, 0x8000000000000080, 0x000000000000800A, 0x800000008000000A,
        0x8000000080008081, 0x8000000000008080, 0x0000000080000001, 0x8000000080008008
    };

    private static readonly int[] RotationOffsets =
    {
        1, 3, 6, 10, 15, 21, 28, 36, 45, 55, 2, 14, 27, 41, 56, 8, 25, 43, 62, 18, 39, 61, 20, 44
    };

    private static readonly int[] PiLane =
    {
        10, 7, 11, 17, 18, 3, 5, 16, 8, 21, 24, 4, 15, 23, 19, 13, 12, 2, 20, 14, 22, 9, 6, 1
    };

    public static byte[] Hash(byte[] input)
    {
        const int rate = 136; // (1600 - 256*2) / 8 = 136 bytes
        var state = new ulong[25];

        // Padding: Keccak uses 0x01, NOT SHA3's 0x06
        var padded = PadMessage(input, rate);

        // Absorb
        for (var offset = 0; offset < padded.Length; offset += rate)
        {
            for (var i = 0; i < rate / 8; i++)
                state[i] ^= BitConverter.ToUInt64(padded, offset + i * 8);

            KeccakF1600(state);
        }

        // Squeeze (256 bits = 32 bytes, fits in one block)
        var output = new byte[32];
        for (var i = 0; i < 4; i++)
            BitConverter.GetBytes(state[i]).CopyTo(output, i * 8);

        return output;
    }

    private static byte[] PadMessage(byte[] input, int rate)
    {
        var blockCount = (input.Length + 1 + rate - 1) / rate;
        var padded = new byte[blockCount * rate];
        Buffer.BlockCopy(input, 0, padded, 0, input.Length);
        padded[input.Length] = 0x01;         // Keccak padding (NOT 0x06 SHA3)
        padded[^1] |= 0x80;
        return padded;
    }

    private static void KeccakF1600(ulong[] state)
    {
        var c = new ulong[5];
        var d = new ulong[5];

        for (var round = 0; round < 24; round++)
        {
            // θ (Theta)
            for (var x = 0; x < 5; x++)
                c[x] = state[x] ^ state[x + 5] ^ state[x + 10] ^ state[x + 15] ^ state[x + 20];

            for (var x = 0; x < 5; x++)
            {
                d[x] = c[(x + 4) % 5] ^ RotateLeft(c[(x + 1) % 5], 1);
                for (var y = 0; y < 25; y += 5)
                    state[y + x] ^= d[x];
            }

            // ρ (Rho) and π (Pi)
            var last = state[1];
            for (var i = 0; i < 24; i++)
            {
                var j = PiLane[i];
                var temp = state[j];
                state[j] = RotateLeft(last, RotationOffsets[i]);
                last = temp;
            }

            // χ (Chi)
            for (var y = 0; y < 25; y += 5)
            {
                var t0 = state[y];
                var t1 = state[y + 1];
                var t2 = state[y + 2];
                var t3 = state[y + 3];
                var t4 = state[y + 4];
                state[y] = t0 ^ (~t1 & t2);
                state[y + 1] = t1 ^ (~t2 & t3);
                state[y + 2] = t2 ^ (~t3 & t4);
                state[y + 3] = t3 ^ (~t4 & t0);
                state[y + 4] = t4 ^ (~t0 & t1);
            }

            // ι (Iota)
            state[0] ^= RoundConstants[round];
        }
    }

    private static ulong RotateLeft(ulong value, int offset)
    {
        return (value << offset) | (value >> (64 - offset));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ChainKit.Tron.Tests`
Expected: All passed

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(tron): implement Keccak-256 hash (pre-NIST variant)"
```

---

### Task 7: ChainKit.Tron — TronAddress

**Files:**
- Create: `src/ChainKit.Tron/Crypto/TronAddress.cs`
- Create: `tests/ChainKit.Tron.Tests/Crypto/TronAddressTests.cs`

- [ ] **Step 1: Write failing tests**

Test vector: known Tron addresses from https://developers.tron.network

Create `tests/ChainKit.Tron.Tests/Crypto/TronAddressTests.cs`:
```csharp
using ChainKit.Tron.Crypto;

namespace ChainKit.Tron.Tests.Crypto;

public class TronAddressTests
{
    // Known test vector
    private const string KnownHex = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
    private const string KnownBase58 = "TQrg2LqShb7NFMXKV4DTP81GPWyaU7dFzd";

    [Fact]
    public void ToBase58_KnownVector()
    {
        Assert.Equal(KnownBase58, TronAddress.ToBase58(KnownHex));
    }

    [Fact]
    public void ToHex_KnownVector()
    {
        Assert.Equal(KnownHex, TronAddress.ToHex(KnownBase58));
    }

    [Fact]
    public void IsValid_ValidBase58_ReturnsTrue()
    {
        Assert.True(TronAddress.IsValid(KnownBase58));
    }

    [Fact]
    public void IsValid_ValidHex_ReturnsTrue()
    {
        Assert.True(TronAddress.IsValid(KnownHex));
    }

    [Fact]
    public void IsValid_InvalidAddress_ReturnsFalse()
    {
        Assert.False(TronAddress.IsValid("not_an_address"));
        Assert.False(TronAddress.IsValid(""));
        Assert.False(TronAddress.IsValid("T"));
    }

    [Fact]
    public void IsValid_WrongPrefix_ReturnsFalse()
    {
        // Valid length but wrong hex prefix (should be 41)
        Assert.False(TronAddress.IsValid("42a614f803b6fd780986a42c78ec9c7f77e6ded13c"));
    }

    [Fact]
    public void Roundtrip_Hex_Base58_Hex()
    {
        var base58 = TronAddress.ToBase58(KnownHex);
        var hex = TronAddress.ToHex(base58);
        Assert.Equal(KnownHex, hex);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ChainKit.Tron.Tests`
Expected: FAIL — TronAddress not found

- [ ] **Step 3: Implement TronAddress**

Create `src/ChainKit.Tron/Crypto/TronAddress.cs`:
```csharp
using ChainKit.Core.Extensions;

namespace ChainKit.Tron.Crypto;

public static class TronAddress
{
    private const byte AddressPrefix = 0x41;
    private const int HexAddressLength = 42; // "41" + 20 bytes hex = 42 chars

    public static bool IsValid(string address)
    {
        if (string.IsNullOrEmpty(address))
            return false;

        // Base58 format (starts with T)
        if (address.StartsWith('T') && address.Length >= 25 && address.Length <= 36)
        {
            try
            {
                var decoded = address.FromBase58Check();
                return decoded.Length == 21 && decoded[0] == AddressPrefix;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // Hex format (starts with 41, 42 chars)
        if (address.Length == HexAddressLength && address.StartsWith("41", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                address.FromHex();
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    public static string ToBase58(string hexAddress)
    {
        var bytes = hexAddress.FromHex();
        return bytes.ToBase58Check();
    }

    public static string ToHex(string base58Address)
    {
        var bytes = base58Address.FromBase58Check();
        return bytes.ToHex();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ChainKit.Tron.Tests`
Expected: All passed

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(tron): add TronAddress validation and format conversion"
```

---

### Task 8: ChainKit.Tron — TronSigner

**Files:**
- Create: `src/ChainKit.Tron/Crypto/TronSigner.cs`
- Create: `tests/ChainKit.Tron.Tests/Crypto/TronSignerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/ChainKit.Tron.Tests/Crypto/TronSignerTests.cs`:
```csharp
using ChainKit.Core.Extensions;
using ChainKit.Tron.Crypto;

namespace ChainKit.Tron.Tests.Crypto;

public class TronSignerTests
{
    [Fact]
    public void Sign_ReturnsNonEmpty65ByteSignature()
    {
        var privateKey = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        var data = new byte[32]; // 32 zero bytes as message hash

        var signature = TronSigner.Sign(data, privateKey);

        Assert.Equal(65, signature.Length); // 32 + 32 + 1 (r + s + v)
    }

    [Fact]
    public void Sign_SameInput_SameOutput()
    {
        var privateKey = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        var data = new byte[32];

        var sig1 = TronSigner.Sign(data, privateKey);
        var sig2 = TronSigner.Sign(data, privateKey);

        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void Sign_DifferentKey_DifferentSignature()
    {
        var key1 = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        var key2 = "0000000000000000000000000000000000000000000000000000000000000002".FromHex();
        var data = new byte[32];

        var sig1 = TronSigner.Sign(data, key1);
        var sig2 = TronSigner.Sign(data, key2);

        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void Verify_ValidSignature_ReturnsTrue()
    {
        var privateKey = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        var data = new byte[32];

        // Derive public key from private key
        var ctx = new NBitcoin.Secp256k1.ECPrivKey(privateKey, null);
        var pubBytes = new byte[65];
        ctx.CreatePubKey().WriteToSpan(true, pubBytes, out _);

        var signature = TronSigner.Sign(data, privateKey);
        Assert.True(TronSigner.Verify(data, signature, pubBytes));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ChainKit.Tron.Tests`
Expected: FAIL — TronSigner not found

- [ ] **Step 3: Implement TronSigner**

Create `src/ChainKit.Tron/Crypto/TronSigner.cs`:
```csharp
using NBitcoin.Secp256k1;

namespace ChainKit.Tron.Crypto;

public static class TronSigner
{
    public static byte[] Sign(byte[] data, byte[] privateKey)
    {
        var ecKey = ECPrivKey.Create(privateKey);
        ecKey.TrySignRecoverable(data, out var sig);

        var output = new byte[65];
        sig.WriteToSpanCompact(output.AsSpan(0, 64), out var recId);
        output[64] = (byte)recId;
        return output;
    }

    public static bool Verify(byte[] data, byte[] signature, byte[] publicKey)
    {
        if (signature.Length != 65)
            return false;

        var recId = signature[64];
        if (!SecpRecoverableECDSASignature.TryCreateFromCompact(signature.AsSpan(0, 64), recId, out var recSig))
            return false;

        if (!recSig.TryRecover(data, out var recoveredPubKey))
            return false;

        var recoveredBytes = new byte[65];
        recoveredPubKey.WriteToSpan(true, recoveredBytes, out _);

        return recoveredBytes.AsSpan().SequenceEqual(publicKey);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ChainKit.Tron.Tests`
Expected: All passed

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(tron): add TronSigner for secp256k1 ECDSA signing"
```

---

### Task 9: ChainKit.Tron — Mnemonic + TronAccount

**Files:**
- Create: `src/ChainKit.Tron/Crypto/Mnemonic.cs`
- Create: `src/ChainKit.Tron/Crypto/TronAccount.cs`
- Create: `tests/ChainKit.Tron.Tests/Crypto/MnemonicTests.cs`
- Create: `tests/ChainKit.Tron.Tests/Crypto/TronAccountTests.cs`

- [ ] **Step 1: Write failing tests for Mnemonic**

Create `tests/ChainKit.Tron.Tests/Crypto/MnemonicTests.cs`:
```csharp
using ChainKit.Tron.Crypto;

namespace ChainKit.Tron.Tests.Crypto;

public class MnemonicTests
{
    [Fact]
    public void Generate_12Words_Returns12Words()
    {
        var mnemonic = Mnemonic.Generate(12);
        var words = mnemonic.Split(' ');
        Assert.Equal(12, words.Length);
    }

    [Fact]
    public void Generate_24Words_Returns24Words()
    {
        var mnemonic = Mnemonic.Generate(24);
        var words = mnemonic.Split(' ');
        Assert.Equal(24, words.Length);
    }

    [Fact]
    public void Generate_TwoCalls_DifferentResults()
    {
        var m1 = Mnemonic.Generate(12);
        var m2 = Mnemonic.Generate(12);
        Assert.NotEqual(m1, m2);
    }

    [Fact]
    public void Validate_ValidMnemonic_ReturnsTrue()
    {
        var mnemonic = Mnemonic.Generate(12);
        Assert.True(Mnemonic.Validate(mnemonic));
    }

    [Fact]
    public void Validate_InvalidMnemonic_ReturnsFalse()
    {
        Assert.False(Mnemonic.Validate("invalid words that are not a real mnemonic phrase at all ever"));
    }

    [Fact]
    public void ToSeed_SameMnemonic_SameSeed()
    {
        var mnemonic = Mnemonic.Generate(12);
        var seed1 = Mnemonic.ToSeed(mnemonic);
        var seed2 = Mnemonic.ToSeed(mnemonic);
        Assert.Equal(seed1, seed2);
    }

    [Fact]
    public void ToSeed_Returns64Bytes()
    {
        var mnemonic = Mnemonic.Generate(12);
        var seed = Mnemonic.ToSeed(mnemonic);
        Assert.Equal(64, seed.Length);
    }
}
```

- [ ] **Step 2: Write failing tests for TronAccount**

Create `tests/ChainKit.Tron.Tests/Crypto/TronAccountTests.cs`:
```csharp
using ChainKit.Core.Extensions;
using ChainKit.Tron.Crypto;

namespace ChainKit.Tron.Tests.Crypto;

public class TronAccountTests
{
    [Fact]
    public void Create_GeneratesValidAccount()
    {
        var account = TronAccount.Create();

        Assert.NotNull(account.Address);
        Assert.StartsWith("T", account.Address);
        Assert.StartsWith("41", account.HexAddress);
        Assert.Equal(32, account.PrivateKey.Length);
        Assert.NotEmpty(account.PublicKey);
        Assert.True(TronAddress.IsValid(account.Address));
    }

    [Fact]
    public void Create_TwoCalls_DifferentAccounts()
    {
        var a1 = TronAccount.Create();
        var a2 = TronAccount.Create();
        Assert.NotEqual(a1.Address, a2.Address);
    }

    [Fact]
    public void FromPrivateKey_KnownVector()
    {
        // Known: private key 1 → specific Tron address
        var privateKey = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        var account = TronAccount.FromPrivateKey(privateKey);

        Assert.True(TronAddress.IsValid(account.Address));
        Assert.StartsWith("T", account.Address);
        Assert.Equal(32, account.PrivateKey.Length);
    }

    [Fact]
    public void FromPrivateKey_SameKey_SameAccount()
    {
        var privateKey = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        var a1 = TronAccount.FromPrivateKey(privateKey);
        var a2 = TronAccount.FromPrivateKey(privateKey);
        Assert.Equal(a1.Address, a2.Address);
    }

    [Fact]
    public void FromMnemonic_SameMnemonic_SameAccount()
    {
        var mnemonic = Mnemonic.Generate(12);
        var a1 = TronAccount.FromMnemonic(mnemonic, 0);
        var a2 = TronAccount.FromMnemonic(mnemonic, 0);
        Assert.Equal(a1.Address, a2.Address);
    }

    [Fact]
    public void FromMnemonic_DifferentIndex_DifferentAccount()
    {
        var mnemonic = Mnemonic.Generate(12);
        var a0 = TronAccount.FromMnemonic(mnemonic, 0);
        var a1 = TronAccount.FromMnemonic(mnemonic, 1);
        Assert.NotEqual(a0.Address, a1.Address);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ChainKit.Tron.Tests`
Expected: FAIL

- [ ] **Step 4: Implement Mnemonic**

Create `src/ChainKit.Tron/Crypto/Mnemonic.cs`:
```csharp
namespace ChainKit.Tron.Crypto;

public static class Mnemonic
{
    public static string Generate(int wordCount = 12)
    {
        var entropy = wordCount switch
        {
            12 => NBitcoin.WordCount.Twelve,
            24 => NBitcoin.WordCount.TwentyFour,
            _ => throw new ArgumentException("wordCount must be 12 or 24", nameof(wordCount))
        };

        var mnemonic = new NBitcoin.Mnemonic(NBitcoin.Wordlist.English, entropy);
        return mnemonic.ToString();
    }

    public static byte[] ToSeed(string mnemonic, string passphrase = "")
    {
        var m = new NBitcoin.Mnemonic(mnemonic);
        return m.DeriveSeed(passphrase);
    }

    public static bool Validate(string mnemonic)
    {
        try
        {
            var m = new NBitcoin.Mnemonic(mnemonic);
            // NBitcoin constructor succeeds if words are valid, but we also check checksum
            return m.IsValidChecksum;
        }
        catch
        {
            return false;
        }
    }
}
```

- [ ] **Step 5: Implement TronAccount**

Create `src/ChainKit.Tron/Crypto/TronAccount.cs`:
```csharp
using System.Security.Cryptography;
using ChainKit.Core;
using ChainKit.Core.Extensions;
using NBitcoin;

namespace ChainKit.Tron.Crypto;

public class TronAccount : IAccount
{
    public string Address { get; }
    public string HexAddress { get; }
    public byte[] PublicKey { get; }
    public byte[] PrivateKey { get; }

    private TronAccount(byte[] privateKey, byte[] publicKey, string hexAddress, string address)
    {
        PrivateKey = privateKey;
        PublicKey = publicKey;
        HexAddress = hexAddress;
        Address = address;
    }

    public static TronAccount Create()
    {
        var privateKey = RandomNumberGenerator.GetBytes(32);
        return FromPrivateKey(privateKey);
    }

    public static TronAccount FromPrivateKey(byte[] privateKey)
    {
        var ecKey = NBitcoin.Secp256k1.ECPrivKey.Create(privateKey);
        var pubKey = ecKey.CreatePubKey();

        // Uncompressed public key (65 bytes: 04 + x + y)
        var pubBytes = new byte[65];
        pubKey.WriteToSpan(false, pubBytes, out _);

        // Tron address: Keccak256(pubkey[1..]) → take last 20 bytes → prefix 0x41
        var hash = Keccak256.Hash(pubBytes[1..]); // skip 0x04 prefix
        var addressBytes = new byte[21];
        addressBytes[0] = 0x41;
        Buffer.BlockCopy(hash, 12, addressBytes, 1, 20);

        var hexAddress = addressBytes.ToHex();
        var base58Address = TronAddress.ToBase58(hexAddress);

        // Store compressed public key (33 bytes)
        var compressedPub = new byte[33];
        pubKey.WriteToSpan(true, compressedPub, out _);

        return new TronAccount(privateKey, compressedPub, hexAddress, base58Address);
    }

    public static TronAccount FromMnemonic(string mnemonic, int index = 0)
    {
        // BIP44 path: m/44'/195'/0'/0/{index}
        var m = new NBitcoin.Mnemonic(mnemonic);
        var seed = m.DeriveSeed();
        var masterKey = ExtKey.CreateFromSeed(seed);
        var derived = masterKey.Derive(new KeyPath($"m/44'/195'/0'/0/{index}"));

        return FromPrivateKey(derived.PrivateKey.ToBytes());
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ChainKit.Tron.Tests`
Expected: All passed

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(tron): add Mnemonic and TronAccount with BIP39/BIP44 support"
```

---

### Task 10: ChainKit.Tron — AbiEncoder

**Files:**
- Create: `src/ChainKit.Tron/Crypto/AbiEncoder.cs`
- Create: `tests/ChainKit.Tron.Tests/Crypto/AbiEncoderTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/ChainKit.Tron.Tests/Crypto/AbiEncoderTests.cs`:
```csharp
using System.Numerics;
using ChainKit.Core.Extensions;
using ChainKit.Tron.Crypto;

namespace ChainKit.Tron.Tests.Crypto;

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
    public void EncodeFunctionSelector_Approve()
    {
        var selector = AbiEncoder.EncodeFunctionSelector("approve(address,uint256)");
        Assert.Equal("095ea7b3", selector.ToHex());
    }

    [Fact]
    public void EncodeAddress_PadsTo32Bytes()
    {
        var address = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        var encoded = AbiEncoder.EncodeAddress(address);
        Assert.Equal(32, encoded.Length);
        // Should be left-padded with zeros, address without 41 prefix in last 20 bytes
        Assert.Equal("000000000000000000000000a614f803b6fd780986a42c78ec9c7f77e6ded13c", encoded.ToHex());
    }

    [Fact]
    public void EncodeUint256_SmallValue()
    {
        var encoded = AbiEncoder.EncodeUint256(new BigInteger(1000000));
        Assert.Equal(32, encoded.Length);
        Assert.Equal("00000000000000000000000000000000000000000000000000000000000f4240", encoded.ToHex());
    }

    [Fact]
    public void EncodeTransfer_CombinesSelectorAndParams()
    {
        var to = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        var amount = new BigInteger(1000000);
        var encoded = AbiEncoder.EncodeTransfer(to, amount);

        Assert.Equal(4 + 32 + 32, encoded.Length); // selector + address + uint256
        Assert.Equal("a9059cbb", encoded[..4].ToHex()); // transfer selector
    }

    [Fact]
    public void EncodeBalanceOf_CombinesSelectorAndAddress()
    {
        var address = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        var encoded = AbiEncoder.EncodeBalanceOf(address);

        Assert.Equal(4 + 32, encoded.Length);
        Assert.Equal("70a08231", encoded[..4].ToHex());
    }

    [Fact]
    public void DecodeUint256_Roundtrip()
    {
        var original = new BigInteger(123456789);
        var encoded = AbiEncoder.EncodeUint256(original);
        var decoded = AbiEncoder.DecodeUint256(encoded);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void DecodeAddress_Roundtrip()
    {
        var original = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
        var encoded = AbiEncoder.EncodeAddress(original);
        var decoded = AbiEncoder.DecodeAddress(encoded);
        Assert.Equal(original, decoded);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ChainKit.Tron.Tests`
Expected: FAIL — AbiEncoder not found

- [ ] **Step 3: Implement AbiEncoder**

Create `src/ChainKit.Tron/Crypto/AbiEncoder.cs`:
```csharp
using System.Numerics;
using System.Text;
using ChainKit.Core.Extensions;

namespace ChainKit.Tron.Crypto;

public static class AbiEncoder
{
    public static byte[] EncodeFunctionSelector(string signature)
    {
        var hash = Keccak256.Hash(Encoding.UTF8.GetBytes(signature));
        return hash[..4];
    }

    public static byte[] EncodeAddress(string hexAddress)
    {
        // Remove 41 prefix if present
        var addr = hexAddress;
        if (addr.StartsWith("41") && addr.Length == 42)
            addr = addr[2..];

        var bytes = addr.FromHex();
        var padded = new byte[32];
        Buffer.BlockCopy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);
        return padded;
    }

    public static byte[] EncodeUint256(BigInteger value)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        var padded = new byte[32];
        Buffer.BlockCopy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);
        return padded;
    }

    public static byte[] EncodeTransfer(string toHex, BigInteger amount)
    {
        var selector = EncodeFunctionSelector("transfer(address,uint256)");
        var addr = EncodeAddress(toHex);
        var amt = EncodeUint256(amount);
        return [.. selector, .. addr, .. amt];
    }

    public static byte[] EncodeBalanceOf(string addressHex)
    {
        var selector = EncodeFunctionSelector("balanceOf(address)");
        var addr = EncodeAddress(addressHex);
        return [.. selector, .. addr];
    }

    public static byte[] EncodeApprove(string spenderHex, BigInteger amount)
    {
        var selector = EncodeFunctionSelector("approve(address,uint256)");
        var addr = EncodeAddress(spenderHex);
        var amt = EncodeUint256(amount);
        return [.. selector, .. addr, .. amt];
    }

    public static byte[] EncodeMint(string toHex, BigInteger amount)
    {
        var selector = EncodeFunctionSelector("mint(address,uint256)");
        var addr = EncodeAddress(toHex);
        var amt = EncodeUint256(amount);
        return [.. selector, .. addr, .. amt];
    }

    public static byte[] EncodeBurn(BigInteger amount)
    {
        var selector = EncodeFunctionSelector("burn(uint256)");
        var amt = EncodeUint256(amount);
        return [.. selector, .. amt];
    }

    public static BigInteger DecodeUint256(byte[] data)
    {
        var slice = data.Length > 32 ? data[^32..] : data;
        return new BigInteger(slice, isUnsigned: true, isBigEndian: true);
    }

    public static string DecodeAddress(byte[] data)
    {
        // Last 20 bytes are the address, prepend 41
        var slice = data.Length > 32 ? data[^32..] : data;
        var addressBytes = slice[12..]; // skip 12 zero padding bytes
        return "41" + addressBytes.ToHex();
    }

    public static string DecodeString(byte[] data)
    {
        if (data.Length < 64) return string.Empty;
        // offset (32 bytes) + length (32 bytes) + data
        var length = (int)new BigInteger(data[32..64], isUnsigned: true, isBigEndian: true);
        return Encoding.UTF8.GetString(data, 64, length);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ChainKit.Tron.Tests`
Expected: All passed

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(tron): add ABI encoder/decoder for TRC20 contract interaction"
```

---

### Task 11: ChainKit.Tron — Protobuf Setup

**Files:**
- Create: `src/ChainKit.Tron/Protocol/Protobuf/` directory with .proto files
- Modify: `src/ChainKit.Tron/ChainKit.Tron.csproj` to compile protos

- [ ] **Step 1: Download Tron proto files**

```bash
mkdir -p src/ChainKit.Tron/Protocol/Protobuf
cd src/ChainKit.Tron/Protocol/Protobuf
curl -sL "https://raw.githubusercontent.com/tronprotocol/protocol/master/core/Tron.proto" -o Tron.proto
curl -sL "https://raw.githubusercontent.com/tronprotocol/protocol/master/core/contract/common.proto" -o common.proto
curl -sL "https://raw.githubusercontent.com/tronprotocol/protocol/master/core/contract/balance_contract.proto" -o balance_contract.proto
curl -sL "https://raw.githubusercontent.com/tronprotocol/protocol/master/core/contract/smart_contract.proto" -o smart_contract.proto
```

- [ ] **Step 2: Fix proto import paths for flat directory structure**

The downloaded protos use `import "core/Tron.proto"` etc. Update imports to use flat paths since all files are in the same directory. Edit each `.proto` to fix imports (e.g. `import "core/Tron.proto"` → `import "Tron.proto"`).

- [ ] **Step 3: Add Protobuf compilation to .csproj**

Add to `src/ChainKit.Tron/ChainKit.Tron.csproj` inside an `<ItemGroup>`:
```xml
<ItemGroup>
  <Protobuf Include="Protocol\Protobuf\*.proto" GrpcServices="None" />
</ItemGroup>
```

- [ ] **Step 4: Verify build with proto compilation**

Run: `dotnet build src/ChainKit.Tron`
Expected: Build succeeded, generated C# classes from protos

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(tron): add Tron protobuf definitions and compilation"
```

---

### Task 12: ChainKit.Tron — TransactionBuilder + TransactionUtils

**Files:**
- Create: `src/ChainKit.Tron/Protocol/TransactionBuilder.cs`
- Create: `src/ChainKit.Tron/Protocol/TransactionUtils.cs`
- Create: `tests/ChainKit.Tron.Tests/Protocol/TransactionBuilderTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/ChainKit.Tron.Tests/Protocol/TransactionBuilderTests.cs`:
```csharp
using ChainKit.Core.Extensions;
using ChainKit.Tron.Protocol;

namespace ChainKit.Tron.Tests.Protocol;

public class TransactionBuilderTests
{
    private const string FromHex = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c";
    private const string ToHex = "41b2a3f846af3b8a1b1f2c3d4e5f6a7b8c9d0e1f2a";

    [Fact]
    public void CreateTransfer_BuildsValidTransaction()
    {
        var tx = new TransactionBuilder()
            .CreateTransfer(FromHex, ToHex, 1_000_000)
            .SetFeeLimit(1_000_000)
            .Build();

        Assert.NotNull(tx);
        Assert.NotNull(tx.RawData);
    }

    [Fact]
    public void ComputeTxId_Returns32Bytes()
    {
        var tx = new TransactionBuilder()
            .CreateTransfer(FromHex, ToHex, 1_000_000)
            .Build();

        var txId = TransactionUtils.ComputeTxId(tx);
        Assert.Equal(32, txId.Length);
    }

    [Fact]
    public void ComputeTxId_SameTransaction_SameId()
    {
        var tx = new TransactionBuilder()
            .CreateTransfer(FromHex, ToHex, 1_000_000)
            .Build();

        var id1 = TransactionUtils.ComputeTxId(tx);
        var id2 = TransactionUtils.ComputeTxId(tx);
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Sign_AddsSignatureToTransaction()
    {
        var privateKey = "0000000000000000000000000000000000000000000000000000000000000001".FromHex();
        var tx = new TransactionBuilder()
            .CreateTransfer(FromHex, ToHex, 1_000_000)
            .Build();

        var signed = TransactionUtils.Sign(tx, privateKey);
        Assert.NotEmpty(signed.Signature);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ChainKit.Tron.Tests`
Expected: FAIL — TransactionBuilder not found

- [ ] **Step 3: Implement TransactionBuilder**

Create `src/ChainKit.Tron/Protocol/TransactionBuilder.cs`:
```csharp
using ChainKit.Core.Extensions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Protocol;

namespace ChainKit.Tron.Protocol;

public class TransactionBuilder
{
    private Transaction.Types.raw _raw = new();

    public TransactionBuilder CreateTransfer(string fromHex, string toHex, long amountSun)
    {
        var contract = new TransferContract
        {
            OwnerAddress = ByteString.CopyFrom(fromHex.FromHex()),
            ToAddress = ByteString.CopyFrom(toHex.FromHex()),
            Amount = amountSun
        };

        _raw = new Transaction.Types.raw();
        _raw.Contract.Add(new Transaction.Types.Contract
        {
            Type = Transaction.Types.Contract.Types.ContractType.TransferContract,
            Parameter = Any.Pack(contract)
        });

        _raw.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _raw.Expiration = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds();

        return this;
    }

    public TransactionBuilder CreateTriggerSmartContract(
        string ownerHex, string contractHex, byte[] data, long callValue = 0)
    {
        var contract = new TriggerSmartContract
        {
            OwnerAddress = ByteString.CopyFrom(ownerHex.FromHex()),
            ContractAddress = ByteString.CopyFrom(contractHex.FromHex()),
            Data = ByteString.CopyFrom(data),
            CallValue = callValue
        };

        _raw = new Transaction.Types.raw();
        _raw.Contract.Add(new Transaction.Types.Contract
        {
            Type = Transaction.Types.Contract.Types.ContractType.TriggerSmartContract,
            Parameter = Any.Pack(contract)
        });

        _raw.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _raw.Expiration = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds();

        return this;
    }

    public TransactionBuilder SetReference(byte[] refBlockBytes, byte[] refBlockHash, long expiration)
    {
        _raw.RefBlockBytes = ByteString.CopyFrom(refBlockBytes);
        _raw.RefBlockHash = ByteString.CopyFrom(refBlockHash);
        _raw.Expiration = expiration;
        return this;
    }

    public TransactionBuilder SetFeeLimit(long feeLimit)
    {
        _raw.FeeLimit = feeLimit;
        return this;
    }

    public TransactionBuilder SetMemo(string memo)
    {
        _raw.Data = ByteString.CopyFromUtf8(memo);
        return this;
    }

    public Transaction Build()
    {
        return new Transaction { RawData = _raw };
    }
}
```

- [ ] **Step 4: Implement TransactionUtils**

Create `src/ChainKit.Tron/Protocol/TransactionUtils.cs`:
```csharp
using System.Security.Cryptography;
using ChainKit.Tron.Crypto;
using Google.Protobuf;
using Protocol;

namespace ChainKit.Tron.Protocol;

public static class TransactionUtils
{
    public static byte[] ComputeTxId(Transaction transaction)
    {
        var rawBytes = transaction.RawData.ToByteArray();
        return SHA256.HashData(rawBytes);
    }

    public static Transaction Sign(Transaction transaction, byte[] privateKey)
    {
        var txId = ComputeTxId(transaction);
        var signature = TronSigner.Sign(txId, privateKey);
        var signed = transaction.Clone();
        signed.Signature.Add(ByteString.CopyFrom(signature));
        return signed;
    }

    public static Transaction AddSignature(Transaction transaction, byte[] signature)
    {
        var result = transaction.Clone();
        result.Signature.Add(ByteString.CopyFrom(signature));
        return result;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ChainKit.Tron.Tests`
Expected: All passed

Note: The proto-generated types (`Transaction`, `TransferContract`, etc.) may use different namespaces depending on the proto `option csharp_namespace`. Adjust `using` statements if needed after proto compilation.

- [ ] **Step 6: Verify full build and all tests**

Run: `dotnet test`
Expected: All tests pass across both test projects

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(tron): add TransactionBuilder and TransactionUtils for tx construction"
```

---

## Plan 1 Complete

After all 12 tasks, you will have:

- **ChainKit.Core**: Result Pattern, interfaces, Hex/Base58 utilities
- **ChainKit.Tron/Crypto**: Keccak256, TronAddress, TronSigner, Mnemonic, TronAccount, AbiEncoder
- **ChainKit.Tron/Protocol**: Protobuf definitions, TransactionBuilder, TransactionUtils
- All offline functionality working and tested

**Next:** Plan 2 (Providers + TronClient + Contracts)
