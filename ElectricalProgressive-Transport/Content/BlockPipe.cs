using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressiveTransport
{
    public class BlockPipe : BlockPipeBase
    {
        // Простая транспортная труба
        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Lang.Get("electricalprogressivetransport:pipe-normal-info"));
            BEPipe pipe = world.BlockAccessor.GetBlockEntity(pos) as BEPipe;
            if (pipe != null)
            {
                pipe.GetBlockInfo(sb);
            }
            
            return sb.ToString();
        }
    }
}