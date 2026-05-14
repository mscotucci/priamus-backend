// TODO: replace when Registry module lands (ADR 0008)
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Host.Infrastructure.Stubs;

/// <summary>
/// Temporary stub for <see cref="IDriverLookup"/> that always returns <c>null</c>.
/// Registered in the composition root (<c>Program.cs</c>) until the Registry module is built.
/// </summary>
/// <remarks>
/// See ADR 0008 (cross-module identity-driver linkage). Registered in Host as a singleton
/// to make the temporary nature visible from the composition root, not inside a module extension.
/// </remarks>
internal sealed class NullDriverLookup : IDriverLookup
{
    /// <inheritdoc />
    public Task<Guid?> GetDriverIdByUserIdAsync(UserId userId, CancellationToken cancellationToken)
        => Task.FromResult<Guid?>(null);
}
