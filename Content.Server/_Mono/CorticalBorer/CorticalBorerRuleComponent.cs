namespace Content.Server._Mono.CorticalBorer;

[RegisterComponent]
public sealed partial class CorticalBorerRuleComponent : Component
{
    [DataField]
    public int EggsLaid = 0;

    [DataField]
    public int HostsInfected = 0;

    [DataField]
    public int EscapingBorers = 0;
}
