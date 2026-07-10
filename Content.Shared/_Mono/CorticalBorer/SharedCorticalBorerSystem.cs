// SPDX-FileCopyrightText: 2025 Coenx-flex
// SPDX-FileCopyrightText: 2025 Cojoke
// SPDX-FileCopyrightText: 2025 Ilya246
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._EinsteinEngines.Language.Components;
using Content.Shared._Mono.CorticalBorer.Components;
using Content.Shared._Starlight.CollectiveMind;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.Alert;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chat;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.MedicalScanner;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Content.Shared.Coordinates;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Species.Components;
using Content.Shared.Stunnable;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._Mono.CorticalBorer;

public class SharedCorticalBorerSystem : EntitySystem
{
    [Dependency] protected readonly StatusEffectsSystem _statusEffects = default!;
    [Dependency] protected readonly SharedTransformSystem _transform = default!;
    [Dependency] protected readonly ISerializationManager _serManager = default!;
    [Dependency] protected readonly DamageableSystem _damage = default!;
    [Dependency] protected readonly SharedPopupSystem _popup = default!;
    [Dependency] protected readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] protected readonly SharedActionsSystem _actions = default!;
    [Dependency] protected readonly SharedContainerSystem _container = default!;
    [Dependency] protected readonly SharedStunSystem _stun = default!; // Trauma
    [Dependency] protected readonly ActionContainerSystem _actionContainer = default!; // Trauma
    [Dependency] protected readonly AlertsSystem _alerts = default!;
    [Dependency] protected readonly IPrototypeManager _proto = default!;
    [Dependency] protected readonly SharedBloodstreamSystem _blood = default!;
    [Dependency] protected readonly IGameTiming _timing = default!;
    [Dependency] protected readonly SharedMindSystem _mind = default!;
    [Dependency] protected readonly ISharedChatManager _chat = default!;
    [Dependency] protected readonly CollectiveMindUpdateSystem _collective = default!;
    [Dependency] protected readonly ISharedAdminLogManager _admin = default!;
    [Dependency] protected readonly SharedBodySystem _body = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var comp in EntityManager.EntityQuery<CorticalBorerComponent>())
        {
            if (_timing.CurTime < comp.UpdateTimer)
                continue;

            comp.UpdateTimer = _timing.CurTime + TimeSpan.FromSeconds(comp.UpdateCooldown);

            if (!comp.HasHost())
                continue;
            UpdateChems((comp.Owner, comp), comp.ChemicalGenerationRate);
            DamageHost(comp.Host, comp.HostDamage);
        }

        foreach (var comp in EntityManager.EntityQuery<CorticalBorerInfestedComponent>())
        {
            if (_timing.CurTime >= comp.ControlTimeEnd)
                EndControl(comp.Borer);
        }
    }

    public bool CanUseAbility(Entity<CorticalBorerComponent> ent, EntityUid target)
    {
        // Trauma TODO migrate this to new status effects system
        if (_statusEffects.HasStatusEffect(target,
                    "CorticalBorerProtection")) // hardcoded the status effect because... TODO stop this Mono chud hardcoding
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-sugar-block"), ent.Owner, ent.Owner, PopupType.Medium);
            return false;
        }

        return true;
    }

    public bool InfestTarget(Entity<CorticalBorerComponent> ent, EntityUid target)
    {
        var (uid, comp) = ent;

        // Make sure the infected person is infected right
        var infestedComp = EnsureComp<CorticalBorerInfestedComponent>(target);

        // Make sure they get into the target
        if (!_container.Insert(uid, infestedComp.InfestationContainer))
        {
            RemCompDeferred<CorticalBorerInfestedComponent>(target); // oh no it didn't work somehow so remove the comp you just added...
            return false;
        }

        // Set up the Borer
        infestedComp.Borer = ent;
        comp.Host = target;

        if (comp.AddOnInfest is not null)
        {
            foreach (var (key, compReg) in comp.AddOnInfest)
            {
                var compType = compReg.Component.GetType();
                if (HasComp(ent, compType))
                    continue;

                var newComp = (Component) _serManager.CreateCopy(compReg.Component, notNullableOverride: true);
                EntityManager.AddComponent(ent, newComp, true);
            }
        }

        if (comp.RemoveOnInfest is not null)
        {
            foreach (var (key, compReg) in comp.RemoveOnInfest)
                RemCompDeferred(ent, compReg.Component.GetType());
        }

        if (TryComp<DamageableComponent>(ent, out var damComp))
            _damage.SetAllDamage(ent, damComp, 0);


        // Trauma: borers can understand only languages their host understands
        if (TryComp<LanguageSpeakerComponent>(ent, out var wormLang)
            && TryComp<LanguageSpeakerComponent>(comp.Host, out var hostLang))
        {
            foreach (var lang in hostLang.UnderstoodLanguages)
            {
                if (!wormLang.UnderstoodLanguages.Contains(lang))
                {
                    wormLang.UnderstoodLanguages.Add(lang);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Attempts to remove the borer from its host.
    /// </summary>
    /// <param name="ent">the borer being removed</param>
    /// <param name="forced">true if the borer is being forcibly removed by surgery or otherwise</param> // Trauma
    /// <returns>true if the borer was ejected, else false</returns>
    public bool TryEjectBorer(Entity<CorticalBorerComponent> ent, bool forced = false)
    {
        var (uid, comp) = ent;

        if (ent.Comp.Host is not { } host)
            return false;

        // Make sure they get out of the host
        if (!_container.TryRemoveFromContainer(uid))
            return false;

        // close all the UIs that relate to host
        if (TryComp<UserInterfaceComponent>(ent, out var uic))
        {
            _ui.CloseUi((ent.Owner,uic), HealthAnalyzerUiKey.Key);
            _ui.CloseUi((ent.Owner,uic), CorticalBorerDispenserUiKey.Key);
        }

        RemCompDeferred<CorticalBorerInfestedComponent>(ent.Comp.Host.Value);

        // Trauma: worm forgets all of the host's languages
        // TODO: this might break translator implants in the worm?
        if (TryComp<LanguageSpeakerComponent>(ent, out var wormLang)
            && TryComp<LanguageSpeakerComponent>(comp.Host, out var hostLang))
        {
            foreach (var lang in hostLang.UnderstoodLanguages)
            {
                wormLang.UnderstoodLanguages.Remove(lang);
            }
        }

        ent.Comp.Host = null;

        if (comp.RemoveOnInfest is not null)
        {
            foreach (var (key, compReg) in comp.RemoveOnInfest)
            {
                var compType = compReg.Component.GetType();
                if (HasComp(ent, compType))
                    continue;

                var newComp = (Component) _serManager.CreateCopy(compReg.Component, notNullableOverride: true);
                EntityManager.AddComponent(ent, newComp, true);
            }
        }

        if (comp.AddOnInfest is not null)
        {
            foreach (var (key, compReg) in comp.AddOnInfest)
                RemCompDeferred(ent, compReg.Component.GetType());
        }

        // Trauma: worms are stunned when forcibly ejected
        if (forced && TryComp<StatusEffectsComponent>(ent, out var status))
        {
            _stun.TryStun(ent.Owner, comp.RemovalStunDuration, false, status);
        }

        return true;
    }

    public EntityUid? LayEgg(Entity<CorticalBorerComponent> ent)
    {
        if (ent.Comp.Host is not { } host)
            return null;

        if (ent.Comp.EggProto is not {} egg)
            return null;

        var coordinates = _transform.ToMapCoordinates(host.ToCoordinates());
        var spawnedEgg = Spawn(egg, coordinates);
        return spawnedEgg;
    }

    public void PsychicBlast(Entity<CorticalBorerComponent> ent, EntityUid target)
    {
        _stun.TryStun(target, ent.Comp.PsychicBlastDuration, false);
    }

    /// <summary>
    /// Trauma
    /// </summary>
    /// <param name="ent"></param>
    /// <param name="actions"></param>
    public void UpdateBorerActions(Entity<CorticalBorerComponent> ent, List<EntProtoId> actions)
    {
        if (!TryComp<ActionsComponent>(ent, out var actionsComponent))
            return;

        foreach (var actionId in actions)
        {
            _actions.AddAction(ent, actionId);
        }
    }

    public void DamageHost(EntityUid? host, DamageSpecifier? damage)
    {
        if (damage is not null && TryComp<DamageableComponent>(host, out var damageable))
        {
            _damage.TryChangeDamage(host, damage, false, false, damageable);
        }
    }

    public void UpdateUiState(Entity<CorticalBorerComponent> ent)
    {
        var chems = GetBorerChemicals(ent);

        var state = new CorticalBorerDispenserBoundUserInterfaceState(chems, ent.Comp.InjectAmount);

        _ui.SetUiState(ent.Owner, CorticalBorerDispenserUiKey.Key, state);

    }

    public List<CorticalBorerDispenserItem> GetBorerChemicals(Entity<CorticalBorerComponent> ent)
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
        if (!_blood.TryAddToChemicals(comp.Host.Value, solution))
            return false;

        UpdateChems(ent, -((int)chemAmount * chemicalPrototype.Cost));
        return true;
    }

    public bool TakeControlHost(Entity<CorticalBorerComponent> ent, CorticalBorerInfestedComponent infestedComp)
    {
        var (worm, comp) = ent;

        if (comp.Host is not { } host)
             return false;

        // make sure they aren't dead, would throw the worm into a ghost mode and just kill em
        if (TryComp<MobStateComponent>(ent.Comp.Host, out var mobState) &&
            mobState.CurrentState == MobState.Dead)
            return false;

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

        // add the end control and vomit egg action
        foreach (var actionId in ent.Comp.ControlActions)
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
        return true;
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
}

#region User Interface

[Serializable, NetSerializable]
public enum CorticalBorerDispenserUiKey
{
    Key
}


[Serializable, NetSerializable]
public sealed class CorticalBorerDispenserSetInjectAmountMessage : BoundUserInterfaceMessage
{
    public readonly int CorticalBorerDispenserDispenseAmount;

    public CorticalBorerDispenserSetInjectAmountMessage(int amount)
    {
        CorticalBorerDispenserDispenseAmount = amount;
    }
}

[Serializable, NetSerializable]
public sealed class CorticalBorerDispenserInjectMessage : BoundUserInterfaceMessage
{
    public readonly string ChemProtoId;

    public CorticalBorerDispenserInjectMessage(string proto)
    {
        ChemProtoId = proto;
    }
}

[Serializable, NetSerializable]
public sealed class CorticalBorerDispenserBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly List<CorticalBorerDispenserItem> DisList;

    public readonly int SelectedDispenseAmount;
    public CorticalBorerDispenserBoundUserInterfaceState(List<CorticalBorerDispenserItem> disList, int dispenseAmount)
    {
        DisList = disList;
        SelectedDispenseAmount = dispenseAmount;
    }
}

[Serializable, NetSerializable]
public sealed class CorticalBorerDispenserItem(string reagentName, string reagentId, int cost, int amount, int chems, Color reagentColor)
{
    public string ReagentName = reagentName;
    public string ReagentId = reagentId;
    public int Cost = cost;
    public int Amount = amount;
    public int Chems = chems;
    public Color ReagentColor = reagentColor;
}

#endregion

#region Events

public sealed class InfestHostAttempt : CancellableEntityEventArgs
{
    /// <summary>
    ///     The equipment that is blocking the entrance
    /// </summary>
    public EntityUid? Blocker = null;
}

// Trauma Start
[DataDefinition]
public sealed partial class CorticalBorerHostDamageChangeEvent : EntityEventArgs
{
    [DataField]
    public DamageSpecifier? HostDamage;
}

[DataDefinition]
public sealed partial class CorticalBorerChemicalPointCapChangeEvent : EntityEventArgs
{
    [DataField]
    public int Delta;
}

[DataDefinition]
public sealed partial class CorticalBorerChemicalDispenserAdditionEvent : EntityEventArgs
{
    [DataField]
    public List<EntProtoId> Chemicals;
}

[DataDefinition]
public sealed partial class CorticalBorerBarotraumaRemovalEvent : EntityEventArgs
{
}

[DataDefinition]
public sealed partial class CorticalBorerDamageModifierChangeEvent : EntityEventArgs
{
    [DataField]
    public EntProtoId ModifierSet;
}

[DataDefinition]
public sealed partial class CorticalBorerMovementSpeedChangeEvent : EntityEventArgs
{
    [DataField]
    public float BaseWalkSpeed;

    [DataField]
    public float BaseSprintSpeed;
}

/// <summary>
/// ProductEvent raised to add an action to the borer. This is important, as a store listing's ProductAction is added
/// to the purchasing Mind rather than to the Entity itself. That allows wizards to keep their spells after a mindswap,
/// but we don't want borers to have access to purchased shop abilities while controlling a host (unless...)
///
/// TODO potentially worth making a generic event under the ActionsSystem for reuse?
/// </summary>
[DataDefinition]
public sealed partial class CorticalBorerUnlockActionsEvent : EntityEventArgs
{
    [DataField]
    public List<EntProtoId> Actions = [];
}
// Trauma End
#endregion
