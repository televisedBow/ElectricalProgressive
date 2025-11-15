using Vintagestory.API.MathTools;

namespace ElectricalProgressiveImmersive.Interface;

public interface IElectricConductor
{
    /// <summary>
    /// Координата проводника
    /// </summary>
    public BlockPos Pos { get; }

   

    /// <summary>
    /// Обновляем Entity
    /// </summary>
    public void Update();
}
