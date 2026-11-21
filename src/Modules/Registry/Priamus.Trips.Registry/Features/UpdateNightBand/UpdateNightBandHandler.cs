using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Registry.Domain.NightBands;
using Priamus.Trips.Registry.Domain.Pricing;
using Priamus.Trips.Registry.Features.Validation;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Features.UpdateNightBand;

/// <summary>
/// Handles <c>PUT /api/registry/night-band</c>: replaces the system night band (PRD §5.8).
/// </summary>
/// <remarks>
/// The version the client read becomes the original value of the concurrency token, so a band
/// changed in the meantime fails the save with <see cref="DbUpdateConcurrencyException"/>, answered
/// as <see cref="NightBandErrors.ConcurrentUpdate"/>. Sending the band already stored writes nothing.
/// </remarks>
internal sealed class UpdateNightBandHandler
{
    private readonly RegistryDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<UpdateNightBandHandler> _logger;

    public UpdateNightBandHandler(
        RegistryDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        ILogger<UpdateNightBandHandler> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Updates the night band from the request.
    /// </summary>
    public async Task<Result> HandleAsync(UpdateNightBandRequest request, CancellationToken cancellationToken)
    {
        var band = NightBand.Create(
            TimeOfDayFormatAttribute.Parse(request.Start)!.Value,
            TimeOfDayFormatAttribute.Parse(request.End)!.Value);
        if (band.IsFailure)
        {
            return band.Error;
        }

        var setting = await _db.NightBandSettings.SingleAsync(cancellationToken);
        var actorId = _currentUser.UserId;

        setting.Update(band.Value, _clock, actorId);

        _db.Entry(setting).Property(s => s.Version).OriginalValue = request.Version!.Value;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return NightBandErrors.ConcurrentUpdate;
        }

        _logger.LogInformation("Night band updated by actor {ActorId}", actorId.Value);

        return Result.Success();
    }
}
