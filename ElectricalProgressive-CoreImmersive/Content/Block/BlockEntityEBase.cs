using ElectricalProgressive.Utils;
using EPImmersive.Content.Block;
using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Block;

public abstract class BlockEntityEBase : BlockEntity
{
    public BEBehaviorEPImmersive? EPImmersive => GetBehavior<BEBehaviorEPImmersive>();


    

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        this.EPImmersive?.OnBlockUnloaded(); // вызываем метод OnBlockUnloaded у BEBehaviorElectricalProgressive
    }


 
}