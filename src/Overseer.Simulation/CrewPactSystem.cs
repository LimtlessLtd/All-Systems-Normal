using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Deterministic lifecycle for interpersonal promises. This deliberately does
/// not decide whether an NPC should make, keep or break a pact; cognition owns
/// that choice. Simulation only validates parties, records state and applies
/// concrete relationship/memory consequences after the choice is made.
/// </summary>
public static class CrewPactSystem
{
    public static bool TryCreate(
        GameState state,
        Guid promisorId,
        Guid promiseeId,
        string promiseText,
        out CrewPact? pact,
        out string reason)
    {
        pact = null;

        if (promisorId == promiseeId)
        {
            reason = "A pact needs two different crew members.";
            return false;
        }

        var promisor = state.Crew.FirstOrDefault(npc => npc.Id == promisorId && npc.IsAlive);
        var promisee = state.Crew.FirstOrDefault(npc => npc.Id == promiseeId && npc.IsAlive);
        if (promisor is null || promisee is null)
        {
            reason = "Both crew members must be alive when the pact is made.";
            return false;
        }

        var normalized = promiseText.Trim();
        if (normalized.Length == 0)
        {
            reason = "A pact needs a concrete promise.";
            return false;
        }

        if (state.CrewPacts.Any(existing =>
            existing.Status == CrewPactStatus.Active
            && existing.PromisorId == promisorId
            && existing.PromiseeId == promiseeId
            && existing.PromiseText.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            reason = "That promise is already active.";
            return false;
        }

        pact = new CrewPact
        {
            Id = $"pact-{state.NextCrewPactSequence++:D4}",
            PromisorId = promisorId,
            PromiseeId = promiseeId,
            PromiseText = normalized,
            CreatedAt = state.Elapsed
        };
        state.CrewPacts.Add(pact);

        promisor.Memories.Add(new Memory(
            $"I promised {promisee.Name}: {normalized}",
            state.Elapsed,
            0.65));
        promisee.Memories.Add(new Memory(
            $"{promisor.Name} promised me: {normalized}",
            state.Elapsed,
            0.65));
        state.EventLog.Add($"{promisor.Name} made a promise to {promisee.Name}: {normalized}");

        reason = "Pact recorded.";
        return true;
    }

    public static bool TryFulfill(
        GameState state,
        string pactId,
        string? settlementNote,
        out string reason) =>
        TrySettle(state, pactId, CrewPactStatus.Fulfilled, settlementNote, out reason);

    public static bool TryBreak(
        GameState state,
        string pactId,
        string? settlementNote,
        out string reason) =>
        TrySettle(state, pactId, CrewPactStatus.Broken, settlementNote, out reason);

    private static bool TrySettle(
        GameState state,
        string pactId,
        CrewPactStatus status,
        string? settlementNote,
        out string reason)
    {
        var pact = state.CrewPacts.FirstOrDefault(candidate =>
            candidate.Id.Equals(pactId, StringComparison.OrdinalIgnoreCase));
        if (pact is null)
        {
            reason = "Pact not found.";
            return false;
        }

        if (pact.Status != CrewPactStatus.Active)
        {
            reason = "That pact is already settled.";
            return false;
        }

        var promisor = state.Crew.FirstOrDefault(npc => npc.Id == pact.PromisorId);
        var promisee = state.Crew.FirstOrDefault(npc => npc.Id == pact.PromiseeId);
        if (promisor is null || promisee is null)
        {
            reason = "A pact party is no longer part of this station state.";
            return false;
        }

        pact.Status = status;
        pact.SettledAt = state.Elapsed;
        pact.SettlementNote = string.IsNullOrWhiteSpace(settlementNote)
            ? null
            : settlementNote.Trim();

        var relationship = GetOrCreateRelationship(promisee, promisor);
        if (status == CrewPactStatus.Fulfilled)
        {
            relationship.Trust = Math.Clamp(relationship.Trust + 8, 0, 100);
            relationship.Affinity = Math.Clamp(relationship.Affinity + 3, 0, 100);

            promisor.Memories.Add(new Memory(
                $"I kept my promise to {promisee.Name}: {pact.PromiseText}",
                state.Elapsed,
                0.75));
            promisee.Memories.Add(new Memory(
                $"{promisor.Name} kept their promise to me: {pact.PromiseText}",
                state.Elapsed,
                0.8));
            state.EventLog.Add($"{promisor.Name} fulfilled a promise to {promisee.Name}: {pact.PromiseText}");
            reason = "Pact fulfilled.";
            return true;
        }

        relationship.Trust = Math.Clamp(relationship.Trust - 15, 0, 100);
        relationship.Resentment = Math.Clamp(relationship.Resentment + 12, 0, 100);
        promisee.Stress = Math.Clamp(promisee.Stress + 4, 0, 100);

        promisor.Memories.Add(new Memory(
            $"I broke my promise to {promisee.Name}: {pact.PromiseText}",
            state.Elapsed,
            0.8));
        promisee.Memories.Add(new Memory(
            $"{promisor.Name} broke their promise to me: {pact.PromiseText}",
            state.Elapsed,
            0.9));
        state.EventLog.Add($"{promisor.Name} broke a promise to {promisee.Name}: {pact.PromiseText}");

        reason = "Pact broken.";
        return true;
    }

    private static Relationship GetOrCreateRelationship(Npc observer, Npc other)
    {
        if (observer.Relationships.TryGetValue(other.Name, out var existing))
            return existing;

        var created = new Relationship { PersonName = other.Name };
        observer.Relationships[other.Name] = created;
        return created;
    }
}
