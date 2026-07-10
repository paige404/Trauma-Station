using Content.Server._Mono.CorticalBorer;
using Content.Server._Mono.Objectives.Components;
using Content.Server.Objectives.Systems;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.CorticalBorer.Components;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;

namespace Content.Server._Mono.Objectives.Systems;

public sealed class CorticalBorerObjectivesSystem : EntitySystem
{
    [Dependency] private readonly NumberObjectiveSystem _number = default!;
    [Dependency] private readonly CorticalBorerRuleSystem _rule = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CorticalBorerInfectedConditionComponent, ObjectiveGetProgressEvent>(OnInfectedGetProgress);
        SubscribeLocalEvent<CorticalBorerEggConditionComponent, ObjectiveGetProgressEvent>(OnEggLayGetProgress);
        SubscribeLocalEvent<CorticalBorerHiveEscapeConditionComponent, ObjectiveGetProgressEvent>(OnEscapeGetProgress);
    }

    private void OnEggLayGetProgress(EntityUid uid, CorticalBorerEggConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        if (args.Mind.CurrentEntity is not { } borer)
            return;
        // if (!TryComp<CorticalBorerComponent>(borer, out var borerComponent))
        //     return;
        args.Progress = EggsLayedProgress(borer, comp, _number.GetTarget(uid));
    }

    private void OnInfectedGetProgress(EntityUid uid, CorticalBorerInfectedConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        if (args.Mind.CurrentEntity is not { } borer)
            return;
        // if (!TryComp<CorticalBorerComponent>(borer, out var borerComponent))
        //     return;
        args.Progress = HostsInfectedProgress(borer, comp, _number.GetTarget(uid));
    }

    private void OnEscapeGetProgress(EntityUid uid, CorticalBorerHiveEscapeConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        if (args.Mind.CurrentEntity is not { } borer)
            return;
        // if (!TryComp<CorticalBorerComponent>(borer, out var borerComponent))
        //     return;
        args.Progress = HiveEscapeProgress(borer, comp, _number.GetTarget(uid));


        // // not escaping alive if you're deleted/dead
        // if (mind.OwnedEntity == null || _mind.IsCharacterDeadIc(mind))
        //     return 0f;
        //
        // // You're not escaping if you're restrained!
        // // Granting 50% as to allow for partial completion of the objective.
        // if (TryComp<CuffableComponent>(mind.OwnedEntity, out var cuffed) && cuffed.CuffedHandCount > 0)
        //     return _emergencyShuttle.IsTargetEscaping(mind.OwnedEntity.Value) ? 0.5f : 0f;
        //
        // // Any emergency shuttle counts for this objective, but not pods.
        // return _emergencyShuttle.IsTargetEscaping(mind.OwnedEntity.Value) ? 1f : 0f;
    }

    private float EggsLayedProgress(EntityUid uid, CorticalBorerEggConditionComponent comp, int target)
    {
        // prevent divide-by-zero
        if (target == 0)
            return 1f;

        if (_rule.GetRule(uid) is { } rule)
            comp.EggsLaid = rule.EggsLaid;

        return MathF.Min((float) comp.EggsLaid / (float) target, 1f);
    }

    private float HostsInfectedProgress(EntityUid uid, CorticalBorerInfectedConditionComponent comp, int target)
    {
        // prevent divide-by-zero
        if (target == 0)
            return 1f;

        if (_rule.GetRule(uid) is { } rule)
            comp.HostsInfected = rule.HostsInfected;

        return MathF.Min((float) comp.HostsInfected / (float) target, 1f);
    }

    private float HiveEscapeProgress(EntityUid uid, CorticalBorerHiveEscapeConditionComponent comp, int target)
    {
        // prevent divide-by-zero
        if (target == 0)
            return 1f;

        if (_rule.GetRule(uid) is { } rule)
            comp.EscapingBorers = rule.EscapingBorers;

        return MathF.Min((float) comp.EscapingBorers / (float) target, 1f);
    }
}
