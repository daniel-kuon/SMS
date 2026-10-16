namespace Sms.Domain;

/// <summary>Spec 08 §8.3.</summary>
public static class VisibilityRules
{
    /// <summary>Effective visibility of an item inside a trip: min(item, trip, ship private ? Crew : Public).</summary>
    public static Visibility Effective(Visibility item, Visibility trip, Visibility ship)
    {
        var shipCap = ship == Visibility.Private ? Visibility.Crew : Visibility.Public;
        return (Visibility)Math.Min((int)item, Math.Min((int)trip, (int)shipCap));
    }

    public static bool CanView(Visibility effective, bool isAuthor, bool isAcceptedCrew, bool isAdmin) => effective switch
    {
        Visibility.Public => true,
        Visibility.Crew => isAcceptedCrew || isAuthor || isAdmin,
        _ => isAuthor || isAdmin,
    };
}
