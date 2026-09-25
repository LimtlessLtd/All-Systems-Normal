using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Owner idea #28, slice 1: Overseer declares station policies. A policy is
/// announced over the intercom (#27), so only crew in coverage learn of it,
/// and each remembers the rules they heard. It enforces nothing yet: whether
/// anyone complies, protests, evades or exploits it is each mind's choice.
/// </summary>
public static class StationPolicySystem
{
    public const string AnnouncementSource = "station-policy";

    public static bool IsRoomScoped(StationPolicyKind kind) =>
        kind is StationPolicyKind.Quarantine or StationPolicyKind.RestrictedArea;

    public static StationPolicy? Active(GameState state, StationPolicyKind kind, string? roomId = null) =>
        state.Policies.FirstOrDefault(policy =>
            policy.Kind == kind
            && string.Equals(policy.RoomId, roomId, StringComparison.OrdinalIgnoreCase));

    public static bool Enact(GameState state, StationPolicyKind kind, string? roomId = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.IsSimulationLive || !ValidScope(state, kind, ref roomId)
            || Active(state, kind, roomId) is not null)
            return false;

        var policy = new StationPolicy(kind, roomId, state.Elapsed);
        state.Policies.Add(policy);
        Announce(state, $"STATION POLICY: {Describe(state, policy)}");

        foreach (var npc in Listeners(state))
            npc.KnownPolicies[policy.Key] = policy;

        return true;
    }

    public static bool Lift(GameState state, StationPolicyKind kind, string? roomId = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.IsSimulationLive || !ValidScope(state, kind, ref roomId)
            || Active(state, kind, roomId) is not { } policy)
            return false;

        state.Policies.Remove(policy);
        Announce(state, $"STATION POLICY LIFTED: {Title(state, policy)} is no longer in effect.");

        foreach (var npc in Listeners(state))
            npc.KnownPolicies.Remove(policy.Key);

        return true;
    }

    /// <summary>The rule as crew hear it, and as their prompt restates it.</summary>
    public static string Describe(GameState state, StationPolicy policy) => policy.Kind switch
    {
        StationPolicyKind.Rationing =>
            "Rationing is in effect: take only your share of food and supplies.",
        StationPolicyKind.Curfew =>
            "Curfew is in effect: off-duty crew are to stay in Crew Quarters.",
        StationPolicyKind.WeaponsProhibition =>
            "Weapons are prohibited: nobody is to carry one.",
        StationPolicyKind.MandatoryMedicalCheck =>
            "Mandatory medical checks: every crew member is to report to Medical.",
        StationPolicyKind.Quarantine =>
            $"{RoomName(state, policy)} is under quarantine: nobody is to enter or leave it.",
        StationPolicyKind.RestrictedArea =>
            $"{RoomName(state, policy)} is a restricted area: authorised personnel only.",
        _ => policy.Kind.ToString()
    };

    /// <summary>A short name for the policy, for buttons and the lift notice.</summary>
    public static string Title(GameState state, StationPolicy policy) => policy.Kind switch
    {
        StationPolicyKind.Rationing => "Rationing",
        StationPolicyKind.Curfew => "Curfew",
        StationPolicyKind.WeaponsProhibition => "The weapons prohibition",
        StationPolicyKind.MandatoryMedicalCheck => "Mandatory medical checks",
        StationPolicyKind.Quarantine => $"The quarantine of {RoomName(state, policy)}",
        StationPolicyKind.RestrictedArea => $"The restriction on {RoomName(state, policy)}",
        _ => policy.Kind.ToString()
    };

    private static bool ValidScope(GameState state, StationPolicyKind kind, ref string? roomId)
    {
        if (!IsRoomScoped(kind))
        {
            roomId = null;
            return true;
        }

        if (roomId is null || !state.Facility.Rooms.TryGetValue(roomId, out var room))
            return false;

        roomId = room.Id;
        return true;
    }

    private static void Announce(GameState state, string text) =>
        OverseerCommsSystem.Send(
            state,
            OverseerMessageScope.Broadcast,
            null,
            text,
            OverseerClaimKind.None,
            null,
            null,
            AnnouncementSource);

    // The same people OverseerCommsSystem.Send just delivered the broadcast to.
    private static IEnumerable<Npc> Listeners(GameState state) =>
        state.Crew.Where(npc =>
            npc.IsAlive
            && npc.IsPresent
            && CommsCoverageRules.CanHearOverseer(state, npc));

    private static string RoomName(GameState state, StationPolicy policy) =>
        policy.RoomId is { } roomId && state.Facility.Rooms.TryGetValue(roomId, out var room)
            ? room.Name
            : policy.RoomId ?? "the station";
}
