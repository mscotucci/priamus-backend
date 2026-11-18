namespace Priamus.Trips.Registry.Domain.Contracts;

/// <summary>
/// A document a <see cref="Contract"/> asks for. The declaration order is the canonical order of
/// a set of required documents (PRD §5.4).
/// </summary>
public enum RequiredDocument
{
    /// <summary>DDT: delivery note.</summary>
    DeliveryNote,

    /// <summary>Bolla: delivery slip.</summary>
    DeliverySlip,

    /// <summary>FIR: waste form.</summary>
    WasteForm,

    /// <summary>Riepilogo di fine giornata: daily summary.</summary>
    DailySummary,
}
