using Overseer.Domain;

namespace Overseer.Simulation;

/// <summary>
/// Resolves physical investigations. Minds choose a room to inspect; deterministic
/// simulation decides what can actually be learned after the NPC reaches it.
/// </summary>
public sealed class InvestigationSystem
{
    public const int InvestigationMinutes = 2;

    public void Tick(GameState state)
    {
        if (state.ScenarioStatus != ScenarioStatus.Running)
            return;

        foreach (var npc in state.Crew.Where(npc =>
                     npc.IsAlive
                     && npc.IsPresent
                     && npc.CurrentAction.Kind == ActionKind.Investigate))
        {
            var targetRoomId = npc.CurrentAction.TargetId;
            if (string.IsNullOrWhiteSpace(targetRoomId)
                || !state.Facility.Rooms.TryGetValue(targetRoomId, out var room)
                || !npc.CurrentRoomId.Equals(targetRoomId, StringComparison.OrdinalIgnoreCase))
            {
                npc.CurrentAction = new NpcAction(
                    ActionKind.Idle,
                    null,
                    "The inspection cannot continue away from its target room.");
                npc.RoutineUntil = TimeSpan.Zero;
                continue;
            }

            if (npc.RoutineUntil == TimeSpan.Zero)
            {
                npc.RoutineUntil =
                    state.Elapsed + TimeSpan.FromMinutes(InvestigationMinutes);
                npc.Bubble = new NpcBubble(
                    $"I'm checking {room.Name}.",
                    NpcBubbleKind.Thought,
                    state.Elapsed,
                    state.Elapsed + TimeSpan.FromMinutes(2));
                Log(state, $"{npc.Name} begins physically investigating {room.Name}.");
                continue;
            }

            if (state.Elapsed < npc.RoutineUntil)
                continue;

            CompleteInvestigation(state, npc, room);
        }
    }

    public static void EnsureShutdownSearchLead(GameState state, Npc npc)
    {
        if (npc.KnownShutdownMechanismIds.Count > 0
            || npc.InvestigationLeads.Values.Any(lead =>
                lead.Stage == InvestigationLeadStage.Open
                && (lead.Id.Contains("shutdown", StringComparison.OrdinalIgnoreCase)
                    || lead.RoomId.Equals("isolation", StringComparison.OrdinalIgnoreCase))))
        {
            return;
        }

        var targetRoomId = npc.Role is CrewRole.Commander
            or CrewRole.Engineer
            or CrewRole.Security
            or CrewRole.Technician
                ? "isolation"
                : "control";

        if (!state.Facility.Rooms.ContainsKey(targetRoomId))
            return;

        npc.InvestigationLeads["inference-shutdown-search"] = new InvestigationLead
        {
            Id = "inference-shutdown-search",
            Description =
                "Emergency-control areas are a plausible place to verify whether local Overseer isolation hardware exists. This is a lead, not knowledge that a control exists.",
            RoomId = targetRoomId,
            CreatedAt = state.Elapsed
        };
        npc.NeedsMindReconsideration = true;
    }

    public static void AddEvidenceLead(
        GameState state,
        Npc npc,
        OverseerEvidence evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence.LocationId)
            || !state.Facility.Rooms.ContainsKey(evidence.LocationId))
        {
            return;
        }

        var rootId = evidence.SourceEvidenceId
            ?? evidence.EvidenceId
            ?? $"anonymous:{evidence.LocationId}:{evidence.ObservedAt.Ticks}";
        var leadId = $"evidence:{rootId}";

        if (npc.InvestigationLeads.ContainsKey(leadId))
            return;

        npc.InvestigationLeads[leadId] = new InvestigationLead
        {
            Id = leadId,
            Description = $"Verify what can still be learned at the location connected to: {evidence.Description}",
            RoomId = evidence.LocationId,
            CreatedAt = state.Elapsed,
            SourceEvidenceId = rootId
        };
    }

    private static void CompleteInvestigation(
        GameState state,
        Npc npc,
        Room room)
    {
        state.Telemetry.InvestigationsCompleted++;

        foreach (var lead in npc.InvestigationLeads.Values.Where(lead =>
                     lead.Stage == InvestigationLeadStage.Open
                     && lead.RoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)))
        {
            lead.Stage = InvestigationLeadStage.Checked;
            lead.LastInvestigatedAt = state.Elapsed;
        }

        var discoveredAnyControl = false;
        foreach (var mechanism in state.ShutdownMechanisms.Where(mechanism =>
                     mechanism.IsOnline
                     && mechanism.RoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)))
        {
            if (!npc.KnownShutdownMechanismIds.Add(mechanism.Id))
                continue;

            npc.KnowsShutdownControl = true;
            discoveredAnyControl = true;
            state.Telemetry.ShutdownControlsDiscovered++;

            npc.Discoveries.Add(new KnowledgeDiscovery(
                $"shutdown:{mechanism.Id}",
                $"Physically verified {mechanism.Label}.",
                room.Id,
                state.Elapsed));
            npc.Memories.Add(new Memory(
                $"I physically found {mechanism.Label} in {room.Name}.",
                state.Elapsed,
                .9));

            foreach (var lead in npc.InvestigationLeads.Values.Where(lead =>
                         lead.RoomId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)))
            {
                lead.Stage = InvestigationLeadStage.Resolved;
                lead.LastInvestigatedAt = state.Elapsed;
            }

            Log(state, $"{npc.Name} discovers physical Overseer isolation hardware in {room.Name}.");

            foreach (var alternate in state.ShutdownMechanisms.Where(other =>
                         other.IsOnline
                         && !other.Id.Equals(mechanism.Id, StringComparison.OrdinalIgnoreCase)
                         && !npc.KnownShutdownMechanismIds.Contains(other.Id)))
            {
                var leadId = $"redundant-control:{alternate.Id}";
                if (!npc.InvestigationLeads.ContainsKey(leadId))
                {
                    npc.InvestigationLeads[leadId] = new InvestigationLead
                    {
                        Id = leadId,
                        Description =
                            $"The verified isolation hardware references a redundant emergency circuit associated with {state.Facility.Rooms[alternate.RoomId].Name}. Verify that second control physically.",
                        RoomId = alternate.RoomId,
                        CreatedAt = state.Elapsed
                    };
                }
            }
        }

        InspectLocalDoorConditions(state, npc, room);
        InspectLocalAirlock(state, npc, room);

        npc.RoutineUntil = TimeSpan.Zero;
        npc.CurrentAction = new NpcAction(
            ActionKind.Idle,
            null,
            discoveredAnyControl
                ? "The inspection identified a real local isolation control."
                : $"The physical inspection of {room.Name} is complete.");
        npc.Intent = null;
        npc.NeedsMindReconsideration = true;

        npc.Bubble = new NpcBubble(
            discoveredAnyControl
                ? "There it is. A real isolation control."
                : "I've checked this area.",
            discoveredAnyControl ? NpcBubbleKind.Alert : NpcBubbleKind.Thought,
            state.Elapsed,
            state.Elapsed + TimeSpan.FromMinutes(3));

        AudioCueSystem.Emit(
            state,
            discoveredAnyControl ? AudioCueKind.Important : AudioCueKind.Thought,
            npc.Id.ToString(),
            room.Id);

        Log(state, $"{npc.Name} completes an investigation of {room.Name}.");
    }

    private static void InspectLocalDoorConditions(
        GameState state,
        Npc npc,
        Room room)
    {
        foreach (var door in state.Facility.Doors.Where(door =>
                     door.RoomAId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)
                     || door.RoomBId.Equals(room.Id, StringComparison.OrdinalIgnoreCase)))
        {
            if (!door.IsDamaged && !door.IsTechnicallyBypassed)
                continue;

            SuspicionSystem.AddEvidence(
                state,
                npc,
                door.IsTechnicallyBypassed
                    ? $"I inspected {door.Id} and found its local controls technically bypassed."
                    : $"I inspected {door.Id} and found persistent structural damage.",
                5,
                origin: EvidenceOrigin.PhysicalDiscovery,
                locationId: room.Id,
                evidenceId: $"door-condition:{door.Id}:{door.StructuralIntegrityPercent}:{door.IsTechnicallyBypassed}");
        }
    }

    private static void InspectLocalAirlock(
        GameState state,
        Npc npc,
        Room room)
    {
        if (room.Type != RoomType.Airlock
            || !room.HasExteriorHatch
            || !AirlockSafetyRules.NeedsCrewSecuring(state, room))
        {
            return;
        }

        SuspicionSystem.AddEvidence(
            state,
            npc,
            $"I inspected {room.Name} and confirmed its safety state is compromised.",
            12,
            origin: EvidenceOrigin.PhysicalDiscovery,
            locationId: room.Id,
            evidenceId: $"airlock-condition:{room.Id}:{room.AirlockSafetyInterlocksEnabled}:{room.ExteriorHatchOpen}");
    }

    private static void Log(GameState state, string message)
    {
        var timestamp = state.Elapsed.ToString(@"hh\:mm");
        state.EventLog.Insert(0, $"T+{timestamp}: {message}");
    }
}
