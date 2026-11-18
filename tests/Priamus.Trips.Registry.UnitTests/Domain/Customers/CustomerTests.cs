using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Customers.Events;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;
using Priamus.Trips.Registry.UnitTests.TestData;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Domain.Customers;

public sealed class CustomerTests
{
    private static readonly UserId Actor = new(new Guid("a0000000-0000-0000-0000-000000000001"));
    private static readonly UserId OtherActor = new(new Guid("a0000000-0000-0000-0000-000000000002"));

    private static FakeClock NewClock() => new(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc));

    private static Customer NewCustomer(
        FakeClock clock,
        string? companyName = "Comune di Penne",
        string? vatNumber = "01455280683",
        string? taxCode = "01455280683",
        string? address = "Corso Alessandrini 1, Penne") =>
        Customer.Create(
            CustomerId.New(),
            companyName,
            vatNumber is null ? null : VatNumber.Create(vatNumber),
            taxCode is null ? null : TaxCode.Create(taxCode),
            address,
            clock,
            Actor);

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidArguments_ReturnsActiveCustomerWithAuditFields()
    {
        var clock = NewClock();
        var id = CustomerId.New();

        var customer = Customer.Create(id, "  Comune di Penne ", VatNumber.Create("IT 01455280683"),
            TaxCode.Create("mrcgli72d45g438k"), "  Corso Alessandrini 1  ", clock, Actor);

        customer.Id.ShouldBe(id);
        customer.CompanyName.ShouldBe("Comune di Penne");
        customer.VatNumber!.Value.ShouldBe("01455280683");
        customer.TaxCode!.Value.ShouldBe("MRCGLI72D45G438K");
        customer.Address.ShouldBe("Corso Alessandrini 1");
        customer.Status.ShouldBe(CustomerStatus.Active);
        customer.CreatedAt.ShouldBe(clock.UtcNow);
        customer.CreatedBy.ShouldBe(Actor);
        customer.LastModifiedAt.ShouldBe(clock.UtcNow);
        customer.LastModifiedBy.ShouldBe(Actor);
        customer.Version.ShouldBe(0);
    }

    [Fact]
    public void Create_WithValidArguments_RaisesCustomerCreatedWithFullState()
    {
        var clock = NewClock();

        var customer = NewCustomer(clock);

        var evt = customer.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<CustomerCreated>();
        evt.CustomerId.ShouldBe(customer.Id);
        evt.CompanyName.ShouldBe("Comune di Penne");
        evt.VatNumber!.Value.ShouldBe("01455280683");
        evt.TaxCode!.Value.ShouldBe("01455280683");
        evt.Address.ShouldBe("Corso Alessandrini 1, Penne");
        evt.Status.ShouldBe(CustomerStatus.Active);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Create_WithoutOptionalData_StoresNone()
    {
        var customer = NewCustomer(NewClock(), vatNumber: null, taxCode: null, address: null);

        customer.VatNumber.ShouldBeNull();
        customer.TaxCode.ShouldBeNull();
        customer.Address.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void Create_WithBlankCompanyName_ThrowsCompanyNameInvalid(string? companyName)
    {
        var act = () => NewCustomer(NewClock(), companyName: companyName);

        act.ShouldThrow<DomainException>().Error.ShouldBe(CustomerErrors.CompanyNameInvalid);
    }

    [Fact]
    public void Create_WithCompanyNameOf150CharsAfterTrim_Succeeds()
    {
        var customer = NewCustomer(NewClock(), companyName: " " + new string('x', 150) + " ");

        customer.CompanyName.Length.ShouldBe(150);
    }

    [Fact]
    public void Create_WithCompanyNameOf151Chars_ThrowsCompanyNameInvalid()
    {
        var act = () => NewCustomer(NewClock(), companyName: new string('x', 151));

        act.ShouldThrow<DomainException>().Error.ShouldBe(CustomerErrors.CompanyNameInvalid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void Create_WithBlankAddress_StoresNone(string? address)
    {
        NewCustomer(NewClock(), address: address).Address.ShouldBeNull();
    }

    [Fact]
    public void Create_WithAddressOf200CharsAfterTrim_Succeeds()
    {
        var customer = NewCustomer(NewClock(), address: " " + new string('x', 200) + " ");

        customer.Address!.Length.ShouldBe(200);
    }

    [Fact]
    public void Create_WithAddressOf201Chars_ThrowsAddressInvalid()
    {
        var act = () => NewCustomer(NewClock(), address: new string('x', 201));

        act.ShouldThrow<DomainException>().Error.ShouldBe(CustomerErrors.AddressInvalid);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public void Update_WithValidArguments_ChangesDataStampsAuditBumpsVersionAndRaisesEvent()
    {
        var clock = NewClock();
        var customer = NewCustomer(clock);
        customer.ClearDomainEvents();
        clock.Advance(TimeSpan.FromHours(1));

        customer.Update("  Trasporti Rossi ", null, TaxCode.Create("rssmra80a01h501u"), "  ", clock, OtherActor);

        customer.CompanyName.ShouldBe("Trasporti Rossi");
        customer.VatNumber.ShouldBeNull();
        customer.TaxCode!.Value.ShouldBe("RSSMRA80A01H501U");
        customer.Address.ShouldBeNull();
        customer.LastModifiedAt.ShouldBe(clock.UtcNow);
        customer.LastModifiedBy.ShouldBe(OtherActor);
        customer.CreatedBy.ShouldBe(Actor);
        customer.Version.ShouldBe(1);
        var evt = customer.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<CustomerUpdated>();
        evt.CustomerId.ShouldBe(customer.Id);
        evt.CompanyName.ShouldBe("Trasporti Rossi");
        evt.VatNumber.ShouldBeNull();
        evt.TaxCode!.Value.ShouldBe("RSSMRA80A01H501U");
        evt.Address.ShouldBeNull();
        evt.Status.ShouldBe(CustomerStatus.Active);
    }

    [Fact]
    public void Update_OnADeactivatedCustomer_KeepsItDeactivated()
    {
        var clock = NewClock();
        var customer = NewCustomer(clock);
        customer.Deactivate(clock, Actor);
        customer.ClearDomainEvents();

        customer.Update("Nuovo nome", customer.VatNumber, customer.TaxCode, customer.Address, clock, Actor);

        customer.Status.ShouldBe(CustomerStatus.Deactivated);
        customer.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<CustomerUpdated>()
            .Status.ShouldBe(CustomerStatus.Deactivated);
    }

    [Fact]
    public void Update_WithInvalidCompanyName_ThrowsAndChangesNothing()
    {
        var clock = NewClock();
        var customer = NewCustomer(clock);
        customer.ClearDomainEvents();

        var act = () => customer.Update(" ", null, null, "Altro indirizzo", clock, Actor);

        act.ShouldThrow<DomainException>().Error.ShouldBe(CustomerErrors.CompanyNameInvalid);
        customer.CompanyName.ShouldBe("Comune di Penne");
        customer.VatNumber.ShouldNotBeNull();
        customer.Address.ShouldBe("Corso Alessandrini 1, Penne");
        customer.Version.ShouldBe(0);
        customer.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Update_WithTooLongAddress_ThrowsAndChangesNothing()
    {
        var clock = NewClock();
        var customer = NewCustomer(clock);
        customer.ClearDomainEvents();

        var act = () => customer.Update("Altro nome", null, null, new string('x', 201), clock, Actor);

        act.ShouldThrow<DomainException>().Error.ShouldBe(CustomerErrors.AddressInvalid);
        customer.CompanyName.ShouldBe("Comune di Penne");
        customer.Address.ShouldBe("Corso Alessandrini 1, Penne");
        customer.Version.ShouldBe(0);
        customer.DomainEvents.ShouldBeEmpty();
    }

    // ── Deactivate / Reactivate ──────────────────────────────────────────────

    [Fact]
    public void Deactivate_OnActiveCustomer_ChangesOnlyStatusBumpsVersionAndRaisesEvent()
    {
        var clock = NewClock();
        var customer = NewCustomer(clock);
        customer.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(5));

        customer.Deactivate(clock, OtherActor);

        customer.Status.ShouldBe(CustomerStatus.Deactivated);
        customer.CompanyName.ShouldBe("Comune di Penne");
        customer.VatNumber!.Value.ShouldBe("01455280683");
        customer.TaxCode!.Value.ShouldBe("01455280683");
        customer.Address.ShouldBe("Corso Alessandrini 1, Penne");
        customer.Version.ShouldBe(1);
        customer.LastModifiedAt.ShouldBe(clock.UtcNow);
        customer.LastModifiedBy.ShouldBe(OtherActor);
        var evt = customer.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<CustomerDeactivated>();
        evt.CustomerId.ShouldBe(customer.Id);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Deactivate_OnDeactivatedCustomer_ChangesNothing()
    {
        var clock = NewClock();
        var customer = NewCustomer(clock);
        customer.Deactivate(clock, Actor);
        customer.ClearDomainEvents();
        var modifiedAt = customer.LastModifiedAt;
        clock.Advance(TimeSpan.FromMinutes(5));

        customer.Deactivate(clock, OtherActor);

        customer.Status.ShouldBe(CustomerStatus.Deactivated);
        customer.Version.ShouldBe(1);
        customer.LastModifiedAt.ShouldBe(modifiedAt);
        customer.LastModifiedBy.ShouldBe(Actor);
        customer.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Reactivate_OnDeactivatedCustomer_ChangesOnlyStatusBumpsVersionAndRaisesEvent()
    {
        var clock = NewClock();
        var customer = NewCustomer(clock);
        customer.Deactivate(clock, Actor);
        customer.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(5));

        customer.Reactivate(clock, OtherActor);

        customer.Status.ShouldBe(CustomerStatus.Active);
        customer.CompanyName.ShouldBe("Comune di Penne");
        customer.VatNumber!.Value.ShouldBe("01455280683");
        customer.TaxCode!.Value.ShouldBe("01455280683");
        customer.Address.ShouldBe("Corso Alessandrini 1, Penne");
        customer.Version.ShouldBe(2);
        customer.LastModifiedAt.ShouldBe(clock.UtcNow);
        customer.LastModifiedBy.ShouldBe(OtherActor);
        var evt = customer.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<CustomerReactivated>();
        evt.CustomerId.ShouldBe(customer.Id);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Reactivate_OnActiveCustomer_ChangesNothing()
    {
        var clock = NewClock();
        var customer = NewCustomer(clock);
        customer.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(5));

        customer.Reactivate(clock, OtherActor);

        customer.Status.ShouldBe(CustomerStatus.Active);
        customer.Version.ShouldBe(0);
        customer.LastModifiedAt.ShouldBe(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc));
        customer.LastModifiedBy.ShouldBe(Actor);
        customer.DomainEvents.ShouldBeEmpty();
    }
}
