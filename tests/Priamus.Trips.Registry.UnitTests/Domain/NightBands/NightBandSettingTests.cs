using Priamus.Trips.Registry.Domain.NightBands;
using Priamus.Trips.Registry.Domain.NightBands.Events;
using Priamus.Trips.Registry.Domain.Pricing;
using Priamus.Trips.Registry.UnitTests.TestData;
using Priamus.Trips.SharedKernel.Security;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Domain.NightBands;

public sealed class NightBandSettingTests
{
    private static readonly UserId Actor = new(new Guid("a0000000-0000-0000-0000-000000000001"));
    private static readonly UserId OtherActor = new(new Guid("a0000000-0000-0000-0000-000000000002"));

    private static FakeClock NewClock() => new(new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc));

    private static NightBand Band(int startHour, int endHour) =>
        NightBand.Create(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0)).Value;

    private static NightBandSetting NewSetting(FakeClock clock) =>
        NightBandSetting.Create(NightBand.Default, clock, Actor);

    [Fact]
    public void Create_WithTheDefaultBand_HoldsItWithoutEventAndVersionZero()
    {
        var clock = NewClock();

        var setting = NightBandSetting.Create(NightBand.Default, clock, Actor);

        setting.Id.ShouldBe(NightBandSetting.SingleId);
        setting.Band.ShouldBe(NightBand.Default);
        setting.LastModifiedAt.ShouldBe(clock.UtcNow);
        setting.LastModifiedBy.ShouldBe(Actor);
        setting.Version.ShouldBe(0);
        setting.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Update_WithAnotherBand_StoresItBumpsTheVersionAndRaisesOneEvent()
    {
        var clock = NewClock();
        var setting = NewSetting(clock);
        clock.Advance(TimeSpan.FromHours(1));
        var band = Band(20, 5);

        setting.Update(band, clock, OtherActor);

        setting.Band.ShouldBe(band);
        setting.Version.ShouldBe(1);
        setting.LastModifiedAt.ShouldBe(clock.UtcNow);
        setting.LastModifiedBy.ShouldBe(OtherActor);
        var raised = setting.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<NightBandUpdated>();
        raised.SettingId.ShouldBe(NightBandSetting.SingleId);
        raised.Band.ShouldBe(band);
        raised.OccurredAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Update_WithTheBandAlreadySaved_ChangesNothingAndRaisesNoEvent()
    {
        var clock = NewClock();
        var setting = NewSetting(clock);
        var savedAt = setting.LastModifiedAt;
        clock.Advance(TimeSpan.FromHours(1));

        setting.Update(Band(22, 6), clock, OtherActor);

        setting.Version.ShouldBe(0);
        setting.LastModifiedAt.ShouldBe(savedAt);
        setting.LastModifiedBy.ShouldBe(Actor);
        setting.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Update_TwiceWithDifferentBands_IncrementsTheVersionEachTime()
    {
        var clock = NewClock();
        var setting = NewSetting(clock);

        setting.Update(Band(21, 5), clock, Actor);
        setting.Update(Band(23, 7), clock, Actor);

        setting.Version.ShouldBe(2);
        setting.Band.ShouldBe(Band(23, 7));
    }
}
