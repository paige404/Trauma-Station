using Content.Server._Mono.Objectives.Components;
using Content.Server.Antag;
using Content.Server.Antag.Components;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Roles;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.CorticalBorer.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Roles;
using Robust.Shared.Audio;

namespace Content.Server._Mono.CorticalBorer;

public sealed class CorticalBorerRuleSystem : GameRuleSystem<CorticalBorerRuleComponent>
{
    [Dependency] private readonly SharedRoleSystem _role = default!;
    [Dependency] private readonly AntagSelectionSystem _antag = default!;
    [Dependency] private readonly EmergencyShuttleSystem _emergencyShuttle = default!;

    private readonly SoundSpecifier _briefingSound = new SoundPathSpecifier("/Audio/_DV/CosmicCult/antag_cosmic_briefing.ogg"); // TODO
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CorticalBorerRuleComponent, AfterAntagEntitySelectedEvent>(OnAntagSelect);
    }

    private void OnAntagSelect(Entity<CorticalBorerRuleComponent> ent, ref AfterAntagEntitySelectedEvent args)
    {
        EnsureComp<CorticalBorerAssociatedRuleComponent>(args.EntityUid, out var associatedRuleComponent);
        associatedRuleComponent.GameRule = ent;

        EnsureComp<RoleBriefingComponent>(args.EntityUid, out var role);

        role.Briefing = Loc.GetString("objective-corticalborer-charactermenu"); // TODO
        _antag.SendBriefing(args.EntityUid, Loc.GetString("corticalborer-role-roundstart-fluff"), Color.FromHex("#4cabb3"), _briefingSound);
        _antag.SendBriefing(args.EntityUid, Loc.GetString("corticalborer-role-short-briefing"), Color.FromHex("#cae8e8"), null);
    }

    /// <summary>
    /// Called when the gamerule is added
    /// </summary>
    protected override void Added(EntityUid uid, CorticalBorerRuleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {

    }

    /// <summary>
    /// Called when the gamerule begins
    /// </summary>
    protected override void Started(EntityUid uid, CorticalBorerRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {

    }

    /// <summary>
    /// Called when the gamerule ends
    /// </summary>
    protected override void Ended(EntityUid uid, CorticalBorerRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {

    }

    /// <summary>
    /// Called at the end of a round when text needs to be added for a game rule.
    /// </summary>
    protected override void AppendRoundEndText(EntityUid uid, CorticalBorerRuleComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        args.AddLine("Worms c:"); // TODO

    }

    /// <summary>
    /// Called on an active gamerule entity in the Update function
    /// </summary>
    protected override void ActiveTick(EntityUid uid, CorticalBorerRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        if (_emergencyShuttle.EmergencyShuttleArrived)
        {
            var escapingWorms = 0;
            var escapeQuery = EntityQueryEnumerator<CorticalBorerHiveEscapeConditionComponent>();
            while (escapeQuery.MoveNext(out var borerUid, out var condition))
            {
                if (_emergencyShuttle.IsTargetEscaping(borerUid))
                {
                    escapingWorms++;
                }
            }

            component.EscapingBorers = escapingWorms;
        }
    }

    public CorticalBorerRuleComponent? GetRule(EntityUid uid)
    {
        if (!TryComp<CorticalBorerAssociatedRuleComponent>(uid, out var associatedRule))
            return null;

        if (!TryComp<CorticalBorerRuleComponent>(associatedRule.GameRule, out var rule))
            return null;

        return rule;
    }

    public void AssociateEgg(Entity<CorticalBorerComponent> borer, EntityUid egg)
    {
        if (!TryComp<CorticalBorerAssociatedRuleComponent>(borer, out var rule))
            return;
        EnsureComp<GhostRoleAntagSpawnerComponent>(egg, out var antagSpawner);
        antagSpawner.Rule = rule.GameRule;
    }
}
