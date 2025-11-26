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
using Content.Shared._Mono.CorticalBorer;
using Content.Shared._Mono.CorticalBorer.Components;
using Content.Shared._Starlight.CollectiveMind;
using Content.Shared.Administration.Logs;
using Content.Shared.Alert;
using Content.Shared.Body.Components;
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
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared.Species.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Mono.CorticalBorer;

public sealed partial class CorticalBorerSystem : SharedCorticalBorerSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly BloodstreamSystem _blood = default!;
    [Dependency] private readonly HealthAnalyzerSystem _analyzer = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly UserInterfaceSystem _userInterfaceSystem = default!;
    [Dependency] private readonly ISharedAdminLogManager _admin = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly GhostRoleSystem _ghost  = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly CollectiveMindUpdateSystem _collective = default!;
    // Trauma start
    [Dependency] private readonly QuickDialogSystem _quickDialog = default!;
    [Dependency] private readonly PrayerSystem _prayer = default!;
    [Dependency] private readonly StoreSystem _store = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;

    private ISawmill _sawmill = default!;

    private EntityQuery<ActorComponent> _actorQuery;
    // Trauma end
    public override void Initialize()
    {
        _sawmill = Logger.GetSawmill("corticalborer");
        _sawmill.Level = LogLevel.Info;
        SubscribeAbilities();

        SubscribeLocalEvent<CorticalBorerComponent, ComponentStartup>(OnStartup);

        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerDispenserInjectMessage>(OnInjectReagentMessage);
        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerDispenserSetInjectAmountMessage>(OnSetInjectAmountMessage);

        SubscribeLocalEvent<InventoryComponent, InfestHostAttempt>(OnInfestHostAttempt);

        // Trauma: worms can't talk
        // SubscribeLocalEvent<CorticalBorerComponent, CheckTargetedSpeechEvent>(OnSpeakEvent);

        SubscribeLocalEvent<CorticalBorerComponent, MindRemovedMessage>(OnMindRemoved);

        // Trauma: worm evolution store purchase events
        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerHostDamageChangeEvent>(OnChangeHostDamage);
        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerChemicalPointCapChangeEvent>(OnChangeChemicalPointCap);
        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerBarotraumaRemovalEvent>(OnBarotraumaRemoved);
        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerChemicalDispenserAdditionEvent>(OnChemDispenserAdd);
        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerDamageModifierChangeEvent>(OnDamageModifierChange);

        _actorQuery = GetEntityQuery<ActorComponent>();
    }

    private void OnDamageModifierChange(Entity<CorticalBorerComponent> ent, ref CorticalBorerDamageModifierChangeEvent args)
    {
        if (!TryComp<DamageableComponent>(ent, out var damageable))
            return;
        _damage.SetDamageModifierSetId(ent.Owner, args.ModifierSet);
    }

    private void OnStartup(Entity<CorticalBorerComponent> ent, ref ComponentStartup args)
    {
        //add actions
        foreach (var actionId in ent.Comp.InitialCorticalBorerActions)
        {
            _actions.AddAction(ent, actionId);
        }

        _alerts.ShowAlert(ent, ent.Comp.ChemicalAlert);
        UpdateUiState(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var comp in EntityManager.EntityQuery<CorticalBorerComponent>())
        {
            if (_timing.CurTime < comp.UpdateTimer)
                continue;

            comp.UpdateTimer = _timing.CurTime + TimeSpan.FromSeconds(comp.UpdateCooldown);

            if (comp.Host != null)
            {
                UpdateChems((comp.Owner, comp), comp.ChemicalGenerationRate);
                DamageHost(comp.Host, comp.HostDamage);
            }
        }

        foreach (var comp in EntityManager.EntityQuery<CorticalBorerInfestedComponent>())
        {
            if (_timing.CurTime >= comp.ControlTimeEnd)
                EndControl(comp.Borer);
        }
    }

    private void DamageHost(EntityUid? host, DamageSpecifier? damage)
    {
        if (damage is not null && TryComp<DamageableComponent>(host, out var damageable))
        {
            _damage.TryChangeDamage(host, damage, false, false, damageable);
        }
    }

    // Trauma: Worms can't talk anymore
    // private void OnSpeakEvent(Entity<CorticalBorerComponent> ent, ref CheckTargetedSpeechEvent args)
    // {
    //     args.ChatTypeIgnore.Add(InGameICChatType.CollectiveMind);
    //
    //     if (ent.Comp.Host.HasValue)
    //     {
    //         args.Targets.Add(ent);
    //         args.Targets.Add(ent.Comp.Host.Value);
    //     }
    // }

    public void UpdateChems(Entity<CorticalBorerComponent> ent, int change)
    {
        var (_, comp) = ent;

        if (comp.ChemicalPoints + change >= comp.ChemicalPointCap)
            comp.ChemicalPoints = comp.ChemicalPointCap;
        else if (comp.ChemicalPoints + change <= 0)
            comp.ChemicalPoints = 0;
        else
            comp.ChemicalPoints += change;

        if (comp.ChemicalPoints % comp.UiUpdateInterval == 0)
            UpdateUiState(ent);

        _alerts.ShowAlert(ent, ent.Comp.ChemicalAlert);

        Dirty(ent);
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

    /// <summary>
    /// Attempts to inject the Borer's host with chems
    /// </summary>
    public bool TryInjectHost(Entity<CorticalBorerComponent> ent,
        CorticalBorerChemicalPrototype chemicalPrototype,
        float chemAmount)
    {
        var (uid, comp) = ent;

        // Need a host to inject something
        if (!comp.Host.HasValue)
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-no-host"), uid, uid, PopupType.Medium);
            return false;
        }

        // Sugar block from injecting stuff
        if (!CanUseAbility(ent, comp.Host.Value))
            return false;

        // Make sure you can even hold the amount of chems you need
        if (chemicalPrototype.Cost > comp.ChemicalPointCap)
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-not-enough-chem-storage"), uid, uid, PopupType.Medium);
            return false;
        }

        // Make sure you have enough chems
        if (chemicalPrototype.Cost > comp.ChemicalPoints)
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-not-enough-chem"), uid, uid, PopupType.Medium);
            return false;
        }

        // no injecting things that don't have blood silly
        if (!TryComp<BloodstreamComponent>(comp.Host, out var bloodstream))
            return false;

        var solution = new Solution();
        solution.AddReagent(chemicalPrototype.Reagent, chemAmount);

        // add the chemicals to the bloodstream of the host
        if (!_blood.TryAddToChemicals(comp.Host.Value, solution)) // , bloodstream
            return false;

        UpdateChems(ent, -((int)chemAmount * chemicalPrototype.Cost));
        return true;
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

    // Trauma: changed from GetAllBorerChemicals. Should be more performant AND more versatile
    private List<CorticalBorerDispenserItem> GetBorerChemicals(Entity<CorticalBorerComponent> ent)
    {
        var chemsList = new List<CorticalBorerDispenserItem>();
        foreach (var chemId in ent.Comp.ReagentList)
        {
            if (!_proto.TryIndex(chemId, out CorticalBorerChemicalPrototype? borerChem))
                continue;
            if (!_proto.TryIndex(borerChem.Reagent, out ReagentPrototype? chemReagent))
                continue;
            var reagentName = chemReagent.LocalizedName;
            var reagentId = chemReagent.ID;
            var cost = borerChem.Cost;
            var amount = ent.Comp.InjectAmount;
            var chems = ent.Comp.ChemicalPoints;
            var color = chemReagent.SubstanceColor;
            chemsList.Add(new CorticalBorerDispenserItem(reagentName,reagentId, cost, amount, chems, color)); // need color and name
        }

        return chemsList;
    }

    private void UpdateUiState(Entity<CorticalBorerComponent> ent)
    {
        var chems = GetBorerChemicals(ent);

        var state = new CorticalBorerDispenserBoundUserInterfaceState(chems, (int)ent.Comp.InjectAmount);

        _userInterfaceSystem.SetUiState(ent.Owner, CorticalBorerDispenserUiKey.Key, state);

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

    public void TakeControlHost(Entity<CorticalBorerComponent> ent, CorticalBorerInfestedComponent infestedComp)
    {
        var (worm, comp) = ent;

        if (comp.Host is not { } host)
            return;

        // make sure they aren't dead, would throw the worm into a ghost mode and just kill em
        if (TryComp<MobStateComponent>(ent.Comp.Host, out var mobState) &&
            mobState.CurrentState == MobState.Dead)
            return;

        // Trauma: no clonespam bullshit, no infinite puppeting
        infestedComp.ControlTimeEnd = _timing.CurTime + comp.ControlDuration;

        if (_mind.TryGetMind(worm, out var wormMind, out _))
            infestedComp.BorerMindId = wormMind;

        if (_mind.TryGetMind(host, out var controlledMind, out _))
        {
            infestedComp.OrigininalMindId = controlledMind; // set this var here just in case somehow the mind changes from when the infestation started

            // Trauma: TODO this reeks of shitcode
            // fish head...
            var dummy = Spawn("FoodMeatFish", MapCoordinates.Nullspace);
            _container.Insert(dummy, infestedComp.ControlContainer);

            _mind.TransferTo(controlledMind, dummy);
        }
        else
        {
            infestedComp.OrigininalMindId = null;
        }

        comp.ControllingHost = true;
        _mind.TransferTo(wormMind, host);

        if (TryComp<GhostRoleComponent>(worm, out var ghostRole))
            _ghost.UnregisterGhostRole((worm, ghostRole)); // prevent players from taking the worm role once mind isn't in the worm

        // add the end control and vomit egg action
        foreach (var actionId in ent.Comp.ControlCorticalBorerActions)
        {
            if (_actions.AddAction(host, actionId) is {} action)
                infestedComp.RemoveAbilities.Add(action);
        }

        if (TryComp<ReformComponent>(host, out var reformComp) && reformComp.ActionEntity.HasValue)
        {
            infestedComp.RemovedReformAction = reformComp.ActionEntity.Value;

            _actions.RemoveAction(host, reformComp.ActionEntity.Value);
        }

        // add collective mind if we don't have it already
        var channel = ent.Comp.HivemindChannel;
        var hadHivemind = _collective.HasCollectiveMind(host, channel);
        infestedComp.HadHivemind = hadHivemind;
        if (TryComp<CollectiveMindComponent>(host, out var collectiveComp))
            infestedComp.OldDefault = collectiveComp.DefaultChannel;
        _collective.AddCollectiveMind(host, channel, true); // also set default

        var str = $"{ToPrettyString(worm)} has taken control over {ToPrettyString(host)}";

        Log.Info(str);
        _admin.Add(LogType.Mind, LogImpact.High, $"{ToPrettyString(worm)} has taken control over {ToPrettyString(host)}");
        _chat.SendAdminAlert(str);
    }

    public void EndControl(Entity<CorticalBorerComponent> worm)
    {
        var (uid, comp) = worm;

        if (comp.Host is not { } host)
            return;

        if (!TryComp<CorticalBorerInfestedComponent>(host, out var infestedComp))
            return;

        // not controlling anyone
        if (!comp.ControllingHost)
            return;

        comp.ControllingHost = false;

        // remove all the actions set to remove
        foreach (var ability in infestedComp.RemoveAbilities)
        {
            _actions.RemoveAction(host, ability);
        }
        infestedComp.RemoveAbilities = new(); // clear out the list

        if (infestedComp.RemovedReformAction.HasValue && TryComp<ReformComponent>(host, out var reformComp))
        {
            var restoredAction = _actions.AddAction(host, reformComp.ActionPrototype);

            if (restoredAction != null)
            {
                reformComp.ActionEntity = restoredAction.Value;
            }

            infestedComp.RemovedReformAction = null;
        }

        if (TryComp<GhostRoleComponent>(worm, out var ghostRole))
            _ghost.RegisterGhostRole((worm, ghostRole)); // re-enable the ghost role after you return to the body

        // Return everyone to their own bodies
        if (!TerminatingOrDeleted(infestedComp.BorerMindId))
            _mind.TransferTo(infestedComp.BorerMindId, infestedComp.Borer);
        if (!TerminatingOrDeleted(infestedComp.OrigininalMindId) && infestedComp.OrigininalMindId.HasValue)
            _mind.TransferTo(infestedComp.OrigininalMindId.Value, host);

        if (!infestedComp.HadHivemind)
            _collective.RemoveCollectiveMind(host, worm.Comp.HivemindChannel);
        if (TryComp<CollectiveMindComponent>(host, out var collectiveComp))
            collectiveComp.DefaultChannel = infestedComp.OldDefault;

        infestedComp.ControlTimeEnd = null;
        _container.CleanContainer(infestedComp.ControlContainer);
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
            _popup.PopupEntity(Loc.GetString("cortical-borer-whisper-mindless"), ent, ent, PopupType.Medium);
            return;
        }

        _quickDialog.OpenDialog(actor.PlayerSession, Loc.GetString("cortical-borer-whisper-title"), "Message", (string message) =>
        {
            _prayer.SendSubtleMessage(actorTarget.PlayerSession, actor.PlayerSession, message, Loc.GetString("cortical-borer-whisper-popup"));
            _popup.PopupEntity(Loc.GetString("cortical-borer-whisper-whisper",
                    ("message", message)),
                ent.Owner, ent.Owner);
        });
    }
    // Trauma end

    private void OnMindRemoved(Entity<CorticalBorerComponent> ent, ref MindRemovedMessage args)
    {
        // Trauma TODO this can break with aghosting, as aghost doesn't fire a MindRemovedMessage. Maybe look into this.
        if (!ent.Comp.ControllingHost)
            TryEjectBorer(ent); // No storing them in hosts if you don't have a soul
    }
}
