using Vintagestory.API.Common;

namespace EPImmersive.Content.Block;

public abstract class BlockEntityEIBase : BlockEntity
{
    public BEBehaviorEPImmersive? EPImmersive => GetBehavior<BEBehaviorEPImmersive>();


    

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        this.EPImmersive?.OnBlockUnloaded(); // вызываем метод OnBlockUnloaded у BEBehaviorElectricalProgressive
    }


 
}