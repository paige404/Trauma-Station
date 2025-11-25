// SPDX-FileCopyrightText: 2025 Coenx-flex
// SPDX-FileCopyrightText: 2025 Cojoke
// SPDX-FileCopyrightText: 2025 Ilya246
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Starlight.CollectiveMind;
using Content.Shared.Alert;
using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Mono.CorticalBorer.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CorticalBorerComponent : Component
{
    /// <summary>
    /// Host of this Borer
    /// </summary>
    [ViewVariables]
    public EntityUid? Host = null;

    /// <summary>
    /// Current number of chemical points this Borer has, used to level up and buy chems
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [AutoNetworkedField]
    [DataField]
    public int ChemicalPoints = 50;

    /// <summary>
    /// Chemicals added every second WHILE IN A HOST
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public int ChemicalGenerationRate = 1;

    /// <summary>
    /// Max Chemicals that can be held
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public int ChemicalPointCap = 250;

    /// <summary>
    /// Reagent injection amount
    /// </summary>
    public int InjectAmount = 10;

    /// <summary>
    /// At what interval does the chem ui update
    /// </summary>
    public int UiUpdateInterval = 5; // every 6 to prevent constant update on cap

    /// <summary>
    /// The max duration you can take control of your host
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public TimeSpan ControlDuration = TimeSpan.FromSeconds(40);

    // Trauma: borers are stunned and vulnerable when surgically removed
    /// <summary>
    /// The stun duration you suffer when forcibly removed from your host
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public TimeSpan RemovalStunDuration = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Cooldown between chem regen events.
    /// </summary>
    public TimeSpan UpdateTimer = TimeSpan.Zero;
    public float UpdateCooldown = 1f;

    /// <summary>
    /// Can this borer make more
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public bool CanReproduce = true;

    /// <summary>
    /// What does it vomit out of its mouth when it lays an egg
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public string EggProto = "CorticalBorerEgg";

    /// <summary>
    /// cost to lay an egg... TODO will not update ability desc if changed
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public int EggCost = 200;

    // Trauma: borer progression mechanics
    /// <summary>
    /// Total evolution points gained by the borer.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TotalEvolutionPoints;

    // Trauma: Borers can't just afk in a host without any upkeep
    /// <summary>
    /// Damage dealt to the host every second while infected
    /// </summary>
    [DataField, AutoNetworkedField]
    public DamageSpecifier? HostDamage;

    // Trauma
    [DataField]
    public TimeSpan PsychicBlastDuration = TimeSpan.FromSeconds(6);

    [DataField]
    public bool ControllingHost;

    [DataField]
    public ComponentRegistry? AddOnInfest;

    [DataField]
    public ComponentRegistry? RemoveOnInfest;

    [DataField]
    public ProtoId<AlertPrototype> ChemicalAlert = "Chemicals";

    [DataField]
    public ProtoId<CollectiveMindPrototype> HivemindChannel = "WormMind"; // Trauma: channel rename

    // Trauma: modifiable chem lists
    /// <summary>
    /// The list of <see cref="CorticalBorerChemicalPrototype"/>s that this borer can inject into hosts.
    /// See cortical_borer_chemicals.yml
    /// </summary>
    public List<EntProtoId> ReagentList = new()
    {
        "borerBicaridine",
        "borerKelotane",
        "borerSaline",
        "borerEthanol",
        "borerMuteToxin",
        "borerCharcoal",
        "borerHappiness",
        "borerEphedrine",
        "borerNorepinephricAcid",
        "borerDexalinPlus",
        "borerHeartbreakerToxin",
        "borerNocturine",
    };

    public readonly List<EntProtoId> InitialCorticalBorerActions = new()
    {
        "ActionCorticalBorerInfest",
        "ActionCorticalBorerEject",
        "ActionCorticalBorerChemMenu",
        "ActionCheckBlood",
        "ActionInvadeThoughts",
        "ActionControlHost",
        "ActionCorticalBorerEvolutionMenu",
    };

    // TODO use this or get rid
    public readonly List<EntProtoId> InfestCorticalBorerActions = new()
    {
        "ActionCorticalBorerEject",
        "ActionCorticalBorerChemMenu",
        "ActionCheckBlood",
        "ActionInvadeThoughts",
        "ActionControlHost",
        "ActionCorticalBorerEvolutionMenu",
    };

    public readonly List<EntProtoId> ControlCorticalBorerActions = new()
    {
        "ActionLayEggHost",
        "ActionEndControlHost",
    };
}


