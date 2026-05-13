using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Test stub for <see cref="ICurrentUser"/>. Returns a fixed known actor id so that
/// audit log rows written during integration tests have a predictable <c>actor_id</c>.
/// </summary>
internal sealed class FakeCurrentUser : ICurrentUser
{
    /// <summary>The fixed actor id used by this stub.</summary>
    public static readonly UserId ActorId = new(new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

    /// <inheritdoc />
    public bool IsAuthenticated => true;

    /// <inheritdoc />
    public UserId UserId => ActorId;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Roles => ["Admin"];
}
