// SPDX-FileCopyrightText: 2025 Ark
// SPDX-FileCopyrightText: 2025 Coenx-flex
// SPDX-FileCopyrightText: 2025 Cojoke
// SPDX-FileCopyrightText: 2025 Ilya246
// SPDX-FileCopyrightText: 2025 ScyronX
// SPDX-FileCopyrightText: 2025 ark1368
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Server.Administration;
using Content.Server.Atmos.Components;
using Content.Server.Body.Systems;
using Content.Server.Chat.Managers;
using Content.Server.DoAfter;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Medical;
using Content.Server.Medical.Components;
using Content.Server.Prayer;
using Content.Server.Store.Systems;
using Content.Shared._EinsteinEngines.Language.Components;
using Content.Shared._Mono.CorticalBorer;
using Content.Shared._Mono.CorticalBorer.Components;
using Content.Shared._Starlight.CollectiveMind;
using Content.Shared.Administration.Logs;
using Content.Shared.Alert;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
// Einstein Engines - Languages
using Content.Shared.Database;
using Content.Shared.Inventory;
using Content.Shared.MedicalScanner;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared.Species.Components;
using Content.Shared.StatusEffect;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

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

        _actorQuery = GetEntityQuery<ActorComponent>();

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

    // public void TakeControlHost(Entity<CorticalBorerComponent> ent, CorticalBorerInfestedComponent infestedComp)
    // {
    //     var (worm, comp) = ent;
    //
    //     if (comp.Host is not { } host)
    //         return;
    //
    //     // make sure they aren't dead, would throw the worm into a ghost mode and just kill em
    //     if (TryComp<MobStateComponent>(ent.Comp.Host, out var mobState) &&
    //         mobState.CurrentState == MobState.Dead)
    //         return;
    //
    //     // Trauma: no clonespam bullshit, no infinite puppeting
    //     infestedComp.ControlTimeEnd = _timing.CurTime + comp.ControlDuration;
    //
    //     if (_mind.TryGetMind(worm, out var wormMind, out _))
    //         infestedComp.BorerMindId = wormMind;
    //
    //     if (_mind.TryGetMind(host, out var controlledMind, out _))
    //     {
    //         infestedComp.OrigininalMindId = controlledMind; // set this var here just in case somehow the mind changes from when the infestation started
    //
    //         // Trauma: TODO this reeks of shitcode
    //         // fish head...
    //         var dummy = Spawn("FoodMeatFish", MapCoordinates.Nullspace);
    //         _container.Insert(dummy, infestedComp.ControlContainer);
    //
    //         _mind.TransferTo(controlledMind, dummy);
    //     }
    //     else
    //     {
    //         infestedComp.OrigininalMindId = null;
    //     }
    //
    //     comp.ControllingHost = true;
    //     _mind.TransferTo(wormMind, host);
    //
    //     if (TryComp<GhostRoleComponent>(worm, out var ghostRole))
    //         _ghost.UnregisterGhostRole((worm, ghostRole)); // prevent players from taking the worm role once mind isn't in the worm
    //
    //     // add the end control and vomit egg action
    //     foreach (var actionId in ent.Comp.ControlActions)
    //     {
    //         if (_actions.AddAction(host, actionId) is {} action)
    //             infestedComp.RemoveAbilities.Add(action);
    //     }
    //
    //     if (TryComp<ReformComponent>(host, out var reformComp) && reformComp.ActionEntity.HasValue)
    //     {
    //         infestedComp.RemovedReformAction = reformComp.ActionEntity.Value;
    //
    //         _actions.RemoveAction(host, reformComp.ActionEntity.Value);
    //     }
    //
    //     // add collective mind if we don't have it already
    //     var channel = ent.Comp.HivemindChannel;
    //     var hadHivemind = _collective.HasCollectiveMind(host, channel);
    //     infestedComp.HadHivemind = hadHivemind;
    //     if (TryComp<CollectiveMindComponent>(host, out var collectiveComp))
    //         infestedComp.OldDefault = collectiveComp.DefaultChannel;
    //     _collective.AddCollectiveMind(host, channel, true); // also set default
    //
    //     var str = $"{ToPrettyString(worm)} has taken control over {ToPrettyString(host)}";
    //
    //     Log.Info(str);
    //     _admin.Add(LogType.Mind, LogImpact.High, $"{ToPrettyString(worm)} has taken control over {ToPrettyString(host)}");
    //     _chat.SendAdminAlert(str);
    // }

    // public void EndControl(Entity<CorticalBorerComponent> worm)
    // {
    //     var (uid, comp) = worm;
    //
    //     if (comp.Host is not { } host)
    //         return;
    //
    //     if (!TryComp<CorticalBorerInfestedComponent>(host, out var infestedComp))
    //         return;
    //
    //     // not controlling anyone
    //     if (!comp.ControllingHost)
    //         return;
    //
    //     comp.ControllingHost = false;
    //
    //     // remove all the actions set to remove
    //     foreach (var ability in infestedComp.RemoveAbilities)
    //     {
    //         _actions.RemoveAction(host, ability);
    //     }
    //     infestedComp.RemoveAbilities = new(); // clear out the list
    //
    //     if (infestedComp.RemovedReformAction.HasValue && TryComp<ReformComponent>(host, out var reformComp))
    //     {
    //         var restoredAction = _actions.AddAction(host, reformComp.ActionPrototype);
    //
    //         if (restoredAction != null)
    //         {
    //             reformComp.ActionEntity = restoredAction.Value;
    //         }
    //
    //         infestedComp.RemovedReformAction = null;
    //     }
    //
    //     if (TryComp<GhostRoleComponent>(worm, out var ghostRole))
    //         _ghost.RegisterGhostRole((worm, ghostRole)); // re-enable the ghost role after you return to the body
    //
    //     // Return everyone to their own bodies
    //     if (!TerminatingOrDeleted(infestedComp.BorerMindId))
    //         _mind.TransferTo(infestedComp.BorerMindId, infestedComp.Borer);
    //     if (!TerminatingOrDeleted(infestedComp.OrigininalMindId) && infestedComp.OrigininalMindId.HasValue)
    //         _mind.TransferTo(infestedComp.OrigininalMindId.Value, host);
    //
    //     if (!infestedComp.HadHivemind)
    //         _collective.RemoveCollectiveMind(host, worm.Comp.HivemindChannel);
    //     if (TryComp<CollectiveMindComponent>(host, out var collectiveComp))
    //         collectiveComp.DefaultChannel = infestedComp.OldDefault;
    //
    //     infestedComp.ControlTimeEnd = null;
    //     _container.CleanContainer(infestedComp.ControlContainer);
    // }

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

    // /// <summary>
    // /// This borrows shamelessly from <seealso cref="Content.Server._White.Xenomorphs.FaceHugger.FaceHuggerSystem"/>
    // /// </summary>
    // /// <param name="ent"></param>
    // /// <param name="eggProto"></param>
    // public void ImplantEgg(Entity<CorticalBorerComponent> ent, string eggProto)
    // {
    //     if (ent.Comp.Host is not { } host)
    //         return;
    //
    //     if (eggProto is not {} egg)
    //         return;
    //
    //     // TODO infection mechanics
    //     var bodyPart = _body.GetBodyChildrenOfType(host,
    //             BodyPartType.Chest, //component.InfectionBodyPart.Type,
    //             symmetry: BodyPartSymmetry.None)
    //         .FirstOrNull();
    //     if (!bodyPart.HasValue)
    //         return;
    //
    //     var organ = Spawn(eggProto);
    //     _body.TryCreateOrganSlot(bodyPart.Value.Id, "xenomorph_larva", out _, bodyPart.Value.Component); // TODO don't hardcode organ slot
    //
    //     if (!_body.InsertOrgan(bodyPart.Value.Id, organ, "xenomorph_larva", bodyPart.Value.Component)) // TODO don't hardcode organ slot
    //     {
    //         QueueDel(organ);
    //     }
    // }
    // Trauma end

    private void OnMindRemoved(Entity<CorticalBorerComponent> ent, ref MindRemovedMessage args)
    {
        // Trauma TODO this can break with aghosting, as aghost doesn't fire a MindRemovedMessage. Maybe look into this.
        if (!ent.Comp.ControllingHost)
            TryEjectBorer(ent); // No storing them in hosts if you don't have a soul
    }
}
