// SPDX-FileCopyrightText: 2025 Ark
// SPDX-FileCopyrightText: 2025 Coenx-flex
// SPDX-FileCopyrightText: 2025 Cojoke
// SPDX-FileCopyrightText: 2025 Ilya246
// SPDX-FileCopyrightText: 2025 ScyronX
// SPDX-FileCopyrightText: 2025 ark1368
// SPDX-FileCopyrightText: 2025 tonotom1
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Mono.Objectives.Components;
using Content.Server.Administration;
using Content.Server.Atmos.Components;
using Content.Server.DoAfter;
using Content.Server.Ghost.Roles;
using Content.Server.Medical;
using Content.Server.Medical.Components;
using Content.Server.Prayer;
using Content.Server.Store.Systems;
using Content.Shared._Mono.CorticalBorer;
using Content.Shared._Mono.CorticalBorer.Components;
using Content.Shared.Damage;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.MedicalScanner;
using Content.Shared.Mind.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Robust.Shared.Player;

// Einstein Engines - Languages

namespace Content.Server._Mono.CorticalBorer;

public sealed partial class CorticalBorerSystem : SharedCorticalBorerSystem
{
    [Dependency] private readonly HealthAnalyzerSystem _analyzer = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly GhostRoleSystem _ghost  = default!;
    // Trauma start
    [Dependency] private readonly QuickDialogSystem _quickDialog = default!;
    [Dependency] private readonly PrayerSystem _prayer = default!;
    [Dependency] private readonly StoreSystem _store = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly CorticalBorerRuleSystem _rule = default!;

    private EntityQuery<ActorComponent> _actorQuery;
    // Trauma end
    public override void Initialize()
    {
        SubscribeAbilities();

        SubscribeLocalEvent<CorticalBorerComponent, ComponentStartup>(OnStartup);

        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerDispenserInjectMessage>(OnInjectReagentMessage);
        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerDispenserSetInjectAmountMessage>(OnSetInjectAmountMessage);

        SubscribeLocalEvent<InventoryComponent, InfestHostAttempt>(OnInfestHostAttempt);

        SubscribeLocalEvent<CorticalBorerComponent, MindRemovedMessage>(OnMindRemoved);

        // Trauma: worm evolution store purchase events
        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerHostDamageChangeEvent>(OnChangeHostDamage);
        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerChemicalPointCapChangeEvent>(OnChangeChemicalPointCap);
        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerBarotraumaRemovalEvent>(OnBarotraumaRemoved);
        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerChemicalDispenserAdditionEvent>(OnChemDispenserAdd);
        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerDamageModifierChangeEvent>(OnDamageModifierChange);
        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerMovementSpeedChangeEvent>(OnMovementSpeedChange);
        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerUnlockActionsEvent>(OnUnlockActions);

        SubscribeLocalEvent<CorticalBorerComponent, AttackAttemptEvent>(OnBorerAttackAttempt);

        _actorQuery = GetEntityQuery<ActorComponent>();

    }

    private void OnBorerAttackAttempt(Entity<CorticalBorerComponent> ent, ref AttackAttemptEvent args)
    {
        var host = ent.Comp.Host;
        if (host.HasValue && args.Target == host)
        {
            if (!CanUseAbility(ent, host.Value))
            {
                args.Cancel();
            }
        }
    }

    private void OnUnlockActions(Entity<CorticalBorerComponent> ent, ref CorticalBorerUnlockActionsEvent args)
    {
        Log.Info("Unlock Actions Event!");
        UpdateBorerActions(ent, args.Actions);
    }

    private void OnMovementSpeedChange(Entity<CorticalBorerComponent> ent, ref CorticalBorerMovementSpeedChangeEvent args)
    {
        if (!TryComp<MovementSpeedModifierComponent>(ent, out var movementSpeedModifier))
            return;
        _movementSpeed.ChangeBaseSpeed(ent, args.BaseWalkSpeed, args.BaseSprintSpeed, movementSpeedModifier.Acceleration);
    }

    private void OnDamageModifierChange(Entity<CorticalBorerComponent> ent, ref CorticalBorerDamageModifierChangeEvent args)
    {
        if (!TryComp<DamageableComponent>(ent, out var damageable))
            return;
        _damage.SetDamageModifierSetId(ent.Owner, args.ModifierSet);
    }

    private void OnStartup(Entity<CorticalBorerComponent> ent, ref ComponentStartup args)
    {
        UpdateBorerActions(ent, ent.Comp.InitialActions);

        _alerts.ShowAlert(ent, ent.Comp.ChemicalAlert);
        UpdateUiState(ent);
    }

    public void OnInfestHostAttempt(Entity<InventoryComponent> entity, ref InfestHostAttempt args)
    {
        IngestionBlockerComponent? blocker;

        if (_inventory.TryGetSlotEntity(entity.Owner, "head", out var headUid) &&
            TryComp(headUid, out blocker) &&
            blocker.Enabled)
        {
            args.Blocker = headUid;
            args.Cancel();
        }
    }

    private void OnInjectReagentMessage(Entity<CorticalBorerComponent> ent, ref CorticalBorerDispenserInjectMessage message)
    {
        CorticalBorerChemicalPrototype? chemProto = null;
        foreach (var chem in _proto.EnumeratePrototypes<CorticalBorerChemicalPrototype>())
        {
            if (chem.Reagent.Equals(message.ChemProtoId))
            {
                chemProto = chem;
                break;
            }
        }

        if (chemProto != null)
            TryInjectHost(ent, chemProto, ent.Comp.InjectAmount);

        UpdateUiState(ent);
    }

    // Trauma
    private void OnBarotraumaRemoved(Entity<CorticalBorerComponent> ent, ref CorticalBorerBarotraumaRemovalEvent args)
    {
        if (TryComp<BarotraumaComponent>(ent, out var barotrauma))
        {
            EntityManager.RemoveComponent(ent, barotrauma);
        }
    }

    // Trauma
    private void OnChangeChemicalPointCap(Entity<CorticalBorerComponent> ent, ref CorticalBorerChemicalPointCapChangeEvent args)
    {
        ent.Comp.ChemicalPointCap += args.Delta;
        Dirty(ent);
    }

    // Trauma
    private void OnChangeHostDamage(Entity<CorticalBorerComponent> ent, ref CorticalBorerHostDamageChangeEvent args)
    {
        ent.Comp.HostDamage = args.HostDamage;
        Dirty(ent);
    }

    // Trauma
    private void OnChemDispenserAdd(Entity<CorticalBorerComponent> ent, ref CorticalBorerChemicalDispenserAdditionEvent args)
    {
        ent.Comp.ReagentList.AddRange(args.Chemicals);
        Dirty(ent);
        UpdateUiState(ent);
    }

    private void OnSetInjectAmountMessage(Entity<CorticalBorerComponent> ent, ref CorticalBorerDispenserSetInjectAmountMessage message)
    {
        ent.Comp.InjectAmount = message.CorticalBorerDispenserDispenseAmount;
        UpdateUiState(ent);
    }

    public bool TryToggleCheckBlood(Entity<CorticalBorerComponent> ent)
    {
        if(!TryComp<UserInterfaceComponent>(ent, out var uic))
            return false;

        if (!TryComp<HealthAnalyzerComponent>(ent, out var health))
            return false;

        _ui.TryToggleUi((ent, uic), HealthAnalyzerUiKey.Key, ent);

        if (health.ScannedEntity is null && ent.Comp.Host.HasValue)
            OpenCheckBlood(ent, uic);

        return true;
    }

    public void OpenCheckBlood(Entity<CorticalBorerComponent> ent, UserInterfaceComponent uic)
    {
        if (!ent.Comp.Host.HasValue)
            return;

        if (!TryComp<HealthAnalyzerComponent>(ent, out var health))
            return;

        if (!_ui.IsUiOpen((ent,uic), HealthAnalyzerUiKey.Key))
            _ui.OpenUi((ent, uic), HealthAnalyzerUiKey.Key, ent);
        _analyzer.BeginAnalyzingEntity((ent, health), ent.Comp.Host.Value);
    }

    public void CloseCheckBlood(Entity<CorticalBorerComponent> ent, UserInterfaceComponent uic)
    {
        if (!ent.Comp.Host.HasValue)
            return;

        if (!TryComp<HealthAnalyzerComponent>(ent, out var health))
            return;

        if(!health.ScannedEntity.HasValue)
            return;

        _ui.CloseUi((ent, uic), HealthAnalyzerUiKey.Key, ent);
        _analyzer.StopAnalyzingEntity((ent, health), health.ScannedEntity.Value);
    }

    // Trauma start
    /// <summary>
    /// Opens a prompt to send a message directly to the host.
    /// </summary>
    /// <param name="ent">the borer entity</param>
    /// <param name="infestedComp">the CorticalBorerInfestedComponent of the host</param>
    public void InvadeThoughts(Entity<CorticalBorerComponent> ent, CorticalBorerInfestedComponent infestedComp)
    {
        var target = ent.Comp.Host;
        if (!_actorQuery.TryComp(ent.Owner, out var actor)
            || !_actorQuery.TryComp(target, out var actorTarget))
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-whisper-mindless"),
                ent,
                ent,
                PopupType.Medium);
            return;
        }

        _quickDialog.OpenDialog(actor.PlayerSession, Loc.GetString("cortical-borer-whisper-title"), "Message", (string message) =>
        {
            _prayer.SendSubtleMessage(actorTarget.PlayerSession, actor.PlayerSession, message, Loc.GetString("cortical-borer-whisper-popup"));
            _popup.PopupEntity(Loc.GetString("cortical-borer-whisper-whisper",
                    ("message", message)),
                ent.Owner,
                ent.Owner);
        });
    }

    private void OnMindRemoved(Entity<CorticalBorerComponent> ent, ref MindRemovedMessage args)
    {
        // Trauma TODO this can break with aghosting, as aghost doesn't fire a MindRemovedMessage. Maybe look into this.
        if (!ent.Comp.ControllingHost)
            TryEjectBorer(ent); // No storing them in hosts if you don't have a soul
    }

    public void UpdateInfectedObjective(EntityUid uid, int delta)
    {
        // if (!_mind.TryGetObjectiveComp<CorticalBorerInfectedConditionComponent>(uid, out var objective))
        //     return;
        if (_rule.GetRule(uid) is not { } rule)
            return;
        rule.HostsInfected += delta;
    }

    public void UpdateEggsObjective(EntityUid uid, int delta)
    {
        if (_rule.GetRule(uid) is not { } rule)
            return;
        rule.EggsLaid += delta;
    }
}
