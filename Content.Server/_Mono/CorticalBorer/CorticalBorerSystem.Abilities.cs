// SPDX-FileCopyrightText: 2025 Ark
// SPDX-FileCopyrightText: 2025 Coenx-flex
// SPDX-FileCopyrightText: 2025 Cojoke
// SPDX-FileCopyrightText: 2025 ark1368
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Mono.Objectives.Components;
using Content.Server.Antag.Components;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Medical;
using Content.Shared._Mono.CorticalBorer;
using Content.Shared._Mono.CorticalBorer.Components;
using Content.Shared.Body.Components;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Item;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Store.Components;

namespace Content.Server._Mono.CorticalBorer;

public sealed partial class CorticalBorerSystem
{
    [Dependency] private readonly VomitSystem _vomit = default!;

    private void SubscribeAbilities()
    {
        SubscribeLocalEvent<CorticalBorerComponent, CorticalInfestEvent>(OnInfest);
        SubscribeLocalEvent<CorticalBorerComponent, CorticalInfestDoAfterEvent>(OnInfestDoAfter);

        SubscribeLocalEvent<CorticalBorerComponent, CorticalEjectEvent>(OnEjectHost);
        SubscribeLocalEvent<CorticalBorerComponent, CorticalTakeControlEvent>(OnTakeControl);

        SubscribeLocalEvent<CorticalBorerComponent, CorticalChemMenuActionEvent>(OnChemicalMenu);
        SubscribeLocalEvent<CorticalBorerComponent, CorticalCheckBloodEvent>(OnCheckBlood);

        SubscribeLocalEvent<CorticalBorerInfestedComponent, CorticalEndControlEvent>(OnEndControl);
        SubscribeLocalEvent<CorticalBorerInfestedComponent, CorticalLayEggEvent>(OnLayEgg);

        SubscribeLocalEvent<CorticalBorerComponent, CorticalInvadeThoughtsEvent>(OnInvadeThoughts); // Trauma
        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerEvolutionMenuEvent>(OnOpenEvolutionMenu); // Trauma
        SubscribeLocalEvent<CorticalBorerComponent, CorticalBorerPsychicBlastEvent>(OnPsychicBlast); // Trauma

    }

    private void OnChemicalMenu(Entity<CorticalBorerComponent> ent, ref CorticalChemMenuActionEvent args)
    {
        if(!TryComp<UserInterfaceComponent>(ent, out var uic))
            return;

        if (ent.Comp.Host is null)
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-no-host"), ent, ent, PopupType.Medium);
            return;
        }

        _ui.TryToggleUi((ent, uic), CorticalBorerDispenserUiKey.Key, ent);
    }

    private void OnInfest(Entity<CorticalBorerComponent> ent, ref CorticalInfestEvent args)
    {
        var (uid, comp) = ent;
        var target = args.Target;
        var targetIdentity = Identity.Entity(target, EntityManager);

        if (comp.Host is not null)
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-has-host"), uid, uid, PopupType.Medium);
            return;
        }

        if (HasComp<CorticalBorerInfestedComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-host-already-infested", ("target", targetIdentity)), uid, uid, PopupType.Medium);
            return;
        }

        // Prevent borers from infesting other borers. :o)
        if (HasComp<CorticalBorerComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-invalid-host", ("target", targetIdentity)), uid, uid, PopupType.Medium);

            return;
        }

        // Trauma: prevent borers from infesting mice, mothroaches, and similar little guys
        if (HasComp<ItemComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-invalid-host", ("target", targetIdentity)), uid, uid, PopupType.Medium);

            return;
        }

        // anything with bloodstream
        if (!HasComp<BloodstreamComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-invalid-host", ("target", targetIdentity)), uid, uid, PopupType.Medium);
            return;
        }

        // target is on sugar for some reason, can't go in there
        if (!CanUseAbility(ent, target))
            return;

        var infestAttempt = new InfestHostAttempt();
        RaiseLocalEvent(target, infestAttempt);

        if (infestAttempt.Cancelled)
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-face-covered", ("target", targetIdentity)), uid, uid, PopupType.Medium);
            return;
        }

        _popup.PopupEntity(Loc.GetString("cortical-borer-start-infest", ("target", targetIdentity)), uid, uid, PopupType.Medium);

        var infestArgs = new DoAfterArgs(EntityManager, uid, TimeSpan.FromSeconds(3), new CorticalInfestDoAfterEvent(), uid, target)
        {
            DistanceThreshold = 1.5f,
            BreakOnDamage = true,
            BreakOnMove = true,
            BreakOnWeightlessMove = true,
            AttemptFrequency = AttemptFrequency.StartAndEnd,
            Hidden = true,
        };
        _doAfter.TryStartDoAfter(infestArgs);
    }

    private void OnInfestDoAfter(Entity<CorticalBorerComponent> ent, ref CorticalInfestDoAfterEvent args)
    {
        if (args.Handled)
            return;

        if (args.Args.Target is not { } target)
            return;

        if (args.Cancelled || HasComp<CorticalBorerInfestedComponent>(target))
            return;

        if (HasComp<CorticalBorerComponent>(target))
            return;

        if (InfestTarget(ent, target))
        {
            UpdateInfectedObjective(ent.Owner, 1);
        }
        args.Handled = true;
    }

    private void OnEjectHost(Entity<CorticalBorerComponent> ent, ref CorticalEjectEvent args)
    {
        if (args.Handled)
            return;

        var (uid, comp) = ent;

        if (comp.Host is null)
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-no-host"), uid, uid, PopupType.Medium);
            return;
        }

        if (!CanUseAbility(ent, comp.Host.Value))
            return;

        if (TryEjectBorer(ent))
        {
            UpdateInfectedObjective(ent.Owner, -1);
        }

        args.Handled = true;
    }

    private void OnCheckBlood(Entity<CorticalBorerComponent> ent, ref CorticalCheckBloodEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Host is null)
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-no-host"), ent, ent, PopupType.Medium);
            return;
        }

        TryToggleCheckBlood(ent);

        args.Handled = true;
    }

    private void OnTakeControl(Entity<CorticalBorerComponent> ent, ref CorticalTakeControlEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Host is null)
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-no-host"), ent, ent, PopupType.Medium);
            return;
        }

        // Host is dead, you can't take control
        if (TryComp<MobStateComponent>(ent.Comp.Host, out var mobState) &&
            mobState.CurrentState == MobState.Dead)
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-dead-host"), ent, ent, PopupType.Medium);
            return;
        }

        if (!TryComp<CorticalBorerInfestedComponent>(ent.Comp.Host, out var infestedComp))
            return;

        if (!CanUseAbility(ent, ent.Comp.Host.Value))
            return;

        // idk how you would cause this...
        if (ent.Comp.ControllingHost)
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-already-control"), ent, ent, PopupType.Medium);
            return;
        }

        if (TakeControlHost(ent, infestedComp))
        {
            if (TryComp<GhostRoleComponent>(ent, out var ghostRole))
                _ghost.UnregisterGhostRole((ent, ghostRole)); // prevent players from taking the worm role once mind isn't in the worm
        }

        args.Handled = true;
    }

    private void OnEndControl(Entity<CorticalBorerInfestedComponent> host, ref CorticalEndControlEvent args)
    {
        if (args.Handled)
            return;
        var worm = host.Comp.Borer;

        EndControl(host.Comp.Borer);
        if (TryComp<GhostRoleComponent>(worm, out var ghostRole))
            _ghost.RegisterGhostRole((worm, ghostRole)); // re-enable the ghost role after you return to the body

        args.Handled = true;
    }

    private void OnLayEgg(Entity<CorticalBorerInfestedComponent> host, ref CorticalLayEggEvent args)
    {
        if (args.Handled)
            return;

        var borer = host.Comp.Borer;

        if (borer.Comp.EggCost > borer.Comp.ChemicalPoints)
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-not-enough-chem"), host, host, PopupType.Medium);
            return;
        }

        _vomit.Vomit(host, -20, -20); // half as much chem vomit, a lot that is coming up is the egg
        if (LayEgg(borer) is { } egg)
        {
            UpdateChems(borer, -borer.Comp.EggCost);
            UpdateEggsObjective(borer, 1);
            _rule.AssociateEgg(borer, egg);
        }

        args.Handled = true;
    }

    private void OnInvadeThoughts(Entity<CorticalBorerComponent> ent, ref CorticalInvadeThoughtsEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Host is null)
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-no-host"), ent, ent, PopupType.Medium);
            return;
        }

        // cannot invade the thoughts of a corpse
        if (TryComp<MobStateComponent>(ent.Comp.Host, out var mobState) &&
            mobState.CurrentState == MobState.Dead)
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-dead-host"), ent, ent, PopupType.Medium);
            return;
        }

        if (!TryComp<CorticalBorerInfestedComponent>(ent.Comp.Host, out var infestedComp))
            return;

        if (!CanUseAbility(ent, ent.Comp.Host.Value))
            return;

        InvadeThoughts(ent, infestedComp);

        args.Handled = true;
    }

    private void OnOpenEvolutionMenu(Entity<CorticalBorerComponent> ent, ref CorticalBorerEvolutionMenuEvent args)
    {
        if (!TryComp<StoreComponent>(ent.Owner, out var store))
            return;

        _store.ToggleUi(ent.Owner, ent.Owner, store);
    }

    private void OnPsychicBlast(Entity<CorticalBorerComponent> ent, ref CorticalBorerPsychicBlastEvent args)
    {
        if (args.Handled)
            return;
        if (ent.Comp.HasHost())
        {
            _popup.PopupEntity(Loc.GetString("cortical-borer-has-host"), ent, ent, PopupType.Medium);
            return;
        }

        PsychicBlast(ent, args.Target);
        Dirty(args.Action);

        args.Handled = true;
    }
}
