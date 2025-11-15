using ElectricalProgressiveImmersive.Interface;
using ElectricalProgressiveImmersive.Utils;
using Vintagestory.API.MathTools;

public class VirtualConductor : IElectricConductor
{
    public BlockPos Pos { get; }

    public VirtualConductor(BlockPos pos)
    {
        this.Pos = pos;
    }

    public void Update() { }
}