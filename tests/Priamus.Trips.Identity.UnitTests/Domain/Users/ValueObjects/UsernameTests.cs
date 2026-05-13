using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.SharedKernel.Domain;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.UnitTests.Domain.Users.ValueObjects;

public sealed class UsernameTests
{
    // ── Valid construction ────────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidUsername_ReturnsValueObject()
    {
        // Arrange / Act
        var username = Username.Create("alice42");

        // Assert
        username.Value.ShouldBe("alice42");
    }

    [Fact]
    public void Create_TrimsLeadingAndTrailingWhitespace()
    {
        // Arrange / Act
        var username = Username.Create("  alice42  ");

        // Assert
        username.Value.ShouldBe("alice42");
    }

    [Fact]
    public void Create_NormalizesToLowerInvariant()
    {
        // Arrange / Act
        var username = Username.Create("ALIce42");

        // Assert
        username.Value.ShouldBe("alice42");
    }

    [Fact]
    public void Create_TrimsAndLowers_CombinedNormalization()
    {
        // Arrange / Act
        var username = Username.Create("  ALICE.Smith_42  ");

        // Assert
        username.Value.ShouldBe("alice.smith_42");
    }

    [Theory]
    [InlineData("abc")]          // exact minimum
    [InlineData("abcde")]
    [InlineData("a1b")]
    [InlineData("user.name")]
    [InlineData("user_name")]
    [InlineData("user-name")]
    [InlineData("0abc")]         // starts with digit
    public void Create_WithAllowedCharactersAndShape_Succeeds(string raw)
    {
        // Arrange / Act
        var username = Username.Create(raw);

        // Assert
        username.ShouldNotBeNull();
        username.Value.ShouldBe(raw.Trim().ToLowerInvariant());
    }

    // ── Length boundaries ─────────────────────────────────────────────────────

    [Fact]
    public void Create_WithExactMinimumLength_Succeeds()
    {
        // Arrange — 3 chars (MinLength)
        var raw = "abc";
        raw.Length.ShouldBe(Username.MinLength);

        // Act
        var username = Username.Create(raw);

        // Assert
        username.Value.ShouldBe("abc");
    }

    [Fact]
    public void Create_WithExactMaximumLength_Succeeds()
    {
        // Arrange — 64 chars (MaxLength)
        var raw = new string('a', Username.MaxLength);
        raw.Length.ShouldBe(Username.MaxLength);

        // Act
        var username = Username.Create(raw);

        // Assert
        username.Value.ShouldBe(raw);
    }

    [Fact]
    public void Create_WithLengthBelowMinimumAfterNormalization_ThrowsDomainException()
    {
        // Arrange — 2 chars (below MinLength = 3)
        var raw = "ab";

        // Act
        var act = () => Username.Create(raw);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(UserErrors.UsernameInvalid.Code);
    }

    [Fact]
    public void Create_WithLengthAboveMaximumAfterNormalization_ThrowsDomainException()
    {
        // Arrange — 65 chars (above MaxLength = 64)
        var raw = new string('a', Username.MaxLength + 1);

        // Act
        var act = () => Username.Create(raw);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(UserErrors.UsernameInvalid.Code);
    }

    [Fact]
    public void Create_WithWhitespaceOnlyInput_ThrowsDomainExceptionDueToMinLength()
    {
        // Arrange — whitespace-only normalizes to empty string → below MinLength
        var raw = "   ";

        // Act
        var act = () => Username.Create(raw);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(UserErrors.UsernameInvalid.Code);
    }

    // ── Null input ────────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithNullInput_ThrowsDomainException()
    {
        // Arrange / Act
        var act = () => Username.Create(null);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(UserErrors.UsernameInvalid.Code);
    }

    // ── Leading non-alphanumeric ──────────────────────────────────────────────

    [Theory]
    [InlineData(".foo")]         // leading dot
    [InlineData("_bar")]         // leading underscore
    [InlineData("-baz")]         // leading hyphen
    public void Create_WithLeadingNonAlphanumeric_ThrowsDomainException(string raw)
    {
        // Arrange / Act
        var act = () => Username.Create(raw);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(UserErrors.UsernameInvalid.Code);
    }

    // ── Forbidden characters ─────────────────────────────────────────────────

    [Theory]
    [InlineData("foo!bar")]      // exclamation mark
    [InlineData("foo bar")]      // space inside
    [InlineData("foo@bar")]      // at sign
    [InlineData("foo#bar")]      // hash
    [InlineData("foo/bar")]      // slash
    public void Create_WithForbiddenCharacter_ThrowsDomainException(string raw)
    {
        // Arrange / Act
        var act = () => Username.Create(raw);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(UserErrors.UsernameInvalid.Code);
    }

    // ── Equality (record semantics) ──────────────────────────────────────────

    [Fact]
    public void Equality_SameNormalizedValue_AreEqual()
    {
        // Arrange
        var a = Username.Create("alice42");
        var b = Username.Create("ALICE42"); // normalizes to same value

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void Equality_DifferentValues_AreNotEqual()
    {
        // Arrange
        var a = Username.Create("alice42");
        var b = Username.Create("bob99");

        // Assert
        a.ShouldNotBe(b);
    }

    // ── ToString ─────────────────────────────────────────────────────────────

    [Fact]
    public void ToString_ReturnsNormalizedValue()
    {
        // Arrange
        var username = Username.Create("Alice42");

        // Act / Assert
        username.ToString().ShouldBe("alice42");
    }
}
