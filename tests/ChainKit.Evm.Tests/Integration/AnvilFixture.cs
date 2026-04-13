using System.Diagnostics;
using ChainKit.Evm.Crypto;
using ChainKit.Evm.Providers;
using Xunit;

namespace ChainKit.Evm.Tests.Integration;

/// <summary>
/// xUnit class fixture that starts and stops a local Anvil node for integration tests.
/// Anvil pre-funds 10 deterministic accounts with 10,000 ETH each.
/// </summary>
public class AnvilFixture : IAsyncLifetime
{
    private Process? _anvilProcess;

    /// <summary>
    /// The HTTP provider connected to the local Anvil node.
    /// </summary>
    public EvmHttpProvider Provider { get; private set; } = null!;

    /// <summary>
    /// Network config for Anvil (chain ID 31337).
    /// </summary>
    public EvmNetworkConfig Network { get; } = new("http://127.0.0.1:8545", 31337, "Anvil", "ETH");

    /// <summary>
    /// Anvil's first deterministic pre-funded account (10,000 ETH).
    /// </summary>
    public EvmAccount Account0 { get; } = EvmAccount.FromPrivateKey(
        Convert.FromHexString("ac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80"));

    /// <summary>
    /// Anvil's second deterministic pre-funded account (10,000 ETH).
    /// </summary>
    public EvmAccount Account1 { get; } = EvmAccount.FromPrivateKey(
        Convert.FromHexString("59c6995e998f97a5a0044966f0945389dc9e86dae88c7a8412f4603b6b78690d"));

    /// <inheritdoc />
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
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        try
        {
            _anvilProcess.Start();
        }
        catch (Exception)
        {
            throw new InvalidOperationException(
                "Anvil not found. Install Foundry: curl -L https://foundry.paradigm.xyz | bash && foundryup");
        }

        Provider = new EvmHttpProvider(Network);

        // Wait for Anvil to be ready (up to 10 seconds)
        for (int i = 0; i < 50; i++)
        {
            try
            {
                await Provider.GetBlockNumberAsync();
                return; // Anvil is ready
            }
            catch
            {
                await Task.Delay(200);
            }
        }

        throw new TimeoutException("Anvil did not start within 10 seconds");
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        Provider?.Dispose();
        if (_anvilProcess is { HasExited: false })
        {
            _anvilProcess.Kill();
            _anvilProcess.WaitForExit(3000);
        }
        _anvilProcess?.Dispose();
        return Task.CompletedTask;
    }
}
