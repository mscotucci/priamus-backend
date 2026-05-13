using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.SharedKernel.Domain;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.UnitTests.Domain.Users.ValueObjects;

public sealed class PasswordHashTests
{
    // ── Valid construction ────────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidHash_ReturnsValueObject()
    {
        // Arrange
        var raw = new string('a', 64);

        // Act
        var hash = PasswordHash.Create(raw);

        // Assert
        hash.Value.ShouldBe(raw);
    }

    [Fact]
    public void Create_WithExactMaxLength_Succeeds()
    {
        // Arrange — 1024 chars (MaxLength)
        var raw = new string('x', PasswordHash.MaxLength);

        // Act
        var hash = PasswordHash.Create(raw);

        // Assert
        hash.Value.ShouldBe(raw);
    }

    [Fact]
    public void Create_WithShortHash_Succeeds()
    {
        // Arrange — minimal non-empty string (1 char)
        var raw = "a";

        // Act
        var hash = PasswordHash.Create(raw);

        // Assert
        hash.Value.ShouldBe("a");
    }

    // ── Null / empty / whitespace ─────────────────────────────────────────────

    [Fact]
    public void Create_WithNullInput_ThrowsDomainException()
    {
        // Arrange / Act
        var act = () => PasswordHash.Create(null);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(UserErrors.PasswordHashInvalid.Code);
    }

    [Fact]
    public void Create_WithEmptyString_ThrowsDomainException()
    {
        // Arrange / Act
        var act = () => PasswordHash.Create(string.Empty);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(UserErrors.PasswordHashInvalid.Code);
    }

    [Fact]
    public void Create_WithWhitespaceOnly_ThrowsDomainException()
    {
        // Arrange / Act
        var act = () => PasswordHash.Create("   ");

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(UserErrors.PasswordHashInvalid.Code);
    }

    // ── Length boundary ───────────────────────────────────────────────────────

    [Fact]
    public void Create_WithLengthExceedingMax_ThrowsDomainException()
    {
        // Arrange — MaxLength + 1
        var raw = new string('a', PasswordHash.MaxLength + 1);

        // Act
        var act = () => PasswordHash.Create(raw);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(UserErrors.PasswordHashInvalid.Code);
    }

    // ── Equality (record semantics) ──────────────────────────────────────────

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        // Arrange
        var raw = new string('a', 64);
        var a = PasswordHash.Create(raw);
        var b = PasswordHash.Create(raw);

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void Equality_DifferentValues_AreNotEqual()
    {
        // Arrange
        var a = PasswordHash.Create(new string('a', 64));
        var b = PasswordHash.Create(new string('b', 64));

        // Assert
        a.ShouldNotBe(b);
    }
}
