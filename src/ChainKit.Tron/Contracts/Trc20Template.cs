using ChainKit.Tron.Models;

namespace ChainKit.Tron.Contracts;

/// <summary>
/// Provides pre-compiled bytecode templates for deploying standard TRC20 tokens.
/// Currently a placeholder -- in production this would contain Solidity-compiled
/// bytecode for mintable/burnable TRC20 contracts.
/// </summary>
public static class Trc20Template
{
    /// <summary>
    /// Returns the deployment bytecode for a TRC20 token contract matching the given options.
    /// </summary>
    /// <param name="options">Token configuration (name, symbol, decimals, mintable, burnable).</param>
    /// <returns>EVM bytecode ready for contract creation.</returns>
    /// <exception cref="NotImplementedException">
    /// Always thrown -- template bytecode is not yet compiled.
    /// Use <see cref="TronClient.DeployContractAsync"/> with custom bytecode instead.
    /// </exception>
    public static byte[] GetBytecode(Trc20TokenOptions options)
    {
        // Placeholder -- actual implementation would:
        // 1. Select a base bytecode depending on Mintable/Burnable flags
        // 2. ABI-encode the constructor args (name, symbol, decimals, initialSupply)
        // 3. Append constructor args to the bytecode
        throw new NotImplementedException(
            "TRC20 template bytecode not yet compiled. Use DeployContractAsync with custom bytecode.");
    }

    /// <summary>
    /// Returns the standard TRC20 ABI string for the given options.
    /// </summary>
    public static string GetAbi(Trc20TokenOptions options)
    {
        // Minimal standard TRC20 ABI -- covers ERC20 + optional mint/burn
        return "[" +
            "{\"constant\":true,\"inputs\":[],\"name\":\"name\",\"outputs\":[{\"name\":\"\",\"type\":\"string\"}],\"type\":\"function\"}," +
            "{\"constant\":true,\"inputs\":[],\"name\":\"symbol\",\"outputs\":[{\"name\":\"\",\"type\":\"string\"}],\"type\":\"function\"}," +
            "{\"constant\":true,\"inputs\":[],\"name\":\"decimals\",\"outputs\":[{\"name\":\"\",\"type\":\"uint8\"}],\"type\":\"function\"}," +
            "{\"constant\":true,\"inputs\":[],\"name\":\"totalSupply\",\"outputs\":[{\"name\":\"\",\"type\":\"uint256\"}],\"type\":\"function\"}," +
            "{\"constant\":true,\"inputs\":[{\"name\":\"owner\",\"type\":\"address\"}],\"name\":\"balanceOf\",\"outputs\":[{\"name\":\"\",\"type\":\"uint256\"}],\"type\":\"function\"}," +
            "{\"constant\":false,\"inputs\":[{\"name\":\"to\",\"type\":\"address\"},{\"name\":\"value\",\"type\":\"uint256\"}],\"name\":\"transfer\",\"outputs\":[{\"name\":\"\",\"type\":\"bool\"}],\"type\":\"function\"}," +
            "{\"constant\":false,\"inputs\":[{\"name\":\"spender\",\"type\":\"address\"},{\"name\":\"value\",\"type\":\"uint256\"}],\"name\":\"approve\",\"outputs\":[{\"name\":\"\",\"type\":\"bool\"}],\"type\":\"function\"}," +
            "{\"constant\":true,\"inputs\":[{\"name\":\"owner\",\"type\":\"address\"},{\"name\":\"spender\",\"type\":\"address\"}],\"name\":\"allowance\",\"outputs\":[{\"name\":\"\",\"type\":\"uint256\"}],\"type\":\"function\"}" +
            (options.Mintable
                ? ",{\"constant\":false,\"inputs\":[{\"name\":\"to\",\"type\":\"address\"},{\"name\":\"amount\",\"type\":\"uint256\"}],\"name\":\"mint\",\"outputs\":[],\"type\":\"function\"}"
                : "") +
            (options.Burnable
                ? ",{\"constant\":false,\"inputs\":[{\"name\":\"amount\",\"type\":\"uint256\"}],\"name\":\"burn\",\"outputs\":[],\"type\":\"function\"}" +
                  ",{\"constant\":false,\"inputs\":[{\"name\":\"from\",\"type\":\"address\"},{\"name\":\"amount\",\"type\":\"uint256\"}],\"name\":\"burnFrom\",\"outputs\":[],\"type\":\"function\"}"
                : "") +
            "]";
    }
}
