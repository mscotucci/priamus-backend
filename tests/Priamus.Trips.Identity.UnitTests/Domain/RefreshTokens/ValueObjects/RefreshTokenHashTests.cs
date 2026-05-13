using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Domain.RefreshTokens.ValueObjects;
using Priamus.Trips.SharedKernel.Domain;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.UnitTests.Domain.RefreshTokens.ValueObjects;

public sealed class RefreshTokenHashTests
{
    // ── Valid construction ────────────────────────────────────────────────────

    [Fact]
    public void Create_WithExactlyExpectedLength_Succeeds()
    {
        // Arrange — SHA-256 hex = 64 chars
        var raw = new string('a', RefreshTokenHash.ExpectedLength);

        // Act
        var hash = RefreshTokenHash.Create(raw);

        // Assert
        hash.Value.ShouldBe(raw);
    }

    // ── Null / empty / whitespace ─────────────────────────────────────────────

    [Fact]
    public void Create_WithNullInput_ThrowsDomainException()
    {
        // Arrange / Act
        var act = () => RefreshTokenHash.Create(null);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(RefreshTokenErrors.HashInvalid.Code);
    }

    [Fact]
    public void Create_WithEmptyString_ThrowsDomainException()
    {
        // Arrange / Act
        var act = () => RefreshTokenHash.Create(string.Empty);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(RefreshTokenErrors.HashInvalid.Code);
    }

    [Fact]
    public void Create_WithWhitespaceOnly_ThrowsDomainException()
    {
        // Arrange / Act
        var act = () => RefreshTokenHash.Create("   ");

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(RefreshTokenErrors.HashInvalid.Code);
    }

    // ── Length boundary — exact enforcement ───────────────────────────────────

    [Fact]
    public void Create_WithOneLessThanExpectedLength_ThrowsDomainException()
    {
        // Arrange — 63 chars (ExpectedLength - 1)
        var raw = new string('a', RefreshTokenHash.ExpectedLength - 1);
        raw.Length.ShouldBe(63);

        // Act
        var act = () => RefreshTokenHash.Create(raw);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(RefreshTokenErrors.HashInvalid.Code);
    }

    [Fact]
    public void Create_WithOneMoreThanExpectedLength_ThrowsDomainException()
    {
        // Arrange — 65 chars (ExpectedLength + 1)
        var raw = new string('a', RefreshTokenHash.ExpectedLength + 1);
        raw.Length.ShouldBe(65);

        // Act
        var act = () => RefreshTokenHash.Create(raw);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(RefreshTokenErrors.HashInvalid.Code);
    }

    [Fact]
    public void Create_WithShortString_ThrowsDomainException()
    {
        // Arrange — far below expected length
        var act = () => RefreshTokenHash.Create("short");

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(RefreshTokenErrors.HashInvalid.Code);
    }

    // ── Equality (record semantics) ──────────────────────────────────────────

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        // Arrange
        var raw = new string('a', RefreshTokenHash.ExpectedLength);
        var a = RefreshTokenHash.Create(raw);
        var b = RefreshTokenHash.Create(raw);

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void Equality_DifferentValues_AreNotEqual()
    {
        // Arrange
        var a = RefreshTokenHash.Create(new string('a', RefreshTokenHash.ExpectedLength));
        var b = RefreshTokenHash.Create(new string('b', RefreshTokenHash.ExpectedLength));

        // Assert
        a.ShouldNotBe(b);
    }
}
