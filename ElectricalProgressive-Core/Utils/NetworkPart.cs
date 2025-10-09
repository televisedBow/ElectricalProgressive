using Vintagestory.API.MathTools;
using ElectricalProgressive.Interface;
using System.Collections.Generic;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Часть сети
    /// </summary>
    public class NetworkPart
    {
        public readonly List<Network>[] Networks = new List<Network>[6];
        public EParams[] eparams = new EParams[] { };
        public readonly BlockPos Position;
        public Facing Connection = Facing.None;
        public IElectricAccumulator? Accumulator;
        public IElectricConsumer? Consumer;
        public IElectricConductor? Conductor;
        public IElectricProducer? Producer;
        public IElectricTransformator? Transformator;
        public bool IsLoaded = false;
        public List<EnergyPacket> packets= new();

        public NetworkPart(BlockPos position)
        {
            Position = position;
        }
    }
}