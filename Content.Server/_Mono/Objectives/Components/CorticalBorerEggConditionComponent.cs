using Content.Server._Mono.Objectives.Systems;

namespace Content.Server._Mono.Objectives.Components;

[RegisterComponent]
public sealed partial class CorticalBorerEggConditionComponent : Component
{
    [DataField]
    public int EggsLaid;
}
