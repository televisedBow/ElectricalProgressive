using Vintagestory.API.Common;

namespace ElectricalProgressiveImmersive.Interface;

public interface IEnergyStorageItem
{
    /// <summary>
    /// Получение энергии предметом
    /// </summary>
    /// <param name="itemstack"></param>
    /// <param name="maxReceive"></param>
    /// <returns></returns>
    int receiveEnergy(ItemStack itemstack, int maxReceive);
}