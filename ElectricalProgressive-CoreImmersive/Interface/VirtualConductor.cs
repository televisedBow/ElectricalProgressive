using EPImmersive.Interface;
using EPImmersive.Utils;
using Vintagestory.API.MathTools;

namespace EPImmersive.Interface;

public class IEImmersiveVConductor : VirtualConductor
{
    public IEImmersiveVConductor(BlockPos pos) : base(pos)
    {
        this.Pos = pos;
    }

    public BlockPos Pos { get; }

    public void Update() { }
}