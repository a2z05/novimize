using System.Collections.Concurrent;
using WinOpt.Core.Interfaces;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Providers;

/// <summary>
/// Registry of tweak providers keyed by method type.
/// </summary>
public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly ConcurrentDictionary<string, ITweakProvider> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<TweakMethod, ITweakProvider> _byMethod = new();

    public void Register(ITweakProvider provider)
    {
        _byName[provider.Name] = provider;
        foreach (var method in provider.SupportedMethods)
        {
            if (_byMethod.TryGetValue(method, out var existing))
            {
                throw new InvalidOperationException(
                    $"TweakMethod '{method}' already handled by provider '{existing.Name}' — cannot register '{provider.Name}' for the same method. " +
                    $"Each TweakMethod must have exactly one provider. Check Program.cs RegisterProviders ordering.");
            }
            _byMethod[method] = provider;
        }
    }

    public ITweakProvider? GetProvider(TweakMethod method)
        => _byMethod.GetValueOrDefault(method);

    public ITweakProvider? GetProvider(string name)
        => _byName.GetValueOrDefault(name);

    public IReadOnlyList<ITweakProvider> GetAll()
        => _byName.Values.ToList().AsReadOnly();

    /// <summary>
    /// Get the provider for a specific tweak definition.
    /// </summary>
    public ITweakProvider? GetProviderFor(TweakDefinition tweak)
        => _byMethod.GetValueOrDefault(tweak.Method);
}
